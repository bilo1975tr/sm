using System;
using System.IO;
using System.Diagnostics;
using SkiaSharp;

namespace StreamMesh.Core
{
    public static class MaintenanceEngine
    {
        public static void EnsureSelfInstallation()
        {
            // V1.8.5: Removed automatic copying to LocalAppData to fix 10s delay.
            // App will run from its current directory.
            GenerateAssetsIfMissing();
            CheckLibVlcStatus();
        }

        private static void CheckLibVlcStatus()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] possiblePaths = {
                    Path.Combine(baseDir, "libvlc", "win-x64"),
                    Path.Combine(baseDir, "libvlc"),
                    @"C:\Program Files\VideoLAN\VLC",
                    baseDir
                };

                bool found = false;
                foreach(var p in possiblePaths)
                {
                    if (File.Exists(Path.Combine(p, "libvlc.dll")))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    Utils.LogService.LogError("BAKIM: LibVLC (libvlc.dll) bulunamadı! Oynatıcı çalışmayabilir. Lütfen VLC Player 64-bit kurun.");
                }
            }
            catch { }
        }

        private static void GenerateAssetsIfMissing()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string logosDir = Path.Combine(baseDir, "logos");
                if (!Directory.Exists(logosDir)) Directory.CreateDirectory(logosDir);

                string logoPath = Path.Combine(logosDir, "StreamMesh_logo.png");
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

                // Build valid Windows ICO file structure
                using var stream = File.OpenWrite(path);
                using var writer = new BinaryWriter(stream);

                // ICONDIR header
                writer.Write((ushort)0); // reserved
                writer.Write((ushort)1); // type 1 = icon
                writer.Write((ushort)1); // 1 image count

                // ICONDIRENTRY (16 bytes)
                writer.Write((byte)0); // width: 256 (0 means 256)
                writer.Write((byte)0); // height: 256 (0 means 256)
                writer.Write((byte)0); // colors
                writer.Write((byte)0); // reserved
                writer.Write((ushort)1); // color planes
                writer.Write((ushort)32); // bits per pixel
                writer.Write((uint)pngBytes.Length); // size of image data
                writer.Write((uint)22); // offset where image data starts (6 + 16)

                // Image Data (PNG)
                writer.Write(pngBytes);
            }
            catch (Exception ex)
            {
                Utils.LogService.LogError("GenerateAppIcon Error", ex);
            }
        }
    }
}
