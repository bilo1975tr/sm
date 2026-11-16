using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;
using SkiaSharp;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media.Mpv;
using StreamMesh.Core.Network;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core
{
    public static class MaintenanceEngine
    {
        public static void EnsureSelfInstallation()
        {
            GenerateAssetsIfMissing();
            EnsureFirewallAndAntivirusRules();
            _ = Task.Run(CheckAndInstallMpvAsync);
        }

        public static void EnsureFirewallAndAntivirusRules()
        {
            Task.Run(() =>
            {
                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(baseDir, "StreamMesh.exe");

                    // Run netsh rules only once per executable path so startup doesn't spawn 4 cmd.exe processes every boot
                    var db = DatabaseEngine.Instance;
                    string configuredPath = db.GetSetting("FirewallConfiguredPath", "");
                    if (string.Equals(configuredPath, exePath, StringComparison.OrdinalIgnoreCase))
                        return;

                    string combinedCmd =
                        $"netsh advfirewall firewall add rule name=\"StreamMesh\" dir=in action=allow program=\"{exePath}\" enable=yes profile=any & " +
                        "netsh advfirewall firewall add rule name=\"StreamMesh Port 8088\" dir=in action=allow protocol=TCP localport=8088 profile=any & " +
                        "netsh advfirewall firewall add rule name=\"StreamMesh HLS Timeshift\" dir=in action=allow protocol=TCP localport=48931-48950 profile=any & " +
                        "netsh advfirewall firewall add rule name=\"AceStream Ports\" dir=in action=allow protocol=TCP localport=6878,8621 profile=any";

                    RunHiddenCmd(combinedCmd);
                    db.SetSetting("FirewallConfiguredPath", exePath);
                    LogService.LogInfo("[FIREWALL] Güvenlik duvarı kuralları başarıyla denetlendi ve kaydedildi.");
                }
                catch (Exception ex)
                {
                    LogService.LogWarning($"[FIREWALL] Güvenlik duvarı kaydı uyarısı: {ex.Message}");
                }
            });
        }

        private static void RunHiddenCmd(string command)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
            }
            catch { }
        }

        private static string GetWritableMpvDir()
        {
            string baseMpv = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mpv");
            try
            {
                Directory.CreateDirectory(baseMpv);
                string probe = Path.Combine(baseMpv, ".write_test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return baseMpv;
            }
            catch
            {
                string localMpv = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "StreamMesh", "mpv");
                Directory.CreateDirectory(localMpv);
                return localMpv;
            }
        }

        public static async Task CheckAndInstallMpvAsync()
        {
            try
            {
                if (MpvPlayerEngine.IsLibraryAvailable())
                {
                    LogService.LogInfo("BAKIM: libmpv oynatıcı motoru hazır.");
                    return;
                }

                string mpvDir = GetWritableMpvDir();
                LogService.LogInfo("BAKIM: libmpv-2.dll eksik, indiriliyor...");

                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(15));
                using var apiReq = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/shinchiro/mpv-winbuild-cmake/releases/latest");
                apiReq.Headers.UserAgent.ParseAdd("StreamMesh/1.1");
                apiReq.Headers.Accept.ParseAdd("application/vnd.github+json");

                string? downloadUrl = null;
                using (var apiResp = await MediaHttpClient.Client.SendAsync(apiReq, cts.Token).ConfigureAwait(false))
                {
                    if (apiResp.IsSuccessStatusCode)
                    {
                        string json = await apiResp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                        var release = JObject.Parse(json);
                        var assets = release["assets"] as JArray;
                        if (assets != null)
                        {
                            foreach (var asset in assets)
                            {
                                string name = asset["name"]?.ToString() ?? "";
                                if (name.StartsWith("mpv-dev-x86_64-", StringComparison.OrdinalIgnoreCase) &&
                                    !name.Contains("-v3-", StringComparison.OrdinalIgnoreCase) &&
                                    name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
                                {
                                    downloadUrl = asset["browser_download_url"]?.ToString();
                                    break;
                                }
                            }
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    LogService.LogError("BAKIM: libmpv indirme adresi GitHub Releases üzerinden alınamadı.");
                    return;
                }

                using var req = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) StreamMesh/1.1");

                using var response = await MediaHttpClient.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    LogService.LogError($"BAKIM: libmpv indirme sunucu hatası (HTTP {response.StatusCode})");
                    return;
                }

                string archivePath = Path.Combine(mpvDir, "mpv_temp.7z");
                await using (var fs = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fs, cts.Token).ConfigureAwait(false);
                }

                int extractedDlls = 0;
                using (var archive = ArchiveFactory.OpenArchive(archivePath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.IsDirectory) continue;
                        string key = entry.Key ?? "";
                        if (!key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;

                        string fileName = Path.GetFileName(key);
                        if (string.IsNullOrEmpty(fileName)) continue;

                        string destPath = Path.Combine(mpvDir, fileName);
                        entry.WriteToFile(destPath, new ExtractionOptions { ExtractFullPath = false, Overwrite = true });
                        extractedDlls++;
                    }
                }

                try { File.Delete(archivePath); } catch { }
                LogService.LogInfo($"BAKIM: libmpv motoru başarıyla kuruldu. ({extractedDlls} DLL yapılandırıldı -> {mpvDir})");
            }
            catch (Exception ex)
            {
                LogService.LogError("BAKIM: libmpv kurulumu sırasında kritik hata", ex);
            }
        }

        private static void GenerateAssetsIfMissing()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string logosDir = Path.Combine(baseDir, "logos");
                Directory.CreateDirectory(logosDir);

                string logoPath = Path.Combine(logosDir, "StreamMesh_logo.png");
                if (!File.Exists(logoPath)) File.WriteAllBytes(logoPath, RenderBadgePng(512, 40, 432, 80, 200, 330));

                string streamMeshIconPath = Path.Combine(logosDir, "StreamMesh_Icon.ico");
                if (!File.Exists(streamMeshIconPath)) WriteIcoFromPng(streamMeshIconPath, RenderBadgePng(256, 20, 216, 40, 100, 165));

                string iconPath = Path.Combine(baseDir, "app_icon.ico");
                if (!File.Exists(iconPath)) WriteIcoFromPng(iconPath, RenderBadgePng(256, 20, 216, 40, 100, 165));
            }
            catch { }
        }

        private static byte[] RenderBadgePng(int size, float margin, float rectSize, float radius, float fontSize, float textY)
        {
            using var surface = SKSurface.Create(new SKImageInfo(size, size));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            using var paint = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(size, size),
                    new[] { SKColor.Parse("#0284c7"), SKColor.Parse("#38bdf8") }, null, SKShaderTileMode.Clamp)
            };
            canvas.DrawRoundRect(margin, margin, rectSize, rectSize, radius, radius, paint);

            paint.Shader = null;
            paint.Color = SKColors.White;
            paint.TextSize = fontSize;
            paint.IsAntialias = true;
            paint.FakeBoldText = true;
            paint.TextAlign = SKTextAlign.Center;
            canvas.DrawText("SM", size / 2f, textY, paint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        private static void WriteIcoFromPng(string path, byte[] pngBytes)
        {
            try
            {
                using var stream = File.OpenWrite(path);
                using var writer = new BinaryWriter(stream);
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)1);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write((uint)pngBytes.Length);
                writer.Write((uint)22);
                writer.Write(pngBytes);
            }
            catch (Exception ex)
            {
                LogService.LogError("GenerateAppIcon Error", ex);
            }
        }
    }
}
