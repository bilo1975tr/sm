using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StreamMesh.Models;
using Microsoft.Data.Sqlite;

namespace StreamMesh.Services
{
    public class AutoUpdateConfig
    {
        [JsonProperty("tv")]
        public List<string> Tv { get; set; } = new List<string>();

        [JsonProperty("film")]
        public List<string> Film { get; set; } = new List<string>();

        [JsonProperty("dizi")]
        public List<string> Dizi { get; set; } = new List<string>();

        [JsonProperty("epg")]
        public List<string> Epg { get; set; } = new List<string>();
    }

    public static class AutoUpdateService
    {
        private static AutoUpdateConfig _config = new AutoUpdateConfig();
        private static bool _isUpdating = false;

        public static AutoUpdateConfig Config => _config;
        public static bool IsUpdating => _isUpdating;

        public static async Task<AutoUpdateConfig> FetchConfigAsync()
        {
            // 1. Önce yerel geliştirme ortamındaki auto_update.json dosyasını kontrol et (Öncelikli)
            try
            {
                string[] localPaths = new[]
                {
                    "auto_update.json",
                    "../../auto_update.json",
                    "../../../auto_update.json",
                    "../../../../auto_update.json",
                    "../../../../../auto_update.json",
                    "/auto_update.json"
                };

                foreach (var path in localPaths)
                {
                    if (File.Exists(path))
                    {
                        string localJson = File.ReadAllText(path, System.Text.Encoding.UTF8);
                        var fetched = JsonConvert.DeserializeObject<AutoUpdateConfig>(localJson);
                        if (fetched != null)
                        {
                            _config = fetched;
                            SaveConfigLocal(localJson);
                            LogService.Log($"auto_update.json yerel dosyası öncelikli olarak başarıyla yüklendi: {path}");
                            return _config;
                        }
                    }
                }
            }
            catch (Exception localEx)
            {
                LogService.LogError("FetchConfigAsync yerel dosya okuma hatası", localEx);
            }

            // 2. Yerel dosya bulunamadıysa veya hata alındıysa GitHub raw CDN üzerinden çek
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(15);
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko)");

                    // 1. Try refs/heads/main as preferred by the user
                    string url = "https://raw.githubusercontent.com/bilo1975tr/sm/refs/heads/main/auto_update.json";
                    LogService.Log($"auto_update.json GitHub'dan çekiliyor (refs/heads/main): {url}");
                    string json = null;
                    try
                    {
                        json = await client.GetStringAsync(url);
                    }
                    catch (Exception ex)
                    {
                        LogService.Log($"refs/heads/main failed, trying fallback /main/: {ex.Message}");
                        // 2. Try the main fallback
                        url = "https://raw.githubusercontent.com/bilo1975tr/sm/main/auto_update.json";
                        json = await client.GetStringAsync(url);
                    }

                    if (!string.IsNullOrEmpty(json))
                    {
                        var fetched = JsonConvert.DeserializeObject<AutoUpdateConfig>(json);
                        if (fetched != null)
                        {
                            _config = fetched;
                            SaveConfigLocal(json);
                            return _config;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("FetchConfigAsync GitHub failed, trying local appdata cache.", ex);
            }

            LoadConfigLocal();
            return _config;
        }

        private static string GetLocalConfigPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StreamMesh", "auto_update.json");
        }

        private static void SaveConfigLocal(string json)
        {
            try
            {
                string path = GetLocalConfigPath();
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, json, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LogService.LogError("SaveConfigLocal failed", ex);
            }
        }

