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

            ViewModel.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(ViewModel.SyncStatus)) {
                    Dispatcher.Invoke(() => {
                        if (SyncProgress != null) SyncProgress.Value = ViewModel.SyncProgress;
                        if (SyncStatusText != null) SyncStatusText.Text = ViewModel.SyncStatus;
                        if (ViewModel.SyncProgress >= 100) StartSyncBtn.IsEnabled = true;
                    });
                }
            };
            LoadSettings();
        }

        private void LoadSettings()
        {
            ViewModel.LoadSettings();
            if (CachingBox != null) CachingBox.Text = _db.GetSetting("FlyleafCache", "1000");
            if (HwAccelCheck != null) HwAccelCheck.IsChecked = _db.GetSetting("FlyleafHwAccel", "true") == "true";

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
                ViewModel.ServerPort = port.ToString();
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
            string port = ViewModel.ServerPort;
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
            if (AiProviderCombo.SelectedIndex == 0) ViewModel.AiUrl = "http://localhost:11434/api/chat";
            else if (AiProviderCombo.SelectedIndex == 1) ViewModel.AiUrl = "http://localhost:1234/v1/chat/completions";
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SaveSettings();
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
                    if (queue.Count > 0) interleavedChannels.Add(queue.Dequeue());
                    if (queue.Count == 0) groupedByHost.RemoveAt(i);
                }
            }
            channelsToTest = interleavedChannels;

            int concurrency = 5;
            if (ComboConcurrency.SelectedItem is ComboBoxItem comboItem && comboItem.Content != null)
            {
                string text = comboItem.Content.ToString() ?? "";
                var match = System.Text.RegularExpressions.Regex.Match(text, @"\d+");
                if (match.Success && int.TryParse(match.Value, out int parsed)) concurrency = Math.Max(1, parsed);
            }

            ViewModel.ValidationLogs.Clear();
            ValidationProgressBar.Value = 0;
            ValidationProgressBar.Maximum = channelsToTest.Count;
            StartValidationBtn.IsEnabled = false;
            StopValidationBtn.Visibility = Visibility.Visible;
            ValidationFailedText.Visibility = Visibility.Visible;
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

            ViewModel.ValidationLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - 🚀 Test başlatıldı: {channelsToTest.Count} kanal ({concurrency} threads)");

            var token = _validationCts.Token;

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
                            foreach (var item in logItems) ViewModel.ValidationLogs.Insert(0, item);
                            while (ViewModel.ValidationLogs.Count > 500) ViewModel.ValidationLogs.RemoveAt(ViewModel.ValidationLogs.Count - 1);
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
                await Task.Run(async () =>
                {
                    using var globalSemaphore = new System.Threading.SemaphoreSlim(concurrency, concurrency);
                    var hostLocks = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.SemaphoreSlim>();
                    var urlCache = new System.Collections.Concurrent.ConcurrentDictionary<string, StreamMesh.Core.Utils.ValidationResult>();

                    var tasks = channelsToTest.Select(async ch =>
                    {
                        if (token.IsCancellationRequested) return;
                        string targetUrl = ch.GetOrderedUrlList().FirstOrDefault() ?? ch.GetUrlList().FirstOrDefault() ?? "";
                        string hostKey = GetChannelHostKey(ch);
                        var hostSemaphore = hostLocks.GetOrAdd(hostKey, _ => new System.Threading.SemaphoreSlim(1, 1));

                        await globalSemaphore.WaitAsync(token).ConfigureAwait(false);
                        try
                        {
                            if (token.IsCancellationRequested) return;

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
                                logQueue.Enqueue($"{DateTime.Now:HH:mm:ss} - ✅ {ch.PrimaryName} Aktif ({result.Status})");
                            }
                            else
                            {
                                ch.IsVerified = false;
                                deadChannelIds.Add(ch.Id);
                                logQueue.Enqueue($"{DateTime.Now:HH:mm:ss} - ❌ {ch.PrimaryName} ({result.Status})");
                            }
                            updatedChannels.Add(ch);
                            System.Threading.Interlocked.Increment(ref processed);
                        }
                        catch (OperationCanceledException) { }
                        catch { }
                        finally { globalSemaphore.Release(); }
                    });
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                });
            }
            finally
            {
                uiCts.Cancel();
                try { await uiUpdaterTask; } catch { }

                if (updatedChannels.Count > 0)
                {
                    DatabaseEngine.SuppressEvents = true;
                    try { await _db.SaveChannelsBatchAsync(updatedChannels.ToList()); } finally { DatabaseEngine.SuppressEvents = false; }
                }

                Dispatcher.Invoke(() => {
                    ValidationProgressText.Text = $"Tamamlandı: {processed}";
                    ViewModel.ValidationLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - 🎉 İşlem tamamlandı.");
                    StartValidationBtn.IsEnabled = true;
                    StopValidationBtn.Visibility = Visibility.Collapsed;
                });
                _validationCts = null;
            }
        }

        private void StopValidation_Click(object sender, RoutedEventArgs e) => _validationCts?.Cancel();

        private static string GetChannelHostKey(Channel ch)
        {
            string url = ch.GetUrlList().FirstOrDefault() ?? "";
            if (string.IsNullOrWhiteSpace(url)) return "unknown";
            try { var uri = new Uri(url); return uri.Host.ToLowerInvariant(); } catch { return "unknown"; }
        }
    }
}
