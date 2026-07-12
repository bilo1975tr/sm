using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO.Compression;

namespace StreamMesh.Services
{
    public static class InventoryService
    {
        public static string InventoryPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Envanter");
        public static string FFmpegPath => Path.Combine(InventoryPath, "ffmpeg.exe");
        
        private static readonly System.Threading.SemaphoreSlim _downloadSemaphore = new System.Threading.SemaphoreSlim(1, 1);

        public static async Task DownloadFFmpegManuallyAsync(Action<string> progressCallback = null)
        {
            await _downloadSemaphore.WaitAsync();
            try
            {
                if (!Directory.Exists(InventoryPath))
                {
                    Directory.CreateDirectory(InventoryPath);
                }

                if (!File.Exists(FFmpegPath))
                {
                    progressCallback?.Invoke("FFmpeg indiriliyor...");
                    await DownloadFFmpegAsync();
                    progressCallback?.Invoke("FFmpeg başarıyla yüklendi!");
                }
                else
                {
                    progressCallback?.Invoke("FFmpeg bilgisayarınızda zaten yüklü.");
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("[Envanter] FFmpeg manuel indirme hatası", ex);
                progressCallback?.Invoke($"Hata: {ex.Message}");
            }
            finally
            {
                _downloadSemaphore.Release();
            }
        }

        public static async Task DownloadAceStreamManuallyAsync(string aceStreamZipUrl, Action<string> progressCallback = null)
        {
            await _downloadSemaphore.WaitAsync();
            try
            {
                if (!Directory.Exists(InventoryPath))
                {
                    Directory.CreateDirectory(InventoryPath);
                }

                string appDataRoaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string aceStreamDir = Path.Combine(appDataRoaming, "AceStream");
                string aceEnginePath = Path.Combine(aceStreamDir, "engine", "ace_engine.exe");

                if (!File.Exists(aceEnginePath))
                {
                    progressCallback?.Invoke("Ace Stream indiriliyor... (Bu işlem boyutuna göre sürebilir)");
                    string aceZipPath = Path.Combine(InventoryPath, "AceStream.zip");

                    using (var client = new HttpClient())
                    {
                        var response = await client.GetAsync(aceStreamZipUrl, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();

                        using (var fs = new FileStream(aceZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await response.Content.CopyToAsync(fs);
                        }
                    }

                    progressCallback?.Invoke("Ace Stream çıkartılıyor...");
                    
                    if (Directory.Exists(aceStreamDir))
                    {
                        Directory.Delete(aceStreamDir, true);
                    }
                    Directory.CreateDirectory(aceStreamDir);

                    // Extract directly into AppData/Roaming/AceStream
                    ZipFile.ExtractToDirectory(aceZipPath, aceStreamDir);
                    
                    File.Delete(aceZipPath);

                    try
                    {
                        var aceService = new AceStreamService();
                        aceService.RegisterBrowserExtension();
                        progressCallback?.Invoke("Ace Stream ve Tarayıcı Eklentisi başarıyla kuruldu!");
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError("[Envanter] Tarayıcı eklentisi kaydedilirken hata oluştu.", ex);
                        progressCallback?.Invoke("Ace Stream kuruldu ancak tarayıcı eklentisi kaydedilemedi.");
                    }
                }
                else
                {
                    progressCallback?.Invoke("Ace Stream bilgisayarınızda zaten yüklü.");
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("[Envanter] Ace Stream manuel indirme hatası", ex);
                progressCallback?.Invoke($"Hata: {ex.Message}");
            }
            finally
            {
                _downloadSemaphore.Release();
            }
        }

        public static async Task DownloadComponentsManuallyAsync(string aceStreamZipUrl, Action<string> progressCallback = null)
        {
            await DownloadFFmpegManuallyAsync(progressCallback);
            await DownloadAceStreamManuallyAsync(aceStreamZipUrl, progressCallback);
        }

        public static bool AreComponentsMissing()
        {
            string appDataRoaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string aceEnginePath = Path.Combine(appDataRoaming, "AceStream", "engine", "ace_engine.exe");

            return !File.Exists(FFmpegPath) || !File.Exists(aceEnginePath);
        }

        public static bool IsFFmpegInstalled()
        {
            return File.Exists(FFmpegPath);
        }

        public static bool IsAceStreamInstalled()
        {
            string appDataRoaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string aceEnginePath = Path.Combine(appDataRoaming, "AceStream", "engine", "ace_engine.exe");
            return File.Exists(aceEnginePath);
        }

        public static async Task CheckAndDownloadInventoryAsync()
        {
            if (!Directory.Exists(InventoryPath))
            {
                Directory.CreateDirectory(InventoryPath);
            }

            if (!File.Exists(FFmpegPath))
            {
                await _downloadSemaphore.WaitAsync();
                try
                {
                    if (!File.Exists(FFmpegPath))
                    {
                        LogService.Log("[Envanter] FFmpeg bulunamadı, indiriliyor...");
                        await DownloadFFmpegAsync();
                    }
                }
                finally
                {
                    _downloadSemaphore.Release();
                }
            }
        }

        private static async Task DownloadFFmpegAsync()
        {
            try
            {
                string zipPath = Path.Combine(InventoryPath, "ffmpeg.zip");
                string extractPath = Path.Combine(InventoryPath, "ffmpeg_temp");

                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync("https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip", HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }

                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, true);
                }

                ZipFile.ExtractToDirectory(zipPath, extractPath);
                
                string sourceExe = Path.Combine(extractPath, "ffmpeg-master-latest-win64-gpl", "bin", "ffmpeg.exe");
                if (File.Exists(sourceExe))
                {
                    File.Copy(sourceExe, FFmpegPath, true);
                    LogService.Log("[Envanter] FFmpeg başarıyla Envanter klasörüne yüklendi.");
                }

                // Temizlik
                Directory.Delete(extractPath, true);
                File.Delete(zipPath);
            }
            catch (Exception ex)
            {
                LogService.LogError("[Envanter] FFmpeg indirme hatası", ex);
            }
        }
    }
}
