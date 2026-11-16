using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Threading;
using StreamMesh.UI.Views;
using StreamMesh.Core.Network;
using StreamMesh.Core.Media;
using StreamMesh.Models;
using StreamMesh.Core.Utils;

using StreamMesh.UI.ViewModels;

namespace StreamMesh.UI.Windows
{
    public partial class MainWindow : Window
    {
        public static MainWindow? Instance { get; private set; }
        public MainWindowViewModel ViewModel { get; } = new MainWindowViewModel();

        private HomeView _homeView = new HomeView();
        private PlayerView _playerView = new PlayerView();
        private SettingsView? _settingsView;
        private SearchAceStreamView? _searchAceView;
        private EpgGuideView? _epgGuideView;

        private readonly UpdateService _updateService = new UpdateService();
        private DispatcherTimer _epgTimer;
        private DispatcherTimer _metaTimer;
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private bool _isExplicitExit = false;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();

            try
            {
                // Set Window Icon from Vector Resource (Runtime Render)
                var windowIcon = RenderVectorIcon(256);
                if (windowIcon != null) this.Icon = windowIcon;
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[STARTUP] Pencere ikonu yükleme hatası: {ex.Message}");
            }

            try
            {
                SetupTrayIcon();
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[STARTUP] Tepsi ikonu yükleme hatası: {ex.Message}");
            }

            DataContext = ViewModel;
            MainContent.Content = _homeView;

            Title = $"StreamMesh Hybrid v{ViewModel.Version}";

            UpdateService.OnVersionUpdated += (newVer) => {
                Dispatcher.Invoke(() => {
                    ViewModel.Version = newVer;
                    Title = $"StreamMesh Hybrid v{newVer}";
                    if (_notifyIcon != null) _notifyIcon.Text = $"StreamMesh Hybrid v{newVer}";
                });
            };

