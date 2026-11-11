using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StreamMesh.Models;
using StreamMesh.Core.Database;
using StreamMesh.Core.Network;
using StreamMesh.Core.Plugins;
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
        private static readonly HttpClient _httpClient;
        private static int _isRunning = 0;
        public static bool IsRunning => System.Threading.Volatile.Read(ref _isRunning) == 1;
        private readonly DatabaseEngine _db = new DatabaseEngine();
        private readonly M3uEngine _m3u = new M3uEngine();

        static GitHubSyncEngine()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                AllowAutoRedirect = true
            };
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 StreamMesh/1.0");
        }

        public event Action<int, string>? OnProgress;
        public static event Action<int, string>? OnStaticProgress;
        public static event Action? OnSyncStarted;
        public static event Action? OnSyncCompleted;
        public static void RaiseSyncStarted() => OnSyncStarted?.Invoke();
        public static void RaiseSyncCompleted() => OnSyncCompleted?.Invoke();

        private void ReportProgress(int pct, string message)
        {
            OnProgress?.Invoke(pct, message);
            OnStaticProgress?.Invoke(pct, message);
        }

        public async Task PullFromGitHubAsync()
        {
            var plugin = PluginManager.Instance.GetPluginById("github_sync") as GitHubSyncPlugin;
            if (plugin != null && !plugin.IsEnabled)
            {
                LogService.LogInfo("GitHubSyncEngine: Bulut senkronizasyon eklentisi (github_sync) devre dışı bırakıldığı için senkronizasyon atlandı.");
                ReportProgress(100, "Bulut senkronizasyon eklentisi devre dışı.");
                return;
            }

            int syncInterval = plugin?.GetInt("SyncIntervalHours", 6) ?? 6;
            if (syncInterval <= 0)
            {
                LogService.LogInfo("GitHubSyncEngine: Senkronizasyon aralığı 0 (Kapalı) olarak ayarlandığı için senkronizasyon atlandı.");
                ReportProgress(100, "Bulut senkronizasyonu kullanıcı ayarıyla kapatılmış.");
                return;
            }

            if (System.Threading.Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
            {
                LogService.LogInfo("GitHubSyncEngine: Senkronizasyon zaten çalışıyor, atlandı.");
                return;
            }

            bool syncTv = plugin?.GetBool("SyncTvChannels") ?? true;
            bool syncMovies = plugin?.GetBool("SyncMovies") ?? true;
            bool syncSeries = plugin?.GetBool("SyncSeries") ?? true;
            bool syncRadio = plugin?.GetBool("SyncRadio") ?? true;
            bool syncEpg = plugin?.GetBool("SyncEpg") ?? true;
            bool syncLogos = plugin?.GetBool("SyncLogos") ?? true;
            bool discoverIptv = plugin?.GetBool("DiscoverIptvAccounts") ?? true;

            RaiseSyncStarted();
            DatabaseEngine.SuppressEvents = true;
            LogService.LogInfo("GitHubSyncEngine: Otomatik güncelleme başlatıldı.");
            ReportProgress(2, "Senkronizasyon ayarları ve yayın listesi kontrol ediliyor...");

            try
            {
                // 1. Önce TV aktifse GitHub Actions tarafından oluşturulan derin doğrulanmış tam temizlenmiş master M3U listesini dene
                string cleanM3uUrl = "https://raw.githubusercontent.com/bilo1975tr/sm/refs/heads/main/out/full_cleaned_playlist.m3u";
                bool cleanM3uLoaded = false;

                if (syncTv)
                {
                    try
                    {
                        using var cleanCts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(5));
                        var response = await _httpClient.GetAsync(cleanM3uUrl, cleanCts.Token);
                        if (response.IsSuccessStatusCode)
                        {
                            ReportProgress(15, "Temizlenmiş master TV listesi indiriliyor...");
                            _db.AddM3uSource(cleanM3uUrl);
                            var channels = await _m3u.ParseM3uAsync(cleanM3uUrl, "TV", false, (subMsg, subPct) =>
                            {
                                ReportProgress(20 + (int)(subPct * 0.6), $"Temizlenmiş liste işleniyor: {subMsg}");
                            });

                            if (channels != null && channels.Count > 0)
                            {
                                ReportProgress(75, $"💾 {channels.Count:N0} adet kanal veritabanına yazılıyor...");
                                await _db.SyncIncomingChannelsAsync(channels, (msg, pct) =>
                                {
                                    ReportProgress(75 + (int)(pct * 0.1), msg);
                                });
                                cleanM3uLoaded = true;
                                ReportProgress(85, $"🎉 {channels.Count:N0} adet doğrulanmış ve temizlenmiş kanal eklendi!");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.LogWarning($"Clean M3U yüklenemedi, yedek moda geçiliyor: {ex.Message}");
                    }
                }
                else
                {
                    LogService.LogInfo("GitHubSyncEngine: TV Kanalları senkronizasyonu kullanıcı ayarıyla kapatılmış.");
                }

                // 2. Eğer clean M3U bulunamadıysa veya diğer kategoriler (Film, Dizi, Radyo) isteniyorsa auto_update.json indir
                bool needAdditionalLists = !cleanM3uLoaded || syncMovies || syncSeries || syncRadio;
                if (needAdditionalLists)
                {
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
                            // Derin repo tarayıcısı ile sadece aktif seçilmiş kategorilerdeki kaynakları tara
                            ReportProgress(5, "GitHub depoları taranıyor...");
                            var crawler = new GitHubRepoCrawler();
                            var seedDict = new Dictionary<string, List<string>>();

                            if (syncTv && !cleanM3uLoaded) seedDict["TV"] = cfg.Tv ?? new List<string>();
                            if (syncMovies) seedDict["Film"] = cfg.Film ?? new List<string>();
                            if (syncSeries) seedDict["Dizi"] = cfg.Dizi ?? new List<string>();
                            if (syncRadio) seedDict["Radyo"] = cfg.Radyo ?? new List<string>();

                            if (seedDict.Count > 0)
                            {
                                var discoveredSources = await crawler.CrawlAllAsync(seedDict, (msg, pct) =>
                                {
                                    ReportProgress(pct, msg);
                                });

                                var tvUrls = syncTv && !cleanM3uLoaded ? discoveredSources.Where(x => x.Category.Equals("TV", StringComparison.OrdinalIgnoreCase)).Select(x => x.Url).ToList() : new List<string>();
                                var filmUrls = syncMovies ? discoveredSources.Where(x => x.Category.Equals("Film", StringComparison.OrdinalIgnoreCase)).Select(x => x.Url).ToList() : new List<string>();
                                var diziUrls = syncSeries ? discoveredSources.Where(x => x.Category.Equals("Dizi", StringComparison.OrdinalIgnoreCase)).Select(x => x.Url).ToList() : new List<string>();
                                var radyoUrls = syncRadio ? discoveredSources.Where(x => x.Category.Equals("Radyo", StringComparison.OrdinalIgnoreCase)).Select(x => x.Url).ToList() : new List<string>();

                                int totalSources = tvUrls.Count + filmUrls.Count + diziUrls.Count + radyoUrls.Count;
                                if (totalSources > 0)
                                {
                                    ReportProgress(20, $"🎉 {totalSources} adet kaynak tespit edildi! Kütüphane indirmesi başlatılıyor...");
                                    LogService.LogInfo($"GitHubSyncEngine: Toplam {totalSources} adet kaynak (TV: {tvUrls.Count}, Film: {filmUrls.Count}, Dizi: {diziUrls.Count}, Radyo: {radyoUrls.Count}) işleniyor...");

                                    int processedSources = 0;
                                    var accumulativeChannels = new List<Channel>();

                                    if (tvUrls.Count > 0)
                                    {
                                        await ProcessListWithProgress(tvUrls, "TV", totalSources, () => ++processedSources, true, accumulativeChannels);
                                        if (accumulativeChannels.Count > 0)
                                        {
                                            LogService.LogInfo($"GitHubSyncEngine: Erken TV gösterimi için {accumulativeChannels.Count} TV kanalı kaydediliyor...");
                                            ReportProgress(55, $"📺 {accumulativeChannels.Count:N0} adet TV kanalı kaydediliyor...");
                                            await _db.SyncIncomingChannelsAsync(new List<Channel>(accumulativeChannels));
                                        }
                                    }
                                    if (filmUrls.Count > 0) await ProcessListWithProgress(filmUrls, "Film", totalSources, () => ++processedSources, true, accumulativeChannels);
                                    if (diziUrls.Count > 0) await ProcessListWithProgress(diziUrls, "Dizi", totalSources, () => ++processedSources, true, accumulativeChannels);
                                    if (radyoUrls.Count > 0) await ProcessListWithProgress(radyoUrls, "Radyo", totalSources, () => ++processedSources, true, accumulativeChannels);

                                    if (accumulativeChannels.Count > 0)
                                    {
                                        LogService.LogInfo($"GitHubSyncEngine: Toplam {accumulativeChannels.Count} kanal kaynaklardan çözümlendi. SQLite veritabanına senkronize ediliyor...");
                                        ReportProgress(85, $"💾 {accumulativeChannels.Count:N0} adet yayın SQLite veritabanına kaydediliyor ve birleştiriliyor...");
                                        await _db.SyncIncomingChannelsAsync(accumulativeChannels, (msg, subPct) =>
                                        {
                                            int mappedPct = 85 + (int)(subPct * 0.05);
                                            ReportProgress(mappedPct, msg);
                                        });
                                    }
                                }
                            }

                            // 3. EPG Verilerini İsteğe Bağlı Güncelle
                            if (syncEpg && cfg.Epg != null && cfg.Epg.Count > 0)
                            {
                                ReportProgress(90, "Yayın akışları (EPG rehberi) güncelleniyor...");
                                var epgEng = new EpgEngine();
                                for (int i = 0; i < cfg.Epg.Count; i++)
                                {
                                    string url = cfg.Epg[i];
                                    if (string.IsNullOrWhiteSpace(url)) continue;

                                    _db.AddEpgSource(url);
                                    await epgEng.LoadEpgAsync(url, (subMsg, subPct) =>
                                    {
                                        ReportProgress(90 + (int)(subPct * 0.03), $"EPG Rehberi ({i + 1}/{cfg.Epg.Count}): {subMsg}");
                                    });
                                }
                            }
                            else if (!syncEpg)
                            {
                                LogService.LogInfo("GitHubSyncEngine: EPG rehber güncellemesi kullanıcı ayarıyla kapatılmış.");
                            }
                        }
                    }
                }

                // 4. Logo senkronizasyonunu kontrol et
                if (syncLogos)
                {
                    try
                    {
                        ReportProgress(93, "Kanal logoları kontrol ediliyor...");
                        var logoSync = new LogoSyncService();
                        await logoSync.SyncIfNecessaryAsync();
                    }
                    catch (Exception logoEx)
                    {
                        LogService.LogWarning($"GitHubSyncEngine: LogoSync tetikleme uyarısı: {logoEx.Message}");
                    }
                }
                else
                {
                    LogService.LogInfo("GitHubSyncEngine: Logo senkronizasyonu kullanıcı ayarıyla kapatılmış.");
                }

                // 5. IPTV SAĞLAYICILARI VE HESAPLARI TARAMA & DOĞRULAMA
                if (discoverIptv)
                {
                    try
                    {
                        ReportProgress(95, "IPTV sağlayıcıları taranıyor ve doğrulanıyor...");
                        var accounts = await _db.DiscoverAndSyncAccountsFromChannelsAsync((msg, pct) =>
                        {
                            ReportProgress((int)pct, msg);
                        }).ConfigureAwait(false);

                        if (accounts != null && accounts.Count > 0)
                        {
                            ReportProgress(99, $"✔ {accounts.Count} adet IPTV sağlayıcısı bağlandı.");
                        }
                    }
                    catch (Exception iptvEx)
                    {
                        LogService.LogWarning($"GitHubSyncEngine: IPTV sağlayıcı keşfi uyarısı: {iptvEx.Message}");
                    }
                }
                else
                {
                    LogService.LogInfo("GitHubSyncEngine: IPTV hesap keşfi kullanıcı ayarıyla kapatılmış.");
                }

                var finalCount = await _db.GetTotalChannelCountAsync();
                LogService.LogInfo($"GitHubSyncEngine: Senkronizasyon sonu SQLite veritabanında toplam {finalCount} kanal mevcut.");

                DatabaseEngine.NotifyDatabaseUpdated();
                LogService.LogInfo("GitHubSyncEngine: Güncelleme tamamlandı.");
                ReportProgress(100, $"🎉 Kütüphane hazırlandı! Toplam {finalCount:N0} içerik kullanıma hazır.");
            }
            catch (Exception ex)
            {
                LogService.LogError("GitHubSyncEngine error", ex);
                ReportProgress(0, $"Hata oluştu: {ex.Message}");
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _isRunning, 0);
                OnSyncCompleted?.Invoke();
                DatabaseEngine.SuppressEvents = false;
            }
        }

        private async Task ProcessListWithProgress(List<string> urls, string categoryLabel, int totalSources, Func<int> incrementCounter, bool forceCategory, List<Channel> collector)
        {
            if (urls == null || urls.Count == 0) return;

            var validUrls = urls.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
            if (validUrls.Count == 0) return;

            // Çoklu indirme: Ağ bant genişliğini ve CPU'yu verimli kullanarak kaynakları paralel çek
            int maxParallelism = Math.Min(20, Math.Max(8, Environment.ProcessorCount * 2));
            using var semaphore = new System.Threading.SemaphoreSlim(maxParallelism, maxParallelism);
            var lockObj = new object();

            var tasks = validUrls.Select(async (url, i) =>
            {
                await semaphore.WaitAsync();
                int currentIdx = incrementCounter();
                double baseProgress = 20.0 + ((double)(currentIdx - 1) / Math.Max(1, totalSources)) * 65.0;
                double itemWeight = 65.0 / Math.Max(1, totalSources);
                double finishedPct = Math.Min(85.0, baseProgress + itemWeight);

                string cleanUrl = url.Trim();
                string shortName = MediaHttpClient.GetShortUrl(cleanUrl);

                if (cleanUrl.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                    cleanUrl.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                    cleanUrl.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                    cleanUrl.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    cleanUrl.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    cleanUrl.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                    cleanUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    cleanUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    ReportProgress((int)finishedPct, $"[{currentIdx}/{totalSources}] {categoryLabel}: Dosya atlandı ({shortName})");
                    semaphore.Release();
                    return;
                }

                try
                {
                    var channels = await _m3u.ParseM3uAsync(cleanUrl, categoryLabel, forceCategory, (subMsg, subPct) =>
                    {
                        double overallPct = Math.Min(85.0, baseProgress + (subPct / 100.0) * itemWeight);
                        ReportProgress((int)overallPct, $"[{currentIdx}/{totalSources}] {categoryLabel}: {subMsg}");
                    });

                    if (channels != null && channels.Count > 0)
                    {
                        lock (lockObj)
                        {
                            _db.AddM3uSource(cleanUrl);
                            collector.AddRange(channels);
                        }
                        ReportProgress((int)finishedPct, $"[{currentIdx}/{totalSources}] {categoryLabel}: {channels.Count:N0} içerik çözümlendi ({shortName})");
                    }
                    else
                    {
                        ReportProgress((int)finishedPct, $"[{currentIdx}/{totalSources}] {categoryLabel}: {shortName} tamamlandı (boş liste)");
                    }
                }
                catch (Exception ex)
                {
                    ReportProgress((int)finishedPct, $"[{currentIdx}/{totalSources}] {categoryLabel}: {shortName} ({ex.Message})");
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }
    }
}
