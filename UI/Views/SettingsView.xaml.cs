using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media;
using StreamMesh.Core.Utils;
using StreamMesh.Models;
using StreamMesh.Core.Network;
using StreamMesh.UI.Windows;
using System.Linq;
using System.Threading.Tasks;

using Button = System.Windows.Controls.Button;

namespace StreamMesh.UI.Views
{
    public class M3uSourceDisplay
    {
        public string Url { get; set; } = "";
        public string Origin { get; set; } = "Yerel";
        public string Color { get; set; } = "#1e293b";
        public int ChannelCount { get; set; } = 0;
    }

    public partial class SettingsView : System.Windows.Controls.UserControl
    {
        private readonly DatabaseEngine _db = new DatabaseEngine();
        private readonly GitHubSyncEngine _sync = new GitHubSyncEngine();
        private readonly AiEngine _ai = new AiEngine();
        private readonly XtreamService _xtream = new XtreamService();

        public ObservableCollection<M3uSourceDisplay> Sources { get; set; } = new ObservableCollection<M3uSourceDisplay>();
        public ObservableCollection<IptvAccount> IptvAccounts { get; set; } = new ObservableCollection<IptvAccount>();
        public ObservableCollection<string> ValidationLogs { get; set; } = new ObservableCollection<string>();

        private bool _isServerRunning = false;
        private System.Threading.CancellationTokenSource? _validationCts;

        public SettingsView()
        {
            InitializeComponent();
            LoadSettings();
            if (ValidationLogsList != null) ValidationLogsList.ItemsSource = ValidationLogs;

            _sync.OnProgress += (p, msg) => {
                Dispatcher.Invoke(() => {
                    if (SyncProgress != null) SyncProgress.Value = p;
                    if (SyncStatusText != null) SyncStatusText.Text = msg;
                    if (p >= 100 || msg.StartsWith("Hata") || msg.StartsWith("🎉"))
                    {
                        if (StartSyncBtn != null) StartSyncBtn.IsEnabled = true;
                        RefreshSourcesList();
                        RefreshEpgList();
                    }
                });
            };
        }

        private void LoadSettings()
        {
            if (AiUrlBox != null) AiUrlBox.Text = _db.GetSetting("AiUrl", "http://localhost:11434/api/chat");
            if (AiModelBox != null) AiModelBox.Text = _db.GetSetting("AiModel", "llama3");
            if (TmdbApiKeyBox != null) TmdbApiKeyBox.Text = _db.GetSetting("TmdbApiKey", "3fd2be6f0c70a2a598f084dd23308883");
            if (CachingBox != null) CachingBox.Text = _db.GetSetting("FlyleafCache", "1000");
            if (HwAccelCheck != null) HwAccelCheck.IsChecked = _db.GetSetting("FlyleafHwAccel", "true") == "true";
            if (ServerPortBox != null) ServerPortBox.Text = _db.GetSetting("ServerPort", "8080");

            RefreshSourcesList();
            RefreshEpgList();
            RefreshIptvList();
            UpdateQuotaUI();
            UpdateServerStatusUI();
        }

        private void RefreshSourcesList()
        {
            Sources.Clear();
            var list = _db.GetM3uSources();
            foreach (var s in list)
            {
                bool isCloud = s.Contains("github") || s.Contains("raw.githubusercontent");
                Sources.Add(new M3uSourceDisplay {
                    Url = s, Origin = isCloud ? "Bulut" : "Yerel",
                    Color = isCloud ? "#0369a1" : "#1e293b",
                    ChannelCount = _db.GetChannelCountBySource(s)
                });
            }
            if (M3uSourcesList != null)
            {
                M3uSourcesList.ItemsSource = null;
                M3uSourcesList.ItemsSource = Sources;
            }
        }

        private void RefreshEpgList()
        {
            var epgs = _db.GetEpgSources();
            var displayList = epgs.Select(url => new {
                Url = url,
                Origin = (url.Contains("github") || url.Contains("raw.githubusercontent")) ? "Bulut" : "Yerel",
                Color = (url.Contains("github") || url.Contains("raw.githubusercontent")) ? "#0369a1" : "#1e293b"
            }).ToList();
            if (EpgSourcesList != null) EpgSourcesList.ItemsSource = displayList;
        }

