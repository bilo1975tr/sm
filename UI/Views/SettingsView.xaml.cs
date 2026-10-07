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
using StreamMesh.UI.ViewModels;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using Button = System.Windows.Controls.Button;

namespace StreamMesh.UI.Views
{
    public partial class SettingsView : System.Windows.Controls.UserControl
    {
        public SettingsViewModel ViewModel { get; } = new SettingsViewModel();
        private readonly DatabaseEngine _db = new DatabaseEngine();
        private readonly AiEngine _ai = new AiEngine();
        private readonly XtreamService _xtream = new XtreamService();

        private bool _isServerRunning = false;
        private System.Threading.CancellationTokenSource? _validationCts;

        public SettingsView()
        {
            InitializeComponent();
            DataContext = ViewModel;
            if (ValidationLogsList != null) ValidationLogsList.ItemsSource = ViewModel.ValidationLogs;
            if (AiLogsList != null) AiLogsList.ItemsSource = ViewModel.AiLogs;

            ViewModel.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(ViewModel.SyncStatus)) {
                    Dispatcher.Invoke(() => {
                        if (SyncProgress != null) SyncProgress.Value = ViewModel.SyncProgress;
                        if (SyncStatusText != null) SyncStatusText.Text = ViewModel.SyncStatus;
                        if (ViewModel.SyncProgress >= 100) StartSyncBtn.IsEnabled = true;
                    });
                }
            };
            this.Loaded += (s, e) => LoadSettings();
        }

        private void LoadSettings()
        {
            ViewModel.LoadSettings();
            if (CachingBox != null) CachingBox.Text = _db.GetSetting("FlyleafCache", "1000");
            if (HwAccelCheck != null) HwAccelCheck.IsChecked = _db.GetSetting("FlyleafHwAccel", "true") == "true";
            if (AudioNormCheck != null) AudioNormCheck.IsChecked = _db.GetSetting("AudioNormEnabled", "true") == "true";
            if (VideoSharpenCheck != null) VideoSharpenCheck.IsChecked = _db.GetSetting("VideoEnhanceEnabled", "false") == "true";

            UpdateQuotaUI();
            UpdateServerStatusUI();
            RefreshEpgList();
        }

        private void RefreshSourcesList() => ViewModel.RefreshSources();
        private void RefreshIptvList() => ViewModel.RefreshIptvAccounts();

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

        private async void AddIptvAccount_Click(object sender, RoutedEventArgs e)
        {
            string url = XtreamUrlBox.Text?.Trim() ?? "";
            string user = XtreamUserBox.Text?.Trim() ?? "";
            string pass = XtreamPassBox.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(url))
            {
                System.Windows.MessageBox.Show("Lütfen Sunucu URL adresini girin.", "Eksik Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "http://" + url;
            }

            try
            {
                var uri = new Uri(url);
                var acc = new IptvAccount {
                    ServerUrl = url,
                    Username = user,
                    Password = pass,
                    Name = uri.Host
                };
                acc.Status = "Bağlanıyor...";
                _db.SaveIptvAccount(acc);
                RefreshIptvList();

                bool success = await _xtream.SyncAccountAsync(acc);
                RefreshIptvList();

                if (success)
                {
                    System.Windows.MessageBox.Show($"IPTV Hesabı ({acc.Name}) başarıyla eklendi ve doğrulandı!", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                    XtreamUrlBox.Clear();
                    XtreamUserBox.Clear();
                    XtreamPassBox.Clear();
                }
                else
                {
                    System.Windows.MessageBox.Show($"Hesap eklendi ancak sunucuya bağlanılamadı veya giriş başarısız oldu.\nDurum: {acc.Status}", "Bağlantı Uyarısı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Hata: " + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveIptv_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.CommandParameter is string id)
            {
                _db.RemoveIptvAccount(id);
                RefreshIptvList();
            }
        }

        private async void RefreshIptvStats_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
            {
                btn.IsEnabled = false;
                btn.Content = "⏳ Güncelleniyor...";
            }

            try
            {
                var accounts = _db.GetAllIptvAccounts();
                foreach (var acc in accounts)
                {
                    try
                    {
                        await _xtream.SyncAccountAsync(acc);
                    }
                    catch { }
                }
                RefreshIptvList();
                System.Windows.MessageBox.Show("Tüm IPTV hesaplarının kalan gün süreleri, eşzamanlı bağlantı limitleri ve kanal sayıları güncellendi!", "Güncelleme Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                if (btn != null)
                {
                    btn.IsEnabled = true;
                    btn.Content = "🔄 Bilgileri ve İstatistikleri Güncelle";
                }
            }
        }

        private void ToggleServer_Click(object sender, RoutedEventArgs e)
        {
            if (!_isServerRunning)
            {
                int port = int.TryParse(ServerPortBox.Text, out int p) ? p : 8080;
                ViewModel.ServerPort = port.ToString();
                _db.SetSetting("ServerPort", port.ToString());

                if (StreamMesh.App.Server == null || StreamMesh.App.Server.Port != port)
                {
                    StreamMesh.App.Server?.Stop();
                    StreamMesh.App.Server = new StreamMesh.Core.Network.MediaServer(port);
                }

                bool started = StreamMesh.App.Server != null && StreamMesh.App.Server.Start();
                _isServerRunning = started && (StreamMesh.App.Server?.IsRunning == true);
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

            if (StreamMesh.App.Server != null)
            {
                _isServerRunning = StreamMesh.App.Server.IsRunning;
            }

            ServerControlBtn.Content = _isServerRunning ? "Sunucuyu Durdur" : "Sunucuyu Başlat";

            // Resolve real LAN IP (Ethernet/Wi-Fi with Gateway priority, fallback to 127.0.0.1)
            string lanIp = StreamMesh.Core.Network.MediaServer.GetPrimaryLocalIPv4Address();
            int currentPort = StreamMesh.App.Server?.Port ?? (int.TryParse(ServerPortBox?.Text, out int p) ? p : 8080);
            string portStr = currentPort.ToString();

            if (ServerPortBox != null && ServerPortBox.Text != portStr)
            {
                ServerPortBox.Text = portStr;
            }

            if (M3uServerLink != null) M3uServerLink.Text = $"http://{lanIp}:{portStr}/playlist.m3u";
            if (M3uDirectServerLink != null) M3uDirectServerLink.Text = $"http://{lanIp}:{portStr}/direct.m3u";
            if (WebServerLink != null) WebServerLink.Text = $"http://{lanIp}:{portStr}/web";
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
            if (AiProviderCombo.SelectedIndex == 0) ViewModel.AiUrl = "http://localhost:11434/api/chat";
            else if (AiProviderCombo.SelectedIndex == 1) ViewModel.AiUrl = "http://localhost:1234/v1/chat/completions";
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SaveSettings();
            _db.SetSetting("FlyleafCache", CachingBox.Text);
            _db.SetSetting("FlyleafHwAccel", HwAccelCheck.IsChecked == true ? "true" : "false");
            _db.SetSetting("AudioNormEnabled", AudioNormCheck?.IsChecked == true ? "true" : "false");
            _db.SetSetting("VideoEnhanceEnabled", VideoSharpenCheck?.IsChecked == true ? "true" : "false");
            System.Windows.MessageBox.Show("Ayarlar kaydedildi. (Oynatıcı filtresi anında güncellendi)");
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

        private void SetDefaultSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.CommandParameter is string url)
            {
                _db.SetDefaultM3uSource(url);
                RefreshSourcesList();
                System.Windows.MessageBox.Show($"'{url}' başarıyla varsayılan yayın kaynağı olarak ayarlandı.", "Varsayılan Kaynak Güncellendi", MessageBoxButton.OK, MessageBoxImage.Information);
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
                ViewModel.AiModel = result.model;
                ViewModel.AiUrl = result.url;
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
            await ViewModel.StartCloudSyncAsync();
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

            int totalToTest = await _db.GetTotalChannelCountAsync();
            if (totalToTest == 0)
            {
                System.Windows.MessageBox.Show("Test edilecek kanal bulunamadı.");
                return;
            }

            bool onlyUnverified = CheckOnlyUnverified.IsChecked == true;

            int concurrency = 5;
            if (ComboConcurrency.SelectedItem is ComboBoxItem comboItem && comboItem.Content != null)
            {
                string text = comboItem.Content.ToString() ?? "";
                var match = System.Text.RegularExpressions.Regex.Match(text, @"\d+");
                if (match.Success && int.TryParse(match.Value, out int parsed)) concurrency = Math.Max(1, parsed);
            }

            ViewModel.ValidationLogs.Clear();
            ValidationProgressBar.Value = 0;
            ValidationProgressBar.Maximum = totalToTest;
            StartValidationBtn.IsEnabled = false;
            StopValidationBtn.Visibility = Visibility.Visible;
            _validationCts = new System.Threading.CancellationTokenSource();

            ValidationLevel level = ValidationLevel.Fast;
            if (RadioDetailed.IsChecked == true) level = ValidationLevel.Detailed;
            if (RadioFull.IsChecked == true) level = ValidationLevel.Full;
            bool autoPurgeDeadAccounts = CheckFastDeadAccountPurge?.IsChecked == true;

            var startTime = DateTime.Now;
            int processed = 0;
            int online = 0;
            int deleted = 0;
            var deadChannelIds = new System.Collections.Concurrent.ConcurrentBag<string>();
            var updatedChannels = new System.Collections.Concurrent.ConcurrentBag<Channel>();
            var logQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var deleteQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();

            var accountFailureTracker = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
            var purgedAccountSignatures = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();

            ViewModel.ValidationLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - 🚀 Test başlatıldı: Toplam {totalToTest} kanal ({concurrency} iş parçacığı - Batch/SQL Paged)");

            var token = _validationCts.Token;

            using var uiCts = new System.Threading.CancellationTokenSource();
            var uiUpdaterTask = Task.Run(async () =>
            {
                while (!uiCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(200).ConfigureAwait(false);
                    var logItems = new List<string>();
                    while (logQueue.TryDequeue(out var item)) logItems.Add(item);

                    int curProcessed = processed;
                    int curOnline = online;
                    int curDeleted = deleted;
                    int curRemaining = Math.Max(0, totalToTest - curProcessed);

                    Dispatcher.Invoke(() =>
                    {
                        if (logItems.Count > 0)
                        {
                            foreach (var item in logItems) ViewModel.ValidationLogs.Insert(0, item);
                            while (ViewModel.ValidationLogs.Count > 500) ViewModel.ValidationLogs.RemoveAt(ViewModel.ValidationLogs.Count - 1);
                        }

                        ValidationProgressBar.Value = curProcessed;
                        var elapsed = DateTime.Now - startTime;
                        double avgMs = curProcessed > 0 ? elapsed.TotalMilliseconds / curProcessed : 0;
                        var remaining = curProcessed > 0 ? TimeSpan.FromMilliseconds((avgMs * curRemaining) / concurrency) : TimeSpan.Zero;

                        ValidationHealthyText.Text = $"✅ Sağlam: {curOnline}";
                        ValidationDeletedText.Text = $"🗑️ Silinen (Bozuk): {curDeleted}";
                        ValidationPendingText.Text = $"⏳ Kalan: {curRemaining}";
                        ValidationTotalText.Text = $"(Toplam: {totalToTest})";
                        ValidationTimeText.Text = $"Geçen: {elapsed:mm\\:ss} / Kalan: {remaining:mm\\:ss}";
                    });
                }
            });

            var deleteWorkerTask = Task.Run(async () =>
            {
                while (!uiCts.Token.IsCancellationRequested || !deleteQueue.IsEmpty)
                {
                    var batchToDelete = new List<string>();
                    while (batchToDelete.Count < 20 && deleteQueue.TryDequeue(out var deadId))
                    {
                        batchToDelete.Add(deadId);
                    }

                    if (batchToDelete.Count > 0)
                    {
                        try
                        {
                            DatabaseEngine.SuppressEvents = true;
                            await _db.DeleteChannelsAsync(batchToDelete).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            LogService.LogWarning($"Validation delete batch failed: {ex.Message}");
                        }
                        finally
                        {
                            DatabaseEngine.SuppressEvents = false;
                        }
                    }
                    else
                    {
                        await Task.Delay(150).ConfigureAwait(false);
                    }
                }
            });

            try
            {
                await Task.Run(async () =>
                {
                    using var globalSemaphore = new System.Threading.SemaphoreSlim(concurrency, concurrency);
                    var hostLocks = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.SemaphoreSlim>();
                    var urlCache = new System.Collections.Concurrent.ConcurrentDictionary<string, StreamMesh.Core.Utils.ValidationResult>();

                    int page = 1;
                    int batchChunkSize = 500;

                    while (!token.IsCancellationRequested)
                    {
                        var (chunk, count) = await _db.GetPagedChannelsAsync(null, "All", null, null, 0, page, batchChunkSize).ConfigureAwait(false);
                        if (chunk == null || chunk.Count == 0) break;

                        var channelsToTest = onlyUnverified ? chunk.Where(c => !c.IsVerified).ToList() : chunk;

                        if (channelsToTest.Count > 0)
                        {
                            var tasks = channelsToTest.Select(async ch =>
                            {
                                if (token.IsCancellationRequested) return;
                                string targetUrl = ch.GetOrderedUrlList().FirstOrDefault() ?? ch.GetUrlList().FirstOrDefault() ?? "";
                                var accInfo = IptvAccountHelper.ParseAccountFromUrl(targetUrl);

                                if (accInfo != null && purgedAccountSignatures.ContainsKey(accInfo.SignatureKey))
                                {
                                    System.Threading.Interlocked.Increment(ref processed);
                                    return;
                                }

                                string hostKey = GetChannelHostKey(ch);
                                var hostSemaphore = hostLocks.GetOrAdd(hostKey, _ => new System.Threading.SemaphoreSlim(1, 1));

                                await globalSemaphore.WaitAsync(token).ConfigureAwait(false);
                                try
                                {
                                    if (token.IsCancellationRequested) return;

                                    if (accInfo != null && purgedAccountSignatures.ContainsKey(accInfo.SignatureKey))
                                    {
                                        System.Threading.Interlocked.Increment(ref processed);
                                        return;
                                    }

                                    StreamMesh.Core.Utils.ValidationResult result;
                                    if (!string.IsNullOrEmpty(targetUrl) && urlCache.TryGetValue(targetUrl, out var cachedResult))
                                    {
                                        result = cachedResult;
                                    }
                                    else
                                    {
                                        await hostSemaphore.WaitAsync(token).ConfigureAwait(false);
                                        try
                                        {
                                            if (token.IsCancellationRequested) return;
                                            using var validator = new StreamValidator();
                                            result = await validator.ValidateAsync(ch, level, null, token).ConfigureAwait(false);
                                            if (!string.IsNullOrEmpty(targetUrl)) urlCache[targetUrl] = result;
                                        }
                                        finally { hostSemaphore.Release(); }
                                    }

                                    if (result.IsOnline)
                                    {
                                        System.Threading.Interlocked.Increment(ref online);
                                        ch.IsVerified = true;
                                        updatedChannels.Add(ch);
                                        logQueue.Enqueue($"{DateTime.Now:HH:mm:ss} - ✅ {ch.PrimaryName} Sağlam ({result.Status})");

                                        if (accInfo != null) accountFailureTracker[accInfo.SignatureKey] = 0;
                                    }
                                    else
                                    {
                                        ch.IsVerified = false;
                                        deadChannelIds.Add(ch.Id);
                                        System.Threading.Interlocked.Increment(ref deleted);
                                        deleteQueue.Enqueue(ch.Id);
                                        logQueue.Enqueue($"{DateTime.Now:HH:mm:ss} - 🗑️ {ch.PrimaryName} SİLİNDİ ({result.Status})");

                                        if (autoPurgeDeadAccounts && accInfo != null && !string.IsNullOrEmpty(accInfo.Username))
                                        {
                                            int failCount = accountFailureTracker.AddOrUpdate(accInfo.SignatureKey, 1, (_, count) => count + 1);
                                            if (failCount >= 3 && purgedAccountSignatures.TryAdd(accInfo.SignatureKey, 1))
                                            {
                                                logQueue.Enqueue($"{DateTime.Now:HH:mm:ss} - ⚡ ÖLÜ HESAP TESPİT EDİLDİ: '{accInfo.Username}' (@{accInfo.HostWithPort}) ardışık {failCount} başarısız oldu.");
                                            }
                                        }
                                    }
                                    System.Threading.Interlocked.Increment(ref processed);
                                }
                                catch (OperationCanceledException) { }
                                catch { }
                                finally { globalSemaphore.Release(); }
                            });

                            await Task.WhenAll(tasks).ConfigureAwait(false);
                        }

                        page++;
                    }
                });
            }
            finally
            {
                uiCts.Cancel();
                try { await uiUpdaterTask; } catch { }
                try { await deleteWorkerTask; } catch { }

                if (updatedChannels.Count > 0)
                {
                    DatabaseEngine.SuppressEvents = true;
                    try { await _db.SaveChannelsBatchAsync(updatedChannels.ToList()); } finally { DatabaseEngine.SuppressEvents = false; }
                }

                DatabaseEngine.NotifyDatabaseUpdated();

                Dispatcher.Invoke(() => {
                    ValidationHealthyText.Text = $"✅ Sağlam: {online}";
                    ValidationDeletedText.Text = $"🗑️ Silinen (Bozuk): {deleted}";
                    ValidationPendingText.Text = $"⏳ Kalan: 0";
                    ValidationTotalText.Text = $"(Toplam: {totalToTest})";
                    ViewModel.ValidationLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - 🎉 İşlem tamamlandı. {online} sağlam kanal doğrulandı, {deleted} bozuk kanal kütüphaneden silindi.");
                    StartValidationBtn.IsEnabled = true;
                    StopValidationBtn.Visibility = Visibility.Collapsed;
                });
                _validationCts = null;
            }
        }

        private void StopValidation_Click(object sender, RoutedEventArgs e) => _validationCts?.Cancel();

        private async void RunLocalAiNormalization_Click(object sender, RoutedEventArgs e)
        {
            var aiEngine = new LocalAiNormalizationEngine();
            bool available = await aiEngine.IsLocalAiAvailableAsync();
            if (!available)
            {
                System.Windows.MessageBox.Show("Yerel Yapay Zeka (Ollama veya LM Studio) aktif değil veya model yüklenmemiş.\nLütfen LM Studio'da bir model yükleyip Local Server'ı başlattığınızdan emin olun.", "Yapay Zeka Bağlantı Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = System.Windows.MessageBox.Show("Yerel Yapay Zeka kütüphanenizdeki kanalları temizlemeye ve kategorize etmeye başlayacak.\nBu işlem arka planda sürerken uygulama çalışmaya devam edecektir. Başlasın mı?", "Yerel AI Normalizasyonu", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                int totalChannels = await _db.GetTotalChannelCountAsync();
                if (totalChannels == 0)
                {
                    System.Windows.MessageBox.Show("Kütüphanede düzenlenecek kanal bulunamadı.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                System.Windows.MessageBox.Show($"Kütüphanenizdeki {totalChannels} kanal Yerel Yapay Zeka kuyruğuna alındı. İşlem arka planda yürütülecektir.", "İşlem Başlatıldı", MessageBoxButton.OK, MessageBoxImage.Information);

                ViewModel.AiLogs.Clear();
                ViewModel.AiBatchReports.Clear();
                ViewModel.AiLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - 🤖 AI Optimizasyonu başlatıldı ({totalChannels} toplam kanal).");

                _ = Task.Run(async () =>
                {
                    int page = 1;
                    int batchSize = 500;
                    while (true)
                    {
                        var (chunk, count) = await _db.GetPagedChannelsAsync(null, "All", null, null, 0, page, batchSize).ConfigureAwait(false);
                        if (chunk == null || chunk.Count == 0) break;

                        var originalStates = chunk.ToDictionary(c => c.Id, c => (name: c.Name, cat: c.Category));

                        Dispatcher.Invoke(() => ViewModel.AiLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Sayfa {page} işleniyor (500 kanal batch)..."));

                        await aiEngine.NormalizeChannelsWithAiAsync(chunk, (msg, processed, total) =>
                        {
                            LogService.LogInfo($"[AI Normalization] {msg} ({processed}/{total})");
                            Dispatcher.Invoke(() => ViewModel.AiLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - {msg} ({processed}/{total})"));
                        });

                        var batchReport = new AiBatchReportItem { PageNumber = page, Title = $"Sayfa {page} Batch" };
                        foreach (var ch in chunk)
                        {
                            if (originalStates.TryGetValue(ch.Id, out var orig))
                            {
                                batchReport.Records.Add(new AiChangeRecord {
                                    ChannelId = ch.Id,
                                    OldName = orig.name,
                                    NewName = ch.Name,
                                    OldCategory = orig.cat,
                                    NewCategory = ch.Category
                                });
                            }
                        }

                        Dispatcher.Invoke(() => ViewModel.AiBatchReports.Insert(0, batchReport));
                        await _db.SaveChannelsBatchAsync(chunk);
                        page++;
                    }
                    DatabaseEngine.NotifyDatabaseUpdated();
                    Dispatcher.Invoke(() => ViewModel.AiLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - 🎉 AI Optimizasyonu başarıyla tamamlandı!"));
                    LogService.LogInfo("[AI Normalization] Tüm kanallar Yapay Zeka ile güncellendi ve veritabanına kaydedildi!");
                });
            }
            catch (Exception ex)
            {
                LogService.LogError("RunLocalAiNormalization_Click error", ex);
                System.Windows.MessageBox.Show($"Hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AiBatchItem_DoubleClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (AiBatchesList.SelectedItem is AiBatchReportItem reportItem)
            {
                var win = new AiBatchDetailsWindow(reportItem);
                win.Owner = Window.GetWindow(this);
                win.ShowDialog();
            }
        }

        private static string GetChannelHostKey(Channel ch)
        {
            string url = ch.GetUrlList().FirstOrDefault() ?? "";
            if (string.IsNullOrWhiteSpace(url)) return "unknown";
            try { var uri = new Uri(url); return uri.Host.ToLowerInvariant(); } catch { return "unknown"; }
        }
    }
}
