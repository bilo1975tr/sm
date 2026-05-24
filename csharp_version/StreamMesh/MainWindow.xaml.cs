using System;
using System.ComponentModel;
using System.Windows;
using LibVLCSharp.Shared;
using StreamMesh.Views;
using StreamMesh.Services;

namespace StreamMesh
{
    public partial class MainWindow : Window
    {
        private PlayerView _playerView;
        private HomeView _homeView;
        private StatsView _statsView;
        private SettingsView _settingsView;

        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private bool _isRealClose = false;

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
            
            InitializeTrayIcon();
            
            // Versiyonu yükle
            try
            {
                // VERSION dosyası ana dizinde
                string versionFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../VERSION");
                if (System.IO.File.Exists(versionFile))
                {
                    VersionText.Text = "v" + System.IO.File.ReadAllText(versionFile).Trim();
                }
                else
                {
                    // Alternatif arama (publish klasörü için)
                    versionFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VERSION");
                    if (System.IO.File.Exists(versionFile))
                        VersionText.Text = "v" + System.IO.File.ReadAllText(versionFile).Trim();
                }
            }
            catch { VersionText.Text = "v0.0 alfa"; }

            // Core'u sadece ana pencere başlarken 1 kere çağırıyoruz.
            Core.Initialize();

            // Sadece gerekli olduğunda yüklenmeleri için ilk açılışta Home ve Player hazırlıyoruz
            _playerView = new PlayerView();
            _homeView = new HomeView();
            
            _homeView.ChannelSelectedEvent += OnChannelSelected;

            // Varsayılan sayfa: Kütüphane (Home)
            NavHome.IsChecked = true;
            MainContent.Content = _homeView;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await InventoryService.CheckAndDownloadInventoryAsync();

            if (InventoryService.AreComponentsMissing())
            {
                var missingWindow = new StreamMesh.Windows.MissingComponentsWindow();
                missingWindow.ShowDialog();
            }
        }

        private void OnChannelSelected(Models.Channel channel, List<Models.Channel> playlist)
        {
            // Switch to Player tab
            NavPlayer.IsChecked = true;
            MainContent.Content = _playerView;
            
            // Tell Player to load the channel and the playlist
            _playerView.LoadChannel(channel, playlist);
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender == NavHome)
            {
                if (_homeView == null) _homeView = new HomeView();
                MainContent.Content = _homeView;
            }
            else if (sender == NavPlayer)
            {
                if (_playerView == null) _playerView = new PlayerView();
                MainContent.Content = _playerView;
            }
            else if (sender == NavStats)
            {
                if (_statsView == null) _statsView = new StatsView();
                MainContent.Content = _statsView;
            }
            else if (sender == NavSettings)
            {
                if (_settingsView == null) _settingsView = new SettingsView();
                MainContent.Content = _settingsView;
            }
        }

        public void ToggleFullscreen(bool isFullscreen)
        {
            if (isFullscreen)
            {
                MainSidebar.Visibility = Visibility.Collapsed;
                SidebarColumn.Width = new GridLength(0);
                MainContent.Margin = new Thickness(0);
                this.WindowStyle = WindowStyle.None;
                this.WindowState = WindowState.Maximized;
                this.ResizeMode = ResizeMode.NoResize;
                this.Topmost = true; // Video önceliği
            }
            else
            {
                MainSidebar.Visibility = Visibility.Visible;
                SidebarColumn.Width = new GridLength(80);
                MainContent.Margin = new Thickness(20);
                this.WindowStyle = WindowStyle.SingleBorderWindow;
                this.WindowState = WindowState.Normal;
                this.ResizeMode = ResizeMode.CanResize;
                this.Topmost = false;
            }
            LogService.Log($"Fullscreen toggled: {isFullscreen}");
        }

        private void VIPButton_Click(object sender, RoutedEventArgs e)
        {
            var donationWindow = new Views.DonationWindow();
            donationWindow.Owner = this;
            donationWindow.ShowDialog();
        }

        private void InitializeTrayIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            try
            {
                _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
            }
            catch
            {
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }
            _notifyIcon.Text = "StreamMesh";
            _notifyIcon.Visible = true;

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();

            var showItem = new System.Windows.Forms.ToolStripMenuItem("Göster");
            showItem.Click += (s, e) => ShowWindow();

            var exitItem = new System.Windows.Forms.ToolStripMenuItem("Çıkış");
            exitItem.Click += (s, e) =>
            {
                _isRealClose = true;
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                
                new AceStreamService().KillEngine();
                
                this.Close();
            };

            contextMenu.Items.Add(showItem);
            contextMenu.Items.Add(exitItem);
            _notifyIcon.ContextMenuStrip = contextMenu;

            _notifyIcon.DoubleClick += (s, ev) => ShowWindow();
        }

        private void ShowWindow()
        {
            this.Show();
            if (this.WindowState == WindowState.Minimized)
            {
                this.WindowState = WindowState.Normal;
            }
            this.Activate();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isRealClose)
            {
                e.Cancel = true;
                this.Hide();

                // Aktif yayınları ve AceStream'i durdur
                _playerView?.StopPlayback();
                new AceStreamService().KillEngine();

                _notifyIcon.ShowBalloonTip(2000, "StreamMesh", "Uygulama arka planda çalışmaya devam ediyor.", System.Windows.Forms.ToolTipIcon.Info);
            }
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _playerView?.Dispose();
            base.OnClosed(e);
        }
    }
}

