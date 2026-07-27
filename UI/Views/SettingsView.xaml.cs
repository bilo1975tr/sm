using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media;
using StreamMesh.Core.Utils;
using StreamMesh.Models;
using StreamMesh.Core.Network;
using System.Linq;
using System.Threading.Tasks;

namespace StreamMesh.UI.Views
{
    public class M3uSourceDisplay
    {
        public string Url { get; set; } = "";
        public string Origin { get; set; } = "Yerel";
        public string Color { get; set; } = "#1e293b";
        public bool CanDelete { get; set; } = true;
    }

    public partial class SettingsView : System.Windows.Controls.UserControl
    {
        private readonly DatabaseEngine _db = new DatabaseEngine();
        private readonly GitHubSyncEngine _sync = new GitHubSyncEngine();
        private readonly AiEngine _ai = new AiEngine();
        private readonly XtreamService _xtream = new XtreamService();
        private readonly AceEngine _aceEngine = new AceEngine();

        public ObservableCollection<M3uSourceDisplay> Sources { get; set; } = new ObservableCollection<M3uSourceDisplay>();
        public ObservableCollection<IptvAccount> IptvAccounts { get; set; } = new ObservableCollection<IptvAccount>();

        private bool _isServerRunning = false;

        public SettingsView()
        {
            InitializeComponent();
            LoadSettings();

            _sync.OnProgress += (p, msg) => {
                Dispatcher.Invoke(() => {
                    if (SyncProgress != null) SyncProgress.Value = p;
                    if (SyncStatusText != null) SyncStatusText.Text = msg;
                    if (p == 100) { RefreshSourcesList(); RefreshEpgList(); }
                });
            };
        }

        private async void LoadSettings()
        {
            if (AiUrlBox != null) AiUrlBox.Text = _db.GetSetting("AiUrl", "http://localhost:11434/api/chat");
            if (AiModelBox != null) AiModelBox.Text = _db.GetSetting("AiModel", "llama3");
            if (TmdbApiKeyBox != null) TmdbApiKeyBox.Text = _db.GetSetting("TmdbApiKey", "3fd2be6f0c70a2a598f084dd23308883");
            if (CachingBox != null) CachingBox.Text = _db.GetSetting("VlcCache", "1500");
            if (UserAgentBox != null) UserAgentBox.Text = _db.GetSetting("UserAgent", "StreamMesh/1.8");
            if (HwAccelCheck != null) HwAccelCheck.IsChecked = _db.GetSetting("HwAccel", "true") == "true";
            if (ServerPortBox != null) ServerPortBox.Text = _db.GetSetting("ServerPort", "8080");

            RefreshSourcesList();
            RefreshEpgList();
            RefreshIptvList();
            UpdateQuotaUI();
            UpdateServerStatusUI();
            await CheckAceStatusAsync();
        }

        private async Task CheckAceStatusAsync()
        {
            if (AceStatusText == null) return;

            bool running = await _aceEngine.IsEngineRunningAsync();
            bool installed = _aceEngine.IsInstalled();

            if (running)
            {
                AceStatusText.Text = "🟢 AceStream Motoru Çalışıyor (HTTP API Aktif - Port 6878)";
            }
            else if (installed)
            {
                AceStatusText.Text = "🟡 AceStream Yüklü (Motor şu an kapalı, yayın açılınca otomatik başlayacak)";
            }
            else
            {
                AceStatusText.Text = "🔴 AceStream Motoru Bulunamadı (P2P yayınları için kurulmalıdır)";
            }
        }

        private async void CheckAceStatus_Click(object sender, RoutedEventArgs e)
        {
            await CheckAceStatusAsync();
        }

        private async void InstallAceStream_Click(object sender, RoutedEventArgs e)
        {
            if (InstallAceButton == null || AceDownloadProgress == null) return;

            InstallAceButton.IsEnabled = false;
            AceDownloadProgress.Visibility = Visibility.Visible;
            AceDownloadProgress.Value = 0;

            try
            {
                bool success = await _aceEngine.DownloadAndExtractEngineAsync(progress =>
                {
                    Dispatcher.Invoke(() => AceDownloadProgress.Value = progress);
                });

                if (success)
                {
                    System.Windows.MessageBox.Show("AceStream motoru ve eklentileri başarıyla yüklendi!", "Kurulum Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                    await _aceEngine.StartEngineAsync();
                }
                else
                {
                    System.Windows.MessageBox.Show("AceStream indirilirken veya kurulurken bir hata oluştu.", "Kurulum Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Kurulum hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                InstallAceButton.IsEnabled = true;
                AceDownloadProgress.Visibility = Visibility.Collapsed;
                await CheckAceStatusAsync();
            }
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
                    Color = isCloud ? "#0369a1" : "#1e293b", CanDelete = !isCloud
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
                else System.Windows.MessageBox.Show("IPTV bağlantı hatası.");
            }
            catch (Exception ex) { System.Windows.MessageBox.Show("Hata: " + ex.Message); }
        }

        private void RemoveIptv_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b && b.CommandParameter is string id)
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
                StreamMesh.App.Ssdp?.Start(port);
                _isServerRunning = true;
            }
            else
            {
                StreamMesh.App.Server?.Stop();
                StreamMesh.App.Ssdp?.Stop();
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

        private void AddEpgSource_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(EpgUrlBox.Text))
            {
                _db.AddEpgSource(EpgUrlBox.Text);
                EpgUrlBox.Clear();
                RefreshEpgList();
                Task.Run(() => new EpgEngine().LoadEpgAsync(EpgUrlBox.Text));
            }
        }

        private void RemoveEpgSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b && b.CommandParameter is string url)
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
            _db.SetSetting("VlcCache", CachingBox.Text);
            _db.SetSetting("UserAgent", UserAgentBox.Text);
            _db.SetSetting("HwAccel", HwAccelCheck.IsChecked == true ? "true" : "false");
            System.Windows.MessageBox.Show("Ayarlar kaydedildi.");
        }

        private async void AddSource_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(M3uUrlBox.Text))
            {
                _db.AddM3uSource(M3uUrlBox.Text);
                M3uUrlBox.Clear();
                RefreshSourcesList();
                await new M3uEngine().ParseM3uAsync(M3uUrlBox.Text);
            }
        }

        private void RemoveSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b && b.CommandParameter is string url)
            {
                _db.RemoveM3uSource(url);
                RefreshSourcesList();
            }
        }

        private async void FetchModels_Click(object sender, RoutedEventArgs e)
        {
            var models = await _ai.GetLocalModelsAsync();
            if (models.Count > 0)
            {
                AiModelBox.Text = models[0];
                System.Windows.MessageBox.Show($"Bulunan Modeller: {string.Join(", ", models)}", "AI Modelleri");
            }
            else System.Windows.MessageBox.Show("Yerel AI sunucusuna bağlanılamadı.");
        }

        private async void StartCloudSync_Click(object sender, RoutedEventArgs e)
        {
            await _sync.PullFromGitHubAsync();
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (System.Windows.MessageBox.Show("Tüm kanallar ve kaynaklar silinecek. Emin misiniz?", "🚨 Kritik Uyarı", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _db.ExecuteRawNonQuery("DELETE FROM Channels");
                _db.ExecuteRawNonQuery("DELETE FROM M3uSources");
                _db.ExecuteRawNonQuery("DELETE FROM EpgSources");
                RefreshSourcesList();
                RefreshEpgList();
            }
        }
    }
}
