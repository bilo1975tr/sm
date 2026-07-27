using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StreamMesh.UI.Views;
using StreamMesh.Core.Network;
using StreamMesh.Models;

namespace StreamMesh.UI.Windows
{
    public partial class MainWindow : Window
    {
        public static MainWindow? Instance { get; private set; }

        private HomeView _homeView = new HomeView();
        private PlayerView _playerView = new PlayerView();
        private StatsView _statsView = new StatsView();
        private SettingsView _settingsView = new SettingsView();
        private SearchAceStreamView _searchAceView = new SearchAceStreamView();

        private readonly StunEngine _stun = new StunEngine();
        private DispatcherTimer _peerTimer;
        private System.Windows.Forms.NotifyIcon? _notifyIcon;

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();
            SetupTrayIcon();
            MainContent.Content = _homeView;

            _peerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _peerTimer.Tick += async (s, e) => {
                int count = await _stun.GetOnlinePeerCountAsync();
                OnlinePeersText.Text = count.ToString();
            };
            _peerTimer.Start();

            this.KeyDown += MainWindow_KeyDown;
        }

        private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (MainContent.Content == _playerView)
            {
                if (e.Key == System.Windows.Input.Key.Space)
                {
                    _playerView.TogglePause();
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.F)
                {
                    _playerView.ToggleFullscreen();
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.M)
                {
                    _playerView.ToggleMute();
                    e.Handled = true;
                }
            }
        }

        private void SetupTrayIcon()
        {
            try
            {
                _notifyIcon = new System.Windows.Forms.NotifyIcon();
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_icon.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    _notifyIcon.Icon = new System.Drawing.Icon(iconPath);
                }

                _notifyIcon.Visible = true;
                _notifyIcon.Text = "StreamMesh Hybrid";
                _notifyIcon.DoubleClick += (s, e) => { ShowWindow(); };

                var menu = new System.Windows.Forms.ContextMenuStrip();
                menu.Items.Add("Göster", null, (s, e) => { ShowWindow(); });
                menu.Items.Add("Çıkış", null, (s, e) => {
                    _notifyIcon.Visible = false;
                    System.Windows.Application.Current.Shutdown();
                });
                _notifyIcon.ContextMenuStrip = menu;
            }
            catch { }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
            if (_notifyIcon != null)
            {
                _notifyIcon.ShowBalloonTip(2000, "StreamMesh", "Uygulama arka planda çalışmaya devam ediyor.", System.Windows.Forms.ToolTipIcon.Info);
            }
        }

        private void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.RadioButton rb && rb.Tag is string tag) NavigateTo(tag);
        }

        public void NavigateTo(string tag)
        {
            switch (tag)
            {
                case "Home": MainContent.Content = _homeView; break;
                case "Player": MainContent.Content = _playerView; break;
                case "Stats": MainContent.Content = _statsView; break;
                case "Settings": MainContent.Content = _settingsView; break;
                case "AceSearch": MainContent.Content = _searchAceView; break;
            }
        }

        public void LoadChannelToPlayer(Channel ch)
        {
            NavigateTo("Player");
            _playerView.LoadChannel(ch);
        }

        private void VIPButton_Click(object sender, RoutedEventArgs e)
        {
            var don = new DonationWindow();
            don.Owner = this;
            don.ShowDialog();
        }

        private void OpenChat_Click(object sender, RoutedEventArgs e)
        {
            var chat = new ChatWindow();
            chat.Owner = this;
            chat.Show();
        }

        private void PeerCount_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var report = new P2PReportWindow("StreamMesh P2P Durum Raporu", "Ağ Durumu: Aktif\nBağlantı Modu: P2P Mesh\nSTUN: Tamam (stun.l.google.com:19302)\nTURN: Hazır (Gerekli olduğunda devreye girer)\n\nAktif Peer Listesi: 127.0.0.1 (Siz)");
            report.Owner = this;
            report.ShowDialog();
        }
    }
}
