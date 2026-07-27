using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StreamMesh.Models;
using StreamMesh.Core.Database;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
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

    public class GitHubSyncEngine
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private readonly DatabaseEngine _db = new DatabaseEngine();
        private readonly M3uEngine _m3u = new M3uEngine();

        public event Action<int, string>? OnProgress;
        public static event Action? OnSyncCompleted;
        public static void RaiseSyncCompleted() => OnSyncCompleted?.Invoke();

        public async Task PullFromGitHubAsync()
        {
            LogService.LogInfo("GitHubSyncEngine: Otomatik güncelleme başlatıldı.");
            OnProgress?.Invoke(5, "Yapılandırma dosyası çekiliyor...");
            try
            {
                // 1. Fetch Config (Remote + Local Fallback)
                string configJson = "";
                string configUrl = "https://raw.githubusercontent.com/bilo1975tr/sm/refs/heads/main/auto_update.json";
                try
                {
                    var configResponse = await _httpClient.GetAsync(configUrl);
                    if (configResponse.IsSuccessStatusCode)
                    {
                        configJson = await configResponse.Content.ReadAsStringAsync();
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(configJson))
                {
                    string localPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "auto_update.json");
                    if (!System.IO.File.Exists(localPath)) localPath = "auto_update.json";
                    if (System.IO.File.Exists(localPath))
                    {
                        configJson = await System.IO.File.ReadAllTextAsync(localPath);
                    }
                }

                if (string.IsNullOrEmpty(configJson))
                {
                    OnProgress?.Invoke(0, "Hata: Yapılandırma dosyası (auto_update.json) okunamadı.");
                    return;
                }

                var cfg = JsonConvert.DeserializeObject<AutoUpdateConfig>(configJson);
                if (cfg == null) return;

                // 2. Process Lists (TV, Film, Dizi)
                OnProgress?.Invoke(20, "TV kanalları işleniyor...");
                await ProcessList(cfg.Tv, "TV");

                OnProgress?.Invoke(50, "Film listeleri güncelleniyor...");
                await ProcessList(cfg.Film, "Film");

                OnProgress?.Invoke(70, "Diziler senkronize ediliyor...");
                await ProcessList(cfg.Dizi, "Dizi");

                // 3. Process EPG Sources
                OnProgress?.Invoke(85, "EPG rehberleri çekiliyor...");
                if (cfg.Epg != null)
                {
                    var epgEng = new EpgEngine();
                    foreach (var url in cfg.Epg)
                    {
                        if (string.IsNullOrEmpty(url)) continue;
                        try
                        {
                            _db.AddEpgSource(url);
                            await epgEng.LoadEpgAsync(url);
                        } catch { }
                    }
                }

                LogService.LogInfo("GitHubSyncEngine: Güncelleme tamamlandı.");
                OnProgress?.Invoke(100, "Güncelleme başarıyla tamamlandı.");
                OnSyncCompleted?.Invoke();
            }
            catch (Exception ex)
            {
                LogService.LogError("GitHubSyncEngine error", ex);
                OnProgress?.Invoke(0, $"Hata oluştu: {ex.Message}");
            }
        }

        private async Task ProcessList(List<string> urls, string category)
        {
            if (urls == null) return;
            foreach (var url in urls)
            {
                if (string.IsNullOrEmpty(url)) continue;
                try
                {
                    _db.AddM3uSource(url); // Save URL to source list
                    var channels = await _m3u.ParseM3uAsync(url, category);
                    if (channels.Count > 0)
                    {
                        await _db.SyncIncomingChannelsAsync(channels);
                    }
                }
                catch { }
            }
        }
    }
}