        private void RefreshIptvList()
        {
            IptvAccounts.Clear();
            var list = _db.GetAllIptvAccounts();
            foreach (var a in list) IptvAccounts.Add(a);
            if (IptvAccountsList != null) IptvAccountsList.ItemsSource = IptvAccounts;
        }

        private async void AddIptvAccount_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(XtreamUrlBox.Text)) return;
            try
            {
                var acc = new IptvAccount {
                    ServerUrl = XtreamUrlBox.Text,
                    Username = XtreamUserBox.Text,
                    Password = XtreamPassBox.Text,
                    Name = new Uri(XtreamUrlBox.Text).Host
                };
                acc.Status = "Bağlanıyor...";
                _db.SaveIptvAccount(acc);
                RefreshIptvList();
                bool success = await _xtream.SyncAccountAsync(acc);
                RefreshIptvList();
                if (success) System.Windows.MessageBox.Show("IPTV Hesabı başarıyla eklendi.");
            }
            catch (Exception ex) { System.Windows.MessageBox.Show("Hata: " + ex.Message); }
        }

        private void RemoveIptv_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.CommandParameter is string id)
            {
                _db.RemoveIptvAccount(id);
                RefreshIptvList();
            }
        }

        private void ToggleServer_Click(object sender, RoutedEventArgs e)
        {
            if (!_isServerRunning)
            {
                int port = int.TryParse(ServerPortBox.Text, out int p) ? p : 8080;
                _db.SetSetting("ServerPort", port.ToString());
                StreamMesh.App.Server?.Start();
                _isServerRunning = true;
            }
            else
            {
                StreamMesh.App.Server?.Stop();
                _isServerRunning = false;
            }
            UpdateServerStatusUI();
        }

        private void UpdateServerStatusUI()
        {
            if (ServerControlBtn == null) return;
            ServerControlBtn.Content = _isServerRunning ? "Sunucuyu Durdur" : "Sunucuyu Başlat";
            string ip = "127.0.0.1";
            string port = ServerPortBox.Text;
            if (M3uServerLink != null) M3uServerLink.Text = $"http://{ip}:{port}/playlist.m3u";
            if (WebServerLink != null) WebServerLink.Text = $"http://{ip}:{port}/web";
        }

        private void EditSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.CommandParameter is string url)
            {
                var win = new SourceEditWindow(url);
                win.Owner = Window.GetWindow(this);
                win.ShowDialog();
                RefreshSourcesList();
            }
        }

        private void AddEpgSource_Click(object sender, RoutedEventArgs e)
        {
            string url = EpgUrlBox.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(url))
            {
                _db.AddEpgSource(url);
                EpgUrlBox.Clear();
                RefreshEpgList();
                Task.Run(() => new EpgEngine().LoadEpgAsync(url));
            }
        }

        private void RemoveEpgSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.CommandParameter is string url)
            {
                _db.RemoveEpgSource(url);
                RefreshEpgList();
            }
        }

        private void UpdateQuotaUI()
        {
            var stats = _db.GetDailyQueryStats();
            if (TmdbQuotaText != null) TmdbQuotaText.Text = $"{stats.count} / 1000";
        }

        private void AiProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AiUrlBox == null) return;
            if (AiProviderCombo.SelectedIndex == 0) AiUrlBox.Text = "http://localhost:11434/api/chat";
            else if (AiProviderCombo.SelectedIndex == 1) AiUrlBox.Text = "http://localhost:1234/v1/chat/completions";
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            _db.SetSetting("AiUrl", AiUrlBox.Text);
            _db.SetSetting("AiModel", AiModelBox.Text);
            _db.SetSetting("TmdbApiKey", TmdbApiKeyBox.Text);
            _db.SetSetting("FlyleafCache", CachingBox.Text);
            _db.SetSetting("FlyleafHwAccel", HwAccelCheck.IsChecked == true ? "true" : "false");
            System.Windows.MessageBox.Show("Ayarlar kaydedildi. (Uygulamayı yeniden başlatmanız gerekebilir)");
        }

        private async void AddSource_Click(object sender, RoutedEventArgs e)
        {
            string url = M3uUrlBox.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(url))
            {
                _db.AddM3uSource(url);
                M3uUrlBox.Clear();
                RefreshSourcesList();
                var channels = await new M3uEngine().ParseM3uAsync(url);
                if (channels != null && channels.Count > 0)
                {
                    await _db.SyncIncomingChannelsAsync(channels);
                    RefreshSourcesList();
                }
            }
        }

        private void RemoveSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.CommandParameter is string url)
            {
                _db.RemoveM3uSource(url);
                RefreshSourcesList();
            }
        }

        private async void FetchModels_Click(object sender, RoutedEventArgs e)
        {
            var result = await _ai.AutoDetectAndConfigureAsync();
            if (result.success && result.models.Count > 0)
            {
                AiModelBox.Text = result.model;
                AiUrlBox.Text = result.url;
                if (AiProviderCombo != null)
                {
                    AiProviderCombo.SelectedIndex = result.provider == "LM Studio" ? 1 : 0;
                }
                System.Windows.MessageBox.Show($"✅ {result.provider} Servisi Algılandı!\nSeçilen Model: {result.model}\nMevcut Modeller: {string.Join(", ", result.models)}", "Yapay Zeka Hazır");
            }
            else
            {
                System.Windows.MessageBox.Show("Yerel AI sunucusuna bağlanılamadı.\nLütfen Ollama (11434) veya LM Studio (1234) uygulamasının çalıştığından ve bir model yüklü olduğundan emin olun.", "AI Servisi Bulunamadı");
            }
        }

        private async void StartCloudSync_Click(object sender, RoutedEventArgs e)
        {
            if (StartSyncBtn != null) StartSyncBtn.IsEnabled = false;
            await Task.Run(async () => await _sync.PullFromGitHubAsync());
        }

        private void ClearSources_Click(object sender, RoutedEventArgs e)
        {
            if (System.Windows.MessageBox.Show("Tüm M3U ve EPG XML yayın kaynakları silinecek. Emin misiniz?", "🚨 Kaynakları Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _db.ClearAllSources();
                RefreshSourcesList();
                RefreshEpgList();
                System.Windows.MessageBox.Show("Tüm yayın ve EPG kaynakları silindi.", "Bilgi");
            }
        }

        private void ClearContents_Click(object sender, RoutedEventArgs e)
        {
            if (System.Windows.MessageBox.Show("Tüm kanallar, filmler, diziler ve EPG yayın akışı verileri silinecek. Emin misiniz?", "🚨 İçerikleri Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _db.ClearAllContents();
                RefreshSourcesList();
                RefreshEpgList();
                System.Windows.MessageBox.Show("Tüm kütüphane içerikleri silindi.", "Bilgi");
            }
        }

        private async void StartValidation_Click(object sender, RoutedEventArgs e)
        {
            if (_validationCts != null) return;

            var allChannels = await _db.GetAllChannelsAsync();
            if (allChannels.Count == 0)
            {
                System.Windows.MessageBox.Show("Test edilecek kanal bulunamadı.");
                return;
            }

            bool onlyUnverified = CheckOnlyUnverified.IsChecked == true;
            var channelsToTest = onlyUnverified ? allChannels.Where(c => !c.IsVerified).ToList() : allChannels;

            if (channelsToTest.Count == 0)
            {
                System.Windows.MessageBox.Show("Test edilecek taranmamış kanal bulunamadı. (Tüm kanallar önceden taranmış ve doğrulanmış)");
                return;
            }

            // Interleave channels by host key so adjacent queue items belong to different servers/hosts
            var groupedByHost = channelsToTest
                .GroupBy(GetChannelHostKey)
                .Select(g => new Queue<Channel>(g))
                .ToList();

            var interleavedChannels = new List<Channel>();
            while (groupedByHost.Count > 0)
            {
                for (int i = groupedByHost.Count - 1; i >= 0; i--)
                {
                    var queue = groupedByHost[i];
                    if (queue.Count > 0)
                    {
                        interleavedChannels.Add(queue.Dequeue());
                    }
                    if (queue.Count == 0)
                    {
                        groupedByHost.RemoveAt(i);
                    }
                }
            }
            channelsToTest = interleavedChannels;

            int concurrency = 5;
            if (ComboConcurrency.SelectedItem is ComboBoxItem comboItem && comboItem.Content != null)
            {
                string text = comboItem.Content.ToString() ?? "";
                var match = System.Text.RegularExpressions.Regex.Match(text, @"\d+");
                if (match.Success && int.TryParse(match.Value, out int parsed))
                {
                    concurrency = Math.Max(1, parsed);
                }
            }

            ValidationLogs.Clear();
            ValidationProgressBar.Value = 0;
            ValidationProgressBar.Maximum = channelsToTest.Count;
            StartValidationBtn.IsEnabled = false;
            StopValidationBtn.Visibility = Visibility.Visible;
            ValidationFailedText.Visibility = Visibility.Visible;
            ValidationFailedText.Text = "Sinyal Yok: 0";
            _validationCts = new System.Threading.CancellationTokenSource();

            ValidationLevel level = ValidationLevel.Fast;
            if (RadioDetailed.IsChecked == true) level = ValidationLevel.Detailed;
            if (RadioFull.IsChecked == true) level = ValidationLevel.Full;

            var startTime = DateTime.Now;
            int processed = 0;
            int online = 0;
            var deadChannelIds = new System.Collections.Concurrent.ConcurrentBag<string>();
            var updatedChannels = new System.Collections.Concurrent.ConcurrentBag<Channel>();
            var logQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();

            ValidationLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - 🚀 Test başlatıldı: {channelsToTest.Count} kanal ({concurrency} eşzamanlı iş parçacığı)");

            var token = _validationCts.Token;

            // Background UI throttler task
            using var uiCts = new System.Threading.CancellationTokenSource();
            var uiUpdaterTask = Task.Run(async () =>
            {
                while (!uiCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(250).ConfigureAwait(false);

                    var logItems = new List<string>();
                    while (logQueue.TryDequeue(out var item)) logItems.Add(item);

                    int curProcessed = processed;
                    int curOnline = online;
                    int curDead = deadChannelIds.Count;

                    Dispatcher.Invoke(() =>
                    {
                        if (logItems.Count > 0)
                        {
                            foreach (var item in logItems)
                            {
                                ValidationLogs.Insert(0, item);
                            }
                            while (ValidationLogs.Count > 500) ValidationLogs.RemoveAt(ValidationLogs.Count - 1);
                        }

                        ValidationProgressBar.Value = curProcessed;
                        var elapsed = DateTime.Now - startTime;
                        double avgMs = curProcessed > 0 ? elapsed.TotalMilliseconds / curProcessed : 0;
                        var remaining = curProcessed > 0 ? TimeSpan.FromMilliseconds((avgMs * (channelsToTest.Count - curProcessed)) / concurrency) : TimeSpan.Zero;

                        ValidationProgressText.Text = $"İşlem: {curProcessed}/{channelsToTest.Count} (Aktif: {curOnline})";
                        ValidationFailedText.Text = $"Sinyal Yok: {curDead}";
                        ValidationTimeText.Text = $"Geçen: {elapsed:mm\\:ss} / Kalan: {remaining:mm\\:ss}";
                    });
                }
            });

            try
            {
                // Run parallel validation in background thread with per-host lock protection
                await Task.Run(async () =>
                {
                    using var globalSemaphore = new System.Threading.SemaphoreSlim(concurrency, concurrency);
                    var hostLocks = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.SemaphoreSlim>();

                    var tasks = channelsToTest.Select(async ch =>
                    {
                        if (token.IsCancellationRequested) return;

                        string hostKey = GetChannelHostKey(ch);
                        var hostSemaphore = hostLocks.GetOrAdd(hostKey, _ => new System.Threading.SemaphoreSlim(1, 1));

                        await globalSemaphore.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            if (token.IsCancellationRequested) return;

                            await hostSemaphore.WaitAsync().ConfigureAwait(false);
                            try
                            {
                                if (token.IsCancellationRequested) return;

                                using var validator = new StreamValidator();
                                var result = await validator.ValidateAsync(ch, level, null).ConfigureAwait(false);

                                if (result.IsOnline)
                                {
                                    System.Threading.Interlocked.Increment(ref online);
                                    ch.IsVerified = true;

                                    string techInfo = "";
                                    if (!string.IsNullOrEmpty(result.Resolution)) techInfo += $"Res: {result.Resolution} ";
                                    if (!string.IsNullOrEmpty(result.VideoCodec)) techInfo += $"Vid: {result.VideoCodec} ";
                                    if (!string.IsNullOrEmpty(result.AudioCodec)) techInfo += $"Aud: {result.AudioCodec}";

                                    if (!string.IsNullOrEmpty(techInfo))
                                    {
                                        ch.Notes = techInfo;
                                        logQueue.Enqueue($"{DateTime.Now:HH:mm:ss} - ✅ {ch.PrimaryName} -> {techInfo}");
                                    }
                                    else
                                    {
                                        logQueue.Enqueue($"{DateTime.Now:HH:mm:ss} - ✅ {ch.PrimaryName} Aktif");
                                    }
                                }
                                else
                                {
                                    ch.IsVerified = false;
                                    deadChannelIds.Add(ch.Id);
                                    logQueue.Enqueue($"{DateTime.Now:HH:mm:ss} - ❌ {ch.PrimaryName} Yanıt Vermedi: {result.Status}");
                                }

                                updatedChannels.Add(ch);
                                System.Threading.Interlocked.Increment(ref processed);
                            }
                            finally
                            {
                                hostSemaphore.Release();
                            }
                        }
                        catch (Exception ex)
                        {
                            logQueue.Enqueue($"{DateTime.Now:HH:mm:ss} - ⚠️ {ch.PrimaryName} Hata: {ex.Message}");
                        }
                        finally
                        {
                            globalSemaphore.Release();
                        }
                    });

                    await Task.WhenAll(tasks).ConfigureAwait(false);
                });
            }
            catch (Exception ex)
            {
                logQueue.Enqueue($"{DateTime.Now:HH:mm:ss} - ❌ Kritik Hata: {ex.Message}");
            }
            finally
            {
                // Stop UI updater loop
                uiCts.Cancel();
                try { await uiUpdaterTask; } catch { }

                // Final UI log flush
                var remainingLogs = new List<string>();
                while (logQueue.TryDequeue(out var item)) remainingLogs.Add(item);
                if (remainingLogs.Count > 0)
                {
                    foreach (var item in remainingLogs) ValidationLogs.Insert(0, item);
                    while (ValidationLogs.Count > 500) ValidationLogs.RemoveAt(ValidationLogs.Count - 1);
                }

                // Save batch to database quietly
                if (updatedChannels.Count > 0)
                {
                    DatabaseEngine.SuppressEvents = true;
                    try
                    {
                        await _db.SaveChannelsBatchAsync(updatedChannels.ToList());
                    }
                    finally
                    {
                        DatabaseEngine.SuppressEvents = false;
                    }
                }

                ValidationProgressText.Text = $"Tamamlandı: {processed}/{channelsToTest.Count} (Aktif: {online})";
                ValidationLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - 🎉 İşlem tamamlandı. Toplam: {processed}, Aktif: {online}, Sorunlu: {deadChannelIds.Count}");

                if (deadChannelIds.Count > 0 && !token.IsCancellationRequested)
                {
                    var result = System.Windows.MessageBox.Show(
                        $"{deadChannelIds.Count} adet yanıt vermeyen kanal tespit edildi. Bu kanalları veritabanından silmek istiyor musunuz?",
                        "Kanal Temizliği",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        await _db.DeleteChannelsAsync(deadChannelIds.ToList());
                        System.Windows.MessageBox.Show($"{deadChannelIds.Count} kanal silindi.", "Başarılı");
                    }
                }

                StartValidationBtn.IsEnabled = true;
                StopValidationBtn.Visibility = Visibility.Collapsed;
                _validationCts = null;
            }
        }

        private void StopValidation_Click(object sender, RoutedEventArgs e)
        {
            _validationCts?.Cancel();
            ValidationLogs.Insert(0, "🛑 Durdurma istendi...");
        }

        private static string GetChannelHostKey(Channel ch)
        {
            string url = ch.GetUrlList().FirstOrDefault() ?? "";
            if (string.IsNullOrWhiteSpace(url)) return "unknown";

            if (url.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("PID:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("PID=", StringComparison.OrdinalIgnoreCase))
            {
                return "acestream_engine";
            }

            try
            {
                var uri = new Uri(url);
                if (uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    return "acestream_engine";
                }
                return $"{uri.Host}:{uri.Port}".ToLowerInvariant();
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
