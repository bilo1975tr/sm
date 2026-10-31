using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Net.Http;
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
using StreamMesh.Core.Plugins;

using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;

namespace StreamMesh.UI.Views

{
    public partial class SettingsView : System.Windows.Controls.UserControl
    {
        public SettingsViewModel ViewModel { get; } = new SettingsViewModel();
        private readonly DatabaseEngine _db = new DatabaseEngine();
        private readonly XtreamService _xtream = new XtreamService();

        private bool _isServerRunning = false;
        private System.Threading.CancellationTokenSource? _validationCts;

        private bool _isLoadedOnce = false;

        public SettingsView()
        {
            InitializeComponent();
            DataContext = ViewModel;
            if (ValidationLogsList != null) ValidationLogsList.ItemsSource = ViewModel.ValidationLogs;

            ViewModel.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(ViewModel.SyncStatus)) {
                    Dispatcher.InvokeAsync(() => {
                        if (SyncProgress != null) SyncProgress.Value = ViewModel.SyncProgress;
                        if (SyncStatusText != null) SyncStatusText.Text = ViewModel.SyncStatus;
                        if (ViewModel.SyncProgress >= 100 && StartSyncBtn != null) StartSyncBtn.IsEnabled = true;
                    });
                }
            };
            this.Loaded += (s, e) => {
                if (!_isLoadedOnce)
                {
                    _isLoadedOnce = true;
                    _ = LoadSettingsAsync();
                }
            };
        }

        private async Task LoadSettingsAsync()
        {
            // 1. Asynchronously load ViewModel properties (AI, TMDB, sources with fast batch counts, IPTV accounts)
            await ViewModel.LoadSettingsAsync();

            // 2. Asynchronously load additional player & UI settings
            await Task.Run(() => {
                string cache = _db.GetSetting("FlyleafCache", "1000");
                bool hw = _db.GetSetting("FlyleafHwAccel", "true") == "true";
                bool audioNorm = _db.GetSetting("FlyleafAudioNorm", "false") == "true";
                bool sharpen = _db.GetSetting("FlyleafSharpen", "false") == "true";
                string userAgent = _db.GetSetting("FlyleafUserAgent", "");
                bool tvMode = _db.GetSetting("TvMode", "false") == "true";
                var epgs = _db.GetEpgSourceItems();

                Dispatcher.InvokeAsync(() => {
                    if (CachingBox != null) CachingBox.Text = cache;
                    if (HwAccelCheck != null) HwAccelCheck.IsChecked = hw;
                    if (AudioNormCheck != null) AudioNormCheck.IsChecked = audioNorm;
                    if (VideoSharpenCheck != null) VideoSharpenCheck.IsChecked = sharpen;
                    if (UserAgentBox != null) UserAgentBox.Text = userAgent;
                    if (TvModeCheck != null) TvModeCheck.IsChecked = tvMode;
                    if (EpgSourcesList != null) EpgSourcesList.ItemsSource = epgs;
                });
            });

            // 3. Asynchronously update server status UI without blocking
            await UpdateServerStatusUIAsync();
        }

        private void RefreshSourcesList() => ViewModel.RefreshSources();
        private void RefreshIptvList() => ViewModel.RefreshIptvAccounts();

        private void RefreshEpgList()
        {
            Task.Run(() =>
            {
                var list = _db.GetEpgSourceItems();
                Dispatcher.InvokeAsync(() => {
                    if (EpgSourcesList != null) EpgSourcesList.ItemsSource = list;
                });
            });
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
                    System.Windows.MessageBox.Show($"Gerçek IPTV Sağlayıcısı başarıyla doğrulandı! Toplu kanal listesi ve VOD arşivi yüklendi (mükerrer yayınlar temizlendi).", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                    XtreamUrlBox.Clear();
                    XtreamUserBox.Clear();
                    XtreamPassBox.Clear();
                }
                else
                {
                    try { _db.RemoveIptvAccount(acc.Id); } catch { }
                    RefreshIptvList();
                    RefreshSourcesList();
                    System.Windows.MessageBox.Show("Bu adres geçerli bir gerçek IPTV sağlayıcısı değildir (toplu kanal listesi / VOD arşivi alınamadı). Normal M3U dosyası veya adresi olarak değerlendirilir, IPTV hesaplarına eklenmedi.", "Geçersiz IPTV Sağlayıcısı", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        private async void ImportAccountChannels_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            string? accId = btn?.CommandParameter as string;
            if (string.IsNullOrEmpty(accId)) return;

            var accounts = _db.GetAllIptvAccounts();
            var acc = accounts.FirstOrDefault(a => a.Id == accId);
            if (acc == null) return;

            if (btn != null)
            {
                btn.IsEnabled = false;
                btn.Content = "⏳ İndiriliyor...";
            }

            try
            {
                string host = "";
                try
                {
                    host = new Uri(acc.ServerUrl).Authority;
                }
                catch
                {
                    host = acc.ServerUrl.Replace("http://", "").Replace("https://", "").TrimEnd('/');
                }

                var accInfo = new IptvAccountInfo
                {
                    HostWithPort = host,
                    Username = acc.Username,
                    Password = acc.Password
                };

                var (success, count, msg) = await Task.Run(async () =>
                {
                    return await IptvAccountHelper.FetchAndImportFullAccountAsync(accInfo, null);
                });

                await _xtream.SyncAccountAsync(acc);
                RefreshIptvList();
                DatabaseEngine.NotifyDatabaseUpdated();

                if (success)
                {
                    System.Windows.MessageBox.Show($"Başarılı!\n{acc.Name} sağlayıcısından {count:N0} adet yayın (Canlı TV, Film ve Diziler) kütüphanenize eklendi.", "Kütüphaneye Aktarıldı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show($"İndirme sırasında bir sorun oluştu:\n{msg}", "Bilgi / Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (btn != null)
                {
                    btn.IsEnabled = true;
                    btn.Content = "📥 Kütüphaneye İndir";
                }
            }
        }

        private async void ImportAllIptvChannels_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
            {
                btn.IsEnabled = false;
                btn.Content = "⏳ Sağlayıcılar İndiriliyor...";
            }

            try
            {
                var accounts = _db.GetAllIptvAccounts();
                if (accounts.Count == 0)
                {
                    System.Windows.MessageBox.Show("Kayıtlı IPTV hesabı bulunamadı.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                int totalAdded = 0;
                int successCount = 0;

                foreach (var acc in accounts)
                {
                    try
                    {
                        string host = "";
                        try
                        {
                            host = new Uri(acc.ServerUrl).Authority;
                        }
                        catch
                        {
                            host = acc.ServerUrl.Replace("http://", "").Replace("https://", "").TrimEnd('/');
                        }

                        var accInfo = new IptvAccountInfo
                        {
                            HostWithPort = host,
                            Username = acc.Username,
                            Password = acc.Password
                        };

                        var (success, count, _) = await Task.Run(async () =>
                        {
                            return await IptvAccountHelper.FetchAndImportFullAccountAsync(accInfo, null);
                        });

                        if (success)
                        {
                            totalAdded += count;
                            successCount++;
                        }
                        await _xtream.SyncAccountAsync(acc);
                    }
                    catch { }
                }

                RefreshIptvList();
                DatabaseEngine.NotifyDatabaseUpdated();
                System.Windows.MessageBox.Show($"Tamamlandı!\n{successCount} sağlayıcıdan toplam {totalAdded:N0} adet yayın kütüphanenize eklendi.", "Toplu Aktarım Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                if (btn != null)
                {
                    btn.IsEnabled = true;
                    btn.Content = "📥 Tüm Sağlayıcıları Kütüphaneye İndir";
                }
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

        private void RepairNetwork_Click(object sender, RoutedEventArgs e)
        {
            int currentPort = StreamMesh.App.Server?.Port ?? (int.TryParse(ServerPortBox?.Text, out int p) ? p : 8080);
            
            bool result = StreamMesh.Core.Network.MediaServer.RepairNetworkAclAndFirewall(currentPort);

            if (_isServerRunning && StreamMesh.App.Server != null)
            {
                StreamMesh.App.Server.Stop();
                StreamMesh.App.Server = new StreamMesh.Core.Network.MediaServer(currentPort);
                StreamMesh.App.Server.Start();
                _isServerRunning = StreamMesh.App.Server.IsRunning;
            }

            UpdateServerStatusUI();

            if (result)
            {
                System.Windows.MessageBox.Show($"Port {currentPort} için Windows Güvenlik Duvarı kuralı ve URL izni otomatik güncellendi!\nAğdaki diğer bilgisayarlardan http://{StreamMesh.Core.Network.MediaServer.GetPrimaryLocalIPv4Address()}:{currentPort}/direct.m3u adresini tekrar deneyin.", "Ağ Onarımı Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show("Ağ izni onarılırken bir uyarı oluştu. Lütfen uygulamayı bir defalığına 'Yönetici Olarak Çalıştır' seçeneği ile başlatın.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task UpdateServerStatusUIAsync()
        {
            if (ServerControlBtn == null) return;

            bool isRunning = StreamMesh.App.Server?.IsRunning == true;
            bool isLanAccessible = StreamMesh.App.Server?.IsLanAccessible == true;
            string bindingMode = StreamMesh.App.Server?.BindingMode ?? "Kapalı";
            _isServerRunning = isRunning;

            ServerControlBtn.Content = _isServerRunning ? "Sunucuyu Durdur" : "Sunucuyu Başlat";

            int currentPort = StreamMesh.App.Server?.Port ?? (int.TryParse(ServerPortBox?.Text, out int p) ? p : 8080);
            string portStr = currentPort.ToString();

            if (ServerPortBox != null && ServerPortBox.Text != portStr)
            {
                ServerPortBox.Text = portStr;
            }

            // Resolve real LAN IP in background task to never pause UI thread
            string lanIp = await Task.Run(() => StreamMesh.Core.Network.MediaServer.GetPrimaryLocalIPv4Address());

            if (M3uServerLink != null) M3uServerLink.Text = $"http://{lanIp}:{portStr}/playlist.m3u";
            if (M3uDirectServerLink != null) M3uDirectServerLink.Text = $"http://{lanIp}:{portStr}/direct.m3u";
            if (WebServerLink != null) WebServerLink.Text = $"http://{lanIp}:{portStr}/web";

            if (LanStatusBorder != null && LanStatusText != null)
            {
                if (!isRunning)
                {
                    LanStatusBorder.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#334155"));
                    LanStatusText.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#94a3b8"));
                    LanStatusText.Text = "🔴 Sunucu Kapalı";
                }
                else if (isLanAccessible)
                {
                    LanStatusBorder.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#064e3b"));
                    LanStatusText.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#a7f3d0"));
                    LanStatusText.Text = $"🟢 Ağ Erişimi Aktif ({bindingMode})";
                }
                else
                {
                    LanStatusBorder.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#854d0e"));
                    LanStatusText.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#fef08a"));
                    LanStatusText.Text = "⚠️ Yerel Ağ Kısıtlı (Sadece Localhost - Onar Butonuna Tıklayın)";
                }
            }
        }

        private void UpdateServerStatusUI()
        {
            _ = UpdateServerStatusUIAsync();
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

        private async void RefreshSingleEpg_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.CommandParameter is string url)
            {
                b.IsEnabled = false;
                try
                {
                    var epgEng = new EpgEngine();
                    await Task.Run(() => epgEng.LoadEpgAsync(url, (msg, pct) => {
                        Dispatcher.Invoke(() => {
                            if (EpgStatusText != null) EpgStatusText.Text = msg;
                        });
                    }));
                    RefreshEpgList();
                    System.Windows.MessageBox.Show($"EPG kaynağı başarıyla güncellendi: {url}", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"EPG güncellenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    b.IsEnabled = true;
                }
            }
        }

        private void TimeShift_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.Tag is string url && cb.SelectedItem is ComboBoxItem item && item.Tag is string tagStr && int.TryParse(tagStr, out int shift))
            {
                _db.SetEpgSourceTimeShift(url, shift);
                DatabaseEngine.NotifyDatabaseUpdated();
            }
        }

        private async void PurgeDeadEpgSources_Click(object sender, RoutedEventArgs e)
        {
            if (System.Windows.MessageBox.Show("Yanıt vermeyen, ölü veya 404 / 500 hatalı EPG kaynakları test edilerek temizlenecek. Emin misiniz?", "Ölü EPG Kaynaklarını Temizle", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            if (EpgStatusText != null) EpgStatusText.Text = "Ölü EPG kaynakları taranıyor...";
            
            int removedCount = 0;
            var sources = _db.GetEpgSources();

            await Task.Run(async () =>
            {
                foreach (var src in sources)
                {
                    if (!src.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;

                    bool isDead = false;
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                        using var req = new HttpRequestMessage(HttpMethod.Get, src);
                        req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) StreamMesh/1.0");
                        using var resp = await MediaHttpClient.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                        
                        // Consider dead if 404 Not Found or 5xx Server Error
                        if ((int)resp.StatusCode == 404 || (int)resp.StatusCode >= 500)
                        {
                            isDead = true;
                        }
                    }
                    catch
                    {
                        isDead = true;
                    }

                    if (isDead)
                    {
                        _db.RemoveEpgSource(src);
                        removedCount++;
                    }
                }
            });

            RefreshEpgList();
            if (EpgStatusText != null) EpgStatusText.Text = $"Ölü EPG temizliği tamamlandı. {removedCount} ölü kaynak silindi.";
            System.Windows.MessageBox.Show($"{removedCount} adet yanıt vermeyen ölü EPG kaynağı başarıyla temizlendi.", "Temizlik Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void UpdateAllEpg_Click(object sender, RoutedEventArgs e)
        {
            if (UpdateAllEpgBtn != null) UpdateAllEpgBtn.IsEnabled = false;
            if (EpgProgressBar != null)
            {
                EpgProgressBar.Visibility = Visibility.Visible;
                EpgProgressBar.Value = 0;
            }
            if (EpgStatusText != null) EpgStatusText.Text = "EPG güncellemesi başlatılıyor...";

            try
            {
                var sources = _db.GetEpgSources();
                if (sources == null || sources.Count == 0)
                {
                    if (EpgStatusText != null) EpgStatusText.Text = "Kayıtlı EPG kaynağı bulunamadı.";
                    return;
                }

                var epgEng = new StreamMesh.Core.Media.EpgEngine();
                for (int i = 0; i < sources.Count; i++)
                {
                    string src = sources[i];
                    int srcIdx = i + 1;
                    int srcTotal = sources.Count;

                    await Task.Run(async () =>
                    {
                        await epgEng.LoadEpgAsync(src, (msg, pct) =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (EpgStatusText != null) EpgStatusText.Text = $"[{srcIdx}/{srcTotal}] {msg}";
                                if (EpgProgressBar != null)
                                {
                                    double totalPct = ((srcIdx - 1) * 100.0 + pct) / srcTotal;
                                    EpgProgressBar.Value = Math.Clamp(totalPct, 0, 100);
                                }
                            });
                        });
                    });
                }

                int totalCount = await _db.GetEpgProgramsCountAsync();
                _db.SetSetting("LastAutoEpgUpdate", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
                if (EpgStatusText != null) EpgStatusText.Text = $"EPG güncellendi! Toplam {totalCount:N0} yayın akışı hazır.";
            }
            catch (Exception ex)
            {
                if (EpgStatusText != null) EpgStatusText.Text = $"Hata: {ex.Message}";
            }
            finally
            {
                if (UpdateAllEpgBtn != null) UpdateAllEpgBtn.IsEnabled = true;
                if (EpgProgressBar != null) EpgProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SaveSettings();
            _db.SetSetting("FlyleafCache", CachingBox.Text ?? "1000");
            _db.SetSetting("FlyleafHwAccel", HwAccelCheck.IsChecked == true ? "true" : "false");
            _db.SetSetting("FlyleafAudioNorm", AudioNormCheck.IsChecked == true ? "true" : "false");
            _db.SetSetting("FlyleafSharpen", VideoSharpenCheck.IsChecked == true ? "true" : "false");
            _db.SetSetting("FlyleafUserAgent", UserAgentBox.Text ?? "");
            _db.SetSetting("TvMode", TvModeCheck.IsChecked == true ? "true" : "false");
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
            _validationCts = new System.Threading.CancellationTokenSource();

            ValidationLevel level = ValidationLevel.Fast;
            if (RadioDetailed.IsChecked == true) level = ValidationLevel.Detailed;
            if (RadioFull.IsChecked == true) level = ValidationLevel.Full;
            bool autoPurgeDeadAccounts = CheckFastDeadAccountPurge?.IsChecked == true;

            var startTime = DateTime.Now;
            int totalToTest = channelsToTest.Count;
            int processed = 0;
            int online = 0;
            int deleted = 0;
            var deadChannelIds = new System.Collections.Concurrent.ConcurrentBag<string>();
            var updatedChannels = new System.Collections.Concurrent.ConcurrentBag<Channel>();
            var logQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();

            // Anında silme için thread-safe kuyruk
            var deleteQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();

            // Hesap bazlı ardışık hata takibi (Ölü paket erken tespiti)
            var accountFailureTracker = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
            var purgedAccountSignatures = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();

            ViewModel.ValidationLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - 🚀 Test başlatıldı: {totalToTest} kanal ({concurrency} iş parçacığı)");

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

            // Arka planda bozuk bulunan kanalları veri tabanından anında silen iş parçacığı
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

                    var tasks = channelsToTest.Select(async ch =>
                    {
                        if (token.IsCancellationRequested) return;
                        string targetUrl = ch.GetOrderedUrlList().FirstOrDefault() ?? ch.GetUrlList().FirstOrDefault() ?? "";
                        var accInfo = IptvAccountHelper.ParseAccountFromUrl(targetUrl);

                        // Eğer bu IPTV paketi zaten ölü olarak tespit edilip komple silindiyse, kanalı atla
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

                                // Başarılı olunca sayaç sıfırlansın
                                if (accInfo != null) accountFailureTracker[accInfo.SignatureKey] = 0;
                            }
                            else
                            {
                                ch.IsVerified = false;
                                deadChannelIds.Add(ch.Id);
                                System.Threading.Interlocked.Increment(ref deleted);
                                // Bozuk kanalı anında silinmek üzere kuyruğa ekle
                                deleteQueue.Enqueue(ch.Id);
                                logQueue.Enqueue($"{DateTime.Now:HH:mm:ss} - 🗑️ {ch.PrimaryName} SİLİNDİ ({result.Status})");

                                // Ölü Hesap / Paket Tespiti: Aynı hesap ardışık 3 kez başarısız olursa paketi komple sil
                                if (autoPurgeDeadAccounts && accInfo != null && !string.IsNullOrEmpty(accInfo.Username))
                                {
                                    int failCount = accountFailureTracker.AddOrUpdate(accInfo.SignatureKey, 1, (_, count) => count + 1);
                                    if (failCount >= 3 && purgedAccountSignatures.TryAdd(accInfo.SignatureKey, 1))
                                    {
                                        logQueue.Enqueue($"{DateTime.Now:HH:mm:ss} - ⚡ ÖLÜ HESAP TESPİT EDİLDİ: '{accInfo.Username}' (@{accInfo.HostWithPort}) ardışık {failCount} başarısız oldu. Kalan tüm kanalları tek seferde komple temizleniyor...");

                                        _ = Task.Run(async () =>
                                        {
                                            try
                                            {
                                                var purgeRes = await IptvAccountHelper.PurgeAccountFromDatabaseAsync(accInfo, allChannels);
                                                logQueue.Enqueue($"{DateTime.Now:HH:mm:ss} - 🚀 ÖLÜ HESAP TEMİZLENDİ: '{accInfo.Username}' paketine ait {purgeRes.TotalUrlsRemoved} link ({purgeRes.ChannelsDeletedEntirely} kanal) tek tıkla komple silindi!");
                                            }
                                            catch (Exception ex)
                                            {
                                                LogService.LogWarning($"Auto-purge failed for account {accInfo.Username}: {ex.Message}");
                                            }
                                        });
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
                });
            }
            finally
            {
                uiCts.Cancel();
                try { await uiUpdaterTask; } catch { }
                try { await deleteWorkerTask; } catch { }

                // Sağlam ve güncellenen kanalları kaydet
                if (updatedChannels.Count > 0)
                {
                    DatabaseEngine.SuppressEvents = true;
                    try { await _db.SaveChannelsBatchAsync(updatedChannels.ToList()); } finally { DatabaseEngine.SuppressEvents = false; }
                }

                // Arayüz listelerini tazelemek için veri tabanı güncellemesini bildir
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

        private static string GetChannelHostKey(Channel ch)
        {
            string url = ch.GetUrlList().FirstOrDefault() ?? "";
            if (string.IsNullOrWhiteSpace(url)) return "unknown";
            try { var uri = new Uri(url); return uri.Host.ToLowerInvariant(); } catch { return "unknown"; }
        }

        #region Plugins & Modules Handlers

        private void TogglePluginCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is IPlugin plugin)
            {
                ViewModel.TogglePlugin(plugin);
            }
        }

        private void ToggleSelectedPlugin_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.TogglePlugin(ViewModel.SelectedPlugin);
        }

        private void SavePluginSettings_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SaveSelectedPluginSettings();
        }

        private void ResetPluginSettings_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ResetSelectedPluginSettings();
        }

        private async void ExecuteSelectedPluginAction_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedPlugin != null)
            {
                string defaultAction = ViewModel.SelectedPlugin.Id switch
                {
                    "ai_assistant" => "TestAiConnection",
                    "epg_guide" => "CleanOldData",
                    "stream_resolution" => "ResetBuffer",
                    "local_network_server" => "TestPortAvailability",
                    "metadata_enricher" => "TestTmdbKey",
                    "logo_manager" => "ClearLogoCache",
                    "xtream_client" => "TestAllAccounts",
                    "github_sync" => "StartSync",
                    "ssdp_discovery" => "BroadcastNow",
                    "p2p_stats" => "TestEngine",
                    "auto_updater" => "CheckForUpdates",
                    "system_maintenance" => "RunMaintenance",
                    "user_auth" => "TestPin",
                    "global_search" => "TestSearch",
                    _ => "Test"
                };
                await ViewModel.ExecuteSelectedPluginActionAsync(defaultAction);
            }
        }

        private void EnableAllPlugins_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.EnableAllPlugins();
        }

        private void DisableAllPlugins_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.DisableAllPlugins();
        }

        #endregion
    }
}
