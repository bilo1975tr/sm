using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using StreamMesh.Services;

namespace StreamMesh.Views
{
    public partial class StatsView : UserControl
    {
        private DatabaseService _dbService;
        private System.Windows.Threading.DispatcherTimer _timer;

        public StatsView()
        {
            InitializeComponent();
            _dbService = new DatabaseService();
            LoadStats();
            
            _timer = new System.Windows.Threading.DispatcherTimer();
            _timer.Interval = System.TimeSpan.FromSeconds(5);
            _timer.Tick += (s, e) => LoadStats();
            _timer.Start();

            LogService.OnLogMessage += LogService_OnLogMessage;
            
            this.Unloaded += (s, e) => { 
                if (_timer != null) _timer.Stop(); 
                LogService.OnLogMessage -= LogService_OnLogMessage;
            };

            LoadInitialLogs();
        }

        private void LoadInitialLogs()
        {
            try
            {
                string logFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "app.log");
                if (System.IO.File.Exists(logFilePath))
                {
                    var lines = System.IO.File.ReadLines(logFilePath).TakeLast(50).ToList();
                    ConsoleOutput.Text = string.Join(Environment.NewLine, lines) + Environment.NewLine;
                    ConsoleOutput.ScrollToEnd();
                }
            }
            catch { }
        }

        private void LogService_OnLogMessage(string logLine)
        {
            Dispatcher.InvokeAsync(() => {
                ConsoleOutput.AppendText(logLine);
                ConsoleOutput.ScrollToEnd();
                
                // Limit the buffer so it doesn't grow infinitely in UI
                if (ConsoleOutput.Text.Length > 20000)
                {
                    ConsoleOutput.Text = ConsoleOutput.Text.Substring(10000); // Cut half
                }
            });
        }

        private void LoadStats()
        {
            var channels = _dbService.GetAllChannels();

            int m3uCount = channels.Count(c => c.SourceType == "M3U");
            int ytCount = channels.Count(c => c.SourceType == "YOUTUBE");
            int aceCount = channels.Count(c => c.SourceType == "ACESTREAM");

            TotalChannelsText.Text = $"Toplam Kanal: {channels.Count}";
            M3uChannelsText.Text = $"M3U: {m3uCount}";
            YoutubeChannelsText.Text = $"YouTube: {ytCount}";
            AceChannelsText.Text = $"AceStream: {aceCount}";

            // P2P gerçek mantık
            var activeNodes = StreamMesh.Services.P2P.P2pNodeManager.GetActiveNodes();
            bool isRunning = StreamMesh.Services.P2P.P2pService.IsRunning;

            ActivePeersText.Text = $"Aktif Eş: {activeNodes.Count}";
            SharedBytesText.Text = "Paylaşılan: Hesaplanıyor...";
            P2pStatusText.Text = isRunning ? "Durum: Aktif (Ağa Bağlı)" : "Durum: Pasif (Bağlantı Bekleniyor)";

            // Cloud Stats
            GitHubChanCountText.Text = $"GitHub'dan Gelen: {GitHubSyncService.LastPulledGitHubChannelCount}";
            GitHubLastSyncText.Text = $"Son Okuma: {(GitHubSyncService.LastGitHubPullTime > DateTime.MinValue ? GitHubSyncService.LastGitHubPullTime.ToString("HH:mm:ss") : "Bekleniyor")}";
            FirebasePushedText.Text = $"Firebase Havuza Gönderilen: {GitHubSyncService.TotalChannelsPushedToFirebase}";

            PeersList.ItemsSource = activeNodes;
        }

        private void RefreshStatsBtn_Click(object sender, RoutedEventArgs e)
        {
            LoadStats();
        }
    }
}
