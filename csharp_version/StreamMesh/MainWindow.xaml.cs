using System;
using System.ComponentModel;
using System.Windows;
using LibVLCSharp.Shared;
using StreamMesh.Views;
using StreamMesh.Services;
using StreamMesh.Services.P2P;

namespace StreamMesh
{
    public partial class MainWindow : Window
    {
        private static MainWindow _instance;
        public static MainWindow Instance => _instance;

        private PlayerView _playerView;
        private HomeView _homeView;
        private StatsView _statsView;
        private SettingsView _settingsView;
        private SearchAceStreamView _searchView;

        public HomeView HomeView => _homeView;

        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private bool _isRealClose = false;

        public MainWindow()
        {
            _instance = this;
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
            
            InitializeTrayIcon();
            
            // Versiyonu yükle
            try
            {
                VersionText.Text = "v" + StreamMesh.Services.UpdateService.GetCurrentVersion();
            }
            catch { VersionText.Text = "v1.0.0"; }

            // Core'u sadece ana pencere başlarken 1 kere çağırıyoruz.
            Core.Initialize();

            // Sadece gerekli olduğunda yüklenmeleri için ilk açılışta Home hazırlıyoruz
            _homeView = new HomeView();
            
            _homeView.ChannelSelectedEvent += OnChannelSelected;

            // Varsayılan sayfa: Kütüphane (Home)
            NavHome.IsChecked = true;
            MainContent.Content = _homeView;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            LogService.Log("[Startup] MainWindow_Loaded started.");

            // Güncelleme kontrolü yap
            _ = StreamMesh.Services.UpdateService.CheckForUpdatesAsync();

            // Bileşen kontrolü ve indirme işlemlerini asenkron olarak arka plana alıyoruz
            _ = Task.Run(async () =>
            {
                try
                {
                    var invStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    await InventoryService.CheckAndDownloadInventoryAsync();
                    invStopwatch.Stop();
                    LogService.Log($"[Startup] InventoryService.CheckAndDownloadInventoryAsync took {invStopwatch.ElapsedMilliseconds} ms.");

                    if (InventoryService.AreComponentsMissing())
                    {
                        Dispatcher.Invoke(() =>
                        {
                            var missingWindow = new StreamMesh.Windows.MissingComponentsWindow();
                            missingWindow.ShowDialog();
                        });
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("Inventory check background task failed", ex);
                }
            });

            // EPG Otomatik Güncelleme Zamanlayıcısını Arka Planda Başlat (24 saatte bir günceller)
            _ = Task.Run(async () =>
            {
                try
                {
                    var epgService = new EpgService();
                    await epgService.StartAutoUpdateTimerAsync();
                }
                catch (Exception ex)
                {
                    LogService.LogError("EPG background auto update initialization failed", ex);
                }
            });

            // Haftalık Otomatik Güncelleme Arka Plan Görevi
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30));
                    var profile = UserService.GetProfile();
                    if (profile != null && profile.WeeklyMovieAndChannelUpdateEnabled)
                    {
                        if ((DateTime.Now - profile.LastMovieAndChannelUpdateTime).TotalDays >= 7)
                        {
                            LogService.Log("Weekly scheduled auto update triggered.");
                            await AutoUpdateService.PerformAutoUpdateAsync(msg => LogService.Log($"[AutoUpdate] {msg}"));
                            profile.LastMovieAndChannelUpdateTime = DateTime.Now;
                            UserService.SaveProfile(profile);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("Weekly background auto updater failure", ex);
                }
            });

            stopwatch.Stop();
            LogService.Log($"[Startup] MainWindow_Loaded completed in {stopwatch.ElapsedMilliseconds} ms.");
        }

        private void OnChannelSelected(Models.Channel channel, List<Models.Channel> playlist)
        {
            if (_playerView == null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _playerView = new PlayerView();
                sw.Stop();
                LogService.Log($"[LazyLoad] PlayerView created in {sw.ElapsedMilliseconds} ms.");
            }

            // Switch to Player tab
            NavPlayer.IsChecked = true;
            MainContent.Content = _playerView;
            
            // Tell Player to load the channel and the playlist
            _playerView.LoadChannel(channel, playlist);
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender != NavPlayer)
            {
                _playerView?.StopPlayback();
            }

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
            else if (sender == NavSearch)
            {
                if (_searchView == null) _searchView = new SearchAceStreamView();
                MainContent.Content = _searchView;
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

        private void AIChatButton_Click(object sender, RoutedEventArgs e)
        {
            var chatWindow = new StreamMesh.Windows.ChatWindow();
            chatWindow.Owner = this;
            chatWindow.Show();
        }

        private void InitializeTrayIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            try
            {
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                {
                    string exePath = process.MainModule?.FileName ?? System.IO.Path.Combine(AppContext.BaseDirectory, "StreamMesh.exe");
                    _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                }
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
            try
            {
                StreamMesh.Services.FirebaseQueueService.Instance.Stop();
            }
            catch { }
            _playerView?.Dispose();
            base.OnClosed(e);
        }
    }
}

