using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Linq;
using System.Reflection;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.Controls.WPF;
using StreamMesh.Models;
using StreamMesh.Core.Media;
using StreamMesh.Converters;
using StreamMesh.Core.Utils;
using StreamMesh.Core.Database;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

using StreamMesh.UI.ViewModels;

namespace StreamMesh.UI.Views
{
    public partial class PlayerView : System.Windows.Controls.UserControl, IDisposable
    {
        public PlayerViewModel ViewModel { get; } = new PlayerViewModel();

        private Player? _player;
        private Config? _config;
        private readonly TaskCompletionSource<bool> _initTcs = new();

        private readonly AceEngine _ace = new AceEngine();
        private readonly DatabaseEngine _db = new DatabaseEngine();
        private static readonly LogoCacheConverter LogoConverter = new LogoCacheConverter();
        private readonly System.Threading.SemaphoreSlim _playSemaphore = new System.Threading.SemaphoreSlim(1, 1);

        private readonly EpgService _epgService = new EpgService();
        private System.Windows.Threading.DispatcherTimer? _osdTimer;
        private System.Windows.Threading.DispatcherTimer? _positionTimer;
        private System.Windows.Threading.DispatcherTimer? _viewersTimer;
        private string? _currentPlayingUrl;

        private bool _isUserDraggingSlider = false;
        private bool _isMouseOverOsd = false;
        private DateTime _streamStartTime = DateTime.UtcNow;
        private static readonly SolidColorBrush LiveRedBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));
        private static readonly SolidColorBrush DelayedAmberBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11));
        private static readonly SolidColorBrush VodBlueBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235));

        private static readonly SolidColorBrush[] MonogramPalette = new SolidColorBrush[]
        {
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 64, 175)), // Blue
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105)),  // Emerald
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(124, 58, 237)), // Purple
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(190, 24, 93)),  // Rose
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(217, 119, 6)),  // Amber
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(13, 148, 136)), // Teal
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(79, 70, 229))   // Indigo
        };

        private DateTime _lastSeekTime = DateTime.MinValue;
        private long _effectivePositionMs = 0;
        private bool _isTimeshiftMode = false;
        private long _timeshiftStartDvrMs = 0;
        private long _pausedDvrPosMs = 0;
        private System.Threading.CancellationTokenSource? _loadCts;
        private long _playbackSessionId = 0;
        private bool _isFallbackActive = false;
        private int _clockLogTickCount = 0;
        private long _lastReportedCurTimeMs = -1;
        private bool _wasPausedByMinimize = false;
        private bool _hasReceivedFirstFrame = false;
        private int _softRetriggerCount = 0;

        // Audio & Video DSP / Enhancement States
        private bool _isAudioNormEnabled = true;
        private bool _isVideoEnhanceEnabled = false;
        private System.Windows.Threading.DispatcherTimer? _toastTimer;
        private readonly List<AudioTrackModel> _detectedAudioTracks = new();
        private int _selectedAudioTrackIndex = -1;
        private string _selectedAudioTrackLangCode = "";
        private string _lastLoadedChannelId = "";

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
            InitializeViewersTimer();
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
            return ViewModel.IsVod(ViewModel.CurrentChannel);
        }

        private void InitializePositionTimer()
        {
            _positionTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _positionTimer.Tick += (s, e) =>
            {
                if (_player == null || _player.Status == Status.Stopped || _isUserDraggingSlider) return;

                try
                {
                    long timeMs = _player.CurTime / 10000;
                    long lengthMs = _player.Duration / 10000;
                    bool isVod = ViewModel.IsVod(ViewModel.CurrentChannel);

                    // Siyah ekran ve ilk video karesi takip mekanizmasi (First Frame & Soft Re-trigger)
                    if (!isVod && ViewModel.CurrentChannel != null && _player.Status == Status.Playing)
                    {
                        if (!_hasReceivedFirstFrame)
                        {
                            if (timeMs > 0)
                            {
                                _hasReceivedFirstFrame = true;
                                LogService.LogInfo($"[PLAYBACK] Ilk video karesi ekrana ulasti (CurTime: {timeMs}ms).");
                                EnsureVideoStreamSelected();
                                if (OsdTitle != null && ViewModel.CurrentChannel != null)
                                {
                                    OsdTitle.Text = ViewModel.CurrentChannel.PrimaryName;
                                    ShowOsdTemporary();
                                }
                            }
                            else if ((DateTime.UtcNow - _streamStartTime).TotalSeconds >= 4.5 && _softRetriggerCount < 2)
                            {
                                _softRetriggerCount++;
                                LogService.LogInfo($"[PLAYBACK] Siyah ekran kurtarma (Soft Re-trigger #{_softRetriggerCount}): Akis indiriliyor fakat ilk kare takildi. Oynatici tetikleniyor...");
                                EnsureVideoStreamSelected();
                                try { _player.Play(); } catch { }

                                if (_softRetriggerCount == 2 && !string.IsNullOrEmpty(_currentPlayingUrl))
                                {
                                    LogService.LogInfo($"[PLAYBACK] Canli akis soketi tazeleniyor -> {_currentPlayingUrl}");
                                    _player.Open(_currentPlayingUrl);
                                    _player.Play();
                                }
                            }
                        }
                    }

                    _clockLogTickCount++;
                    if (_clockLogTickCount % 4 == 0) // Every ~2 seconds
                    {
                        long delta = _lastReportedCurTimeMs >= 0 ? (timeMs - _lastReportedCurTimeMs) : 0;
                        bool isAdvancing = delta > 0 || (_lastReportedCurTimeMs < 0 && timeMs > 0);
                        _lastReportedCurTimeMs = timeMs;
                        LogService.LogInfo($"[PLAYBACK CLOCK] Status={_player.Status} CurTime={timeMs}ms Duration={lengthMs}ms Advancing={isAdvancing} (delta={delta}ms/2s, IsVod={isVod})");
                    }

                    // Seek koruması: Motor henüz yeni hedefe ulaşmamışsa eski zamana geri zıplamayı engelle (maksimum 2.0 saniye)
                    if ((DateTime.Now - _lastSeekTime).TotalSeconds < 2.0)
                    {
                        if (isVod)
                        {
                            if (_effectivePositionMs > 0 && Math.Abs(timeMs - _effectivePositionMs) < 1200)
                            {
                                _lastSeekTime = DateTime.MinValue;
                            }
                            else
                            {
                                return;
                            }
                        }
                    }

                    if (isVod)
                    {
                        if (LiveBadge != null) LiveBadge.Background = VodBlueBrush;
                        if (LiveBadgeText != null) LiveBadgeText.Text = "🎬 VOD";

                        if (lengthMs > 0)
                        {
                            TimeSlider.IsEnabled = true;
                            TimeSlider.Minimum = 0;
                            TimeSlider.Maximum = lengthMs;
                            _effectivePositionMs = timeMs;
                            if (!_isUserDraggingSlider) TimeSlider.Value = Math.Clamp(timeMs, 0, lengthMs);

                            TimeCurrentText.Text = TimeSpan.FromMilliseconds(timeMs).ToString(@"hh\:mm\:ss");
                            TimeTotalText.Text = TimeSpan.FromMilliseconds(lengthMs).ToString(@"hh\:mm\:ss");
                        }
                        else
                        {
                            TimeSlider.IsEnabled = false;
                            TimeCurrentText.Text = TimeSpan.FromMilliseconds(timeMs).ToString(@"hh\:mm\:ss");
                            TimeTotalText.Text = "--:--:--";
                        }
                    }
                    else
                    {
                        // Sync Slider with Timeshift HLS/TS Proxy Session for DVR
                        var proxySession = HlsProxyEngine.Instance.GetSession(ViewModel.CurrentChannel?.Url ?? "")
                                           ?? (_currentPlayingUrl != null ? HlsProxyEngine.Instance.GetSession(_currentPlayingUrl) : null);

                        if (proxySession != null && proxySession.Segments.Count > 0)
                        {
                            var segments = proxySession.Segments;
                            double earliestSec = segments.Count > 0 ? segments[0].StartTimeSeconds : 0;
                            double latestSec = proxySession.TotalDurationSeconds;
                            long earliestMs = (long)(earliestSec * 1000);
                            long latestMs = (long)(latestSec * 1000);

                            TimeSlider.IsEnabled = true;
                            TimeSlider.Minimum = earliestMs;
                            TimeSlider.Maximum = Math.Max(earliestMs + 1000, latestMs);

                            long currentDvrPosMs;
                            if (_player.Status == Status.Paused && _pausedDvrPosMs > 0)
                            {
                                currentDvrPosMs = _pausedDvrPosMs;
                            }
                            else if (_isTimeshiftMode)
                            {
                                currentDvrPosMs = Math.Clamp(_timeshiftStartDvrMs + timeMs, earliestMs, latestMs);
                                long delayFromLive = latestMs - currentDvrPosMs;
                                if (delayFromLive <= 3000)
                                {
                                    _isTimeshiftMode = false;
                                    _timeshiftStartDvrMs = 0;
                                    currentDvrPosMs = latestMs;
                                }
                            }
                            else
                            {
                                currentDvrPosMs = latestMs;
                            }

                            _effectivePositionMs = currentDvrPosMs;
                            if (!_isUserDraggingSlider)
                            {
                                TimeSlider.Value = currentDvrPosMs;
                            }

                            UpdateOsdTimeAndBadge(currentDvrPosMs, latestMs, proxySession.StartWallClockTime);
                        }
                        else
                        {
                            TimeSlider.IsEnabled = false;
                            TimeSlider.Minimum = 0;
                            TimeSlider.Maximum = 100;
                            if (!_isUserDraggingSlider) TimeSlider.Value = 100;
                            TimeCurrentText.Text = DateTime.Now.ToString("HH:mm:ss");
                            TimeTotalText.Text = "CANLI YAYIN";
                            if (LiveBadge != null) LiveBadge.Background = LiveRedBrush;
                            if (LiveBadgeText != null) LiveBadgeText.Text = "🔴 CANLI";
                            if (GoLiveBtn != null) GoLiveBtn.Background = LiveRedBrush;
                            if (GoLiveText != null) GoLiveText.Text = "CANLI";
                            UpdateOsdEpgForTime(DateTime.Now);
                        }
                    }
                }
                catch { }
            };
            _positionTimer.Start();
        }

        private void InitializeViewersTimer()
        {
            _viewersTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _viewersTimer.Tick += async (s, e) =>
            {
                if (_player == null || _player.Status != Status.Playing || ViewModel.CurrentChannel == null)
                {
                    if (OsdViewersBadge != null) OsdViewersBadge.Visibility = Visibility.Collapsed;
                    return;
                }

                try
                {
                    string currentUrl = ViewModel.CurrentChannel.Url ?? "";
                    if (_ace.IsAceStreamUrl(currentUrl) || currentUrl.Contains(":6878/ace/"))
                    {
                        int peers = await _ace.GetStreamPeersAsync(currentUrl);
                        if (peers > 0)
                        {
                            OsdViewersText.Text = $"{peers} Eş";
                            OsdViewersBadge.Visibility = Visibility.Visible;
                            return;
                        }
                    }
                    else if (currentUrl.Contains("youtube.com") || currentUrl.Contains("youtu.be"))
                    {
                        var yt = new YoutubeEngine();
                        int viewers = await yt.GetActiveLiveViewersAsync(currentUrl);
                        if (viewers > 0)
                        {
                            OsdViewersText.Text = viewers > 1000 ? $"{viewers / 1000.0:0.#}k İzleyici" : $"{viewers} İzleyici";
                            OsdViewersBadge.Visibility = Visibility.Visible;
                            return;
                        }
                    }
                }
                catch { }

                if (OsdViewersBadge != null) OsdViewersBadge.Visibility = Visibility.Collapsed;
            };
            _viewersTimer.Start();
        }

        private void UpdateOsdTimeAndBadge(long currentMs, long totalMs, DateTime startWallTime)
        {
            if (TimeCurrentText == null) return;

            long offsetMs = Math.Max(0, totalMs - currentMs);

            if (offsetMs > 3000)
            {
                DateTime airedTime = startWallTime.AddMilliseconds(currentMs);
                TimeCurrentText.Text = airedTime.ToString("HH:mm:ss");

                TimeSpan delay = TimeSpan.FromMilliseconds(offsetMs);
                string delayStr = delay.TotalHours >= 1 
                    ? $"-{delay:hh\\:mm\\:ss}" 
                    : $"-{delay:mm\\:ss}";

                if (LiveBadge != null) LiveBadge.Background = DelayedAmberBrush;
                if (LiveBadgeText != null) LiveBadgeText.Text = delayStr;
                if (GoLiveBtn != null) GoLiveBtn.Background = DelayedAmberBrush;
                if (GoLiveText != null) GoLiveText.Text = $"⚡ {delayStr}";
                if (TimeTotalText != null) TimeTotalText.Text = $"GERİDE ({delayStr})";
                UpdateOsdEpgForTime(airedTime);
            }
            else
            {
                TimeCurrentText.Text = DateTime.Now.ToString("HH:mm:ss");
                if (LiveBadge != null) LiveBadge.Background = LiveRedBrush;
                if (LiveBadgeText != null) LiveBadgeText.Text = "🔴 CANLI";
                if (GoLiveBtn != null) GoLiveBtn.Background = LiveRedBrush;
                if (GoLiveText != null) GoLiveText.Text = "CANLI";
                if (TimeTotalText != null) TimeTotalText.Text = "CANLI YAYIN";
                UpdateOsdEpgForTime(DateTime.Now);
            }
        }

        private void InitializePlayer()
        {
            if (_player != null)
            {
                VideoPlayer.Player = _player;
                return;
            }

            try
            {
                FlyleafHelper.SafeStart();

                _config = new Config();
                try
                {
                    // Demuxer Network, Buffering & Timestamp Regeneration Configurations
                    _config.Demuxer.FormatOpt["user_agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";
                    _config.Demuxer.FormatOpt["reorder_queue_size"] = "1000";
                    _config.Demuxer.FormatOpt["fflags"] = "+genpts+discardcorrupt";
                    _config.Demuxer.FormatOpt["ts_discont_detect"] = "1";
                    _config.Demuxer.FormatOpt["max_delay"] = "5000000";
                    _config.Demuxer.FormatOpt["probesize"] = "5000000";
                    _config.Demuxer.FormatOpt["analyzeduration"] = "5000000";

                    _config.Demuxer.BufferDuration = 4000 * 10000; // 4.0s buffer for smooth, jitter-free playback
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
                VideoPlayer.Player = _player;
                _initTcs.TrySetResult(true);

                _player.OpenCompleted += (s, e) =>
                {
                    LogService.LogInfo($"[PLAYBACK] Player.OpenCompleted: Success={e.Success}, Error={e.Error ?? "None"}, Status={_player?.Status}");
                };

                _player.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(Player.Status))
                    {
                        long curMs = _player?.CurTime / 10000 ?? 0;
                        long durMs = _player?.Duration / 10000 ?? 0;
                        LogService.LogInfo($"[PLAYBACK] Player.Status changed: {_player?.Status} (CurTime: {curMs}ms, Duration: {durMs}ms)");
                        if (_player?.Status == Status.Playing)
                        {
                            LogService.LogInfo($"[PLAYBACK] Playback started successfully (Status: Playing, CurTime: {curMs}ms)");
                        }

                        Dispatcher.InvokeAsync(() =>
                        {
                            if (_player == null) return;
                            if (_player.Status == Status.Playing)
                            {
                                UpdatePlayPauseIcon(true);
                                RefreshAudioTracks();
                                EnsureVideoStreamSelected();

                                _ = Task.Run(async () =>
                                {
                                    await Task.Delay(1500);
                                    await Dispatcher.InvokeAsync(() => { if (_player?.Status == Status.Playing) RefreshAudioTracks(); });
                                    await Task.Delay(2000);
                                    await Dispatcher.InvokeAsync(() => { if (_player?.Status == Status.Playing) RefreshAudioTracks(); });
                                });
                            }
                            else if (_player.Status == Status.Paused)
                            {
                                // Initial buffer fill / opening window: keep stream playing
                                if ((DateTime.UtcNow - _streamStartTime).TotalSeconds < 10)
                                {
                                    LogService.LogInfo("[PLAYBACK] Player entered Paused during startup window; triggering Play().");
                                    try { _player.Play(); } catch { }
                                    UpdatePlayPauseIcon(true);
                                }
                                else
                                {
                                    UpdatePlayPauseIcon(false);
                                }
                            }
                            else if (_player.Status == Status.Stopped)
                            {
                                UpdatePlayPauseIcon(false);
                            }
                            else if (_player.Status == Status.Ended)
                            {
                                UpdatePlayPauseIcon(false);
                                long endCurMs = _player?.CurTime / 10000 ?? 0;
                                LogService.LogWarning($"[PLAYBACK] Stream ended (CurTime: {endCurMs}ms).");

                                // Canlı yayında veya AceStream'de başlangıçta yaşanan tampon boşluğu durumunda otomatik yeniden bağlan
                                if (ViewModel.CurrentChannel != null && !ViewModel.IsVod(ViewModel.CurrentChannel) && !string.IsNullOrEmpty(_currentPlayingUrl))
                                {
                                    string reconnectUrl = _currentPlayingUrl;
                                    LogService.LogInfo($"[PLAYBACK] Canli yayin icin otomatik kurtarma (1.2 sn sonra) -> {reconnectUrl}");
                                    _ = Task.Run(async () =>
                                    {
                                        await Task.Delay(1200);
                                        if (_player != null && (_player.Status == Status.Ended || _player.Status == Status.Stopped))
                                        {
                                            await Dispatcher.InvokeAsync(() =>
                                            {
                                                if (_player != null && (_player.Status == Status.Ended || _player.Status == Status.Stopped))
                                                {
                                                    LogService.LogInfo($"[PLAYBACK] Otomatik kurtarma akisi yeniden aciyor -> {reconnectUrl}");
                                                    _player.Open(reconnectUrl);
                                                }
                                            });
                                        }
                                    });
                                }
                            }
                            else if (_player.Status == Status.Failed)
                            {
                                UpdatePlayPauseIcon(false);
                                if (ViewModel.CurrentChannel != null && !_isFallbackActive)
                                {
                                    OsdTitle.Text = $"{ViewModel.CurrentChannel.PrimaryName} (Sinyal Yok / Bağlantı Koptu)";
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
                LogService.LogWarning($"Player: Flyleaf henüz hazır değil ({ex.Message}), arka planda yeniden denenecek.");
                _ = Task.Run(async () =>
                {
                    string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
                    for (int waitSec = 0; waitSec < 30; waitSec++)
                    {
                        await Task.Delay(2000);
                        if (Directory.Exists(ffmpegPath) && Directory.GetFiles(ffmpegPath, "avcodec*.dll").Length > 0)
                        {
                            await Dispatcher.InvokeAsync(() => InitializePlayer());
                            break;
                        }
                    }
                });
            }
        }

        public void PrewarmPlayer()
        {
            if (_player == null)
            {
                InitializePlayer();
            }
        }

        private void EnsureVideoStreamSelected()
        {
            try
            {
                if (_player == null) return;
                var videoProp = _player.GetType().GetProperty("Video")?.GetValue(_player);
                if (videoProp != null)
                {
                    var curStream = videoProp.GetType().GetProperty("Stream")?.GetValue(videoProp);
                    if (curStream == null)
                    {
                        var streams = videoProp.GetType().GetProperty("Streams")?.GetValue(videoProp) as System.Collections.IList;
                        if (streams != null && streams.Count > 0)
                        {
                            LogService.LogInfo($"[PLAYBACK] Video.Stream null idi, ilk video akisi ({streams[0]}) seciliyor.");
                            videoProp.GetType().GetProperty("Stream")?.SetValue(videoProp, streams[0]);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[PLAYBACK] EnsureVideoStreamSelected hatasi: {ex.Message}");
            }
        }

        private bool SwitchAudioTrack(int targetIdx, object? rawStream = null)
        {
            if (_player == null || _player.Audio == null) return false;
            try
            {
                var audioProp = _player.Audio;
                var type = audioProp.GetType();
                var streamsProp = type.GetProperty("Streams", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                var streamsList = streamsProp?.GetValue(audioProp) as System.Collections.IList;

                if (streamsList == null || streamsList.Count == 0)
                {
                    LogService.LogWarning("[AUDIO_SWITCH] Streams list is empty or null.");
                    return false;
                }

                if (targetIdx < 0 || targetIdx >= streamsList.Count)
                {
                    targetIdx = 0;
                }

                object? targetStream = rawStream ?? streamsList[targetIdx];
                if (targetStream == null) return false;

                LogService.LogInfo($"[AUDIO_SWITCH] Switching audio track directly to StreamIndex = {targetIdx} (Total Streams: {streamsList.Count})");

                bool switched = false;

                // 1. Set via direct Player.Audio.Stream property (Flyleaf standard API)
                try
                {
                    dynamic dAudio = audioProp;
                    dAudio.Stream = targetStream;
                    switched = true;
                    LogService.LogInfo($"[AUDIO_SWITCH] Successfully assigned dAudio.Stream to target track #{targetIdx}");
                }
                catch (Exception exDirect)
                {
                    LogService.LogWarning($"[AUDIO_SWITCH] Direct dAudio.Stream assign fallback: {exDirect.Message}");
                }

                // 2. Reflection fallback for Stream or SelectedStream
                if (!switched)
                {
                    var streamProp = type.GetProperty("Stream", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy) ??
                                     type.GetProperty("SelectedStream", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

                    if (streamProp != null && streamProp.CanWrite)
                    {
                        streamProp.SetValue(audioProp, targetStream);
                        switched = true;
                    }
                    else
                    {
                        var setStreamMethod = type.GetMethod("SetStream", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                        if (setStreamMethod != null)
                        {
                            setStreamMethod.Invoke(audioProp, new object[] { targetStream });
                            switched = true;
                        }
                    }
                }

                // 3. Update StreamIndex if present
                var curIndexProp = type.GetProperty("StreamIndex", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (curIndexProp != null && curIndexProp.CanWrite)
                {
                    curIndexProp.SetValue(audioProp, targetIdx);
                }

                // 4. Update demuxer options for subsequent read
                if (_config != null && _config.Demuxer != null)
                {
                    _config.Demuxer.FormatOpt["audio_stream"] = targetIdx.ToString();
                    _config.Demuxer.FormatOpt["ast"] = targetIdx.ToString();
                    _config.Demuxer.FormatOpt["aid"] = (targetIdx + 1).ToString();
                }

                // 5. Ensure unmuted
                try
                {
                    var muteProp = type.GetProperty("Mute", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                    if (muteProp != null && (bool)(muteProp.GetValue(audioProp) ?? false) == true)
                    {
                        muteProp.SetValue(audioProp, false);
                    }
                }
                catch { }

                if (switched)
                {
                    LogService.LogInfo($"[AUDIO_SWITCH] Audio track successfully switched to #{targetIdx}.");
                }

                return switched;
            }
            catch (Exception ex)
            {
                LogService.LogError("[AUDIO_SWITCH] General exception in SwitchAudioTrack", ex);
                return false;
            }
        }





        private void EnsureAudioStreamSelected()
        {
            try
            {
                if (_player == null || _player.Audio == null) return;
                if (_selectedAudioTrackIndex > 0)
                {
                    SwitchAudioTrack(_selectedAudioTrackIndex);
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[PLAYBACK] EnsureAudioStreamSelected hatasi: {ex.Message}");
            }
        }


        private void EnsureAvStreamsSelected()
        {
            EnsureVideoStreamSelected();
            EnsureAudioStreamSelected();
        }

        private void UpdateChannelLogo(Channel channel)
        {
            if (channel == null || OsdLogo == null || OsdLogoFallback == null) return;

            // 1. Instantly display monogram fallback on UI (0ms latency, zero freezing)
            string cleanName = ChannelUtils.GetCleanName(channel.PrimaryName ?? channel.Name ?? "TV");
            string monogram = ChannelLogoManager.GenerateMonogram(cleanName);
            int hash = Math.Abs(cleanName.GetHashCode());
            var bg = ChannelLogoManager.MonogramPalette[hash % ChannelLogoManager.MonogramPalette.Length];

            OsdLogoFallbackText.Text = monogram;
            OsdLogoFallback.Background = bg;
            OsdLogo.Visibility = Visibility.Collapsed;
            OsdLogoFallback.Visibility = Visibility.Visible;

            // 2. Fetch and apply remote logo asynchronously in background without touching UI thread
            _ = Task.Run(async () =>
            {
                try
                {
                    var (source, _, _) = await ChannelLogoManager.ResolveLogoAsync(channel).ConfigureAwait(false);
                    if (source != null)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (ViewModel.CurrentChannel?.Id == channel.Id)
                            {
                                OsdLogo.Source = source;
                                OsdLogo.Visibility = Visibility.Visible;
                                OsdLogoFallback.Visibility = Visibility.Collapsed;
                            }
                        });
                    }
                }
                catch { }
            });
        }

        public void LoadChannel(Channel channel)
        {
            if (channel == null) return;
            LogService.LogInfo($"[PLAYBACK] CHANNEL SELECTED: {channel.Name} (ID: {channel.Id}, Category: {channel.Category}, Url: {channel.Url})");

            ViewModel.CurrentChannel = channel;

            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new System.Threading.CancellationTokenSource();
            var token = _loadCts.Token;
            long sessionId = Interlocked.Increment(ref _playbackSessionId);

            _currentPlayingUrl = null;
            _streamStartTime = DateTime.UtcNow;
            _hasReceivedFirstFrame = false;
            _softRetriggerCount = 0;
            _isTimeshiftMode = false;
            _timeshiftStartDvrMs = 0;
            _pausedDvrPosMs = 0;
            _effectivePositionMs = 0;
            bool isAce = channel.SourceType == "ACESTREAM" ||
                         channel.Url?.Contains("acestream://", StringComparison.OrdinalIgnoreCase) == true ||
                         channel.Url?.Contains(":6878/ace/", StringComparison.OrdinalIgnoreCase) == true;
            string initialStatus = isAce ? $"{channel.PrimaryName} (Görüntü Hazırlanıyor / Tamponlanıyor...)" : $"{channel.PrimaryName} (Bağlanıyor...)";

            _ = Dispatcher.InvokeAsync(() => {
                OsdTitle.Text = initialStatus;
                OsdCategory.Text = channel.Category;
                OsdCurrentEpg.Text = "Yayın akışı bilgisi yükleniyor...";
                OsdNextEpg.Text = "Sıradaki: --:--";
                ShowOsdTemporary();
                UpdatePlayPauseIcon(true);
                UpdateChannelLogo(channel);
            });

            Task.Run(async () =>
            {
                if (token.IsCancellationRequested || sessionId != _playbackSessionId) return;

                // Wait for Player initialization (Flyleaf/FFmpeg setup)
                try
                {
                    if (_player == null)
                    {
                        await Dispatcher.InvokeAsync(InitializePlayer);
                    }
                    else
                    {
                        _initTcs.TrySetResult(true);
                    }

                    LogService.LogInfo("[PLAYBACK] Waiting for Player Init...");
                    using var initTimeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(6));
                    using var linkedInit = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(token, initTimeout.Token);
                    await _initTcs.Task.WaitAsync(linkedInit.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested || sessionId != _playbackSessionId) return;
                    LogService.LogError("[PLAYBACK] Player Init TIMEOUT");
                    _ = Dispatcher.InvokeAsync(() => {
                        OsdTitle.Text = "Görüntü motoru başlatılamadı (Zaman aşımı)";
                        ShowOsdTemporary();
                    });
                    return;
                }

                if (_player == null || token.IsCancellationRequested || sessionId != _playbackSessionId) return;

                LogService.LogInfo("[PLAYBACK] Waiting for Play Semaphore...");
                bool acquired = await _playSemaphore.WaitAsync(10000, token).ConfigureAwait(false);
                if (!acquired || token.IsCancellationRequested || sessionId != _playbackSessionId)
                {
                    LogService.LogWarning("[PLAYBACK] Play Semaphore Timeout or Cancelled");
                    return;
                }

                try
                {
                    if (ViewModel.IsVod(channel))
                    {
                        ViewModel.SaveVodPosition(channel, _player.CurTime / 10000);
                    }

                    // Removed StopAllStreamsAsync/StopAllSessions to prevent killing P2P engine session during channel load.
                    _player.Stop();
                    HlsProxyEngine.Instance.ClearChannelCache();

                    if (token.IsCancellationRequested || sessionId != _playbackSessionId) return;

                    bool isSameChannel = (_lastLoadedChannelId == channel.Id);
                    _lastLoadedChannelId = channel.Id;

                    if (!isSameChannel)
                    {
                        _selectedAudioTrackIndex = -1;
                        _selectedAudioTrackLangCode = "";
                    }

                    _ = Task.Run(async () =>
                    {
                        await ViewModel.RefreshEpgAsync(channel).ConfigureAwait(false);
                        _ = Dispatcher.InvokeAsync(() => UpdateOsdEpgForTime(DateTime.Now));
                    });

                    ApplyAudioNormalization();
                    ApplyVideoEnhancement();

                    LogService.LogInfo("[PLAYBACK] Getting candidate stream URLs...");
                    var candidateUrls = await ViewModel.GetSmartOrderedCandidatesAsync(channel, token, (status) => {
                        _ = Dispatcher.InvokeAsync(() => { OsdTitle.Text = status; ShowOsdTemporary(); });
                    }).ConfigureAwait(false);

                    if (token.IsCancellationRequested || sessionId != _playbackSessionId) return;

                    if (candidateUrls == null || candidateUrls.Count == 0)
                    {
                        LogService.LogWarning("[PLAYBACK] No candidate URLs found for channel");
                        _ = Dispatcher.InvokeAsync(() => {
                            OsdTitle.Text = $"{channel.PrimaryName} (Sinyal Zayıf veya Adres Geçersiz)";
                            UpdatePlayPauseIcon(false);
                            ShowOsdTemporary();
                        });
                        return;
                    }

                    bool playbackSucceeded = false;
                    string successfulPreparedUrl = "";

                    for (int i = 0; i < candidateUrls.Count; i++)
                    {
                        if (token.IsCancellationRequested || sessionId != _playbackSessionId) break;

                        string rawCandidate = candidateUrls[i];
                        if (i > 0)
                        {
                            _ = Dispatcher.InvokeAsync(() => {
                                OsdTitle.Text = $"{channel.PrimaryName} (Alternatif {i + 1}/{candidateUrls.Count} deneniyor...)";
                                ShowOsdTemporary();
                            });
                        }

                        LogService.LogInfo($"[SmartRouter] Trying candidate {i + 1}/{candidateUrls.Count}: '{rawCandidate}'");

                        string preparedUrl = await ViewModel.PrepareSingleStreamUrlAsync(rawCandidate, channel, token, (status) => {
                            _ = Dispatcher.InvokeAsync(() => { OsdTitle.Text = status; ShowOsdTemporary(); });
                        }).ConfigureAwait(false);

                        if (token.IsCancellationRequested || sessionId != _playbackSessionId) break;

                        if (string.IsNullOrEmpty(preparedUrl))
                        {
                            LogService.LogWarning($"[SmartRouter] Candidate {i + 1}/{candidateUrls.Count} could not be prepared (empty URL).");
                            continue;
                        }

                        // Per-candidate watchdog (15s for IPTV/Direct, 35s for AceStream P2P tracker and peer discovery)
                        bool isAceStreamCandidate = channel.SourceType == "ACESTREAM" ||
                                                    rawCandidate.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase) ||
                                                    rawCandidate.Contains(":6878/ace/") ||
                                                    rawCandidate.Contains("session=ace_") ||
                                                    preparedUrl.Contains(":6878/ace/") ||
                                                    preparedUrl.Contains("session=ace_");
                        int watchdogMs = isAceStreamCandidate ? 35000 : 15000;

                        if (isAceStreamCandidate)
                        {
                            _ = Dispatcher.InvokeAsync(() => {
                                OsdTitle.Text = $"{channel.PrimaryName} (P2P Eşleri Aranıyor & Bağlanıyor...)";
                                ShowOsdTemporary();
                            });
                        }

                        _currentPlayingUrl = preparedUrl; // Erken tampon boşluğunda Status.Ended tetiklenirse auto-recovery'nin hemen devreye girmesi için

                        bool candidateStarted = await OpenAndAwaitPlaybackAsync(preparedUrl, channel, watchdogMs, token, sessionId).ConfigureAwait(false);

                        if (token.IsCancellationRequested || sessionId != _playbackSessionId) break;

                        if (candidateStarted)
                        {
                            playbackSucceeded = true;
                            successfulPreparedUrl = preparedUrl;
                            LogService.LogInfo($"[SmartRouter] Playback started successfully on candidate {i + 1}/{candidateUrls.Count} -> {preparedUrl}");
                            break;
                        }
                        else
                        {
                            _currentPlayingUrl = null;
                            LogService.LogWarning($"[SmartRouter] Candidate {i + 1}/{candidateUrls.Count} failed (Status: {_player?.Status}). Moving to next alternative...");
                            try { _player?.Stop(); } catch { }
                        }
                    }

                    if (token.IsCancellationRequested || sessionId != _playbackSessionId) return;

                    if (playbackSucceeded)
                    {
                        _currentPlayingUrl = successfulPreparedUrl;
                        _ = Dispatcher.InvokeAsync(() => {
                            if (ViewModel.CurrentChannel != null) OsdTitle.Text = ViewModel.CurrentChannel.PrimaryName;
                            UpdatePlayPauseIcon(true);
                            ShowOsdTemporary();
                        });

                        if (ViewModel.IsVod(channel) && channel.LastPositionMs > 0 && _player != null)
                        {
                            try { _player.Seek((int)channel.LastPositionMs); _lastSeekTime = DateTime.Now; _effectivePositionMs = channel.LastPositionMs; } catch { }
                        }

                        _ = Dispatcher.InvokeAsync(() => _positionTimer?.Start());
                    }
                    else
                    {
                        _currentPlayingUrl = null;
                        LogService.LogWarning($"[SmartRouter] All {candidateUrls.Count} candidate URLs failed for channel '{channel.Name}'.");
                        _ = Dispatcher.InvokeAsync(() => {
                            OsdTitle.Text = $"{channel.PrimaryName} (Sinyal Yok / Tüm Kaynaklar Başarısız)";
                            UpdatePlayPauseIcon(false);
                            ShowOsdTemporary();
                            CheckAndShowDeadAccountAlert(candidateUrls);
                        });
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("[PLAYBACK] CRITICAL ERROR", ex);
                    if (!token.IsCancellationRequested && sessionId == _playbackSessionId)
                    {
                        _ = Dispatcher.InvokeAsync(() => {
                            OsdTitle.Text = $"{channel.PrimaryName} (Hata: {ex.Message})";
                            ShowOsdTemporary();
                        });
                    }
                }
                finally
                {
                    _isFallbackActive = false;
                    _playSemaphore.Release();
                    LogService.LogInfo("[PLAYBACK] Play Semaphore Released");
                }
            }, token);
        }

        private async Task<bool> OpenAndAwaitPlaybackAsync(string url, Channel channel, int watchdogMs, CancellationToken token, long sessionId)
        {
            if (_player == null || token.IsCancellationRequested || sessionId != _playbackSessionId)
                return false;

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            PropertyChangedEventHandler statusHandler = (s, e) =>
            {
                if (e.PropertyName == nameof(Player.Status))
                {
                    if (sessionId != _playbackSessionId)
                    {
                        tcs.TrySetResult(false);
                        return;
                    }

                    if (_player.Status == Status.Playing)
                    {
                        tcs.TrySetResult(true);
                    }
                    else if (_player.Status == Status.Paused)
                    {
                        Dispatcher.InvokeAsync(() => {
                            try { if (_player?.Status == Status.Paused) _player.Play(); } catch { }
                        });
                        tcs.TrySetResult(true);
                    }
                    else if (_player.Status == Status.Failed)
                    {
                        tcs.TrySetResult(false);
                    }
                }
            };

            EventHandler<OpenCompletedArgs>? openHandler = null;
            openHandler = (s, e) =>
            {
                if (sessionId != _playbackSessionId)
                {
                    tcs.TrySetResult(false);
                    return;
                }

                if (e.Success)
                {
                    LogService.LogInfo($"[PLAYBACK] OpenCompleted successfully! Status={_player?.Status}. Ensuring playback is started.");
                    Dispatcher.InvokeAsync(() => {
                        try
                        {
                            if (_player != null && (_player.Status == Status.Paused || _player.Status == Status.Stopped))
                            {
                                _player.Play();
                            }
                            EnsureVideoStreamSelected();
                            EnsureAudioStreamSelected();
                            RefreshAudioTracks();
                        }
                        catch { }
                    });
                    tcs.TrySetResult(true);
                }
                else
                {
                    LogService.LogWarning($"[PLAYBACK] OpenCompleted failed: {e.Error}");
                    tcs.TrySetResult(false);
                }
            };

            _player.PropertyChanged += statusHandler;
            _player.OpenCompleted += openHandler;

            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();

                if (_config != null)
                {
                    // 1. Channel User-Agent (örn: Firefox, VLC, Chrome vb.)
                    string ua = !string.IsNullOrWhiteSpace(channel.HttpUserAgent)
                        ? channel.HttpUserAgent
                        : "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";
                    _config.Demuxer.FormatOpt["user_agent"] = ua;
                    
                    bool isAceStreamStream = channel.SourceType == "ACESTREAM" ||
                                             url.Contains("session=ace_") ||
                                             url.Contains(":6878/ace/") ||
                                             url.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase);

                    // Timeout ve demuxer ayarları (Çoklu dil ve ses izlerinin eksiksiz algılanması için geniş probe profili)
                    if (isAceStreamStream)
                    {
                        _config.Demuxer.FormatOpt["stimeout"] = "30000000";
                        _config.Demuxer.FormatOpt["reconnect"] = "1";
                        _config.Demuxer.FormatOpt["reconnect_at_eof"] = "1";
                        _config.Demuxer.FormatOpt["reconnect_streamed"] = "1";
                        _config.Demuxer.FormatOpt["reconnect_delay_max"] = "2";
                        _config.Demuxer.FormatOpt["timeout"] = "30000000";
                        _config.Demuxer.FormatOpt["probesize"] = "10000000";       // 10 MB (Tüm ses izlerinin ve PMT PIDs haritasının tam okunması için)
                        _config.Demuxer.FormatOpt["analyzeduration"] = "10000000"; // 10 saniye
                        _config.Demuxer.FormatOpt["fflags"] = "+genpts+igndts+discardcorrupt";
                        _config.Demuxer.FormatOpt["flush_packets"] = "1";
                        _config.Demuxer.FormatOpt["ts_discont_detect"] = "1";
                        _config.Demuxer.FormatOpt["rw_timeout"] = "30000000";
                        _config.Demuxer.BufferDuration = 3000 * 10000;
                    }
                    else
                    {
                        _config.Demuxer.FormatOpt["stimeout"] = "15000000";
                        _config.Demuxer.FormatOpt["reconnect"] = "1";
                        _config.Demuxer.FormatOpt["reconnect_at_eof"] = "1";
                        _config.Demuxer.FormatOpt["reconnect_streamed"] = "1";
                        _config.Demuxer.FormatOpt["reconnect_delay_max"] = "2";
                        _config.Demuxer.FormatOpt["timeout"] = "15000000";
                        _config.Demuxer.FormatOpt["probesize"] = "10000000";       // 10 MB (MPEG-TS çift/çoklu dil kanallarının tüm ses PID'lerini bulması için)
                        _config.Demuxer.FormatOpt["analyzeduration"] = "5000000";  // 5 saniye
                        _config.Demuxer.FormatOpt["fflags"] = "+genpts+igndts+discardcorrupt";
                        _config.Demuxer.FormatOpt["flush_packets"] = "1";
                        _config.Demuxer.FormatOpt["ts_discont_detect"] = "1";
                        _config.Demuxer.FormatOpt["rw_timeout"] = "15000000";
                        _config.Demuxer.BufferDuration = 3000 * 10000;
                    }

                    // 2. Channel HTTP Headers (Referer, Cookie, Origin vb.)
                    var customHeaders = new System.Text.StringBuilder();
                    if (!string.IsNullOrWhiteSpace(channel.HttpReferer))
                        customHeaders.Append($"Referer: {channel.HttpReferer}\r\n");
                    if (!string.IsNullOrWhiteSpace(channel.HttpCookie))
                        customHeaders.Append($"Cookie: {channel.HttpCookie}\r\n");
                    if (!string.IsNullOrWhiteSpace(channel.HttpOrigin))
                        customHeaders.Append($"Origin: {channel.HttpOrigin}\r\n");
                    if (channel.CustomHeaders != null)
                    {
                        foreach (var kv in channel.CustomHeaders)
                        {
                            if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                                customHeaders.Append($"{kv.Key}: {kv.Value}\r\n");
                        }
                    }

                    if (customHeaders.Length > 0)
                    {
                        _config.Demuxer.FormatOpt["headers"] = customHeaders.ToString();
                    }
                    else
                    {
                        _config.Demuxer.FormatOpt.Remove("headers");
                    }

                    // 3. Audio stream selection index for multi-audio tracks (e.g. MPEG-TS / AceStream)
                    if (_selectedAudioTrackIndex >= 0)
                    {
                        _config.Demuxer.FormatOpt["audio_stream"] = _selectedAudioTrackIndex.ToString();
                        _config.Demuxer.FormatOpt["ast"] = _selectedAudioTrackIndex.ToString();
                        _config.Demuxer.FormatOpt["aid"] = (_selectedAudioTrackIndex + 1).ToString();
                    }
                    else
                    {
                        _config.Demuxer.FormatOpt.Remove("audio_stream");
                        _config.Demuxer.FormatOpt.Remove("ast");
                        _config.Demuxer.FormatOpt.Remove("aid");
                    }
                }

                var openTask = Task.Run(async () =>
                {
                    try
                    {
                        bool isAceStream = channel.SourceType == "ACESTREAM" ||
                                           url.Contains("session=ace_") ||
                                           url.Contains(":6878/ace/") ||
                                           url.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase);
                        if (isAceStream)
                        {
                            // AceStream motorunun çalıştığından ve HTTP portunun hazır olduğundan emin ol (stop_all gönderip motoru meşgule DÜŞÜRMEZ!)
                            await _ace.StartEngineIfNotRunningAsync().ConfigureAwait(false);
                        }

                        await Dispatcher.InvokeAsync(() =>
                        {
                            _player.Open(url);
                            if (_player.Status == Status.Paused)
                            {
                                try { _player.Play(); } catch { }
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError($"[PLAYBACK] Player.Open Exception for {url}", ex);
                        tcs.TrySetResult(false);
                    }
                }, token);

                if (_player.Status == Status.Playing || _player.Status == Status.Paused)
                {
                    if (_player.Status == Status.Paused)
                    {
                        _ = Dispatcher.InvokeAsync(() => { try { _player?.Play(); } catch { } });
                    }
                    return true;
                }

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var timeoutTask = Task.Delay(watchdogMs, linkedCts.Token);

                var completed = await Task.WhenAny(tcs.Task, timeoutTask).ConfigureAwait(false);
                sw.Stop();

                if (token.IsCancellationRequested || sessionId != _playbackSessionId)
                    return false;

                if (completed == tcs.Task)
                {
                    linkedCts.Cancel();
                    bool isSuccess = await tcs.Task.ConfigureAwait(false);
                    LogService.LogInfo($"[SmartRouter] Candidate response in {sw.ElapsedMilliseconds}ms -> Success: {isSuccess}, Player.Status: {_player.Status}");
                    return isSuccess;
                }
                else
                {
                    LogService.LogWarning($"[SmartRouter] Candidate watchdog timeout after {sw.ElapsedMilliseconds}ms ({watchdogMs}ms limit) for {url}");
                    return false;
                }
            }
            finally
            {
                _player.PropertyChanged -= statusHandler;
                _player.OpenCompleted -= openHandler;
            }
        }

        public async void Stop()
        {
            _wasPausedByMinimize = false;
            _loadCts?.Cancel();
            _positionTimer?.Stop();

            var ch = ViewModel.CurrentChannel;
            if (IsCurrentStreamVod() && ch != null && _player != null)
            {
                try
                {
                    ch.LastPositionMs = _player.CurTime / 10000;
                    _ = _db.SaveChannelAsync(ch);
                }
                catch { }
            }

            _player?.Stop();
            _effectivePositionMs = 0;
            _pausedDvrPosMs = 0;
            _isTimeshiftMode = false;
            _timeshiftStartDvrMs = 0;
            HlsProxyEngine.Instance.ClearChannelCache();
            if (ch?.SourceType == "ACESTREAM" || (ch?.Url?.Contains("acestream://") == true))
            {
                await _ace.StopAllStreamsAsync();
                AceStreamService.Instance.StopAllSessions();
            }
        }

        public void PauseForMinimize()
        {
            if (_player == null || ViewModel.CurrentChannel == null) return;

            if (_player.Status == Status.Playing)
            {
                _wasPausedByMinimize = true;
                TogglePause();
                LogService.LogInfo("[WINDOW] Yayının oynatılması pencere simge durumuna küçültüldüğü için geçici olarak duraklatıldı.");
            }
        }

        public void ResumeFromMinimize()
        {
            if (_player == null || ViewModel.CurrentChannel == null) return;

            if (_wasPausedByMinimize)
            {
                _wasPausedByMinimize = false;
                if (_player.Status == Status.Paused)
                {
                    TogglePause();
                    LogService.LogInfo("[WINDOW] Pencere tekrar açıldı, yayın duraklatıldığı yerden devam ettiriliyor.");
                }
            }
        }

        public void PlayPause_Click(object sender, RoutedEventArgs e) { TogglePause(); }
        public void Stop_Click(object sender, RoutedEventArgs e)
        {
            Stop();
            ShowOsdToast("⏹ Yayın Durduruldu", "GeoStop");
            ShowOsdTemporary();
        }

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
            if (_player == null || ViewModel.CurrentChannel == null) return;
            ShowOsdTemporary();

            bool isVod = ViewModel.IsVod(ViewModel.CurrentChannel);

            if (_player.Status == Status.Playing)
            {
                if (!isVod)
                {
                    // Canlı yayında duraklatılan DVR zamanını sakla
                    var proxySession = HlsProxyEngine.Instance.GetSession(ViewModel.CurrentChannel?.Url ?? "")
                                       ?? (_currentPlayingUrl != null ? HlsProxyEngine.Instance.GetSession(_currentPlayingUrl) : null);

                    if (proxySession != null && proxySession.Segments.Count > 0)
                    {
                        var segments = proxySession.Segments;
                        double earliestSec = segments.Count > 0 ? segments[0].StartTimeSeconds : 0;
                        double latestSec = proxySession.TotalDurationSeconds;
                        long earliestMs = (long)(earliestSec * 1000);
                        long latestMs = (long)(latestSec * 1000);

                        if (_isTimeshiftMode)
                        {
                            long curMs = _player.CurTime / 10000;
                            _pausedDvrPosMs = Math.Clamp(_timeshiftStartDvrMs + curMs, earliestMs, latestMs);
                        }
                        else
                        {
                            _pausedDvrPosMs = _effectivePositionMs > 0 ? Math.Clamp(_effectivePositionMs, earliestMs, latestMs) : latestMs;
                        }
                    }
                }

                _player.Pause();
                UpdatePlayPauseIcon(false);
            }
            else
            {
                if (!isVod && _pausedDvrPosMs > 0)
                {
                    // Canlı yayında duraklatıldığı noktadan mevcut Timeshift zinciriyle devam et
                    long resumeTargetMs = _pausedDvrPosMs;
                    _pausedDvrPosMs = 0;
                    SeekAbsolute(resumeTargetMs);
                }
                else
                {
                    _player.Play();
                }
                UpdatePlayPauseIcon(true);
            }
        }

        public void SeekRelative(int deltaMs)
        {
            if (_player == null || ViewModel.CurrentChannel == null) return;

            if (ViewModel.IsVod(ViewModel.CurrentChannel))
            {
                long currentPosMs;
                if ((DateTime.Now - _lastSeekTime).TotalSeconds < 2.0 && _effectivePositionMs > 0)
                {
                    currentPosMs = _effectivePositionMs;
                }
                else
                {
                    currentPosMs = _player.CurTime / 10000;
                }

                long targetMs = currentPosMs + deltaMs;
                PerformSeek(targetMs);
            }
            else
            {
                var proxySession = HlsProxyEngine.Instance.GetSession(ViewModel.CurrentChannel?.Url ?? "")
                                   ?? (_currentPlayingUrl != null ? HlsProxyEngine.Instance.GetSession(_currentPlayingUrl) : null);

                if (proxySession != null && proxySession.Segments.Count > 0)
                {
                    long totalMsDvr = (long)(proxySession.TotalDurationSeconds * 1000);
                    long currentDvrPosMs;

                    if (_isTimeshiftMode)
                    {
                        if ((DateTime.Now - _lastSeekTime).TotalSeconds < 2.0 && _effectivePositionMs > 0)
                            currentDvrPosMs = _effectivePositionMs;
                        else
                            currentDvrPosMs = _timeshiftStartDvrMs + (_player.CurTime / 10000);
                    }
                    else
                    {
                        currentDvrPosMs = totalMsDvr;
                    }

                    long targetMs = currentDvrPosMs + deltaMs;
                    PerformSeek(targetMs);
                }
            }
        }

        public void SeekAbsolute(long targetMs)
        {
            PerformSeek(targetMs);
        }

        private void PerformSeek(long targetMs)
        {
            if (_player == null || ViewModel.CurrentChannel == null) return;

            bool isVod = ViewModel.IsVod(ViewModel.CurrentChannel);

            if (isVod)
            {
                long lengthMs = _player.Duration / 10000;
                if (lengthMs <= 0) return;

                targetMs = Math.Clamp(targetMs, 0, lengthMs);

                try
                {
                    _player.Seek((int)targetMs);
                    _lastSeekTime = DateTime.Now;
                    _effectivePositionMs = targetMs;

                    TimeSlider.Value = targetMs;
                    TimeCurrentText.Text = TimeSpan.FromMilliseconds(targetMs).ToString(@"hh\:mm\:ss");
                    ShowOsdTemporary();
                }
                catch (Exception ex)
                {
                    LogService.LogError($"Player: VOD Seek to {targetMs}ms failed", ex);
                    _effectivePositionMs = _player.CurTime / 10000;
                }
            }
            else
            {
                var proxySession = HlsProxyEngine.Instance.GetSession(ViewModel.CurrentChannel?.Url ?? "")
                                   ?? (_currentPlayingUrl != null ? HlsProxyEngine.Instance.GetSession(_currentPlayingUrl) : null);

                if (proxySession != null && proxySession.Segments.Count > 0)
                {
                    var segments = proxySession.Segments;
                    double earliestSec = segments.Count > 0 ? segments[0].StartTimeSeconds : 0;
                    double latestSec = proxySession.TotalDurationSeconds;
                    long earliestMs = (long)(earliestSec * 1000);
                    long latestMs = (long)(latestSec * 1000);

                    targetMs = Math.Clamp(targetMs, earliestMs, latestMs);
                    long offsetMs = latestMs - targetMs;

                    if (offsetMs <= 3000)
                    {
                        GoLive();
                        return;
                    }

                    try
                    {
                        string sourceUrl = ViewModel.CurrentChannel?.Url ?? _currentPlayingUrl ?? "";
                        if (string.IsNullOrEmpty(sourceUrl) || _player == null) return;

                        int targetSec = (int)(targetMs / 1000);
                        string timeshiftUrl = HlsProxyEngine.Instance.GetProxyPlaybackUrl(sourceUrl, targetSec);

                        _isTimeshiftMode = true;
                        _timeshiftStartDvrMs = targetMs;
                        _lastSeekTime = DateTime.Now;
                        _effectivePositionMs = targetMs;

                        _player.Open(timeshiftUrl);

                        TimeSlider.Value = targetMs;
                        UpdateOsdTimeAndBadge(targetMs, latestMs, proxySession.StartWallClockTime);
                        ShowOsdTemporary();
                        LogService.LogInfo($"Player: Seeked DVR buffer to {targetSec}s (offset: -{TimeSpan.FromMilliseconds(offsetMs):hh\\:mm\\:ss})");
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError($"Player: DVR Seek to {targetMs}ms failed", ex);
                        _effectivePositionMs = _player.CurTime / 10000;
                    }
                }
                else
                {
                    LogService.LogInfo("Player: Live stream has no DVR buffer, seek ignored.");
                }
            }
        }

        public void Rewind(int ms) { SeekRelative(-ms); }
        public void Forward(int ms) { SeekRelative(ms); }
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
                if (ViewModel.IsVod(ViewModel.CurrentChannel))
                {
                    TimeCurrentText.Text = TimeSpan.FromMilliseconds((long)e.NewValue).ToString(@"hh\:mm\:ss");
                }
                else
                {
                    var proxySession = HlsProxyEngine.Instance.GetSession(ViewModel.CurrentChannel?.Url ?? "")
                                       ?? (_currentPlayingUrl != null ? HlsProxyEngine.Instance.GetSession(_currentPlayingUrl) : null);
                    if (proxySession != null)
                    {
                        UpdateOsdTimeAndBadge((long)e.NewValue, (long)(proxySession.TotalDurationSeconds * 1000), proxySession.StartWallClockTime);
                    }
                }
            }
        }

        private void ApplySliderSeek(double value)
        {
            SeekAbsolute((long)value);
        }

        public void Rewind10_Click(object sender, RoutedEventArgs e) { Rewind(10000); }
        public void Forward10_Click(object sender, RoutedEventArgs e) { Forward(10000); }

        public void GoLive()
        {
            if (_player == null || ViewModel.CurrentChannel == null) return;

            var proxySession = HlsProxyEngine.Instance.GetSession(ViewModel.CurrentChannel.Url ?? "")
                               ?? (_currentPlayingUrl != null ? HlsProxyEngine.Instance.GetSession(_currentPlayingUrl) : null);
            if (proxySession != null && proxySession.Segments.Count > 0)
            {
                long totalMs = (long)(proxySession.TotalDurationSeconds * 1000);
                _isTimeshiftMode = false;
                _timeshiftStartDvrMs = 0;
                _effectivePositionMs = totalMs;

                string liveSourceUrl = ViewModel.CurrentChannel.Url ?? _currentPlayingUrl ?? "";
                string liveUrl = HlsProxyEngine.Instance.GetProxyPlaybackUrl(liveSourceUrl, -1);
                _player.Open(liveUrl);

                TimeSlider.Value = totalMs;
                TimeCurrentText.Text = DateTime.Now.ToString("HH:mm:ss");
                if (LiveBadge != null) LiveBadge.Background = LiveRedBrush;
                if (LiveBadgeText != null) LiveBadgeText.Text = "🔴 CANLI";
                if (GoLiveBtn != null) GoLiveBtn.Background = LiveRedBrush;
                if (GoLiveText != null) GoLiveText.Text = "CANLI";
                TimeTotalText.Text = "CANLI YAYIN";
                UpdateOsdEpgForTime(DateTime.Now);

                LogService.LogInfo($"Player: Returned to Live Edge at {totalMs / 1000}s");
            }
            else if (_player.Duration > 0 && !ViewModel.IsVod(ViewModel.CurrentChannel))
            {
                long durationMs = _player.Duration / 10000;
                _player.Seek((int)durationMs);
            }
            ShowOsdTemporary();
        }

        public void GoLive_Click(object sender, RoutedEventArgs e) { GoLive(); }
        public void LiveBadge_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e) { GoLive(); }
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

                    bool switched = SwitchAudioTrack(item.Index, item.RawStream);
                    if (switched)
                    {
                        LogService.LogInfo($"Player: Audio track switched successfully to {item.DisplayName} ({item.LangCode})");
                    }
                    else
                    {
                        LogService.LogWarning($"Player: Audio track switch returned false for {item.DisplayName}");
                    }

                    if (!string.IsNullOrEmpty(_lastLoadedChannelId))
                    {
                        ViewModel.SavePreferredLanguageForChannel(_lastLoadedChannelId, item.LangCode);
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

                    // Automatic language preference matching for multi-audio streams
                    string preferredLang = ViewModel.GetPreferredLanguageForChannel(_lastLoadedChannelId);
                    if (_selectedAudioTrackIndex < 0 && !string.IsNullOrEmpty(preferredLang) && _detectedAudioTracks.Count > 1)
                    {
                        var matchingTrack = _detectedAudioTracks.FirstOrDefault(t => string.Equals(t.LangCode, preferredLang, StringComparison.OrdinalIgnoreCase))
                                            ?? _detectedAudioTracks.FirstOrDefault(t => t.DisplayName.Contains(preferredLang, StringComparison.OrdinalIgnoreCase));
                        if (matchingTrack != null && matchingTrack.Index != 0)
                        {
                            _selectedAudioTrackIndex = matchingTrack.Index;
                            _selectedAudioTrackLangCode = matchingTrack.LangCode;
                            SwitchAudioTrack(matchingTrack.Index, matchingTrack.RawStream);
                            foreach (var t in _detectedAudioTracks) t.IsSelected = (t.Index == matchingTrack.Index);
                        }
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

            if (l == "deu" || l == "ger" || l == "de" || t.Contains("german") || t.Contains("almanca"))
                return ("Almanca", "DE");
            if (l == "eng" || l == "en" || t.Contains("english") || t.Contains("ingilizce"))
                return ("İngilizce", "EN");
            if (l == "tur" || l == "tr" || t.Contains("türkçe") || t.Contains("turkce"))
                return ("Türkçe", "TR");
            if (l == "fra" || l == "fre" || l == "fr" || t.Contains("french") || t.Contains("fransızca"))
                return ("Fransızca", "FR");
            if (l == "ita" || l == "it" || t.Contains("italian") || t.Contains("italyanca"))
                return ("İtalyanca", "IT");
            if (l == "spa" || l == "es" || t.Contains("spanish") || t.Contains("ispanyolca"))
                return ("İspanyolca", "ES");
            if (l == "rus" || l == "ru" || t.Contains("russian") || t.Contains("rusça"))
                return ("Rusça", "RU");
            if (l == "ara" || l == "ar" || t.Contains("arabic") || t.Contains("arapça"))
                return ("Arapça", "AR");
            if (l == "aze" || l == "az" || t.Contains("azerbaijani") || t.Contains("azerice"))
                return ("Azerice", "AZ");
            if (l == "jpn" || l == "ja" || t.Contains("japanese") || t.Contains("japonca"))
                return ("Japonca", "JA");
            if (l == "kor" || l == "ko" || t.Contains("korean") || t.Contains("korece"))
                return ("Korece", "KO");
            if (l == "chi" || l == "zho" || l == "zh" || t.Contains("chinese") || t.Contains("çince"))
                return ("Çince", "ZH");
            if (l == "por" || l == "pt" || t.Contains("portuguese") || t.Contains("portekizce"))
                return ("Portekizce", "PT");

            if (!string.IsNullOrWhiteSpace(title))
                return (title, title.Length <= 3 ? title.ToUpperInvariant() : "SES");

            return ($"Ses İzi {index + 1}", $"SES {index + 1}");
        }

        #endregion

        #region Audio Normalization & Video Enhancer (Ses Normalizasyonu & Görüntü Netleştirici)

        private void LoadAudioVideoSettings()
        {
            _isAudioNormEnabled = ViewModel.IsAudioNormEnabled;
            _isVideoEnhanceEnabled = ViewModel.IsVideoEnhanceEnabled;
        }

        private void UpdateAudioNormButtonUI()
        {
            if (AudioNormBtn == null) return;
            if (_isAudioNormEnabled)
            {
                AudioNormBtn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105)); // Emerald Green
                AudioNormBtn.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 211, 153));
                AudioNormBtn.ToolTip = "Ses Normalizasyonu: AÇIK (EBU R128 & Dinamik DSP Seviye Dengeleme)";
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
                VideoEnhanceBtn.ToolTip = "Görüntü Netleştirici: AÇIK (GPU Hibrit Keskinlik & D3D11 Kontrast)";
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
                        SetDynamicProperty(videoObj, "Contrast", _isVideoEnhanceEnabled ? 1.15f : 1.0f);
                        SetDynamicProperty(videoObj, "Saturation", _isVideoEnhanceEnabled ? 1.10f : 1.0f);
                        SetDynamicProperty(videoObj, "Brightness", _isVideoEnhanceEnabled ? 0.02f : 0.0f);

                        var filtersProp = videoObj.GetType().GetProperty("Filters");
                        if (filtersProp?.GetValue(videoObj) is System.Collections.IList filtersList)
                        {
                            filtersList.Clear();
                            if (_isVideoEnhanceEnabled)
                            {
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
            if (ViewModel.CurrentChannel == null || _player == null) return;

            try
            {
                long currentPosMs = 0;
                if (ViewModel.IsVod(ViewModel.CurrentChannel))
                {
                    currentPosMs = _player.CurTime / 10000;
                    ViewModel.CurrentChannel.LastPositionMs = currentPosMs;
                }

                ApplyAudioNormalization();
                ApplyVideoEnhancement();

                LogService.LogInfo($"Player: Fast reloading current stream for '{ViewModel.CurrentChannel.PrimaryName}' to apply filters/reconnect (VOD pos: {currentPosMs}ms)");
                LoadChannel(ViewModel.CurrentChannel);
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
            if (ViewModel.CurrentChannel == null || OsdCurrentEpg == null || OsdNextEpg == null) return;
            if (ViewModel.EpgList != null && ViewModel.EpgList.Count > 0)
            {
                var matching = ViewModel.EpgList.Where(p => targetTime >= p.StartTime && targetTime <= p.EndTime).FirstOrDefault();
                OsdCurrentEpg.Text = matching != null ? $"{matching.StartTime:HH:mm} - {matching.EndTime:HH:mm} {matching.Title}" : "Yayın akışı bilgisi yok";

                var future = ViewModel.EpgList.Where(p => p.StartTime > targetTime).OrderBy(p => p.StartTime).FirstOrDefault();
                OsdNextEpg.Text = future != null ? $"Sıradaki: {future.StartTime:HH:mm} {future.Title}" : "Sıradaki: --:-- Bilgi yok";
            }
            else
            {
                OsdCurrentEpg.Text = "Yayın akışı bilgisi yok";
                OsdNextEpg.Text = "Sıradaki: --:-- Bilgi yok";
            }
        }

        private IptvAccountInfo? _currentDeadAccountDetected = null;

        private async void CheckAndShowDeadAccountAlert(List<string> candidateUrls)
        {
            try
            {
                if (candidateUrls == null || candidateUrls.Count == 0) return;

                IptvAccountInfo? foundAccount = null;
                foreach (var url in candidateUrls)
                {
                    foundAccount = IptvAccountHelper.ParseAccountFromUrl(url);
                    if (foundAccount != null && !string.IsNullOrEmpty(foundAccount.Username)) break;
                }

                if (foundAccount == null)
                {
                    DeadAccountAlertCard.Visibility = Visibility.Collapsed;
                    return;
                }

                _currentDeadAccountDetected = foundAccount;
                var counts = await IptvAccountHelper.CountAccountOccurrencesAsync(foundAccount);

                if (counts.totalChannels > 1 || counts.totalUrls > 1)
                {
                    DeadAccountAlertText.Text = $"Bu yayın `{foundAccount.Username}` (@{foundAccount.HostWithPort}) hesabına bağlı ve yanıt vermiyor. Veritabanında bu hesaba ait toplam {counts.totalUrls} adet link ({counts.totalChannels} kanal/içerik) var. Bu hesaba ait tüm linkleri topluca temizlemek ister misiniz?";
                    DeadAccountAlertCard.Visibility = Visibility.Visible;
                }
            }
            catch { }
        }

        private async void BtnPurgeDeadAccount_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDeadAccountDetected == null) return;

            var acc = _currentDeadAccountDetected;
            DeadAccountAlertCard.Visibility = Visibility.Collapsed;

            var res = System.Windows.MessageBox.Show(
                $"'{acc.Username}' (@{acc.HostWithPort}) hesabına ait tüm IPTV yayınları ve bu hesaba özel kanallar veritabanından kalıcı olarak silinecektir.\n\nOnaylıyor musunuz?",
                "Ölü IPTV Hesabını Temizle",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res == MessageBoxResult.Yes)
            {
                try
                {
                    var purgeResult = await IptvAccountHelper.PurgeAccountFromDatabaseAsync(acc);
                    ShowOsdToast($"Ölü Hesap Temizlendi: {purgeResult.TotalUrlsRemoved} URL, {purgeResult.ChannelsDeletedEntirely} Kanal Silindi", "GeoTrash");
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Temizleme hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnCloseDeadAccountAlert_Click(object sender, RoutedEventArgs e)
        {
            DeadAccountAlertCard.Visibility = Visibility.Collapsed;
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
                    var ch = ViewModel.CurrentChannel;
                    if (ch != null && IsCurrentStreamVod())
                    {
                        ViewModel.SaveVodPosition(ch, _player.CurTime / 10000);
                    }
                    _player.Stop();

                    if (ch?.SourceType == "ACESTREAM" || (ch?.Url?.Contains("acestream://") == true))
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
