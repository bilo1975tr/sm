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
        private System.Threading.CancellationTokenSource? _loadCts;

        // Audio & Video DSP / Enhancement States
        private bool _isAudioNormEnabled = true;
        private bool _isVideoEnhanceEnabled = false;
        private System.Windows.Threading.DispatcherTimer? _toastTimer;
        private readonly List<AudioTrackModel> _detectedAudioTracks = new();
        private int _selectedAudioTrackIndex = -1;
        private string _selectedAudioTrackLangCode = "";

        public PlayerView()
        {
            InitializeComponent();
            LoadAudioVideoSettings();

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
                try
                {
                    // Demuxer Network & Timeout Configurations (Optimized for lightning fast live playback)
                    _config.Demuxer.FormatOpt["reconnect"] = "1";
                    _config.Demuxer.FormatOpt["reconnect_streamed"] = "1";
                    _config.Demuxer.FormatOpt["reconnect_delay_max"] = "3";
                    _config.Demuxer.FormatOpt["reconnect_at_eof"] = "1";
                    _config.Demuxer.FormatOpt["reconnect_on_network_error"] = "1";
                    _config.Demuxer.FormatOpt["timeout"] = "8000000"; // 8s socket timeout (faster fallback on dead streams)
                    _config.Demuxer.FormatOpt["stimeout"] = "8000000"; // 8s for RTSP/RTMP
                    _config.Demuxer.FormatOpt["rw_timeout"] = "8000000"; // 8s socket read/write
                    _config.Demuxer.FormatOpt["probesize"] = "1048576"; // 1MB probe size (accelerates stream startup)
                    _config.Demuxer.FormatOpt["analyzeduration"] = "2000000"; // 2s max analyze duration (prevents hanging)
                    _config.Demuxer.FormatOpt["fflags"] = "+nobuffer+fastseek+flush_packets";
                    _config.Demuxer.FormatOpt["flags"] = "low_delay";
                    _config.Demuxer.FormatOpt["user_agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

                    _config.Demuxer.BufferDuration = 1000 * 10000; // 1.0s initial buffer for near-instant stream start
                    _config.Player.AutoPlay = true;

                    // Apply Audio Normalization & Video Enhancement initial filter
                    ApplyAudioNormalization();
                    ApplyVideoEnhancement();
                }
                catch (Exception ex)
                {
                    LogService.LogError("Player: Config setup exception", ex);
                }

                _player = new Player(_config);
                _player.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(Player.Status))
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            if (_player == null) return;
                            if (_player.Status == Status.Playing)
                            {
                                UpdatePlayPauseIcon(true);
                                RefreshAudioTracks();
                            }
                            else if (_player.Status == Status.Paused || _player.Status == Status.Stopped)
                            {
                                UpdatePlayPauseIcon(false);
                            }
                            else if (_player.Status == Status.Failed)
                            {
                                UpdatePlayPauseIcon(false);
                                if (_currentChannel != null)
                                {
                                    OsdTitle.Text = $"{_currentChannel.PrimaryName} (Sinyal Yok / Bağlantı Koptu)";
                                    ShowOsdTemporary();
                                }
                            }
                        });
                    }
                };

                VideoPlayer.Player = _player;

                LogService.LogInfo("Player: Flyleaf initialized successfully");
                Dispatcher.Invoke(() => {
                    OsdTitle.Text = "Sistem Hazır.";
                    OsdPanel.Visibility = Visibility.Visible;
                    UpdateAudioNormButtonUI();
                    UpdateVideoEnhanceButtonUI();
                    ShowOsdTemporary();
                });
            }
            catch (Exception ex)
            {
                LogService.LogError("Player: Flyleaf Init Failed", ex);
            }
        }

        private string PrepareHlsStream(string url)
        {
            if (url.Contains(".m3u8"))
            {
                // Register & inspect HLS stream entirely in background so live playback starts instantly
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(3000);
                        var session = await HlsProxyEngine.Instance.InspectAndPrepareHlsAsync(url).WaitAsync(cts.Token).ConfigureAwait(false);
                        if (session != null)
                        {
                            LogService.LogInfo($"Player: HLS background session ready for Timeshift: {url}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.LogWarning($"Player: HLS background prep notice: {ex.Message}");
                    }
                });
            }
            return url;
        }

        public void LoadChannel(Channel channel)
        {
            if (channel == null) return;

            // 1. Cancel previous load operation immediately to release threads
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new System.Threading.CancellationTokenSource();
            var token = _loadCts.Token;

            _currentChannel = channel;
            _streamStartTime = DateTime.UtcNow;
            _lastTeleportPositionMs = -1; // Reset to Live mode

            // 2. Instant responsive UI update
            Dispatcher.Invoke(() => {
                OsdTitle.Text = $"{channel.PrimaryName} (Bağlanıyor...)";
                OsdCategory.Text = channel.Category;
                ShowOsdTemporary();
                UpdatePlayPauseIcon(true);
                try {
                    var convertedLogo = LogoConverter.Convert(channel.PrimaryLogoUrl, typeof(ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture);
                    if (convertedLogo != null) OsdLogo.Source = (ImageSource)convertedLogo;
                } catch { }
            });

            // 3. Isolated background task execution (prevents UI freezing)
            Task.Run(async () =>
            {
                if (token.IsCancellationRequested) return;

                // Wait for player ready
                for (int i = 0; i < 25 && _player == null; i++)
                {
                    if (token.IsCancellationRequested) return;
                    await Task.Delay(150, token).ConfigureAwait(false);
                }

                if (_player == null)
                {
                    LogService.LogError("Player: Flyleaf player not ready.");
                    return;
                }

                // Acquire semaphore with 5s timeout to prevent deadlocks
                bool acquired = await _playSemaphore.WaitAsync(5000, token).ConfigureAwait(false);
                if (!acquired || token.IsCancellationRequested) return;

                try
                {
                    // Save VOD position if applicable
                    if (IsCurrentStreamVod() && _currentChannel != null)
                    {
                        try
                        {
                            _currentChannel.LastPositionMs = _player.CurTime / 10000;
                            _ = _db.SaveChannelAsync(_currentChannel);
                        }
                        catch { }
                    }

                    // Stop previous stream safely
                    try
                    {
                        if (channel.SourceType == "ACESTREAM" || (channel.Url ?? "").Contains("acestream://"))
                        {
                            await _ace.StopAllStreamsAsync().ConfigureAwait(false);
                        }
                        _player.Stop();
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError("Player: Stop exception", ex);
                    }

                    if (token.IsCancellationRequested) return;

                    // Fetch EPG in background
                    _selectedAudioTrackIndex = -1;
                    _selectedAudioTrackLangCode = "";
                    try
                    {
                        _currentChannelEpgList = await _epgService.GetChannelEpgHistoryAsync(channel).ConfigureAwait(false);
                        _ = Dispatcher.InvokeAsync(() => UpdateOsdEpgForTime(DateTime.Now));
                    }
                    catch { }

                    // Retrieve URL candidates cleanly using GetUrlList()
                    var rawUrls = channel.GetUrlList();
                    if (rawUrls.Count == 0 && !string.IsNullOrWhiteSpace(channel.Url))
                    {
                        rawUrls = new List<string> { channel.Url.Trim() };
                    }

                    LogService.LogInfo($"Player: Found {rawUrls.Count} URL candidates in database for {channel.PrimaryName}");

                    // Apply current active audio/video enhancement configuration
                    ApplyAudioNormalization();
                    ApplyVideoEnhancement();

                    bool streamStarted = false;

                    foreach (var raw in rawUrls)
                    {
                        if (token.IsCancellationRequested) return;

                        string tryUrl = raw.Trim();
                        LogService.LogInfo($"Player: Processing candidate URL: {tryUrl}");

                        try
                        {
                            bool isAce = _ace.IsAceStreamUrl(tryUrl) || channel.SourceType == "ACESTREAM";
                            if (isAce)
                            {
                                LogService.LogInfo("Player: AceStream playback sequence initiated.");
                                await _ace.StartEngineAsync().ConfigureAwait(false);

                                string hash = _ace.ExtractHash(tryUrl);
                                await _ace.OpenStreamAsync(hash).ConfigureAwait(false);

                                var aceUrls = await _ace.GetHttpUrlsWithTokenAsync(tryUrl).ConfigureAwait(false);
                                if (aceUrls != null && aceUrls.Count > 0)
                                {
                                    tryUrl = aceUrls[0];
                                    _ = Dispatcher.InvokeAsync(() => { OsdTitle.Text = "AceStream: Başlatılıyor..."; ShowOsdTemporary(); });

                                    bool ready = await _ace.WaitForStreamReadyAsync(tryUrl, 4).ConfigureAwait(false);
                                    if (!ready && aceUrls.Count > 1)
                                    {
                                        tryUrl = aceUrls[1];
                                        LogService.LogInfo($"Player: Primary AceStream link failed, trying fallback: {tryUrl}");
                                    }
                                }
                                else
                                {
                                    LogService.LogWarning("Player: AceStream engine failed to resolve any playback URLs.");
                                }
                            }
                            else if (tryUrl.Contains("youtube.com"))
                            {
                                LogService.LogInfo("Player: YouTube link detected, fetching stream manifest...");
                                tryUrl = await _yt.GetStreamUrlAsync(tryUrl).ConfigureAwait(false) ?? tryUrl;
                            }
                            else if (tryUrl.Contains(".m3u8") && !tryUrl.Contains(":6878/ace/"))
                            {
                                // HLS Proxy Integration (background session registration for timeshift)
                                LogService.LogInfo("Player: HLS (m3u8) detected, ensuring Timeshift cache engine...");
                                tryUrl = PrepareHlsStream(tryUrl);
                            }

                            if (token.IsCancellationRequested) return;

                            LogService.LogInfo($"Player: [FINAL] Flyleaf opening -> {tryUrl}");

                            // Execute Player.Open in an isolated task with adaptive watchdog timeout
                            var openTask = Task.Run(() =>
                            {
                                try { _player.Open(tryUrl); }
                                catch (Exception ex) { LogService.LogError("Flyleaf Player.Open exception", ex); }
                            }, token);

                            int watchdogTimeoutMs = isAce ? 18000 : 8000;
                            var completed = await Task.WhenAny(openTask, Task.Delay(watchdogTimeoutMs, token)).ConfigureAwait(false);

                            if (completed != openTask)
                            {
                                LogService.LogWarning($"Player: Open timed out for {tryUrl} after {watchdogTimeoutMs}ms, trying next mirror if available");
                                continue;
                            }

                            if (token.IsCancellationRequested) return;

                            _ = Dispatcher.InvokeAsync(() => {
                                if (_currentChannel != null) OsdTitle.Text = _currentChannel.PrimaryName;
                            });

                            if (IsCurrentStreamVod() && channel.LastPositionMs > 0)
                            {
                                LogService.LogInfo($"Player: Resuming VOD at {channel.LastPositionMs}ms");
                                try { _player.Seek((int)channel.LastPositionMs); } catch { }
                            }

                            streamStarted = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            LogService.LogError($"Player: Candidate failed -> {tryUrl}", ex);
                        }
                    }

                    if (!streamStarted && !token.IsCancellationRequested)
                    {
                        _ = Dispatcher.InvokeAsync(() => {
                            OsdTitle.Text = $"{channel.PrimaryName} (Sinyal Zayıf / Yanıt Vermiyor)";
                            OsdCategory.Text = "Sinyal Yok";
                            UpdatePlayPauseIcon(false);
                            ShowOsdTemporary();
                        });
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { LogService.LogError("Player: Load error", ex); }
                finally
                {
                    _playSemaphore.Release();
                }
            }, token);
        }

        public async void Stop()
        {
            _loadCts?.Cancel();
            _player?.Stop();
            if (_currentChannel?.SourceType == "ACESTREAM" || (_currentChannel?.Url?.Contains("acestream://") == true))
            {
                await _ace.StopAllStreamsAsync();
            }
        }

        public void PlayPause_Click(object sender, RoutedEventArgs e) { TogglePause(); }

        private void UpdatePlayPauseIcon(bool isPlaying)
        {
            if (PlayPauseBtn == null) return;
            var geo = TryFindResource(isPlaying ? "GeoPause" : "GeoPlay") as Geometry;
            if (geo != null)
            {
                PlayPauseBtn.Content = new System.Windows.Shapes.Path
                {
                    Data = geo,
                    Fill = System.Windows.Media.Brushes.White,
                    Width = 20,
                    Height = 20,
                    Stretch = System.Windows.Media.Stretch.Uniform
                };
            }
        }

        private void UpdateMuteIcon(bool isMuted)
        {
            if (MuteBtn == null) return;
            var geo = TryFindResource(isMuted ? "GeoVolumeMute" : "GeoVolume") as Geometry;
            if (geo != null)
            {
                MuteBtn.Content = new System.Windows.Shapes.Path
                {
                    Data = geo,
                    Fill = System.Windows.Media.Brushes.White,
                    Width = 18,
                    Height = 18,
                    Stretch = System.Windows.Media.Stretch.Uniform
                };
            }
        }

        public void TogglePause()
        {
            if (_player == null) return;
            ShowOsdTemporary();

            if (_player.Status == Status.Playing)
            {
                _player.Pause();
                UpdatePlayPauseIcon(false);
            }
            else
            {
                _player.Play();
                UpdatePlayPauseIcon(true);
            }
        }

        public void Rewind(int ms) { if (_player != null) _player.Seek((int)((_player.CurTime / 10000) - ms)); ShowOsdTemporary(); }
        public void Forward(int ms) { if (_player != null) _player.Seek((int)((_player.CurTime / 10000) + ms)); ShowOsdTemporary(); }
        public void ChangeVolume(int delta) { if (_player != null) _player.Audio.Volume = Math.Clamp(_player.Audio.Volume + delta, 0, 100); ShowOsdTemporary(); }
        public void ToggleMute()
        {
            if (_player != null)
            {
                _player.Audio.Mute = !_player.Audio.Mute;
                UpdateMuteIcon(_player.Audio.Mute);
            }
            ShowOsdTemporary();
        }

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

        #region Audio Tracks / Language Selection (Çoklu Dil & Ses İzi)

        public void AudioTrackBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowOsdTemporary();
            if (AudioTracksFlyout == null) return;
            AudioTracksFlyout.Visibility = AudioTracksFlyout.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (AudioTracksFlyout.Visibility == Visibility.Visible)
            {
                RefreshAudioTracks();
            }
        }

        public void CloseAudioTracksFlyout_Click(object sender, RoutedEventArgs e)
        {
            if (AudioTracksFlyout != null) AudioTracksFlyout.Visibility = Visibility.Collapsed;
        }

        public void AudioTrackItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is AudioTrackModel item)
            {
                try
                {
                    _selectedAudioTrackIndex = item.Index;
                    _selectedAudioTrackLangCode = item.LangCode;

                    if (_player != null && item.RawStream != null)
                    {
                        try
                        {
                            // In FlyleafLib, audio streams are switched by passing the AudioStream instance to Player.Open
                            bool switched = false;
                            var rawType = item.RawStream.GetType();

                            // Search for Player.Open overloads that accept an AudioStream or StreamBase
                            var openMethods = _player.GetType().GetMethods().Where(m => m.Name == "Open" || m.Name == "OpenAsync").ToList();
                            foreach (var m in openMethods)
                            {
                                var prms = m.GetParameters();
                                if (prms.Length == 1 && prms[0].ParameterType.IsAssignableFrom(rawType))
                                {
                                    m.Invoke(_player, new object[] { item.RawStream });
                                    switched = true;
                                    break;
                                }
                                else if (prms.Length >= 2 && prms[0].ParameterType.IsAssignableFrom(rawType))
                                {
                                    // Player.Open(stream, start, resync, ...)
                                    var args = new object[prms.Length];
                                    args[0] = item.RawStream;
                                    for (int pi = 1; pi < prms.Length; pi++)
                                    {
                                        if (prms[pi].ParameterType == typeof(bool)) args[pi] = true;
                                        else args[pi] = prms[pi].DefaultValue ?? null!;
                                    }
                                    m.Invoke(_player, args);
                                    switched = true;
                                    break;
                                }
                            }

                            if (!switched)
                            {
                                dynamic dPlayer = _player;
                                dynamic dStream = item.RawStream;
                                dPlayer.Open(dStream);
                            }

                            LogService.LogInfo($"Player: Audio track switched to {item.DisplayName} ({item.LangCode})");
                        }
                        catch (Exception ex)
                        {
                            LogService.LogWarning($"Player: Audio stream switch execution error: {ex.Message}");
                        }
                    }

                    foreach (var t in _detectedAudioTracks) t.IsSelected = (t.Index == item.Index);
                    if (AudioTracksItemsList != null)
                    {
                        AudioTracksItemsList.ItemsSource = null;
                        AudioTracksItemsList.ItemsSource = _detectedAudioTracks;
                    }
                    if (AudioTrackBtnText != null) AudioTrackBtnText.Text = item.LangCode;

                    ShowOsdToast($"🌐 Ses İzi: {item.DisplayName} seçildi", "GeoLanguage");
                    if (AudioTracksFlyout != null) AudioTracksFlyout.Visibility = Visibility.Collapsed;
                }
                catch (Exception ex)
                {
                    LogService.LogError("Player: Select AudioTrack exception", ex);
                }
            }
        }

        public void RefreshAudioTracks()
        {
            Dispatcher.InvokeAsync(() =>
            {
                _detectedAudioTracks.Clear();
                if (_player == null) return;

                try
                {
                    var audioProp = _player.Audio;
                    if (audioProp != null)
                    {
                        var streamsProp = audioProp.GetType().GetProperty("Streams");
                        var streamsList = streamsProp?.GetValue(audioProp) as System.Collections.IEnumerable;
                        var curStream = audioProp.GetType().GetProperty("Stream")?.GetValue(audioProp);

                        int i = 0;
                        if (streamsList != null)
                        {
                            foreach (var s in streamsList)
                            {
                                if (s == null) continue;
                                var sType = s.GetType();
                                var langObj = sType.GetProperty("Language")?.GetValue(s);
                                string langStr = langObj != null ? langObj.ToString() ?? "" : "";
                                string titleStr = sType.GetProperty("Title")?.GetValue(s)?.ToString() ?? "";
                                string codecStr = sType.GetProperty("Codec")?.GetValue(s)?.ToString() ?? "AAC";
                                int channels = 2;
                                if (int.TryParse(sType.GetProperty("Channels")?.GetValue(s)?.ToString(), out int chVal)) channels = chVal;
                                long bitRate = 0;
                                if (long.TryParse(sType.GetProperty("BitRate")?.GetValue(s)?.ToString(), out long brVal)) bitRate = brVal;

                                var (langName, langCode) = ParseLanguageInfo(langStr, titleStr, i);
                                
                                // Check if this track is the currently selected one
                                bool isCur = false;
                                if (_selectedAudioTrackIndex >= 0)
                                {
                                    isCur = (i == _selectedAudioTrackIndex);
                                }
                                else if (curStream != null)
                                {
                                    isCur = (s == curStream);
                                }
                                else
                                {
                                    isCur = (i == 0);
                                }

                                string details = $"{codecStr} • {(channels == 6 ? "5.1 Surround" : channels == 2 ? "Stereo" : $"{channels} Kanal")}";
                                if (bitRate > 0) details += $" • {bitRate / 1000} kbps";

                                _detectedAudioTracks.Add(new AudioTrackModel
                                {
                                    Index = i,
                                    DisplayName = $"{i + 1}. {langName}",
                                    Details = details,
                                    LangCode = langCode,
                                    IsSelected = isCur,
                                    RawStream = s
                                });
                                i++;
                            }
                        }
                    }

                    if (_detectedAudioTracks.Count == 0)
                    {
                        // Fallback when single audio stream
                        _detectedAudioTracks.Add(new AudioTrackModel
                        {
                            Index = 0,
                            DisplayName = "1. Ana Yayın Sesi",
                            Details = "Varsayılan Ses Akışı (Stereo)",
                            LangCode = "TR",
                            IsSelected = true,
                            RawStream = null
                        });
                    }

                    if (AudioTracksItemsList != null)
                    {
                        AudioTracksItemsList.ItemsSource = null;
                        AudioTracksItemsList.ItemsSource = _detectedAudioTracks;
                    }

                    var selected = _detectedAudioTracks.FirstOrDefault(t => t.IsSelected) ?? _detectedAudioTracks.FirstOrDefault();
                    if (AudioTrackBtnText != null && selected != null)
                    {
                        AudioTrackBtnText.Text = selected.LangCode;
                    }

                    if (AudioTrackCountBadge != null && AudioTrackCountText != null)
                    {
                        if (_detectedAudioTracks.Count > 1)
                        {
                            AudioTrackCountBadge.Visibility = Visibility.Visible;
                            AudioTrackCountText.Text = _detectedAudioTracks.Count.ToString();
                            if (AudioTracksInfoFooter != null) AudioTracksInfoFooter.Text = $"🎉 Çoklu Dil: {_detectedAudioTracks.Count} ses izi mevcut";
                        }
                        else
                        {
                            AudioTrackCountBadge.Visibility = Visibility.Collapsed;
                            if (AudioTracksInfoFooter != null) AudioTracksInfoFooter.Text = "Tek ses izi algılandı";
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("Player: RefreshAudioTracks error", ex);
                }
            });
        }

        private static (string name, string code) ParseLanguageInfo(string? lang, string? title, int index)
        {
            string l = (lang ?? "").Trim().ToLowerInvariant();
            string t = (title ?? "").Trim().ToLowerInvariant();

            if (l.Contains("tur") || l.Contains("tr") || t.Contains("türkçe") || t.Contains("turkce") || t.Contains("tur"))
                return ("Türkçe", "TR");
            if (l.Contains("eng") || l.Contains("en") || t.Contains("english") || t.Contains("ingilizce") || t.Contains("eng"))
                return ("İngilizce", "EN");
            if (l.Contains("fra") || l.Contains("fre") || l.Contains("fr") || t.Contains("french") || t.Contains("fransızca"))
                return ("Fransızca", "FR");
            if (l.Contains("deu") || l.Contains("ger") || l.Contains("de") || t.Contains("german") || t.Contains("almanca"))
                return ("Almanca", "DE");
            if (l.Contains("ita") || l.Contains("it") || t.Contains("italian") || t.Contains("italyanca"))
                return ("İtalyanca", "IT");
            if (l.Contains("spa") || l.Contains("es") || t.Contains("spanish") || t.Contains("ispanyolca"))
                return ("İspanyolca", "ES");
            if (l.Contains("rus") || l.Contains("ru") || t.Contains("russian") || t.Contains("rusça"))
                return ("Rusça", "RU");
            if (l.Contains("ara") || l.Contains("ar") || t.Contains("arabic") || t.Contains("arapça"))
                return ("Arapça", "AR");
            if (l.Contains("aze") || l.Contains("az") || t.Contains("azerbaijani") || t.Contains("azerice"))
                return ("Azerice", "AZ");
            if (l.Contains("jpn") || l.Contains("ja") || t.Contains("japanese") || t.Contains("japonca"))
                return ("Japonca", "JA");
            if (l.Contains("kor") || l.Contains("ko") || t.Contains("korean") || t.Contains("korece"))
                return ("Korece", "KO");
            if (l.Contains("chi") || l.Contains("zho") || l.Contains("zh") || t.Contains("chinese") || t.Contains("çince"))
                return ("Çince", "ZH");
            if (l.Contains("por") || l.Contains("pt") || t.Contains("portuguese") || t.Contains("portekizce"))
                return ("Portekizce", "PT");
            if (l.Contains("hin") || l.Contains("hi") || t.Contains("hindi") || t.Contains("hintçe"))
                return ("Hintçe", "HI");

            if (!string.IsNullOrWhiteSpace(title))
                return (title, title.Length <= 3 ? title.ToUpperInvariant() : "SES");

            return ($"Ses İzi {index + 1}", $"SES {index + 1}");
        }

        #endregion

        #region Audio Normalization & Video Enhancer (Ses Normalizasyonu & Görüntü Netleştirici)

        private void LoadAudioVideoSettings()
        {
            _isAudioNormEnabled = _db.GetSetting("AudioNormEnabled", "true") == "true";
            _isVideoEnhanceEnabled = _db.GetSetting("VideoEnhanceEnabled", "false") == "true";
        }

        private void UpdateAudioNormButtonUI()
        {
            if (AudioNormBtn == null) return;
            if (_isAudioNormEnabled)
            {
                AudioNormBtn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105)); // Emerald Green
                AudioNormBtn.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 211, 153));
                AudioNormBtn.ToolTip = "Ses Normalizasyonu: AÇIK (Ani patlamaları dengeler, konuşmaları netleştirir)";
            }
            else
            {
                AudioNormBtn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85)); // Slate
                AudioNormBtn.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(51, 255, 255, 255));
                AudioNormBtn.ToolTip = "Ses Normalizasyonu: KAPALI (Ham Ses)";
            }
        }

        private void UpdateVideoEnhanceButtonUI()
        {
            if (VideoEnhanceBtn == null) return;
            if (_isVideoEnhanceEnabled)
            {
                VideoEnhanceBtn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(217, 119, 6)); // Amber Gold
                VideoEnhanceBtn.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36));
                VideoEnhanceBtn.ToolTip = "Görüntü Netleştirici: AÇIK (HD Keskinlik & Kontrast İyileştirme)";
            }
            else
            {
                VideoEnhanceBtn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85)); // Slate
                VideoEnhanceBtn.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(51, 255, 255, 255));
                VideoEnhanceBtn.ToolTip = "Görüntü Netleştirici: KAPALI (Standart Görüntü)";
            }
        }

        public void AudioNormBtn_Click(object sender, RoutedEventArgs e)
        {
            _isAudioNormEnabled = !_isAudioNormEnabled;
            _db.SetSetting("AudioNormEnabled", _isAudioNormEnabled ? "true" : "false");
            UpdateAudioNormButtonUI();
            FastReloadCurrentStream();
            ShowOsdToast(_isAudioNormEnabled ? "🔊 Ses Normalizasyonu: AÇIK (Dengeli Düzey)" : "🔇 Ses Normalizasyonu: KAPALI (Ham Ses)", "GeoSoundWave");
            ShowOsdTemporary();
        }

        private void ApplyAudioNormalization()
        {
            try
            {
                if (_config != null)
                {
                    var audioObj = _config.Audio;
                    if (audioObj != null)
                    {
                        var filtersProp = audioObj.GetType().GetProperty("Filters");
                        if (filtersProp?.GetValue(audioObj) is System.Collections.IList filtersList)
                        {
                            filtersList.Clear();
                            if (_isAudioNormEnabled)
                            {
                                var filterInst = CreateFlyleafFilter("dynaudnorm=f=150:g=15:m=10.0:r=0.9:b=1");
                                if (filterInst != null)
                                {
                                    filtersList.Add(filterInst);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("Player: ApplyAudioNormalization error", ex);
            }
        }

        public void VideoEnhanceBtn_Click(object sender, RoutedEventArgs e)
        {
            _isVideoEnhanceEnabled = !_isVideoEnhanceEnabled;
            _db.SetSetting("VideoEnhanceEnabled", _isVideoEnhanceEnabled ? "true" : "false");
            UpdateVideoEnhanceButtonUI();
            FastReloadCurrentStream();
            ShowOsdToast(_isVideoEnhanceEnabled ? "✨ Görüntü Netleştirici: AÇIK (HD Keskinlik)" : "📺 Görüntü Netleştirici: KAPALI", "GeoSparkle");
            ShowOsdTemporary();
        }

        private void ApplyVideoEnhancement()
        {
            try
            {
                if (_config != null)
                {
                    var videoObj = _config.Video;
                    if (videoObj != null)
                    {
                        // Video color & dynamic range tweaks
                        SetDynamicProperty(videoObj, "Contrast", _isVideoEnhanceEnabled ? 1.15f : 1.0f);
                        SetDynamicProperty(videoObj, "Saturation", _isVideoEnhanceEnabled ? 1.10f : 1.0f);
                        SetDynamicProperty(videoObj, "Brightness", _isVideoEnhanceEnabled ? 0.02f : 0.0f);

                        var filtersProp = videoObj.GetType().GetProperty("Filters");
                        if (filtersProp?.GetValue(videoObj) is System.Collections.IList filtersList)
                        {
                            filtersList.Clear();
                            if (_isVideoEnhanceEnabled)
                            {
                                // High quality Unsharp Mask filter for crystal-clear sharpness
                                var filterInst = CreateFlyleafFilter("unsharp=5:5:0.8:5:5:0.0");
                                if (filterInst != null)
                                {
                                    filtersList.Add(filterInst);
                                }
                            }
                        }
                    }
                }

                if (_player != null)
                {
                    var videoObj = _player.GetType().GetProperty("Video")?.GetValue(_player);
                    if (videoObj != null)
                    {
                        SetDynamicProperty(videoObj, "Contrast", _isVideoEnhanceEnabled ? 1.15f : 1.0f);
                        SetDynamicProperty(videoObj, "Saturation", _isVideoEnhanceEnabled ? 1.10f : 1.0f);
                        SetDynamicProperty(videoObj, "Brightness", _isVideoEnhanceEnabled ? 0.02f : 0.0f);
                        SetDynamicProperty(videoObj, "Sharpness", _isVideoEnhanceEnabled ? 1.2f : 0.0f);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("Player: ApplyVideoEnhancement error", ex);
            }
        }

        private void FastReloadCurrentStream()
        {
            if (_currentChannel == null || _player == null) return;

            try
            {
                // 1. Keep position for VOD / Film / Dizi
                long currentPosMs = 0;
                if (IsCurrentStreamVod())
                {
                    currentPosMs = _player.CurTime / 10000;
                    _currentChannel.LastPositionMs = currentPosMs;
                }

                // 2. Re-apply filter settings to configuration and engine
                ApplyAudioNormalization();
                ApplyVideoEnhancement();

                // 3. Fast seamless reload if player is currently active
                if (_player.Status == Status.Playing || _player.Status == Status.Paused || _player.Status == Status.Opening)
                {
                    LogService.LogInfo($"Player: Fast reloading current stream for '{_currentChannel.PrimaryName}' to apply filters immediately (VOD pos: {currentPosMs}ms)");
                    LoadChannel(_currentChannel);
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("Player: FastReloadCurrentStream error", ex);
            }
        }

        private object? CreateFlyleafFilter(string filterStr)
        {
            try
            {
                var filterType = typeof(FlyleafLib.Engine).Assembly.GetType("FlyleafLib.MediaFramework.MediaDecoder.Filter")
                                 ?? typeof(FlyleafLib.Config).Assembly.GetType("FlyleafLib.MediaFramework.MediaDecoder.Filter")
                                 ?? AppDomain.CurrentDomain.GetAssemblies()
                                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                                    .FirstOrDefault(t => t.FullName == "FlyleafLib.MediaFramework.MediaDecoder.Filter" || t.Name == "Filter");

                if (filterType == null) return null;

                // Try single string constructor
                var strCtor = filterType.GetConstructor(new[] { typeof(string) });
                if (strCtor != null)
                {
                    return strCtor.Invoke(new object[] { filterStr });
                }

                // Try default constructor and setting properties
                var defaultCtor = filterType.GetConstructor(Type.EmptyTypes);
                if (defaultCtor != null)
                {
                    var inst = defaultCtor.Invoke(null);
                    var prop = filterType.GetProperty("FilterStr") 
                               ?? filterType.GetProperty("Filter") 
                               ?? filterType.GetProperty("Name") 
                               ?? filterType.GetProperty("Value")
                               ?? filterType.GetProperty("Text");
                    prop?.SetValue(inst, filterStr);
                    return inst;
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"Player: CreateFlyleafFilter failed for '{filterStr}'", ex);
            }
            return null;
        }

        private static void SetDynamicProperty(object target, string propName, object value)
        {
            try
            {
                var prop = target.GetType().GetProperty(propName);
                if (prop != null && prop.CanWrite)
                {
                    var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    object convertedValue = Convert.ChangeType(value, targetType);
                    prop.SetValue(target, convertedValue);
                }
            }
            catch { }
        }

        public void ShowOsdToast(string message, string iconGeoKey)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (OsdToastBorder == null || OsdToastText == null || OsdToastIcon == null) return;

                OsdToastText.Text = message;
                if (TryFindResource(iconGeoKey) is Geometry geo)
                {
                    OsdToastIcon.Data = geo;
                }

                OsdToastBorder.Visibility = Visibility.Visible;
                _toastTimer?.Stop();
                _toastTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                _toastTimer.Tick += (s, e) =>
                {
                    if (OsdToastBorder != null) OsdToastBorder.Visibility = Visibility.Collapsed;
                    _toastTimer?.Stop();
                };
                _toastTimer.Start();
            });
        }

        #endregion

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
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;
            _positionTimer?.Stop();
            _osdTimer?.Stop();
            _toastTimer?.Stop();
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

    public class AudioTrackModel
    {
        public int Index { get; set; }
        public string DisplayName { get; set; } = "";
        public string Details { get; set; } = "";
        public string LangCode { get; set; } = "TR";
        public bool IsSelected { get; set; }
        public System.Windows.Visibility CheckmarkVisibility => IsSelected ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        public System.Windows.Media.Brush BackgroundBrush => IsSelected ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 56, 189, 248)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 255, 255, 255));
        public System.Windows.Media.Brush BorderBrush => IsSelected ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 189, 248)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255));
        public System.Windows.Media.Brush BadgeBackground => IsSelected ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 189, 248)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 41, 59));
        public System.Windows.Media.Brush BadgeForeground => IsSelected ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White;
        public object? RawStream { get; set; }
    }
}
