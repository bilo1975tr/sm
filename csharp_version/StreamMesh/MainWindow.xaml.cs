using System;
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

        public MainWindow()
        {
            InitializeComponent();
            
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

        protected override void OnClosed(EventArgs e)
        {
            _playerView?.Dispose();
            base.OnClosed(e);
        }
    }
}

