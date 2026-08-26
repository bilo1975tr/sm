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

        [JsonProperty("karma")]
        public List<string> Karma { get; set; } = new List<string>();

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
            OnProgress?.Invoke(2, "Temizlenmiş yayın listesi kontrol ediliyor (cleaned_playlist.m3u)...");
            try
            {
                // 1. Önce GitHub Actions tarafından oluşturulan temizlenmiş master M3U listesini dene
                string cleanM3uUrl = "https://raw.githubusercontent.com/bilo1975tr/sm/refs/heads/main/out/cleaned_playlist.m3u";
                bool cleanM3uLoaded = false;

                try
                {
                    using var cleanCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var response = await _httpClient.GetAsync(cleanM3uUrl, cleanCts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        OnProgress?.Invoke(15, "Temizlenmiş master liste indiriliyor...");
                        _db.AddM3uSource(cleanM3uUrl);
                        var channels = await _m3u.ParseM3uAsync(cleanM3uUrl, "TV", false, (subMsg, subPct) =>
                        {
                            OnProgress?.Invoke(20 + (int)(subPct * 0.6), $"Temizlenmiş liste işleniyor: {subMsg}");
                        });

                        if (channels != null && channels.Count > 0)
                        {
                            await _db.SyncIncomingChannelsAsync(channels);
                            cleanM3uLoaded = true;
                            OnProgress?.Invoke(80, $"🎉 {channels.Count} adet doğrulanmış ve temizlenmiş kanal eklendi!");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogWarning($"Clean M3U yüklenemedi, yedek moda geçiliyor: {ex.Message}");
                }

                // 2. Eğer clean M3U bulunamadıysa varsayılan auto_update.json kaynaklarını indir
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

                if (!string.IsNullOrEmpty(configJson))
                {
                    var cfg = JsonConvert.DeserializeObject<AutoUpdateConfig>(configJson);
                    if (cfg != null)
                    {
                        // Eğer clean M3U yüklenemediyse ham listeleri tara
                        if (!cleanM3uLoaded)
                        {
                            int totalSources = (cfg.Tv?.Count ?? 0) + (cfg.Film?.Count ?? 0) + (cfg.Dizi?.Count ?? 0) + (cfg.Radyo?.Count ?? 0) + (cfg.Karma?.Count ?? 0) + (cfg.Epg?.Count ?? 0);
                            int processedSources = 0;

                            if (cfg.Tv != null && cfg.Tv.Count > 0) await ProcessListWithProgress(cfg.Tv, "TV", totalSources, () => ++processedSources, true);
                            if (cfg.Film != null && cfg.Film.Count > 0) await ProcessListWithProgress(cfg.Film, "Film", totalSources, () => ++processedSources, true);
                            if (cfg.Dizi != null && cfg.Dizi.Count > 0) await ProcessListWithProgress(cfg.Dizi, "Dizi", totalSources, () => ++processedSources, true);
                            if (cfg.Radyo != null && cfg.Radyo.Count > 0) await ProcessListWithProgress(cfg.Radyo, "Radyo", totalSources, () => ++processedSources, true);
                            if (cfg.Karma != null && cfg.Karma.Count > 0) await ProcessListWithProgress(cfg.Karma, "Karma", totalSources, () => ++processedSources, false);
                        }

                        // EPG Verilerini Her Durumda Güncelle
                        if (cfg.Epg != null && cfg.Epg.Count > 0)
                        {
                            OnProgress?.Invoke(85, "Yayın akışları (EPG) güncelleniyor...");
                            var epgEng = new EpgEngine();
                            for (int i = 0; i < cfg.Epg.Count; i++)
                            {
                                string url = cfg.Epg[i];
                                if (string.IsNullOrWhiteSpace(url)) continue;

                                _db.AddEpgSource(url);
                                await epgEng.LoadEpgAsync(url, (subMsg, subPct) =>
                                {
                                    OnProgress?.Invoke(85 + (int)(subPct * 0.14), $"EPG Rehberi ({i + 1}/{cfg.Epg.Count}): {subMsg}");
                                });
                            }
                        }
                    }
                }

                // Logo senkronizasyonunu ve eksik logoların zenginleştirilmesini tetikle
                try
                {
                    var logoSync = new LogoSyncService();
                    await logoSync.SyncIfNecessaryAsync();
                }
                catch (Exception logoEx)
                {
                    LogService.LogWarning($"GitHubSyncEngine: LogoSync tetikleme uyarısı: {logoEx.Message}");
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

        private async Task ProcessListWithProgress(List<string> urls, string categoryLabel, int totalSources, Func<int> incrementCounter, bool forceCategory = true)
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
                    var channels = await _m3u.ParseM3uAsync(url, categoryLabel, forceCategory, (subMsg, subPct) =>
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
