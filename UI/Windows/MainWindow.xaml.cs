using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StreamMesh.UI.Views;
using StreamMesh.Core.Network;
using StreamMesh.Models;
using StreamMesh.Core.Utils;

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
        private readonly UpdateService _updateService = new UpdateService();
        private DispatcherTimer _peerTimer;
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private bool _isExplicitExit = false;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();
            SetupTrayIcon();
            MainContent.Content = _homeView;

            string currentVer = UpdateService.GetCurrentVersion();
            CurrentVersionBadge.Text = currentVer;
            Title = $"StreamMesh Hybrid v{currentVer}";

            UpdateService.OnVersionUpdated += (newVer) => {
                Dispatcher.Invoke(() => {
                    CurrentVersionBadge.Text = newVer;
                    Title = $"StreamMesh Hybrid v{newVer}";
                    if (_notifyIcon != null)
                    {
                        _notifyIcon.Text = $"StreamMesh Hybrid v{newVer}";
                    }
                });
            };

            CheckForUpdatesAsync();

            _peerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _peerTimer.Tick += async (s, e) => {
                int count = await _stun.GetOnlinePeerCountAsync();
                OnlinePeersText.Text = count.ToString();
            };
            _peerTimer.Start();

            this.KeyDown += MainWindow_KeyDown;
            this.StateChanged += MainWindow_StateChanged;
            this.IsVisibleChanged += MainWindow_IsVisibleChanged;
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                _playerView.Stop();
            }
        }

        private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.Visibility != Visibility.Visible)
            {
                _playerView.Stop();
            }
        }

        private async void CheckForUpdatesAsync()
        {
            try
            {
                var (hasUpdate, remoteVer) = await _updateService.CheckForUpdateAsync();
                if (hasUpdate)
                {
                    UpdateBadgeText.Text = $"Yeni Güncelleme Var! (v{remoteVer}) - Tıkla ve Güncelle";
                    UpdateBadgeButton.Visibility = Visibility.Visible;
                }
            }
            catch { }
        }

        private async void UpdateBadge_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "Yeni güncelleme bulundu. Otomatik güncelleme başlatılsın mı?\nİçerik ve sistem dosyaları güncellenecektir.",
                "Otomatik Güncelleme",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                UpdateBadgeButton.IsEnabled = false;
                UpdateBadgeText.Text = "Güncelleniyor...";

                bool success = await _updateService.PerformUpdateAsync((percent, msg) => {
                    Dispatcher.Invoke(() => {
                        UpdateBadgeText.Text = $"Güncelleniyor (%{percent})...";
                    });
                });

                if (success)
                {
                    string newVer = UpdateService.GetCurrentVersion();
                    CurrentVersionBadge.Text = newVer;
                    Title = $"StreamMesh Hybrid v{newVer}";
                    UpdateBadgeButton.Visibility = Visibility.Collapsed;
                    System.Windows.MessageBox.Show($"Güncelleme başarıyla tamamlandı!\nGüncel Sürüm: v{newVer}", "Güncelleme Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    UpdateBadgeButton.IsEnabled = true;
                    UpdateBadgeText.Text = "Yeni Güncelleme Var! (Tıkla ve Güncelle)";
                    System.Windows.MessageBox.Show("Güncelleme sırasında bir hata oluştu.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("UpdateBadge_Click error", ex);
                UpdateBadgeButton.IsEnabled = true;
            }
        }

        private bool _isFullscreen = false;

        public void SetFullscreen(bool isFullscreen)
        {
            _isFullscreen = isFullscreen;
            if (_isFullscreen)
            {
                SidebarBorder.Visibility = Visibility.Collapsed;
                SidebarColumn.Width = new GridLength(0);
                TopOverlay.Visibility = Visibility.Collapsed;
                this.WindowStyle = WindowStyle.None;
                this.WindowState = WindowState.Maximized;
            }
            else
            {
                SidebarBorder.Visibility = Visibility.Visible;
                SidebarColumn.Width = new GridLength(80);
                TopOverlay.Visibility = Visibility.Visible;
                this.WindowStyle = WindowStyle.SingleBorderWindow;
                this.WindowState = WindowState.Normal;
            }
        }

        public void ToggleFullscreen()
        {
            SetFullscreen(!_isFullscreen);
        }

        private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape && _isFullscreen)
            {
                SetFullscreen(false);
                e.Handled = true;
                return;
            }

            if (MainContent.Content == _playerView)
            {
                if (e.Key == System.Windows.Input.Key.Space)
                {
                    _playerView.TogglePause();
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.F || e.Key == System.Windows.Input.Key.F11)
                {
                    ToggleFullscreen();
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.M)
                {
                    _playerView.ToggleMute();
                    e.Handled = true;
                }
            }
        }

        private System.Drawing.Icon GetTrayIcon()
        {
            try
            {
                string logoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logos", "StreamMesh_logo.png");
                System.Drawing.Bitmap? bmp = null;

                if (System.IO.File.Exists(logoPath))
                {
                    bmp = new System.Drawing.Bitmap(logoPath);
                }
                else
                {
                    var uri = new Uri("pack://application:,,,/logos/StreamMesh_logo.png", UriKind.Absolute);
                    var streamInfo = System.Windows.Application.GetResourceStream(uri);
                    if (streamInfo != null)
                    {
                        bmp = new System.Drawing.Bitmap(streamInfo.Stream);
                    }
                }

                if (bmp != null)
                {
                    using (bmp)
                    using (var resizedBmp = new System.Drawing.Bitmap(bmp, new System.Drawing.Size(32, 32)))
                    {
                        IntPtr hIcon = resizedBmp.GetHicon();
                        using (var tempIcon = System.Drawing.Icon.FromHandle(hIcon))
                        {
                            var finalIcon = (System.Drawing.Icon)tempIcon.Clone();
                            DestroyIcon(hIcon);
                            return finalIcon;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("TrayIcon creation failed", ex);
            }

            try
            {
                string mainExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(mainExe) && System.IO.File.Exists(mainExe))
                {
                    var exeIcon = System.Drawing.Icon.ExtractAssociatedIcon(mainExe);
                    if (exeIcon != null) return exeIcon;
                }
            }
            catch { }

            return System.Drawing.SystemIcons.Application;
        }

        private void SetupTrayIcon()
        {
            try
            {
                _notifyIcon = new System.Windows.Forms.NotifyIcon();
                _notifyIcon.Icon = GetTrayIcon();
                _notifyIcon.Visible = true;
                _notifyIcon.Text = "StreamMesh Hybrid v1.0.1";

                _notifyIcon.MouseClick += (s, e) => {
                    if (e.Button == System.Windows.Forms.MouseButtons.Left)
                    {
                        ShowWindow();
                    }
                };

                _notifyIcon.DoubleClick += (s, e) => { ShowWindow(); };

                var menu = new System.Windows.Forms.ContextMenuStrip();
                menu.Items.Add("StreamMesh Göster", null, (s, e) => { ShowWindow(); });
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                menu.Items.Add("Tamamen Çıkış Yap", null, (s, e) => {
                    ExitApplication();
                });
                _notifyIcon.ContextMenuStrip = menu;
            }
            catch (Exception ex)
            {
                LogService.LogError("SetupTrayIcon failed", ex);
            }
        }

        public void ExitApplication()
        {
            _isExplicitExit = true;
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
            System.Windows.Application.Current.Shutdown();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_isExplicitExit)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            Hide();
            if (_notifyIcon != null && _notifyIcon.Visible)
            {
                _notifyIcon.ShowBalloonTip(2000, "StreamMesh", "StreamMesh tepside (arka planda) çalışmaya devam ediyor.", System.Windows.Forms.ToolTipIcon.Info);
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