            // AceStream motoru kuruluysa uygulama acilisinda arka planda otomatik baslat
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(3500).ConfigureAwait(false);
                    string aceExe = AceEngine.GetEngineExecutablePath();
                    if (System.IO.File.Exists(aceExe))
                    {
                        LogService.LogInfo("[STARTUP] AceStream motoru bulundu, arka planda başlatılıyor...");
                        await AceEngine.Instance.StartEngineIfNotRunningAsync().ConfigureAwait(false);
                    }
                }
                catch { }
            });

            _ = Task.Run(async () =>
            {
                await Task.Delay(5000).ConfigureAwait(false);
                CheckForUpdatesAsync();
            });

            _epgTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(12) };
            _epgTimer.Tick += async (s, e) => { await RunAutoEpgUpdateAsync(); };
            _epgTimer.Start();
            _ = Task.Run(async () => { await Task.Delay(10000).ConfigureAwait(false); await RunAutoEpgUpdateAsync(); });

            // 30-Minute Film & Series Metadata Auto-Enricher Worker (Açılışta 90 sn sonra sessizce çalışır)
            _metaTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
            _metaTimer.Tick += async (s, e) => { await RunAutoMetadataEnrichmentAsync(); };
            _metaTimer.Start();
            _ = Task.Run(async () => { await Task.Delay(90000); await RunAutoMetadataEnrichmentAsync(); });

            this.KeyDown += MainWindow_KeyDown;
            this.StateChanged += MainWindow_StateChanged;
            this.IsVisibleChanged += MainWindow_IsVisibleChanged;

            // Pre-warm SettingsView in background so opening settings is instantaneous
            _ = Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    _settingsView ??= new SettingsView();
                }
                catch { }
            }, DispatcherPriority.Background);
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                if (this.Visibility == Visibility.Visible)
                {
                    _playerView.PauseForMinimize();
                }
            }
            else if (this.WindowState == WindowState.Normal || this.WindowState == WindowState.Maximized)
            {
                if (this.Visibility == Visibility.Visible)
                {
                    _playerView.ResumeFromMinimize();
                }
            }
        }

        private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.Visibility != Visibility.Visible)
            {
                _playerView.Stop();
            }
        }

        private string _availableRemoteVersion = "";

        private async void CheckForUpdatesAsync()
        {
            try
            {
                var updatePlugin = StreamMesh.Core.Plugins.PluginManager.Instance.GetPluginById("auto_updater") as StreamMesh.Core.Plugins.UpdatePlugin;
                if (updatePlugin != null && (!updatePlugin.IsEnabled || !updatePlugin.GetBool("CheckOnStartup")))
                {
                    return;
                }

                var (hasUpdate, remoteVer) = await _updateService.CheckForUpdateAsync();
                if (hasUpdate)
                {
                    _availableRemoteVersion = remoteVer;
                    ViewModel.IsUpdateAvailable = true;
                    ViewModel.UpdateMessage = $"Yeni Güncelleme Var! (v{remoteVer}) - Tıkla ve Güncelle";
                    UpdateBadgeButton.Visibility = Visibility.Visible;
                }
            }
            catch { }
        }

        private async void UpdateBadge_Click(object sender, RoutedEventArgs e)
        {
            string targetVer = !string.IsNullOrWhiteSpace(_availableRemoteVersion) ? _availableRemoteVersion : UpdateService.GetCurrentVersion();

            var result = System.Windows.MessageBox.Show(
                $"StreamMesh v{targetVer} sürümüne otomatik güncellensin mi?\n\n- Yeni kurulum paketi indirilecek\n- Uygulama otomatik kapanıp yeni sürüm kurulacak\n- Kurulum bittiğinde uygulama otomatik yeniden açılacaktır.",
                "StreamMesh Otomatik Güncelleme",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            var progressWin = new UpdateProgressWindow(targetVer);
            progressWin.Owner = this;
            progressWin.Show();

            try
            {
                UpdateBadgeButton.IsEnabled = false;

                bool success = await _updateService.DownloadAndInstallUpdateAsync(targetVer, (percent, msg, detail) => {
                    progressWin.UpdateProgress(percent, msg, detail);
                });

                if (!success)
                {
                    progressWin.ShowError("Kurulum dosyası indirilemedi veya başlatılamadı.");
                    UpdateBadgeButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("UpdateBadge_Click error", ex);
                progressWin.ShowError(ex.Message);
                UpdateBadgeButton.IsEnabled = true;
            }
        }

        private bool _isFullscreen = false;
        private bool _isMiniPlayer = false;
        private double _preMiniWidth = 1300;
        private double _preMiniHeight = 800;
        private double _preMiniLeft = 100;
        private double _preMiniTop = 100;

        public void SetFullscreen(bool isFullscreen)
        {
            if (_isMiniPlayer && isFullscreen) SetMiniPlayer(false);
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

        public void SetMiniPlayer(bool enable)
        {
            if (_isMiniPlayer == enable) return;
            _isMiniPlayer = enable;

            if (enable)
            {
                if (_isFullscreen) SetFullscreen(false);
                _preMiniWidth = this.Width > 0 ? this.Width : 1300;
                _preMiniHeight = this.Height > 0 ? this.Height : 800;
                _preMiniLeft = this.Left;
                _preMiniTop = this.Top;

                SidebarBorder.Visibility = Visibility.Collapsed;
                SidebarColumn.Width = new GridLength(0);
                TopOverlay.Visibility = Visibility.Collapsed;
                this.WindowState = WindowState.Normal;
                this.WindowStyle = WindowStyle.None;
                this.ResizeMode = ResizeMode.CanResizeWithGrip;
                this.Topmost = true;
                this.Width = 480;
                this.Height = 270;

                var workArea = SystemParameters.WorkArea;
                this.Left = Math.Max(workArea.Left, workArea.Right - 500);
                this.Top = Math.Max(workArea.Top, workArea.Bottom - 290);
                this.MouseLeftButtonDown += MiniPlayer_MouseLeftButtonDown;
            }
            else
            {
                this.MouseLeftButtonDown -= MiniPlayer_MouseLeftButtonDown;
                this.Topmost = false;
                this.WindowStyle = WindowStyle.SingleBorderWindow;
                this.ResizeMode = ResizeMode.CanResize;
                SidebarBorder.Visibility = Visibility.Visible;
                SidebarColumn.Width = new GridLength(80);
                TopOverlay.Visibility = Visibility.Visible;
                this.Width = _preMiniWidth;
                this.Height = _preMiniHeight;
                this.Left = _preMiniLeft;
                this.Top = _preMiniTop;
            }
        }

        public void ToggleMiniPlayer()
        {
            SetMiniPlayer(!_isMiniPlayer);
        }

        private void MiniPlayer_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isMiniPlayer && e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }

        public void TryDragMiniPlayer()
        {
            if (_isMiniPlayer && System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }

        public void HandleForwardedKeyDown(System.Windows.Input.KeyEventArgs e) => MainWindow_KeyDown(this, e);

        private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape && _isMiniPlayer)
            {
                SetMiniPlayer(false);
                e.Handled = true;
                return;
            }

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
                else if (e.Key == System.Windows.Input.Key.Left)
                {
                    _playerView.Rewind(10000);
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.Right)
                {
                    _playerView.Forward(10000);
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.Up)
                {
                    _playerView.ChangeVolume(5);
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.Down)
                {
                    _playerView.ChangeVolume(-5);
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.L)
                {
                    _playerView.GoLive();
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

        private static System.Windows.Media.Imaging.RenderTargetBitmap? RenderVectorIcon(int size)
        {
            try
            {
                if (System.Windows.Application.Current?.Resources["AppIconImage"] is System.Windows.Media.DrawingImage drawingImage)
                {
                    var visual = new System.Windows.Media.DrawingVisual();
                    using (var context = visual.RenderOpen())
                    {
                        context.DrawDrawing(drawingImage.Drawing);
                    }

                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(visual);
                    return bitmap;
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[STARTUP] RenderVectorIcon hatası: {ex.Message}");
            }
            return null;
        }

        private System.Drawing.Icon GetTrayIcon()
        {
            try
            {
                // Render Vector to Tray Icon
                var bitmap = RenderVectorIcon(64);
                if (bitmap != null)
                {
                    using var stream = new System.IO.MemoryStream();
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    encoder.Save(stream);
                    using var drawingBmp = new System.Drawing.Bitmap(stream);
                    IntPtr hIcon = drawingBmp.GetHicon();
                    var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();
                    DestroyIcon(hIcon);
                    return icon;
                }
            }
            catch { }

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
                _notifyIcon.Text = $"StreamMesh Hybrid v{UpdateService.GetCurrentVersion()}";

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
                case "EpgGuide":
                    _epgGuideView ??= new EpgGuideView();
                    MainContent.Content = _epgGuideView;
                    _ = _epgGuideView.LoadGuideAsync();
                    break;
                case "Settings":
                    _settingsView ??= new SettingsView();
                    MainContent.Content = _settingsView;
                    break;
                case "AceSearch":
                    _searchAceView ??= new SearchAceStreamView();
                    MainContent.Content = _searchAceView;
                    break;
            }
        }

        public void LoadChannelToPlayer(Channel ch)
        {
            NavigateTo("Player");
            _playerView.LoadChannel(ch);
        }

        public void PlayCatchup(Channel ch, EpgProgram prog)
        {
            NavigateTo("Player");
            _playerView.PlayCatchupProgram(ch, prog);
        }

        public async System.Threading.Tasks.Task RunAutoEpgUpdateAsync(bool force = false)
        {
            try
            {
                var epgPlugin = StreamMesh.Core.Plugins.PluginManager.Instance.GetPluginById("epg_guide") as StreamMesh.Core.Plugins.EpgPlugin;
                if (!force && epgPlugin != null && !epgPlugin.IsEnabled)
                {
                    return;
                }

                var db = StreamMesh.Core.Database.DatabaseEngine.Instance;

                if (epgPlugin == null || epgPlugin.GetBool("AutoPurgeExpired"))
                {
                    await db.CleanupOldEpgProgramsAsync(2);
                }

                int epgCount = await db.GetEpgProgramsCountAsync();
                string lastEpgStr = db.GetSetting("LastAutoEpgUpdate", "0");
                long.TryParse(lastEpgStr, out long lastEpgUnix);
                long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                int refreshHours = epgPlugin?.GetInt("AutoRefreshHours", 12) ?? 12;
                long refreshSeconds = Math.Max(1, refreshHours) * 3600L;

                if (force || epgCount == 0 || (nowUnix - lastEpgUnix >= refreshSeconds))
                {
                    LogService.LogInfo($"[AutoEPG] EPG güncellemesi başlatılıyor (force={force}, mevcutKayıt={epgCount})...");
                    var epgSources = db.GetEpgSources();
                    if (epgSources == null || epgSources.Count == 0)
                    {
                        var defaultEpgs = new[]
                        {
                            "https://iptv-epg.org/files/epg-tr.xml.gz",
                            "https://iptv-epg.org/files/epg-de.xml.gz",
                            "https://raw.githubusercontent.com/epgshare01/XMLTV/master/epg_ripper_TR1.xml.gz",
                            "https://raw.githubusercontent.com/epgshare01/XMLTV/master/epg_ripper_DE1.xml.gz"
                        };
                        foreach (var src in defaultEpgs) db.AddEpgSource(src);
                        epgSources = db.GetEpgSources();
                    }

                    if (epgSources != null && epgSources.Count > 0)
                    {
                        var epgEng = new StreamMesh.Core.Media.EpgEngine();
                        foreach (var url in epgSources)
                        {
                            if (!string.IsNullOrWhiteSpace(url))
                            {
                                try { await epgEng.LoadEpgAsync(url).ConfigureAwait(false); }
                                catch (Exception ex) { LogService.LogWarning($"[AutoEPG] EPG kaynağı atlandı ({url}): {ex.Message}"); }
                            }
                        }
                    }
                    db.SetSetting("LastAutoEpgUpdate", nowUnix.ToString());
                    StreamMesh.Core.Database.DatabaseEngine.NotifyDatabaseUpdated();
                    LogService.LogInfo("[AutoEPG] Otomatik EPG güncellemesi başarıyla tamamlandı.");
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("[AutoEPG] Otomatik EPG güncelleme hatası", ex);
            }
        }

        private async System.Threading.Tasks.Task RunAutoMetadataEnrichmentAsync()
        {
            try
            {
                var metaPlugin = StreamMesh.Core.Plugins.PluginManager.Instance.GetPluginById("metadata_enricher") as StreamMesh.Core.Plugins.MetadataPlugin;
                if (metaPlugin != null && !metaPlugin.IsEnabled)
                {
                    return;
                }

                var db = StreamMesh.Core.Database.DatabaseEngine.Instance;
                var metaEng = new StreamMesh.Core.Media.MetadataEngine();
                int dailyLimit = Math.Max(10, metaPlugin?.GetInt("DailyQueryLimit", 1000) ?? 1000);

                var stats = db.GetDailyQueryStats();
                if (stats.count >= dailyLimit - 50)
                {
                    LogService.LogInfo($"[AutoMetadata] Günlük meta veri sorgu limitine ({dailyLimit}) yaklaşıldı. Otomatik zenginleştirme duraklatıldı.");
                    return;
                }

                var allChannels = await db.GetAllChannelsAsync();
                var pendingMedia = allChannels.Where(c =>
                    !StreamMesh.Core.Media.MetadataEngine.IsLikelyLiveTv(c) &&
                    (c.Category == "Film" || c.Category == "Filmler" || c.Category == "Movie" || c.Category == "Dizi" || c.Category == "Series" || !string.IsNullOrWhiteSpace(c.SeriesBaseName)) &&
                    string.IsNullOrWhiteSpace(c.Overview)
                ).Take(50).ToList();

                if (pendingMedia.Count > 0)
                {
                    LogService.LogInfo($"[AutoMetadata] {pendingMedia.Count} adet eksik açıklamalı film/dizi zenginleştiriliyor...");
                    foreach (var item in pendingMedia)
                    {
                        try
                        {
                            var curStats = db.GetDailyQueryStats();
                            if (curStats.count >= dailyLimit - 50) break;

                            await metaEng.EnrichChannelAsync(item);
                            await System.Threading.Tasks.Task.Delay(500);
                        }
                        catch (Exception innerEx)
                        {
                            LogService.LogError($"[AutoMetadata] Kanal metadata hatası: {item.Name}", innerEx);
                        }
                    }
                    LogService.LogInfo("[AutoMetadata] Otomatik metadata taraması tamamlandı.");
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("[AutoMetadata] Otomatik metadata tarama hatası", ex);
            }
        }
    }
}
