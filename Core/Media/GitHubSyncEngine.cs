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

        [JsonProperty("radyo")]
        public List<string> Radyo { get; set; } = new List<string>();

        [JsonProperty("epg")]
        public List<string> Epg { get; set; } = new List<string>();
    }

    public class GitHubSyncEngine
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        private readonly DatabaseEngine _db = new DatabaseEngine();
        private readonly M3uEngine _m3u = new M3uEngine();

        public event Action<int, string>? OnProgress;
        public static event Action? OnSyncStarted;
        public static event Action? OnSyncCompleted;
        public static void RaiseSyncStarted() => OnSyncStarted?.Invoke();
        public static void RaiseSyncCompleted() => OnSyncCompleted?.Invoke();

        public async Task PullFromGitHubAsync()
        {
            RaiseSyncStarted();
            LogService.LogInfo("GitHubSyncEngine: Otomatik güncelleme başlatıldı.");
            OnProgress?.Invoke(2, "Yapılandırma dosyası çekiliyor (auto_update.json)...");
            try
            {
                // 1. Fetch Config
                string configJson = "";
                string configUrl = "https://raw.githubusercontent.com/bilo1975tr/sm/refs/heads/main/auto_update.json";
                try
                {
                    using var configCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var configResponse = await _httpClient.GetAsync(configUrl, configCts.Token);
                    if (configResponse.IsSuccessStatusCode)
                    {
                        configJson = await configResponse.Content.ReadAsStringAsync(configCts.Token);
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
                if (cfg == null)
                {
                    OnProgress?.Invoke(0, "Hata: Yapılandırma dosyası boş veya geçersiz.");
                    return;
                }

                int totalSources = (cfg.Tv?.Count ?? 0) + (cfg.Film?.Count ?? 0) + (cfg.Dizi?.Count ?? 0) + (cfg.Radyo?.Count ?? 0) + (cfg.Epg?.Count ?? 0);
                if (totalSources == 0)
                {
                    OnProgress?.Invoke(100, "Güncellenecek yayın kaynağı bulunamadı.");
                    return;
                }

                int processedSources = 0;

                // 2. Process TV Lists
                if (cfg.Tv != null && cfg.Tv.Count > 0)
                {
                    await ProcessListWithProgress(cfg.Tv, "TV", totalSources, () => ++processedSources);
                }

                // 3. Process Film Lists
                if (cfg.Film != null && cfg.Film.Count > 0)
                {
                    await ProcessListWithProgress(cfg.Film, "Film", totalSources, () => ++processedSources);
                }

                // 4. Process Dizi Lists
                if (cfg.Dizi != null && cfg.Dizi.Count > 0)
                {
                    await ProcessListWithProgress(cfg.Dizi, "Dizi", totalSources, () => ++processedSources);
                }

                // 4.5. Process Radio Lists
                if (cfg.Radyo != null && cfg.Radyo.Count > 0)
                {
                    await ProcessListWithProgress(cfg.Radyo, "Radyo", totalSources, () => ++processedSources);
                }

                // 5. Process EPG Sources
                if (cfg.Epg != null && cfg.Epg.Count > 0)
                {
                    var epgEng = new EpgEngine();
                    for (int i = 0; i < cfg.Epg.Count; i++)
                    {
                        string url = cfg.Epg[i];
                        if (string.IsNullOrWhiteSpace(url)) continue;
                        int currentIdx = ++processedSources;
                        double baseProgress = (double)(currentIdx - 1) / totalSources * 100.0;
                        double itemWeight = 100.0 / totalSources;

                        _db.AddEpgSource(url);
                        await epgEng.LoadEpgAsync(url, (subMsg, subPct) =>
                        {
                            double overallPct = Math.Min(99.0, baseProgress + (subPct / 100.0) * itemWeight);
                            OnProgress?.Invoke((int)overallPct, $"[{currentIdx}/{totalSources}] EPG Rehberi ({i + 1}/{cfg.Epg.Count}): {subMsg}");
                        });
                    }
                }

                DatabaseEngine.NotifyDatabaseUpdated();
                LogService.LogInfo("GitHubSyncEngine: Güncelleme tamamlandı.");
                OnProgress?.Invoke(100, "🎉 Bulut güncelleme başarıyla tamamlandı!");
                OnSyncCompleted?.Invoke();
            }
            catch (Exception ex)
            {
                LogService.LogError("GitHubSyncEngine error", ex);
                OnProgress?.Invoke(0, $"Hata oluştu: {ex.Message}");
            }
        }

        private async Task ProcessListWithProgress(List<string> urls, string categoryLabel, int totalSources, Func<int> incrementCounter)
        {
            if (urls == null) return;
            for (int i = 0; i < urls.Count; i++)
            {
                string url = urls[i];
                if (string.IsNullOrWhiteSpace(url)) continue;

                int currentIdx = incrementCounter();
                double baseProgress = (double)(currentIdx - 1) / totalSources * 100.0;
                double itemWeight = 100.0 / totalSources;

                try
                {
                    _db.AddM3uSource(url);
                    var channels = await _m3u.ParseM3uAsync(url, categoryLabel, (subMsg, subPct) =>
                    {
                        double overallPct = Math.Min(99.0, baseProgress + (subPct / 100.0) * itemWeight);
                        OnProgress?.Invoke((int)overallPct, $"[{currentIdx}/{totalSources}] {categoryLabel} ({i + 1}/{urls.Count}): {subMsg}");
                    });

                    if (channels.Count > 0)
                    {
                        await _db.SyncIncomingChannelsAsync(channels);
                        double finishedPct = Math.Min(99.0, baseProgress + itemWeight);
                        OnProgress?.Invoke((int)finishedPct, $"[{currentIdx}/{totalSources}] {categoryLabel} ({i + 1}/{urls.Count}): {channels.Count} içerik kaydedildi.");
                    }
                }
                catch (Exception ex)
                {
                    OnProgress?.Invoke((int)baseProgress, $"[{currentIdx}/{totalSources}] {categoryLabel} ({i + 1}/{urls.Count}) Hata: {ex.Message}");
                }
            }
        }
    }
}
