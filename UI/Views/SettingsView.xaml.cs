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

        private bool _isServerRunning = false;

        public SettingsView()
        {
            InitializeComponent();
            LoadSettings();

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
            if (CachingBox != null) CachingBox.Text = _db.GetSetting("VlcCaching", "1500");
            if (UserAgentBox != null) UserAgentBox.Text = _db.GetSetting("VlcUserAgent", "Mozilla/5.0");
            if (HwAccelCheck != null) HwAccelCheck.IsChecked = _db.GetSetting("VlcHwAccel", "true") == "true";
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
            _db.SetSetting("VlcCaching", CachingBox.Text);
            _db.SetSetting("VlcUserAgent", UserAgentBox.Text);
            _db.SetSetting("VlcHwAccel", HwAccelCheck.IsChecked == true ? "true" : "false");
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
            if (sender is Button b && b.CommandParameter is string url)
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
    }
}
