using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Linq;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.Controls.WPF;
using StreamMesh.Models;
using StreamMesh.Core.Media;
using StreamMesh.Converters;
using StreamMesh.Core.Utils;
using StreamMesh.Core.Database;
using System.Threading.Tasks;
using System.Text;

namespace StreamMesh.UI.Views
{
    public partial class PlayerView : System.Windows.Controls.UserControl, IDisposable
    {
        private Player? _player;
        private Config? _config;

        private readonly YoutubeEngine _yt = new YoutubeEngine();
        private readonly AceEngine _ace = new AceEngine();
        private readonly DatabaseEngine _db = new DatabaseEngine();
        private static readonly LogoCacheConverter LogoConverter = new LogoCacheConverter();
        private readonly System.Threading.SemaphoreSlim _playSemaphore = new System.Threading.SemaphoreSlim(1, 1);

        private readonly EpgService _epgService = new EpgService();
        private System.Windows.Threading.DispatcherTimer? _osdTimer;
        private System.Windows.Threading.DispatcherTimer? _positionTimer;
        private Channel? _currentChannel;
        private List<EpgProgram> _currentChannelEpgList = new();

        private bool _isUserDraggingSlider = false;
        private bool _isMouseOverOsd = false;
        private DateTime _streamStartTime = DateTime.UtcNow;
        private static readonly SolidColorBrush LiveRedBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));
        private static readonly SolidColorBrush DelayedAmberBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11));
        private static readonly SolidColorBrush VodBlueBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235));

        private long _lastTeleportPositionMs = 0;
        private DateTime _lastSeekTime = DateTime.MinValue;

        public PlayerView()
        {
            InitializeComponent();
            this.Loaded += (s, e) =>
            {
                InitializePlayer();
                // Global fare dinleyicisi ekle
                System.Windows.Input.InputManager.Current.PostProcessInput += GlobalMouseTracker;
            };
            this.Unloaded += (s, e) =>
            {
                // Bellek sızıntısını önlemek için dinleyiciyi kaldır
                System.Windows.Input.InputManager.Current.PostProcessInput -= GlobalMouseTracker;
            };

            InitializeOsdTimer();
            InitializePositionTimer();
            this.Focusable = true;
        }

        private System.Windows.Point _lastMousePos;
        private void GlobalMouseTracker(object sender, System.Windows.Input.ProcessInputEventArgs e)
        {
            if (e.StagingItem.Input is System.Windows.Input.MouseEventArgs mouseArgs)
            {
                // Sadece fare hareket ettiğinde veya tıklandığında işlem yap
                if (mouseArgs.RoutedEvent == System.Windows.Input.Mouse.MouseMoveEvent ||
                    mouseArgs.RoutedEvent == System.Windows.Input.Mouse.MouseDownEvent)
                {
                    System.Windows.Point currentPos = mouseArgs.GetPosition(this);

                    // Fare bu kontrolün sınırları içindeyse
                    if (currentPos.X >= 0 && currentPos.Y >= 0 &&
                        currentPos.X <= this.ActualWidth && currentPos.Y <= this.ActualHeight)
                    {
                        // Fare gerçekten hareket ettiyse OSD'yi göster
                        if (currentPos != _lastMousePos)
                        {
                            _lastMousePos = currentPos;
                            ShowOsdTemporary();
                        }
                    }
                }
            }
        }

        private void InitializeOsdTimer()
        {
            _osdTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
            _osdTimer.Tick += (s, e) =>
            {
                if (_isUserDraggingSlider || _isMouseOverOsd) return;

                if (OsdPanel != null) OsdPanel.Visibility = Visibility.Collapsed;
                _osdTimer.Stop();
            };
        }

        public void ShowOsdTemporary()
        {
            Dispatcher.Invoke(() =>
            {
                if (OsdPanel != null) OsdPanel.Visibility = Visibility.Visible;
                _osdTimer?.Stop();
                _osdTimer?.Start();
            });
        }

        private void OsdPanel_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) { _isMouseOverOsd = true; _osdTimer?.Stop(); }
        private void OsdPanel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) { _isMouseOverOsd = false; _osdTimer?.Start(); }
        private void Player_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { ShowOsdTemporary(); }

        private bool IsCurrentStreamVod()
        {
            if (_currentChannel == null) return false;
            string cat = (_currentChannel.Category ?? "").Trim().ToLowerInvariant();
            if (cat.Contains("dizi") || cat.Contains("film") || cat.Contains("vod") || cat.Contains("sinema") || cat.Contains("movie") || cat.Contains("series"))
            {
                return true;
            }
            string url = (_currentChannel.Url ?? "").ToLowerInvariant();
            if (url.EndsWith(".mp4") || url.EndsWith(".mkv") || url.EndsWith(".avi") || url.Contains("/movie/") || url.Contains("/series/"))
            {
                return true;
            }
            return false;
        }

        private void InitializePositionTimer()
        {
            _positionTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _positionTimer.Tick += (s, e) =>
            {
                if (_player == null || _isUserDraggingSlider) return;

                // Seek işleminden hemen sonra UI'ın eski değere zıplamasını engelle (2.5sn koruma)
                if ((DateTime.Now - _lastSeekTime).TotalSeconds < 2.5) return;

                try
                {
                    long timeMs = _player.CurTime / 10000;
                    long lengthMs = _player.Duration / 10000;
                    bool isVod = IsCurrentStreamVod();

                    if (isVod && lengthMs > 0)
                    {
                        if (LiveBadge != null) LiveBadge.Background = VodBlueBrush;
                        if (LiveBadgeText != null) LiveBadgeText.Text = "🎬 VOD";

                        TimeCurrentText.Text = TimeSpan.FromMilliseconds(timeMs).ToString(@"hh\:mm\:ss");
                        TimeTotalText.Text = TimeSpan.FromMilliseconds(lengthMs).ToString(@"hh\:mm\:ss");

                        TimeSlider.Minimum = 0;
                        TimeSlider.Maximum = lengthMs;
                        TimeSlider.Value = Math.Clamp(timeMs, 0, lengthMs);
                    }
                    else
                    {
                        // Sync Slider with HLS Proxy Session for DVR
                        var proxySession = HlsProxyEngine.Instance.GetSession(_currentChannel?.Url ?? "");
                        if (proxySession != null && proxySession.Segments.Count > 0)
                        {
                            double totalSec = proxySession.TotalDurationSeconds;
                            long totalMsDvr = (long)(totalSec * 1000);

                            // Absolute Position = Initial Seek Point + Current Player Time
                            long absolutePosMs = _lastTeleportPositionMs + timeMs;
                            if (_lastTeleportPositionMs < 0) absolutePosMs = totalMsDvr; // Live mode

                            TimeSlider.Minimum = 0;
                            TimeSlider.Maximum = totalMsDvr;
                            TimeSlider.Value = absolutePosMs;

                            UpdateOsdTimeAndBadge(absolutePosMs, totalMsDvr, proxySession.StartWallClockTime);
                            TimeTotalText.Text = "CANLI ARŞİV";
                        }
                        else
                        {
                            TimeSlider.Minimum = 0;
                            TimeSlider.Maximum = 100;
                            if (!_isUserDraggingSlider) TimeSlider.Value = 100;
                            TimeCurrentText.Text = DateTime.Now.ToString("HH:mm:ss");
                            TimeTotalText.Text = "CANLI YAYIN";
                        }
                    }
                }
                catch { }
            };
            _positionTimer.Start();
        }

        private void UpdateOsdTimeAndBadge(long currentMs, long totalMs, DateTime startWallTime)
        {
            if (TimeCurrentText == null) return;

            // Calculate Aired Time
            DateTime airedTime = startWallTime.AddMilliseconds(currentMs);
            TimeCurrentText.Text = airedTime.ToString("HH:mm:ss");

            // Calculate Delay from Live Edge
            long offsetMs = totalMs - currentMs;
            if (offsetMs > 15000)
            {
                if (LiveBadge != null) LiveBadge.Background = DelayedAmberBrush;
                if (LiveBadgeText != null) LiveBadgeText.Text = "-" + TimeSpan.FromMilliseconds(offsetMs).ToString(@"hh\:mm\:ss");
            }
            else
            {
                if (LiveBadge != null) LiveBadge.Background = LiveRedBrush;
                if (LiveBadgeText != null) LiveBadgeText.Text = "🔴 CANLI";
            }
        }

        private async void InitializePlayer()
        {
            if (_player != null) return;

            try
            {
                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");

                // Check for FFmpeg DLLs
                for (int i = 0; i < 60; i++)
                {
                    if (Directory.Exists(ffmpegPath) && Directory.GetFiles(ffmpegPath, "avcodec*.dll").Length > 0) break;

                    Dispatcher.Invoke(() => {
                        OsdTitle.Text = $"Görüntü Motoru Hazırlanıyor... ({i}s)";
                        OsdPanel.Visibility = Visibility.Visible;
                    });
                    await Task.Delay(1000);
                }

                FlyleafHelper.SafeStart();

                _config = new Config();
                _player = new Player(_config);
                VideoPlayer.Player = _player;

                LogService.LogInfo("Player: Flyleaf initialized successfully");
                Dispatcher.Invoke(() => {
                    OsdTitle.Text = "Sistem Hazır.";
                    OsdPanel.Visibility = Visibility.Visible;
                    ShowOsdTemporary();
                });
            }
            catch (Exception ex)
            {
                LogService.LogError("Player: Flyleaf Init Failed", ex);
            }
        }

        private async Task<string> PrepareHlsStream(string url)
        {
            if (url.Contains(".m3u8"))
            {
                var session = await HlsProxyEngine.Instance.InspectAndPrepareHlsAsync(url);
                if (session != null)
                {
                    return HlsProxyEngine.Instance.GetProxyPlaybackUrl(url);
                }
            }
            return url;
        }

        public async void LoadChannel(Channel channel)
        {
            for (int i = 0; i < 20 && _player == null; i++) await Task.Delay(200);

            if (_player == null)
            {
                LogService.LogError("Player: Flyleaf player not ready.");
                return;
            }

            if (_currentChannel != null && IsCurrentStreamVod())
            {
                _currentChannel.LastPositionMs = _player.CurTime / 10000;
                await _db.SaveChannelAsync(_currentChannel);
            }

            _currentChannel = channel;
            _streamStartTime = DateTime.UtcNow;
            _lastTeleportPositionMs = -1; // Reset to Live mode

            await _playSemaphore.WaitAsync();

            try
            {
                // Ensure AceStream is stopped before starting a new one
                if (channel.SourceType == "ACESTREAM" || (channel.Url ?? "").Contains("acestream://"))
                {
                    await _ace.StopAllStreamsAsync();
                }

                _player.Stop();
                LogService.LogInfo($"Player: Stop signal sent to previous stream. Preparing: {channel.PrimaryName}");

                Dispatcher.Invoke(() => {
                    OsdTitle.Text = channel.PrimaryName;
                    OsdCategory.Text = channel.Category;
                    ShowOsdTemporary();
                    if (PlayPauseBtn != null) PlayPauseBtn.Content = "⏸";
                    try {
                        var convertedLogo = LogoConverter.Convert(channel.LogoUrl, typeof(ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture);
                        if (convertedLogo != null) OsdLogo.Source = (ImageSource)convertedLogo;
                    } catch { }
                });

                _currentChannelEpgList = await _epgService.GetChannelEpgHistoryAsync(channel);
                Dispatcher.Invoke(() => UpdateOsdEpgForTime(DateTime.Now));

                var rawUrls = (channel.Url ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                LogService.LogInfo($"Player: Found {rawUrls.Count} URL candidates in database.");

                foreach (var raw in rawUrls)
                {
                    string tryUrl = raw.Trim();
                    LogService.LogInfo($"Player: Processing candidate URL: {tryUrl}");

                    if (_ace.IsAceStreamUrl(tryUrl) || channel.SourceType == "ACESTREAM")
                    {
                        LogService.LogInfo("Player: AceStream playback sequence initiated.");
                        await _ace.StartEngineAsync();

                        var aceUrls = await _ace.GetHttpUrlsWithTokenAsync(tryUrl);
                        if (aceUrls != null && aceUrls.Count > 0)
                        {
                            tryUrl = aceUrls[0];
                            LogService.LogInfo($"Player: AceStream dynamic link created: {tryUrl}");

                            // VLC-Style: No pre-checks, no waiting, no session hijacking.
                            // We just pass the exact URL directly to the Flyleaf engine.
                            Dispatcher.Invoke(() => { OsdTitle.Text = "AceStream: Başlatılıyor..."; ShowOsdTemporary(); });
                        }
                        else
                        {
                            LogService.LogWarning("Player: AceStream engine failed to resolve any playback URLs.");
                        }
                    }
                    else if (tryUrl.Contains("youtube.com"))
                    {
                        LogService.LogInfo("Player: YouTube link detected, fetching stream manifest...");
                        tryUrl = await _yt.GetStreamUrlAsync(tryUrl) ?? tryUrl;
                    }

                    // HLS Proxy Integration (Only for non-AceStream links)
                    if (tryUrl.Contains(".m3u8") && !tryUrl.Contains(":6878/ace/"))
                    {
                        LogService.LogInfo("Player: HLS (m3u8) detected, preparing through Helper 2 (Proxy)...");
                        tryUrl = await PrepareHlsStream(tryUrl);
                    }

                    LogService.LogInfo($"Player: [FINAL] Flyleaf opening -> {tryUrl}");
                    _player.Open(tryUrl);

                    if (IsCurrentStreamVod() && channel.LastPositionMs > 0)
                    {
                        LogService.LogInfo($"Player: Resuming VOD at {channel.LastPositionMs}ms");
                        _player.Seek((int)channel.LastPositionMs);
                    }
                    break;
                }
            }
            catch (Exception ex) { LogService.LogError("Player: Load error", ex); }
            finally { _playSemaphore.Release(); }
        }

        public async void Stop()
        {
            _player?.Stop();
            if (_currentChannel?.SourceType == "ACESTREAM" || (_currentChannel?.Url?.Contains("acestream://") == true))
            {
                await _ace.StopAllStreamsAsync();
            }
        }

        public void PlayPause_Click(object sender, RoutedEventArgs e) { TogglePause(); }

        public void TogglePause()
        {
            if (_player == null) return;
            ShowOsdTemporary();

            if (_player.Status == Status.Playing)
            {
                _player.Pause();
                if (PlayPauseBtn != null) PlayPauseBtn.Content = "▶";
            }
            else
            {
                _player.Play();
                if (PlayPauseBtn != null) PlayPauseBtn.Content = "⏸";
            }
        }

        public void Rewind(int ms) { if (_player != null) _player.Seek((int)((_player.CurTime / 10000) - ms)); ShowOsdTemporary(); }
        public void Forward(int ms) { if (_player != null) _player.Seek((int)((_player.CurTime / 10000) + ms)); ShowOsdTemporary(); }
        public void ChangeVolume(int delta) { if (_player != null) _player.Audio.Volume = Math.Clamp(_player.Audio.Volume + delta, 0, 100); ShowOsdTemporary(); }
        public void ToggleMute() { if (_player != null) { _player.Audio.Mute = !_player.Audio.Mute; if (MuteBtn != null) MuteBtn.Content = _player.Audio.Mute ? "🔇" : "🔊"; } ShowOsdTemporary(); }

        private void TimeSlider_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { _isUserDraggingSlider = true; }

        private void TimeSlider_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isUserDraggingSlider = false;
            ApplySliderSeek(TimeSlider.Value);
        }

        private void TimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUserDraggingSlider)
            {
                // UI'ı sürükleme anında güncelle (Anlık sarı yazı ve saat geri bildirimi)
                var proxySession = HlsProxyEngine.Instance.GetSession(_currentChannel?.Url ?? "");
                if (proxySession != null)
                {
                    UpdateOsdTimeAndBadge((long)e.NewValue, (long)(proxySession.TotalDurationSeconds * 1000), proxySession.StartWallClockTime);
                }
            }
        }

        private void ApplySliderSeek(double value)
        {
            if (_player == null || _currentChannel == null) return;

            _lastSeekTime = DateTime.Now; // Cooldown başlat

            if (IsCurrentStreamVod())
            {
                _player.Seek((int)value);
            }
            else if (_currentChannel.Url.Contains(".m3u8"))
            {
                // Time-Machine Teleport: Reload stream starting at requested second
                _player.Stop();
                _lastTeleportPositionMs = (long)value;
                string teleportUrl = HlsProxyEngine.Instance.GetProxyPlaybackUrl(_currentChannel.Url, value / 1000.0);
                _player.Open(teleportUrl);
                LogService.LogInfo($"Player: Teleported to {value / 1000}s");
            }
        }

        public void Rewind10_Click(object sender, RoutedEventArgs e) { Rewind(10000); }
        public void Forward10_Click(object sender, RoutedEventArgs e) { Forward(10000); }

        public void GoLive()
        {
            if (_player == null || _currentChannel == null) return;

            _lastSeekTime = DateTime.Now; // Cooldown

            if (_currentChannel.Url.Contains(".m3u8"))
            {
                // Force proxy to return to live edge
                _player.Stop();
                _lastTeleportPositionMs = -1; // Live edge
                string liveUrl = HlsProxyEngine.Instance.GetProxyPlaybackUrl(_currentChannel.Url, -1);
                _player.Open(liveUrl);
            }
            else
            {
                _player.Seek((int)(_player.Duration / 10000));
            }
            ShowOsdTemporary();
        }

        public void GoLive_Click(object sender, RoutedEventArgs e) { GoLive(); }
        public void Mute_Click(object sender, RoutedEventArgs e) { ToggleMute(); }

        public void Fullscreen_Click(object sender, RoutedEventArgs e)
        {
            var win = Window.GetWindow(this);
            if (win == null) return;
            if (win.WindowStyle == WindowStyle.None)
            {
                win.WindowStyle = WindowStyle.SingleBorderWindow;
                win.WindowState = WindowState.Normal;
            }
            else
            {
                win.WindowStyle = WindowStyle.None;
                win.WindowState = WindowState.Maximized;
            }
        }

        private void Player_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) { ShowOsdTemporary(); }

        private void UpdateOsdEpgForTime(DateTime targetTime)
        {
            if (_currentChannel == null || OsdCurrentEpg == null || OsdNextEpg == null) return;
            if (_currentChannelEpgList != null && _currentChannelEpgList.Count > 0)
            {
                var matching = _currentChannelEpgList.Where(p => targetTime >= p.StartTime && targetTime <= p.EndTime).FirstOrDefault();
                OsdCurrentEpg.Text = matching != null ? $"{matching.StartTime:HH:mm} - {matching.EndTime:HH:mm} {matching.Title}" : "Yayın akışı bilgisi yok";

                var future = _currentChannelEpgList.Where(p => p.StartTime > targetTime).OrderBy(p => p.StartTime).FirstOrDefault();
                OsdNextEpg.Text = future != null ? $"Sıradaki: {future.StartTime:HH:mm} {future.Title}" : "Sıradaki: --:-- Bilgi yok";
            }
        }

        public void Dispose()
        {
            _positionTimer?.Stop();
            _osdTimer?.Stop();
            if (_player != null)
            {
                try
                {
                    if (_currentChannel != null && IsCurrentStreamVod())
                    {
                        _currentChannel.LastPositionMs = _player.CurTime / 10000;
                        _db.SaveChannelSync(_currentChannel);
                    }
                    _player.Stop();

                    if (_currentChannel?.SourceType == "ACESTREAM" || (_currentChannel?.Url?.Contains("acestream://") == true))
                    {
                        Task.Run(async () => await _ace.StopAllStreamsAsync());
                    }

                    _player.Dispose();
                }
                catch { }
                _player = null;
            }
        }
    }
}
