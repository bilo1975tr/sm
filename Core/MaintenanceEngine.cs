using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
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

        private static readonly SemaphoreSlim _mpvInstallLock = new(1, 1);
        private static Task<bool>? _activeMpvInstallTask;
        private static readonly object _mpvInstallTaskLock = new();
        private static readonly Lazy<HttpClient> _mpvDownloadClient = new(() =>
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                AllowAutoRedirect = true
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(15)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) StreamMesh/1.2");
            return client;
        });

        public static string? LastMpvInstallError { get; private set; }
        public static event Action<int, string>? MpvDownloadProgress;

        public static Task CheckAndInstallMpvAsync() => EnsureMpvInstalledAsync();

        public static Task<bool> EnsureMpvInstalledAsync(Action<int, string>? onProgress = null)
        {
            if (MpvPlayerEngine.IsLibraryAvailable())
                return Task.FromResult(true);

            Task<bool> task;
            lock (_mpvInstallTaskLock)
            {
                if (_activeMpvInstallTask == null || _activeMpvInstallTask.IsCompleted)
                {
                    _activeMpvInstallTask = InstallMpvInternalAsync();
                }
                task = _activeMpvInstallTask;
            }

            if (onProgress == null)
                return task;

            return AwaitWithProgressAsync(task, onProgress);
        }

        private static async Task<bool> AwaitWithProgressAsync(Task<bool> task, Action<int, string> onProgress)
        {
            void Forward(int pct, string msg) => onProgress(pct, msg);
            MpvDownloadProgress += Forward;
            try
            {
                return await task.ConfigureAwait(false);
            }
            finally
            {
                MpvDownloadProgress -= Forward;
            }
        }

        private static void ReportMpvProgress(int pct, string msg)
        {
            try { MpvDownloadProgress?.Invoke(pct, msg); } catch { }
        }

        private static async Task<bool> InstallMpvInternalAsync()
        {
            await _mpvInstallLock.WaitAsync().ConfigureAwait(false);
            string? archivePath = null;
            string? stagingDir = null;
            try
            {
                if (MpvPlayerEngine.IsLibraryAvailable())
                {
                    LogService.LogInfo("BAKIM: libmpv oynatıcı motoru hazır.");
                    return true;
                }

                LastMpvInstallError = null;
                string mpvDir = GetWritableMpvDir();
                LogService.LogInfo("BAKIM: libmpv-2.dll eksik, indiriliyor...");
                ReportMpvProgress(2, "libmpv sürüm adresi kontrol ediliyor...");

                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(15));
                var http = _mpvDownloadClient.Value;

                string? downloadUrl = await ResolveMpvAssetUrlAsync(http, cts.Token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    LastMpvInstallError = "GitHub üzerinden libmpv indirme adresi alınamadı.";
                    LogService.LogError($"BAKIM: {LastMpvInstallError}");
                    return false;
                }

                LogService.LogInfo($"BAKIM: libmpv paketi indiriliyor -> {downloadUrl}");
                ReportMpvProgress(5, "libmpv paketi indiriliyor...");

                using var req = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                using var response = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    LastMpvInstallError = $"libmpv indirme sunucu hatası (HTTP {(int)response.StatusCode})";
                    LogService.LogError($"BAKIM: {LastMpvInstallError}");
                    return false;
                }

                archivePath = Path.Combine(mpvDir, $"mpv_temp_{Guid.NewGuid():N}.7z");
                long totalBytes = response.Content.Headers.ContentLength ?? -1;
                await using (var netStream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false))
                await using (var fs = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    byte[] buffer = new byte[81920];
                    long totalRead = 0;
                    int lastReportedPct = 5;
                    int bytesRead;
                    while ((bytesRead = await netStream.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false)) > 0)
                    {
                        await fs.WriteAsync(buffer, 0, bytesRead, cts.Token).ConfigureAwait(false);
                        totalRead += bytesRead;
                        if (totalBytes > 0)
                        {
                            int pct = Math.Clamp(5 + (int)((double)totalRead / totalBytes * 80.0), 5, 85);
                            if (pct >= lastReportedPct + 3 || pct == 85)
                            {
                                lastReportedPct = pct;
                                double curMb = totalRead / 1024.0 / 1024.0;
                                double totMb = totalBytes / 1024.0 / 1024.0;
                                ReportMpvProgress(pct, $"libmpv indiriliyor %{pct} ({curMb:F1}/{totMb:F1} MB)...");
                            }
                        }
                    }
                }

                ReportMpvProgress(88, "libmpv dosyaları ayıklanıyor...");
                stagingDir = Path.Combine(mpvDir, $"_staging_{Guid.NewGuid():N}");
                Directory.CreateDirectory(stagingDir);

                var stagedFiles = new System.Collections.Generic.List<string>();
                using (var archive = ArchiveFactory.OpenArchive(archivePath))
                using (var reader = archive.ExtractAllEntries())
                {
                    while (reader.MoveToNextEntry())
                    {
                        var entry = reader.Entry;
                        if (entry.IsDirectory) continue;
                        string key = entry.Key ?? "";
                        if (!key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;

                        string fileName = Path.GetFileName(key);
                        if (string.IsNullOrEmpty(fileName)) continue;

                        string stagedPath = Path.Combine(stagingDir, fileName);
                        reader.WriteEntryToFile(stagedPath, new ExtractionOptions { ExtractFullPath = false, Overwrite = true });
                        stagedFiles.Add(stagedPath);
                    }
                }

                // Move helper DLLs first, then move libmpv-2.dll last & atomically so readers never see a half-extracted main DLL
                string? mainDllStaged = null;
                foreach (var srcFile in stagedFiles)
                {
                    string fileName = Path.GetFileName(srcFile);
                    if (string.Equals(fileName, "libmpv-2.dll", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(fileName, "mpv-2.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        mainDllStaged = srcFile;
                        continue;
                    }
                    string destPath = Path.Combine(mpvDir, fileName);
                    File.Move(srcFile, destPath, overwrite: true);
                }

                if (mainDllStaged != null && File.Exists(mainDllStaged))
                {
                    string mainFileName = Path.GetFileName(mainDllStaged);
                    string finalMainPath = Path.Combine(mpvDir, mainFileName);
                    string tmpMainPath = finalMainPath + ".tmp";
                    File.Move(mainDllStaged, tmpMainPath, overwrite: true);
                    File.Move(tmpMainPath, finalMainPath, overwrite: true);
                }

                if (!MpvPlayerEngine.IsLibraryAvailable())
                {
                    LastMpvInstallError = "Arşivden libmpv-2.dll ayıklanamadı.";
                    LogService.LogError($"BAKIM: {LastMpvInstallError}");
                    return false;
                }

                ReportMpvProgress(100, "libmpv motoru hazır.");
                LogService.LogInfo($"BAKIM: libmpv motoru başarıyla kuruldu. ({stagedFiles.Count} DLL yapılandırıldı -> {mpvDir})");
                return true;
            }
            catch (Exception ex)
            {
                LastMpvInstallError = ex.Message;
                LogService.LogError("BAKIM: libmpv kurulumu sırasında kritik hata", ex);
                return false;
            }
            finally
            {
                if (!string.IsNullOrEmpty(archivePath))
                    try { if (File.Exists(archivePath)) File.Delete(archivePath); } catch { }
                if (!string.IsNullOrEmpty(stagingDir))
                    try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true); } catch { }
                _mpvInstallLock.Release();
            }
        }

        private static async Task<string?> ResolveMpvAssetUrlAsync(HttpClient http, System.Threading.CancellationToken token)
        {
            // 1. Try GitHub API first (fast JSON, may hit unauthenticated 403 rate limit)
            try
            {
                using var apiReq = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/shinchiro/mpv-winbuild-cmake/releases/latest");
                apiReq.Headers.Accept.ParseAdd("application/vnd.github+json");
                using var apiResp = await http.SendAsync(apiReq, token).ConfigureAwait(false);
                if (apiResp.IsSuccessStatusCode)
                {
                    string json = await apiResp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    var release = JObject.Parse(json);
                    if (release["assets"] is JArray assets)
                    {
                        var namesAndUrls = new System.Collections.Generic.List<(string Name, string Url)>();
                        foreach (var asset in assets)
                        {
                            string name = asset["name"]?.ToString() ?? "";
                            string url = asset["browser_download_url"]?.ToString() ?? "";
                            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
                                namesAndUrls.Add((name, url));
                        }
                        string? picked = PickBestMpvAssetUrl(namesAndUrls);
                        if (!string.IsNullOrWhiteSpace(picked)) return picked;
                    }
                }
                else
                {
                    LogService.LogWarning($"BAKIM: GitHub API releases/latest HTTP {(int)apiResp.StatusCode}, HTML fallback deneniyor...");
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"BAKIM: GitHub API sorgusu başarısız ({ex.Message}), HTML fallback deneniyor...");
            }

            // 2. Fallback: GitHub web releases/latest -> expanded_assets (zero API rate-limit)
            try
            {
                using var latestResp = await http.GetAsync("https://github.com/shinchiro/mpv-winbuild-cmake/releases/latest", token).ConfigureAwait(false);
                string? tag = null;
                string finalUri = latestResp.RequestMessage?.RequestUri?.ToString() ?? "";
                var uriTagMatch = System.Text.RegularExpressions.Regex.Match(finalUri, @"/releases/tag/([^/?#]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (uriTagMatch.Success)
                {
                    tag = uriTagMatch.Groups[1].Value;
                }
                else
                {
                    string latestHtml = await latestResp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    var htmlTagMatch = System.Text.RegularExpressions.Regex.Match(latestHtml, @"/shinchiro/mpv-winbuild-cmake/releases/tag/([^""/?#]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (htmlTagMatch.Success) tag = htmlTagMatch.Groups[1].Value;
                }

                if (!string.IsNullOrWhiteSpace(tag))
                {
                    string expandedUrl = $"https://github.com/shinchiro/mpv-winbuild-cmake/releases/expanded_assets/{Uri.EscapeDataString(tag)}";
                    string expandedHtml = await http.GetStringAsync(expandedUrl, token).ConfigureAwait(false);
                    var matches = System.Text.RegularExpressions.Regex.Matches(
                        expandedHtml,
                        @"href=""(/shinchiro/mpv-winbuild-cmake/releases/download/[^""]+\.7z)""",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    var namesAndUrls = new System.Collections.Generic.List<(string Name, string Url)>();
                    foreach (System.Text.RegularExpressions.Match m in matches)
                    {
                        string relUrl = m.Groups[1].Value;
                        string fileName = Path.GetFileName(relUrl);
                        namesAndUrls.Add((fileName, "https://github.com" + relUrl));
                    }

                    string? picked = PickBestMpvAssetUrl(namesAndUrls);
                    if (!string.IsNullOrWhiteSpace(picked)) return picked;
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"BAKIM: GitHub HTML releases fallback hatası: {ex.Message}");
            }

            return null;
        }

        private static string? PickBestMpvAssetUrl(System.Collections.Generic.List<(string Name, string Url)> assets)
        {
            // 1. Standard compatible x86_64 dev package (contains libmpv-2.dll, runs on all x64 CPUs)
            foreach (var (name, url) in assets)
            {
                if (name.StartsWith("mpv-dev-x86_64-", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("-v3-", StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
                    return url;
            }
            // 2. AVX2 (-v3-) dev package fallback
            foreach (var (name, url) in assets)
            {
                if (name.StartsWith("mpv-dev-x86_64-", StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
                    return url;
            }
            return null;
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