        private static void LoadConfigLocal()
        {
            try
            {
                string path = GetLocalConfigPath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
                    var fetched = JsonConvert.DeserializeObject<AutoUpdateConfig>(json);
                    if (fetched != null)
                    {
                        _config = fetched;
                        LogService.Log("auto_update.json yerel önbellekten yüklendi.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("LoadConfigLocal failed", ex);
            }
        }

        public static bool IsUrlInAutoUpdate(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            string target = url.Trim().ToLowerInvariant();

            foreach (var u in _config.Tv) if (u.Trim().ToLowerInvariant() == target) return true;
            foreach (var u in _config.Film) if (u.Trim().ToLowerInvariant() == target) return true;
            foreach (var u in _config.Dizi) if (u.Trim().ToLowerInvariant() == target) return true;
            foreach (var u in _config.Epg) if (u.Trim().ToLowerInvariant() == target) return true;

            return false;
        }

        public static async Task PerformAutoUpdateAsync(Action<string> statusCallback)
        {
            if (_isUpdating)
            {
                statusCallback?.Invoke("Zaten bir güncelleme işlemi devam ediyor...");
                return;
            }

            _isUpdating = true;
            statusCallback?.Invoke("Güncelleme listesi GitHub'dan çekiliyor...");
            
            try
            {
                var cfg = await FetchConfigAsync();
                var db = new DatabaseService();
                var m3uService = new M3uService();
                var epgService = new EpgService();

                int totalAddedChannels = 0;
                int totalEpgLoaded = 0;

                // Process TV
                foreach (var url in cfg.Tv)
                {
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    statusCallback?.Invoke($"TV yayını indiriliyor: {url}");
                    try
                    {
                        var channels = await m3uService.ParseM3uAsync(url, "TV");
                        if (channels != null && channels.Count > 0)
                        {
                            db.AddM3uSource(url);
                            await Task.Run(() => db.SaveChannels(channels, url));
                            ForceSetCategory(url, "TV");
                            totalAddedChannels += channels.Count;
                            LogService.Log($"[AutoUpdate] {channels.Count} TV kanalı eklendi/güncellendi: {url}");
                            GC.Collect();
                            await Task.Delay(1500); // UI thread'in nefes almasına izin ver
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError($"Auto update TV source failed: {url}", ex);
                    }
                }

                // Process Film
                foreach (var url in cfg.Film)
                {
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    statusCallback?.Invoke($"Film yayını indiriliyor: {url}");
                    try
                    {
                        var channels = await m3uService.ParseM3uAsync(url, "Film");
                        if (channels != null && channels.Count > 0)
                        {
                            db.AddM3uSource(url);
                            await Task.Run(() => db.SaveChannels(channels, url));
                            ForceSetCategory(url, "Film");
                            totalAddedChannels += channels.Count;
                            LogService.Log($"[AutoUpdate] {channels.Count} Film eklendi/güncellendi: {url}");
                            GC.Collect();
                            await Task.Delay(1500); // UI thread'in nefes almasına izin ver
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError($"Auto update Film source failed: {url}", ex);
                    }
                }

                // Process Dizi
                foreach (var url in cfg.Dizi)
                {
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    statusCallback?.Invoke($"Dizi yayını indiriliyor: {url}");
                    try
                    {
                        var channels = await m3uService.ParseM3uAsync(url, "Dizi");
                        if (channels != null && channels.Count > 0)
                        {
                            db.AddM3uSource(url);
                            await Task.Run(() => db.SaveChannels(channels, url));
                            ForceSetCategory(url, "Dizi");
                            totalAddedChannels += channels.Count;
                            LogService.Log($"[AutoUpdate] {channels.Count} Dizi eklendi/güncellendi: {url}");
                            GC.Collect();
                            await Task.Delay(1500); // UI thread'in nefes almasına izin ver
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError($"Auto update Dizi source failed: {url}", ex);
                    }
                }

                // Process EPG
                foreach (var url in cfg.Epg)
                {
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    statusCallback?.Invoke($"EPG kaynağı indiriliyor ve eşleştiriliyor: {url}");
                    try
                    {
                        bool success = await epgService.ParseEpgUrlAsync(url);
                        if (success)
                        {
                            db.AddEpgSource(url);
                            db.SetSetting($"epg_updated_{url}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                            totalEpgLoaded++;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError($"Auto update EPG source failed: {url}", ex);
                    }
                }

                statusCallback?.Invoke($"Otomatik güncelleme başarıyla tamamlandı!\nEklenen/Güncellenen Kanal: {totalAddedChannels}\nYüklenen EPG: {totalEpgLoaded}");
            }
            catch (Exception ex)
            {
                LogService.LogError("PerformAutoUpdateAsync failed", ex);
                statusCallback?.Invoke($"Güncelleme başarısız oldu: {ex.Message}");
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private static void ForceSetCategory(string playlistUrl, string category)
        {
            try
            {
                var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StreamMesh", "database.db");
                using (var connection = new SqliteConnection($"Data Source={dbPath}"))
                {
                    connection.Open();
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "UPDATE Channels SET Category = @Category WHERE PlaylistUrl = @Url OR Url = @Url";
                        cmd.Parameters.AddWithValue("@Category", category);
                        cmd.Parameters.AddWithValue("@Url", playlistUrl);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"ForceSetCategory error: {playlistUrl} to {category}", ex);
            }
        }
    }
}
