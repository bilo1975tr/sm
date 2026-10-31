using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using StreamMesh.Core.Database;
using StreamMesh.Core.Network;
using StreamMesh.Core.Utils;
using UserControl = System.Windows.Controls.UserControl;
using MessageBox = System.Windows.MessageBox;

namespace StreamMesh.UI.Views
{
    public partial class StatsView : UserControl
    {
        private readonly DatabaseEngine _db = new DatabaseEngine();
        private readonly StunEngine _stun = new StunEngine();

        public StatsView()
        {
            InitializeComponent();
            this.Loaded += async (s, e) => await LoadStatsAsync();
        }

        public async Task LoadStatsAsync()
        {
            try
            {
                // Kanal ve Kaynak Sayıları
                int totalChannels = await _db.GetTotalChannelCountAsync();
                TxtTotalChannels.Text = totalChannels.ToString("N0");

                var allChannels = await _db.GetAllChannelsAsync();
                int tvCount = allChannels.Count(c => string.Equals(c.Category, "TV", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(c.Category));
                int movieCount = allChannels.Count(c => string.Equals(c.Category, "VOD", StringComparison.OrdinalIgnoreCase) || string.Equals(c.Category, "Film", StringComparison.OrdinalIgnoreCase));
                int seriesCount = allChannels.Count(c => string.Equals(c.Category, "Dizi", StringComparison.OrdinalIgnoreCase));
                TxtChannelBreakdown.Text = $"TV: {tvCount} | Film: {movieCount} | Dizi: {seriesCount}";

                // P2P Eşleri
                int peers = await _stun.GetOnlinePeerCountAsync();
                TxtOnlinePeers.Text = peers.ToString();

                // IPTV Hesapları ve M3U Kaynakları
                var accounts = _db.GetAllIptvAccounts();
                TxtIptvAccounts.Text = accounts.Count.ToString();

                var sources = _db.GetAllM3uSources();
                TxtM3uSources.Text = $"{sources.Count} Kaynak Kayıtlı";

                // RAM ve Disk
                using (var proc = Process.GetCurrentProcess())
                {
                    long memMb = proc.WorkingSet64 / (1024 * 1024);
                    TxtRamUsage.Text = $"{memMb} MB";
                }

                string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "streammesh.db");
                if (File.Exists(dbPath))
                {
                    var fi = new FileInfo(dbPath);
                    TxtDbSize.Text = $"Veritabanı: {(fi.Length / (1024.0 * 1024.0)):F2} MB";
                }
                else
                {
                    TxtDbSize.Text = "Veritabanı: Aktif (SQLite)";
                }

                // Port & Sunucu Bilgileri
                TxtServerPort.Text = _db.GetSetting("ServerPort", "8080");
                TxtAppVersion.Text = $"StreamMesh v{UpdateService.GetCurrentVersion()}";
                TxtDotNetVersion.Text = Environment.Version.ToString();
                TxtOsInfo.Text = Environment.OSVersion.ToString();

                string aiUrl = _db.GetSetting("AiUrl", "http://localhost:11434/api/chat");
                string aiModel = _db.GetSetting("AiModel", "llama3");
                TxtAiEngineStatus.Text = $"{aiModel} ({new Uri(aiUrl).Host})";

                TxtDiagnosticLog.Text = _stun.GenerateDiagnosticReport();
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[StatsView] İstatistikler yüklenirken hata: {ex.Message}");
            }
        }

        private async void RefreshStats_Click(object sender, RoutedEventArgs e)
        {
            await LoadStatsAsync();
        }

        private void ClearCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                MessageBox.Show("Sistem bellek ve arabelleği başarıyla temizlendi.", "StreamMesh Bakım", MessageBoxButton.OK, MessageBoxImage.Information);
                _ = LoadStatsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Temizleme sırasında hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
