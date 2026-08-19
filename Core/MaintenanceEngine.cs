using System;
using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO.Compression;
using SkiaSharp;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core
{
    public static class MaintenanceEngine
    {
        public static void EnsureSelfInstallation()
        {
            GenerateAssetsIfMissing();
            Task.Run(async () => await CheckAndInstallFFmpegAsync());
        }

        private static async Task CheckAndInstallFFmpegAsync()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string ffmpegDir = Path.Combine(baseDir, "ffmpeg");

                // Robust DLL check: check for any core ffmpeg dlls (avcodec, avformat, avutil)
                string[] coreDlls = { "avcodec-61.dll", "avcodec-60.dll", "avformat-61.dll", "avutil-58.dll" };
                bool dllsFound = false;
                if (Directory.Exists(ffmpegDir))
                {
                    foreach (var d in coreDlls) { if (File.Exists(Path.Combine(ffmpegDir, d))) { dllsFound = true; break; } }
                }

                if (dllsFound)
                {
                    LogService.LogInfo("BAKIM: FFmpeg kütüphanesi hazır.");
                    return;
                }

                LogService.LogInfo("BAKIM: FFmpeg kütüphanesi eksik, indiriliyor...");
                if (!Directory.Exists(ffmpegDir)) Directory.CreateDirectory(ffmpegDir);

                // Source: GyanD FFmpeg 7.0.2 Shared
                string downloadUrl = "https://github.com/GyanD/codexffmpeg/releases/download/7.0.2/ffmpeg-7.0.2-full_build-shared.zip";

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(20);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) StreamMesh/1.1");

                var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                if (response.IsSuccessStatusCode)
                {
                    string zipPath = Path.Combine(ffmpegDir, "ffmpeg_temp.zip");
                    using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fs);
                    }

                    LogService.LogInfo("BAKIM: FFmpeg paketi indirildi (yaklaşık 100MB), ayıklanıyor...");
                    ZipFile.ExtractToDirectory(zipPath, ffmpegDir, true);
                    File.Delete(zipPath);

                    // Robust Scan: Move ALL .dll files found in any subfolder to the ffmpegDir root
                    var allFiles = Directory.GetFiles(ffmpegDir, "*", SearchOption.AllDirectories);
                    int movedCount = 0;
                    foreach (var file in allFiles)
                    {
                        if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            string fileName = Path.GetFileName(file);
                            string targetPath = Path.Combine(ffmpegDir, fileName);
                            if (file != targetPath)
                            {
                                File.Copy(file, targetPath, true);
                                movedCount++;
                            }
                        }
                    }

                    LogService.LogInfo($"BAKIM: FFmpeg motoru başarıyla kuruldu. ({movedCount} DLL yapılandırıldı)");
                }
                else
                {
                    LogService.LogError($"BAKIM: FFmpeg indirme sunucu hatası (HTTP {response.StatusCode})");
                }
            }
            catch (Exception ex) { LogService.LogError("BAKIM: FFmpeg kurulumu sırasında kritik hata", ex); }
        }

        private static void GenerateAssetsIfMissing()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string logosDir = Path.Combine(baseDir, "logos");
                if (!Directory.Exists(logosDir)) Directory.CreateDirectory(logosDir);

                string logoPath = Path.Combine(logosDir, "StreamMesh_Logo.png");
                if (!File.Exists(logoPath)) GenerateLogoPng(logoPath);

                string iconPath = Path.Combine(baseDir, "app_icon.ico");
                if (!File.Exists(iconPath)) GenerateAppIcon(iconPath);
            }
            catch { }
        }

        private static void GenerateLogoPng(string path)
        {
            var info = new SKImageInfo(512, 512);
            using (var surface = SKSurface.Create(info))
            {
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);
                using (var paint = new SKPaint())
                {
                    paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(512, 512),
                        new[] { SKColor.Parse("#0284c7"), SKColor.Parse("#38bdf8") }, null, SKShaderTileMode.Clamp);
                    canvas.DrawRoundRect(40, 40, 432, 432, 80, 80, paint);

                    paint.Shader = null;
                    paint.Color = SKColors.White;
                    paint.TextSize = 200;
                    paint.IsAntialias = true;
                    paint.FakeBoldText = true;
                    paint.TextAlign = SKTextAlign.Center;
                    canvas.DrawText("SM", 256, 330, paint);
                }
                using (var image = surface.Snapshot())
                using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                using (var stream = File.OpenWrite(path)) { data.SaveTo(stream); }
            }
        }

        private static void GenerateAppIcon(string path)
        {
            try
            {
                var info = new SKImageInfo(256, 256);
                using var surface = SKSurface.Create(info);
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);
                using (var paint = new SKPaint())
                {
                    paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(256, 256),
                        new[] { SKColor.Parse("#0284c7"), SKColor.Parse("#38bdf8") }, null, SKShaderTileMode.Clamp);
                    canvas.DrawRoundRect(20, 20, 216, 216, 40, 40, paint);

                    paint.Shader = null;
                    paint.Color = SKColors.White;
                    paint.TextSize = 100;
                    paint.IsAntialias = true;
                    paint.FakeBoldText = true;
                    paint.TextAlign = SKTextAlign.Center;
                    canvas.DrawText("SM", 128, 165, paint);
                }

                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                byte[] pngBytes = data.ToArray();

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
