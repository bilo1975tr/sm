using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using StreamMesh.Core;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media;
using StreamMesh.Core.Media.Iptv;
using StreamMesh.Core.Media.Mpv;
using StreamMesh.Core.Network;
using StreamMesh.Core.Plugins;
using StreamMesh.Core.Utils;
using StreamMesh.Models;
using StreamMesh.UI.ViewModels;

namespace StreamMesh.UI.Views
{
    public partial class PlayerView : System.Windows.Controls.UserControl, IDisposable
    {
        public PlayerViewModel ViewModel { get; } = new PlayerViewModel();

        private MpvPlayerEngine? _mpv;
        private readonly TaskCompletionSource<bool> _initTcs = new();
        private readonly AceEngine _ace = AceEngine.Instance;
        private readonly DatabaseEngine _db = DatabaseEngine.Instance;
        private readonly SemaphoreSlim _playSemaphore = new(1, 1);

        private CancellationTokenSource? _recordCts;
        private bool _isRecording;
        private string? _recordFilePath;

        private System.Windows.Threading.DispatcherTimer? _osdTimer;
        private System.Windows.Threading.DispatcherTimer? _positionTimer;
        private System.Windows.Threading.DispatcherTimer? _viewersTimer;
        private System.Windows.Threading.DispatcherTimer? _toastTimer;
        private string? _currentPlayingUrl;

        private bool _isUserDraggingSlider;
        private bool _isMouseOverOsd;
        private DateTime _streamStartTime = DateTime.UtcNow;

        private static readonly SolidColorBrush LiveRedBrush = CreateFrozenBrush(220, 38, 38);
        private static readonly SolidColorBrush DelayedAmberBrush = CreateFrozenBrush(245, 158, 11);
        private static readonly SolidColorBrush VodBlueBrush = CreateFrozenBrush(37, 99, 235);
        private static readonly SolidColorBrush SlateBtnBrush = CreateFrozenBrush(51, 65, 85);
        private static readonly SolidColorBrush SlateBorderBrush = CreateFrozenBrush(255, 255, 255, 51);
        private static readonly SolidColorBrush NormActiveBrush = CreateFrozenBrush(5, 150, 105);
        private static readonly SolidColorBrush NormBorderBrush = CreateFrozenBrush(52, 211, 153);
        private static readonly SolidColorBrush SharpenActiveBrush = CreateFrozenBrush(217, 119, 6);
        private static readonly SolidColorBrush SharpenBorderBrush = CreateFrozenBrush(251, 191, 36);
        private static readonly SolidColorBrush RecordActiveIconBrush = CreateFrozenBrush(239, 68, 68);

        private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b, byte a = 255)
        {
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        // Unified playback / timeshift state
        private DateTime _lastSeekTime = DateTime.MinValue;
        private long _effectivePositionMs;
        private bool _isTimeshiftMode;
        private long _timeshiftStartDvrMs;
        private DateTime _timeshiftPlayAnchorUtc = DateTime.MinValue;
        private long _timeshiftAccumulatedPlayMs;
        private long _pausedDvrPosMs;
        private bool _hasPausedDvrPos;
        private bool _userPaused;
        private DateTime _livePauseStartedUtc = DateTime.MinValue;
        private long _accumulatedLiveLagMs;

        private HlsSessionInfo? _activeProxySession;
        private CancellationTokenSource? _loadCts;
        private long _playbackSessionId;
        private bool _wasPausedByMinimize;
        private bool _hasReceivedFirstFrame;
        private bool _mpvDownloadTriggered;

        // UI deduplication caches
        private DateTime _lastMouseOsdResetUtc = DateTime.MinValue;
        private string _lastTimeCurrentText = "";
        private string _lastTimeTotalText = "";
        private string _lastLiveBadgeText = "";
        private string _lastGoLiveText = "";
        private string _lastCurrentEpgText = "";
        private string _lastNextEpgText = "";

        // Audio & Video DSP States
        private bool _isAudioNormEnabled;
        private bool _isVideoEnhanceEnabled;
        private readonly List<AudioTrackModel> _detectedAudioTracks = new();
        private int _selectedAudioTrackId = -1;
        private string _lastLoadedChannelId = "";
        private IptvAccountInfo? _currentDeadAccountDetected;

        public PlayerView()
        {
            InitializeComponent();
            _isAudioNormEnabled = ViewModel.IsAudioNormEnabled;
            _isVideoEnhanceEnabled = ViewModel.IsVideoEnhanceEnabled;

            VideoPlayer.VideoMouseActivity += (_, _) =>
            {
                if ((DateTime.UtcNow - _lastMouseOsdResetUtc).TotalMilliseconds >= 250 || OsdPanel?.Visibility != Visibility.Visible)
                    ShowOsdTemporary();
            };
            VideoPlayer.VideoDoubleClick += (_, _) => Fullscreen_Click(this, new RoutedEventArgs());

            Loaded += (_, _) => InitializePlayer();

            _osdTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
            _osdTimer.Tick += (_, _) =>
            {
                if (_isUserDraggingSlider || _isMouseOverOsd) return;
                if (OsdPanel != null) OsdPanel.Visibility = Visibility.Collapsed;
                _osdTimer.Stop();
            };

            _positionTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _positionTimer.Tick += PositionTimer_Tick;
            _positionTimer.Start();

            InitializeViewersTimer();
            Focusable = true;
        }

        public void ShowOsdTemporary()
        {
            void ApplyShow()
            {
                _lastMouseOsdResetUtc = DateTime.UtcNow;
                bool opening = OsdPanel != null && OsdPanel.Visibility != Visibility.Visible;
                if (opening && OsdPanel != null)
                {
                    OsdPanel.Visibility = Visibility.Visible;
                    RefreshOsdPlaybackState();
                }
                _osdTimer?.Stop();
                _osdTimer?.Start();
            }

            if (Dispatcher.CheckAccess()) ApplyShow();
            else Dispatcher.InvokeAsync(ApplyShow);
        }

        private void OsdPanel_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) { _isMouseOverOsd = true; _osdTimer?.Stop(); }
        private void OsdPanel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) { _isMouseOverOsd = false; _osdTimer?.Start(); }

        private HlsSessionInfo? GetActiveProxySession()
        {
            if (_activeProxySession != null && _activeProxySession.Segments.Count > 0)
                return _activeProxySession;

            var found = HlsProxyEngine.Instance.GetSession(ViewModel.CurrentChannel?.Url ?? "")
                        ?? (_currentPlayingUrl != null ? HlsProxyEngine.Instance.GetSession(_currentPlayingUrl) : null);

            if (found != null) _activeProxySession = found;
            return found;
        }

        private static string FormatDelay(long delayMs)
        {
            var delay = TimeSpan.FromMilliseconds(Math.Max(0, delayMs));
            return delay.TotalHours >= 1 ? $"-{delay:hh\\:mm\\:ss}" : $"-{delay:mm\\:ss}";
        }

        private void SetLiveBadgeState(SolidColorBrush brush, string text, string goLiveText, string totalText, string currentText)
        {
            if (TimeCurrentText != null && _lastTimeCurrentText != currentText)
            {
                _lastTimeCurrentText = currentText;
                TimeCurrentText.Text = currentText;
            }
            if (TimeTotalText != null && _lastTimeTotalText != totalText)
            {
                _lastTimeTotalText = totalText;
                TimeTotalText.Text = totalText;
            }
            if (LiveBadge != null && !ReferenceEquals(LiveBadge.Background, brush))
                LiveBadge.Background = brush;
            if (LiveBadgeText != null && _lastLiveBadgeText != text)
            {
                _lastLiveBadgeText = text;
                LiveBadgeText.Text = text;
            }
            if (GoLiveBtn != null && !ReferenceEquals(GoLiveBtn.Background, brush))
                GoLiveBtn.Background = brush;
            if (GoLiveText != null && _lastGoLiveText != goLiveText)
            {
                _lastGoLiveText = goLiveText;
                GoLiveText.Text = goLiveText;
            }
        }

        private void PositionTimer_Tick(object? sender, EventArgs e)
        {
            if (_mpv == null || ViewModel.CurrentChannel == null || _isUserDraggingSlider) return;
            if (_mpv.Status == MpvPlaybackStatus.Stopped && !_userPaused) return;

            try
            {
                long timeMs = _mpv.PositionMs;
                bool isVod = ViewModel.IsVod(ViewModel.CurrentChannel);

                if (!isVod && _mpv.Status == MpvPlaybackStatus.Playing && !_userPaused && !_hasReceivedFirstFrame && timeMs > 0)
                {
                    _hasReceivedFirstFrame = true;
                    if (OsdTitle != null && ViewModel.CurrentChannel != null)
                    {
                        OsdTitle.Text = ViewModel.CurrentChannel.PrimaryName;
                        ShowOsdTemporary();
                    }
                }

                // Skip all OSD/EPG/Slider UI mutations when OSD is collapsed to avoid overlay churn
                if (OsdPanel == null || OsdPanel.Visibility != Visibility.Visible)
                    return;

                RefreshOsdPlaybackState();
            }
            catch { }
        }

        private void RefreshOsdPlaybackState()
        {
            if (_mpv == null || ViewModel.CurrentChannel == null || _isUserDraggingSlider) return;

            long timeMs = _mpv.PositionMs;
            long lengthMs = _mpv.DurationMs;
            bool isVod = ViewModel.IsVod(ViewModel.CurrentChannel);
            bool isPaused = _userPaused || _mpv.Status == MpvPlaybackStatus.Paused;

            if ((DateTime.Now - _lastSeekTime).TotalSeconds < 2.0 && isVod)
            {
                if (_effectivePositionMs > 0 && Math.Abs(timeMs - _effectivePositionMs) < 1200)
                    _lastSeekTime = DateTime.MinValue;
                else
                    return;
            }

            if (isVod)
            {
                bool isCatchup = ViewModel.CurrentChannel?.Id?.StartsWith("catchup_", StringComparison.Ordinal) == true;
                string badge = isCatchup ? "⏮️ CATCH-UP" : "🎬 VOD";
                if (LiveBadge != null && !ReferenceEquals(LiveBadge.Background, VodBlueBrush)) LiveBadge.Background = VodBlueBrush;
                if (LiveBadgeText != null && _lastLiveBadgeText != badge) { _lastLiveBadgeText = badge; LiveBadgeText.Text = badge; }

                if (lengthMs > 0)
                {
                    TimeSlider.IsEnabled = true;
                    TimeSlider.Minimum = 0;
                    TimeSlider.Maximum = lengthMs;
                    _effectivePositionMs = timeMs;
                    TimeSlider.Value = Math.Clamp(timeMs, 0, lengthMs);
                    SetTextIfChanged(TimeCurrentText, ref _lastTimeCurrentText, TimeSpan.FromMilliseconds(timeMs).ToString(@"hh\:mm\:ss"));
                    SetTextIfChanged(TimeTotalText, ref _lastTimeTotalText, TimeSpan.FromMilliseconds(lengthMs).ToString(@"hh\:mm\:ss"));
                }
                else
                {
                    TimeSlider.IsEnabled = false;
                    SetTextIfChanged(TimeCurrentText, ref _lastTimeCurrentText, TimeSpan.FromMilliseconds(timeMs).ToString(@"hh\:mm\:ss"));
                    SetTextIfChanged(TimeTotalText, ref _lastTimeTotalText, "--:--:--");
                }
                return;
            }

            var proxySession = GetActiveProxySession();
            if (proxySession != null && proxySession.Segments.Count > 0)
            {
                var (earliestMs, latestMs, currentDvrPosMs) = ComputeHlsLiveState(proxySession, isPaused);

                TimeSlider.IsEnabled = true;
                TimeSlider.Minimum = earliestMs;
                TimeSlider.Maximum = Math.Max(earliestMs + 1000, latestMs);
                TimeSlider.Value = currentDvrPosMs;
                _effectivePositionMs = currentDvrPosMs;

                UpdateOsdTimeAndBadge(currentDvrPosMs, latestMs, proxySession.StartWallClockTime, isPaused);
            }
            else
            {
                long liveDelayMs = _accumulatedLiveLagMs;
                if (isPaused && _livePauseStartedUtc != DateTime.MinValue)
                    liveDelayMs += Math.Max(0, (long)(DateTime.UtcNow - _livePauseStartedUtc).TotalMilliseconds);

                if (lengthMs > 2000)
                {
                    TimeSlider.IsEnabled = true;
                    TimeSlider.Minimum = 0;
                    TimeSlider.Maximum = lengthMs;
                    TimeSlider.Value = Math.Clamp(timeMs, 0, lengthMs);
                    if (liveDelayMs <= 0 && !isPaused && (lengthMs - timeMs) > 3000)
                        liveDelayMs = lengthMs - timeMs;
                }
                else
                {
                    TimeSlider.IsEnabled = false;
                    TimeSlider.Minimum = 0;
                    TimeSlider.Maximum = 100;
                    TimeSlider.Value = 100;
                }

                if (isPaused || liveDelayMs > 3000)
                {
                    string delayStr = FormatDelay(liveDelayMs);
                    DateTime airedTime = DateTime.Now.AddMilliseconds(-liveDelayMs);
                    SetLiveBadgeState(DelayedAmberBrush, isPaused ? $"⏸ {delayStr}" : delayStr, $"⚡ {delayStr}", $"GERİDE ({delayStr})", airedTime.ToString("HH:mm:ss"));
                    UpdateOsdEpgForTime(airedTime);
                }
                else
                {
                    SetLiveBadgeState(LiveRedBrush, "🔴 CANLI", "CANLI", "CANLI YAYIN", DateTime.Now.ToString("HH:mm:ss"));
                    UpdateOsdEpgForTime(DateTime.Now);
                }
            }
        }

        private (long earliestMs, long latestMs, long currentMs) ComputeHlsLiveState(HlsSessionInfo proxySession, bool isPaused)
        {
            long earliestMs = (long)(proxySession.Segments[0].StartTimeSeconds * 1000);
            long latestMs = (long)(proxySession.TotalDurationSeconds * 1000);

            if (isPaused && _hasPausedDvrPos && _livePauseStartedUtc != DateTime.MinValue)
            {
                long pausedElapsedMs = Math.Max(0, (long)(DateTime.UtcNow - _livePauseStartedUtc).TotalMilliseconds);
                latestMs = Math.Max(latestMs, _pausedDvrPosMs + _accumulatedLiveLagMs + pausedElapsedMs);
            }

            long currentDvrPosMs;
            if (isPaused && _hasPausedDvrPos)
            {
                currentDvrPosMs = Math.Clamp(_pausedDvrPosMs, earliestMs, latestMs);
            }
            else if (_isTimeshiftMode)
            {
                long playedMs = _timeshiftAccumulatedPlayMs;
                if (_timeshiftPlayAnchorUtc != DateTime.MinValue)
                    playedMs += Math.Max(0, (long)(DateTime.UtcNow - _timeshiftPlayAnchorUtc).TotalMilliseconds);

                currentDvrPosMs = Math.Clamp(_timeshiftStartDvrMs + playedMs, earliestMs, latestMs);
                if (latestMs - currentDvrPosMs <= 2500)
                {
                    _isTimeshiftMode = false;
                    _timeshiftStartDvrMs = 0;
                    _timeshiftAccumulatedPlayMs = 0;
                    _timeshiftPlayAnchorUtc = DateTime.MinValue;
                    currentDvrPosMs = latestMs;
                }
            }
            else
            {
                currentDvrPosMs = latestMs;
            }

            return (earliestMs, latestMs, currentDvrPosMs);
        }

        private static void SetTextIfChanged(System.Windows.Controls.TextBlock? tb, ref string cache, string value)
        {
            if (tb == null || cache == value) return;
            cache = value;
            tb.Text = value;
        }

        private void UpdateOsdTimeAndBadge(long currentMs, long totalMs, DateTime startWallTime, bool isPaused = false)
        {
            if (TimeCurrentText == null) return;
            long offsetMs = Math.Max(0, totalMs - currentMs);

            if (isPaused || offsetMs > 3000)
            {
                DateTime airedTime = startWallTime == DateTime.MinValue
                    ? DateTime.Now.AddMilliseconds(-offsetMs)
                    : startWallTime.AddMilliseconds(currentMs);
                if (airedTime > DateTime.Now) airedTime = DateTime.Now.AddMilliseconds(-offsetMs);

                string delayStr = FormatDelay(offsetMs);
                SetLiveBadgeState(DelayedAmberBrush, isPaused ? $"⏸ {delayStr}" : delayStr, $"⚡ {delayStr}", $"GERİDE ({delayStr})", airedTime.ToString("HH:mm:ss"));
                UpdateOsdEpgForTime(airedTime);
            }
            else
            {
                SetLiveBadgeState(LiveRedBrush, "🔴 CANLI", "CANLI", "CANLI YAYIN", DateTime.Now.ToString("HH:mm:ss"));
                UpdateOsdEpgForTime(DateTime.Now);
            }
        }

        private void InitializeViewersTimer()
        {
            var p2pPlugin = PluginManager.Instance.GetPluginById("p2p_stats") as P2PStatsPlugin;
            int pollSec = Math.Clamp(p2pPlugin?.GetInt("PollIntervalSeconds", 3) ?? 3, 2, 30);

            _viewersTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(pollSec) };
            _viewersTimer.Tick += async (_, _) =>
            {
                if (_mpv == null || _mpv.Status != MpvPlaybackStatus.Playing || ViewModel.CurrentChannel == null ||
                    OsdPanel == null || OsdPanel.Visibility != Visibility.Visible)
                {
                    if (OsdViewersBadge != null) OsdViewersBadge.Visibility = Visibility.Collapsed;
                    return;
                }

                try
                {
                    string currentUrl = ViewModel.CurrentChannel.Url ?? "";
                    if (_ace.IsAceStreamUrl(currentUrl) || currentUrl.Contains(":6878/ace/"))
                    {
                        var activeP2p = PluginManager.Instance.GetPluginById("p2p_stats") as P2PStatsPlugin;
                        if (activeP2p != null && !activeP2p.IsEnabled)
                        {
                            if (OsdViewersBadge != null) OsdViewersBadge.Visibility = Visibility.Collapsed;
                            return;
                        }

                        int peers = activeP2p != null ? await activeP2p.GetPeersAsync(currentUrl) : await _ace.GetStreamPeersAsync(currentUrl);
                        if (peers > 0)
                        {
                            OsdViewersText.Text = $"{peers} Eş";
                            OsdViewersBadge.Visibility = Visibility.Visible;
                            return;
                        }
                    }
                    else if (currentUrl.Contains("youtube.com") || currentUrl.Contains("youtu.be"))
                    {
                        int viewers = await new YoutubeEngine().GetActiveLiveViewersAsync(currentUrl);
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

        private (bool hwAccel, int cacheMs, string userAgent) ReadEngineSettings()
        {
            bool hwAccel = _db.GetSetting("FlyleafHwAccel", "true") == "true";
            int cacheMs = Math.Clamp(int.TryParse(_db.GetSetting("FlyleafCache", "3500"), out int parsed) ? parsed : 3500, 2500, 15000);
            string defaultUa = _db.GetSetting("FlyleafUserAgent", "");
            if (string.IsNullOrWhiteSpace(defaultUa))
                defaultUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

            return (hwAccel, cacheMs, defaultUa);
        }

        private void ConfigureMpvForUrl(string url, Channel channel)
        {
            if (_mpv == null) return;
            var (hwAccel, cacheMs, defaultUa) = ReadEngineSettings();

            string ua = !string.IsNullOrWhiteSpace(channel.HttpUserAgent) ? channel.HttpUserAgent : defaultUa;
            bool isLocalProxy = url.Contains("127.0.0.1:48931", StringComparison.OrdinalIgnoreCase) || url.Contains("localhost:48931", StringComparison.OrdinalIgnoreCase);
            bool isHls = url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) || isLocalProxy;
            bool isAceStream = channel.SourceType == "ACESTREAM" || url.Contains("session=ace_") || url.Contains(":6878/ace/");

            var headers = new List<string>();
            if (!string.IsNullOrWhiteSpace(channel.HttpReferer)) headers.Add($"Referer: {channel.HttpReferer}");
            if (!string.IsNullOrWhiteSpace(channel.HttpCookie)) headers.Add($"Cookie: {channel.HttpCookie}");
            if (!string.IsNullOrWhiteSpace(channel.HttpOrigin)) headers.Add($"Origin: {channel.HttpOrigin}");
            if (channel.CustomHeaders != null)
            {
                foreach (var kv in channel.CustomHeaders)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                        headers.Add($"{kv.Key}: {kv.Value}");
                }
            }

            _mpv.ConfigureRuntimeOptions(hwAccel, cacheMs, ua, headers, isAceStream, isHls);
        }

        private void InitializePlayer()
        {
            if (_mpv != null)
            {
                VideoPlayer.SyncOverlayBounds();
                return;
            }

            if (!MpvPlayerEngine.IsLibraryAvailable())
            {
                LogService.LogWarning("Player: libmpv-2.dll henüz hazır değil, arka planda indiriliyor/bekleniyor...");
                if (!_mpvDownloadTriggered)
                {
                    _mpvDownloadTriggered = true;
                    _ = Task.Run(MaintenanceEngine.CheckAndInstallMpvAsync);
                }

                _ = Task.Run(async () =>
                {
                    for (int i = 0; i < 60; i++)
                    {
                        await Task.Delay(1500).ConfigureAwait(false);
                        if (MpvPlayerEngine.IsLibraryAvailable())
                        {
                            await Dispatcher.InvokeAsync(InitializePlayer);
                            break;
                        }
                    }
                });
                return;
            }

            try
            {
                var (hwAccel, cacheMs, defaultUa) = ReadEngineSettings();
                IntPtr hwnd = VideoPlayer.VideoHandle;

                var engine = new MpvPlayerEngine();
                engine.Initialize(hwnd, hwAccel, cacheMs, defaultUa);
                engine.SetAudioNormalization(_isAudioNormEnabled);
                engine.SetVideoEnhancement(_isVideoEnhanceEnabled);

                engine.OpenCompleted += (_, err) =>
                {
                    LogService.LogInfo($"[PLAYBACK/MPV] OpenCompleted: Error={err ?? "None"}");
                };
                engine.StatusChanged += Mpv_StatusChanged;
                engine.TracksChanged += RefreshAudioTracks;

                _mpv = engine;
                _initTcs.TrySetResult(true);

                Dispatcher.InvokeAsync(() =>
                {
                    if (string.IsNullOrEmpty(OsdTitle.Text) || OsdTitle.Text == "Kanal Yükleniyor...")
                        OsdTitle.Text = "Sistem Hazır (libmpv).";
                    OsdPanel.Visibility = Visibility.Visible;
                    UpdateAudioNormButtonUI();
                    UpdateVideoEnhanceButtonUI();
                    ShowOsdTemporary();
                    VideoPlayer.SyncOverlayBounds();
                });
            }
            catch (Exception ex)
            {
                LogService.LogError("[PLAYBACK/MPV] Başlatma hatası", ex);
            }
        }

        private void Mpv_StatusChanged(MpvPlaybackStatus status)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_mpv == null) return;

                if (status == MpvPlaybackStatus.Playing)
                {
                    UpdatePlayPauseIcon(true);
                }
                else if (status == MpvPlaybackStatus.Paused)
                {
                    if (!_userPaused && (DateTime.UtcNow - _streamStartTime).TotalSeconds < 6)
                    {
                        _mpv.Play();
                        UpdatePlayPauseIcon(true);
                    }
                    else
                    {
                        UpdatePlayPauseIcon(false);
                    }
                }
                else if (status == MpvPlaybackStatus.Stopped)
                {
                    UpdatePlayPauseIcon(false);
                }
                else if (status == MpvPlaybackStatus.Ended)
                {
                    UpdatePlayPauseIcon(false);
                    if (!_userPaused && ViewModel.CurrentChannel != null && !ViewModel.IsVod(ViewModel.CurrentChannel) && !string.IsNullOrEmpty(_currentPlayingUrl))
                    {
                        string reconnectUrl = _currentPlayingUrl;
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(1200).ConfigureAwait(false);
                            if (!_userPaused && _mpv != null && (_mpv.Status == MpvPlaybackStatus.Ended || _mpv.Status == MpvPlaybackStatus.Stopped))
                            {
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    if (!_userPaused && _mpv != null && (_mpv.Status == MpvPlaybackStatus.Ended || _mpv.Status == MpvPlaybackStatus.Stopped))
                                        _mpv.Open(reconnectUrl);
                                });
                            }
                        });
                    }
                }
                else if (status == MpvPlaybackStatus.Failed)
                {
                    UpdatePlayPauseIcon(false);
                    if (ViewModel.CurrentChannel != null)
                    {
                        OsdTitle.Text = $"{ViewModel.CurrentChannel.PrimaryName} (Sinyal Yok / Bağlantı Koptu)";
                        ShowOsdTemporary();
                    }
                }
            });
        }

        private void UpdateChannelLogo(Channel channel)
        {
            if (channel == null || OsdLogo == null || OsdLogoFallback == null) return;

            string displayBase = ChannelUtils.GetDisplayBaseName(channel.PrimaryName ?? channel.Name ?? "TV");
            OsdLogoFallbackText.Text = ChannelLogoManager.GenerateMonogram(displayBase);
            OsdLogoFallback.Background = ChannelLogoManager.MonogramPalette[Math.Abs(displayBase.GetHashCode()) % ChannelLogoManager.MonogramPalette.Length];
            OsdLogo.Visibility = Visibility.Collapsed;
            OsdLogoFallback.Visibility = Visibility.Visible;

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

        private static bool PromptParentalPin(string channelName, UserAuthPlugin authPlugin)
        {
            var win = new Window
            {
                Title = "Ebeveyn Koruması - PIN Gerekli",
                Width = 360,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = UI.Windows.MainWindow.Instance,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 23, 42)),
                Foreground = System.Windows.Media.Brushes.White
            };

            var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"🔒 '{channelName}' kilitli kanal. Devam etmek için 4 haneli PIN girin:",
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var pinBox = new System.Windows.Controls.PasswordBox { Height = 34, FontSize = 16, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 0, 14) };
            stack.Children.Add(pinBox);

            var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var cancelBtn = new System.Windows.Controls.Button { Content = "İptal", Width = 80, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
            var okBtn = new System.Windows.Controls.Button { Content = "Doğrula", Width = 90, Height = 32, IsDefault = true };
            cancelBtn.Click += (_, _) => win.DialogResult = false;
            okBtn.Click += (_, _) =>
            {
                if (authPlugin.VerifyPin(pinBox.Password)) win.DialogResult = true;
                else
                {
                    System.Windows.MessageBox.Show("Hatalı PIN kodu!", "Erişim Engellendi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    pinBox.Clear();
                    pinBox.Focus();
                }
            };
            btnPanel.Children.Add(cancelBtn);
            btnPanel.Children.Add(okBtn);
            stack.Children.Add(btnPanel);
            win.Content = stack;
            win.Loaded += (_, _) => pinBox.Focus();
            return win.ShowDialog() == true;
        }

        private void ResetTimeshiftState()
        {
            _userPaused = false;
            _hasPausedDvrPos = false;
            _pausedDvrPosMs = 0;
            _livePauseStartedUtc = DateTime.MinValue;
            _accumulatedLiveLagMs = 0;
            _isTimeshiftMode = false;
            _timeshiftStartDvrMs = 0;
            _timeshiftPlayAnchorUtc = DateTime.MinValue;
            _timeshiftAccumulatedPlayMs = 0;
            _effectivePositionMs = 0;
        }

        public void LoadChannel(Channel channel)
        {
            if (channel == null) return;

            var authPlugin = PluginManager.Instance.GetPluginById("user_auth") as UserAuthPlugin;
            if (channel.IsLocked && authPlugin is { IsEnabled: true } && authPlugin.GetBool("EnableParentalPin"))
            {
                bool authorized = Dispatcher.Invoke(() => PromptParentalPin(channel.PrimaryName, authPlugin));
                if (!authorized) return;
            }

            if (_isRecording) StopRecording();

            ViewModel.CurrentChannel = channel;
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;
            long sessionId = Interlocked.Increment(ref _playbackSessionId);

            _currentPlayingUrl = null;
            _activeProxySession = null;
            _streamStartTime = DateTime.UtcNow;
            _hasReceivedFirstFrame = false;
            ResetTimeshiftState();

            bool isAce = channel.SourceType == "ACESTREAM" ||
                         channel.Url?.Contains("acestream://", StringComparison.OrdinalIgnoreCase) == true ||
                         channel.Url?.Contains(":6878/ace/", StringComparison.OrdinalIgnoreCase) == true;

            _ = Dispatcher.InvokeAsync(() =>
            {
                OsdTitle.Text = !MpvPlayerEngine.IsLibraryAvailable()
                    ? $"{channel.PrimaryName} (libmpv motoru hazırlanıyor...)"
                    : (isAce ? $"{channel.PrimaryName} (P2P Tamponlanıyor...)" : $"{channel.PrimaryName} (Bağlanıyor...)");
                OsdCategory.Text = channel.Category;
                OsdCurrentEpg.Text = "Yayın akışı bilgisi yükleniyor...";
                OsdNextEpg.Text = "Sıradaki: --:--";
                DeadAccountAlertCard.Visibility = Visibility.Collapsed;
                ShowOsdTemporary();
                UpdatePlayPauseIcon(true);
                UpdateChannelLogo(channel);
            });

            Task.Run(async () =>
            {
                if (token.IsCancellationRequested || sessionId != _playbackSessionId) return;

                try
                {
                    if (_mpv == null) await Dispatcher.InvokeAsync(InitializePlayer);
                    else _initTcs.TrySetResult(true);

                    using var initTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    using var linkedInit = CancellationTokenSource.CreateLinkedTokenSource(token, initTimeout.Token);
                    await _initTcs.Task.WaitAsync(linkedInit.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (!token.IsCancellationRequested && sessionId == _playbackSessionId)
                        _ = Dispatcher.InvokeAsync(() => { OsdTitle.Text = "Görüntü motoru (libmpv) başlatılamadı"; ShowOsdTemporary(); });
                    return;
                }

                if (_mpv == null || token.IsCancellationRequested || sessionId != _playbackSessionId) return;
                if (!await _playSemaphore.WaitAsync(10000, token).ConfigureAwait(false)) return;

                try
                {
                    if (ViewModel.IsVod(channel))
                        ViewModel.SaveVodPosition(channel, _mpv.PositionMs);

                    _mpv.Stop();
                    HlsProxyEngine.Instance.ClearChannelCache();

                    if (token.IsCancellationRequested || sessionId != _playbackSessionId) return;

                    if (_lastLoadedChannelId != channel.Id)
                    {
                        _lastLoadedChannelId = channel.Id;
                        _selectedAudioTrackId = -1;
                    }

                    _ = Task.Run(async () =>
                    {
                        await ViewModel.RefreshEpgAsync(channel).ConfigureAwait(false);
                        _ = Dispatcher.InvokeAsync(() => UpdateOsdEpgForTime(DateTime.Now));
                    });

                    _mpv.SetAudioNormalization(_isAudioNormEnabled);
                    _mpv.SetVideoEnhancement(_isVideoEnhanceEnabled);

                    var candidateUrls = await ViewModel.GetSmartOrderedCandidatesAsync(channel, token, status =>
                        _ = Dispatcher.InvokeAsync(() => { OsdTitle.Text = status; ShowOsdTemporary(); })).ConfigureAwait(false);

                    if (token.IsCancellationRequested || sessionId != _playbackSessionId) return;
                    if (candidateUrls.Count == 0)
                    {
                        _ = Dispatcher.InvokeAsync(() =>
                        {
                            OsdTitle.Text = $"{channel.PrimaryName} (Sinyal Zayıf veya Adres Geçersiz)";
                            UpdatePlayPauseIcon(false);
                            ShowOsdTemporary();
                        });
                        return;
                    }

                    var resPlugin = PluginManager.Instance.GetPluginById("stream_resolution") as StreamResolutionPlugin;
                    bool allowFallback = resPlugin == null || !resPlugin.IsEnabled || resPlugin.GetBool("AutoFallbackOnFail");
                    int customTimeoutSec = resPlugin is { IsEnabled: true } ? Math.Clamp(resPlugin.GetInt("ConnectionTimeoutSeconds", 8), 5, 45) : 15;
                    int maxTries = allowFallback ? candidateUrls.Count : 1;

                    bool playbackSucceeded = false;
                    string successfulPreparedUrl = "";

                    for (int i = 0; i < maxTries; i++)
                    {
                        if (token.IsCancellationRequested || sessionId != _playbackSessionId) break;
                        string rawCandidate = candidateUrls[i];
                        if (i > 0)
                        {
                            _ = Dispatcher.InvokeAsync(() =>
                            {
                                OsdTitle.Text = $"{channel.PrimaryName} (Alternatif {i + 1}/{candidateUrls.Count} deneniyor...)";
                                ShowOsdTemporary();
                            });
                        }

                        string preparedUrl = await ViewModel.PrepareSingleStreamUrlAsync(rawCandidate, channel, token, status =>
                            _ = Dispatcher.InvokeAsync(() => { OsdTitle.Text = status; ShowOsdTemporary(); })).ConfigureAwait(false);

                        if (token.IsCancellationRequested || sessionId != _playbackSessionId) break;
                        if (string.IsNullOrEmpty(preparedUrl)) continue;

                        bool isAceCandidate = channel.SourceType == "ACESTREAM" || preparedUrl.Contains(":6878/ace/") || preparedUrl.Contains("session=ace_");
                        int watchdogMs = isAceCandidate ? 35000 : customTimeoutSec * 1000;

                        _currentPlayingUrl = preparedUrl;
                        if (await OpenAndAwaitPlaybackAsync(preparedUrl, channel, watchdogMs, token, sessionId).ConfigureAwait(false))
                        {
                            playbackSucceeded = true;
                            successfulPreparedUrl = preparedUrl;
                            resPlugin?.NotifyStreamResolved(preparedUrl, i > 0);
                            break;
                        }

                        _currentPlayingUrl = null;
                        try { _mpv?.Stop(); } catch { }
                    }

                    if (token.IsCancellationRequested || sessionId != _playbackSessionId) return;

                    if (playbackSucceeded)
                    {
                        _currentPlayingUrl = successfulPreparedUrl;
                        _activeProxySession = HlsProxyEngine.Instance.GetSession(successfulPreparedUrl) ?? HlsProxyEngine.Instance.GetSession(channel.Url ?? "");

                        if (!(channel.Id?.StartsWith("catchup_", StringComparison.Ordinal) ?? false))
                        {
                            channel.PersonalWatchCount++;
                            _ = _db.SaveChannelAsync(channel, notifyUpdated: false);
                        }

                        if (ViewModel.IsVod(channel) && channel.LastPositionMs > 0)
                        {
                            try { _mpv?.SeekMs(channel.LastPositionMs); _lastSeekTime = DateTime.Now; _effectivePositionMs = channel.LastPositionMs; } catch { }
                        }

                        _ = Dispatcher.InvokeAsync(() =>
                        {
                            if (ViewModel.CurrentChannel != null) OsdTitle.Text = ViewModel.CurrentChannel.PrimaryName;
                            UpdatePlayPauseIcon(true);
                            _positionTimer?.Start();
                            RefreshAudioTracks();
                            RefreshOsdPlaybackState();
                            ShowOsdTemporary();
                        });
                    }
                    else
                    {
                        _currentPlayingUrl = null;
                        _ = Dispatcher.InvokeAsync(() =>
                        {
                            OsdTitle.Text = $"{channel.PrimaryName} (Sinyal Yok / Tüm Kaynaklar Başarısız)";
                            UpdatePlayPauseIcon(false);
                            ShowOsdTemporary();
                            CheckAndShowDeadAccountAlert(candidateUrls);
                        });
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("[PLAYBACK] Error loading channel", ex);
                }
                finally
                {
                    _playSemaphore.Release();
                }
            }, token);
        }

        private async Task<bool> OpenAndAwaitPlaybackAsync(string url, Channel channel, int watchdogMs, CancellationToken token, long sessionId)
        {
            if (_mpv == null || token.IsCancellationRequested || sessionId != _playbackSessionId)
                return false;

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Action<MpvPlaybackStatus> statusHandler = status =>
            {
                if (sessionId != _playbackSessionId) { tcs.TrySetResult(false); return; }
                if (status == MpvPlaybackStatus.Playing) tcs.TrySetResult(true);
                else if (status == MpvPlaybackStatus.Failed) tcs.TrySetResult(false);
            };

            Action<bool, string?> openHandler = (success, _) =>
            {
                if (sessionId != _playbackSessionId) { tcs.TrySetResult(false); return; }
                if (success)
                {
                    if (_selectedAudioTrackId > 0)
                        _mpv?.SwitchAudioTrack(_selectedAudioTrackId);
                    tcs.TrySetResult(true);
                }
                else
                {
                    tcs.TrySetResult(false);
                }
            };

            _mpv.StatusChanged += statusHandler;
            _mpv.OpenCompleted += openHandler;

            try
            {
                ConfigureMpvForUrl(url, channel);

                if (channel.SourceType == "ACESTREAM" || url.Contains("session=ace_") || url.Contains(":6878/ace/"))
                    await _ace.StartEngineIfNotRunningAsync().ConfigureAwait(false);

                _mpv.Open(url);

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var completed = await Task.WhenAny(tcs.Task, Task.Delay(watchdogMs, linkedCts.Token)).ConfigureAwait(false);
                if (token.IsCancellationRequested || sessionId != _playbackSessionId) return false;

                if (completed == tcs.Task)
                {
                    linkedCts.Cancel();
                    return await tcs.Task.ConfigureAwait(false);
                }
                return false;
            }
            finally
            {
                _mpv.StatusChanged -= statusHandler;
                _mpv.OpenCompleted -= openHandler;
            }
        }

        public async void Stop()
        {
            if (_isRecording) StopRecording();
            _wasPausedByMinimize = false;
            _loadCts?.Cancel();
            _positionTimer?.Stop();

            var ch = ViewModel.CurrentChannel;
            if (ch != null && ViewModel.IsVod(ch) && _mpv != null && !(ch.Id?.StartsWith("catchup_", StringComparison.Ordinal) ?? false))
            {
                try { ch.LastPositionMs = _mpv.PositionMs; _ = _db.SaveChannelAsync(ch); } catch { }
            }

            _mpv?.Stop();
            _activeProxySession = null;
            ResetTimeshiftState();
            HlsProxyEngine.Instance.ClearChannelCache();

            if (ch?.SourceType == "ACESTREAM" || ch?.Url?.Contains("acestream://") == true)
            {
                await _ace.StopAllStreamsAsync();
                AceStreamService.Instance.StopAllSessions();
            }
        }

        public void PlayCatchupProgram(Channel channel, EpgProgram program)
        {
            if (channel == null || program == null) return;
            string? timeshiftUrl = XtreamCatchupHelper.BuildCatchupUrl(channel, program);
            if (string.IsNullOrWhiteSpace(timeshiftUrl))
            {
                System.Windows.MessageBox.Show("Bu kanal için Xtream Catch-up (Timeshift) bağlantısı oluşturulamadı.", "Catch-up Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LoadChannel(new Channel
            {
                Id = $"catchup_{channel.Id}_{program.StartTime:yyyyMMddHHmm}",
                Name = $"{channel.PrimaryName} [Catch-up: {program.Title}]",
                Url = timeshiftUrl,
                LogoUrl = channel.LogoUrl,
                Category = "Catchup",
                GroupTitle = channel.GroupTitle,
                SourceType = channel.SourceType,
                HttpUserAgent = channel.HttpUserAgent,
                HttpReferer = channel.HttpReferer,
                HttpCookie = channel.HttpCookie,
                HttpOrigin = channel.HttpOrigin
            });
        }

        private void RecordBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isRecording) StopRecording();
            else StartRecording();
        }

        private void StartRecording()
        {
            string? streamUrl = _currentPlayingUrl ?? ViewModel.CurrentChannel?.PrimaryUrl;
            if (string.IsNullOrWhiteSpace(streamUrl) || !streamUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                ShowOsdToast("Kayıt başlatılamadı: Aktif HTTP yayını yok", "GeoRecord");
                return;
            }

            try
            {
                string recDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "StreamMesh Recordings");
                Directory.CreateDirectory(recDir);

                string chName = ViewModel.CurrentChannel?.PrimaryName ?? "Yayin";
                foreach (var c in Path.GetInvalidFileNameChars()) chName = chName.Replace(c, '_');
                string fileName = $"{chName}_{DateTime.Now:yyyyMMdd_HHmmss}.ts";
                _recordFilePath = Path.Combine(recDir, fileName);

                _recordCts?.Cancel();
                _recordCts = new CancellationTokenSource();
                _isRecording = true;

                if (RecordBtn != null) RecordBtn.Background = LiveRedBrush;
                if (RecordBtnIcon != null) RecordBtnIcon.Fill = System.Windows.Media.Brushes.White;
                ShowOsdToast($"● Kayıt Başladı: {fileName}", "GeoRecord");

                _ = Task.Run(() => RecordStreamLoopAsync(streamUrl, _recordFilePath, _recordCts.Token), _recordCts.Token);
            }
            catch (Exception ex)
            {
                _isRecording = false;
                ShowOsdToast($"Kayıt hatası: {ex.Message}", "GeoRecord");
            }
        }

        private async Task RecordStreamLoopAsync(string url, string outputPath, CancellationToken ct)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
                using var resp = await MediaHttpClient.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();

                await using var netStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read, 65536, useAsync: true);
                byte[] buffer = new byte[65536];
                while (!ct.IsCancellationRequested)
                {
                    int read = await netStream.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (read <= 0) break;
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
            }
            catch { }
        }

        private void StopRecording()
        {
            if (!_isRecording) return;
            _isRecording = false;
            try { _recordCts?.Cancel(); } catch { }
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (RecordBtn != null) RecordBtn.Background = SlateBtnBrush;
                if (RecordBtnIcon != null) RecordBtnIcon.Fill = RecordActiveIconBrush;
                string savedName = !string.IsNullOrEmpty(_recordFilePath) ? Path.GetFileName(_recordFilePath) : "dosya";
                ShowOsdToast($"■ Kayıt Kaydedildi: Documents\\StreamMesh Recordings\\{savedName}", "GeoRecord");
            });
        }

        private void MiniPlayerBtn_Click(object sender, RoutedEventArgs e)
        {
            (Window.GetWindow(this) as UI.Windows.MainWindow ?? UI.Windows.MainWindow.Instance)?.ToggleMiniPlayer();
        }

        public void PauseForMinimize()
        {
            if (_mpv != null && ViewModel.CurrentChannel != null && _mpv.Status == MpvPlaybackStatus.Playing)
            {
                _wasPausedByMinimize = true;
                TogglePause();
            }
        }

        public void ResumeFromMinimize()
        {
            if (_mpv != null && ViewModel.CurrentChannel != null && _wasPausedByMinimize)
            {
                _wasPausedByMinimize = false;
                if (_mpv.Status == MpvPlaybackStatus.Paused) TogglePause();
            }
        }

        public void PlayPause_Click(object sender, RoutedEventArgs e) => TogglePause();
        public void Stop_Click(object sender, RoutedEventArgs e) { Stop(); ShowOsdToast("⏹ Yayın Durduruldu", "GeoStop"); ShowOsdTemporary(); }

        private void UpdatePlayPauseIcon(bool isPlaying)
        {
            if (PlayPauseBtn == null) return;
            if (TryFindResource(isPlaying ? "GeoPause" : "GeoPlay") is Geometry geo)
            {
                PlayPauseBtn.Content = new System.Windows.Shapes.Path { Data = geo, Fill = System.Windows.Media.Brushes.White, Width = 20, Height = 20, Stretch = Stretch.Uniform };
            }
        }

        private void UpdateMuteIcon(bool isMuted)
        {
            if (MuteBtn == null) return;
            if (TryFindResource(isMuted ? "GeoVolumeMute" : "GeoVolume") is Geometry geo)
            {
                MuteBtn.Content = new System.Windows.Shapes.Path { Data = geo, Fill = System.Windows.Media.Brushes.White, Width = 18, Height = 18, Stretch = Stretch.Uniform };
            }
        }

        public void TogglePause()
        {
            if (_mpv == null || ViewModel.CurrentChannel == null) return;
            ShowOsdTemporary();

            bool isVod = ViewModel.IsVod(ViewModel.CurrentChannel);
            bool currentlyPaused = _userPaused || _mpv.Status == MpvPlaybackStatus.Paused;
            var proxySession = isVod ? null : GetActiveProxySession();

            if (!currentlyPaused && _mpv.Status != MpvPlaybackStatus.Stopped)
            {
                _userPaused = true;
                _livePauseStartedUtc = DateTime.UtcNow;

                if (proxySession != null && proxySession.Segments.Count > 0)
                {
                    HlsProxyEngine.Instance.SetSessionPaused(proxySession, true);
                    var (earliestMs, latestMs, currentMs) = ComputeHlsLiveState(proxySession, isPaused: false);
                    _pausedDvrPosMs = Math.Clamp(currentMs, earliestMs, latestMs);
                    _hasPausedDvrPos = true;

                    if (_isTimeshiftMode)
                    {
                        if (_timeshiftPlayAnchorUtc != DateTime.MinValue)
                            _timeshiftAccumulatedPlayMs += Math.Max(0, (long)(DateTime.UtcNow - _timeshiftPlayAnchorUtc).TotalMilliseconds);
                        _timeshiftPlayAnchorUtc = DateTime.MinValue;
                    }
                }
                else
                {
                    _hasPausedDvrPos = false;
                }

                try { _mpv.Pause(); } catch { }
                UpdatePlayPauseIcon(false);
                RefreshOsdPlaybackState();
            }
            else
            {
                _userPaused = false;
                long pauseDurationMs = _livePauseStartedUtc != DateTime.MinValue
                    ? Math.Max(0, (long)(DateTime.UtcNow - _livePauseStartedUtc).TotalMilliseconds)
                    : 0;
                _livePauseStartedUtc = DateTime.MinValue;

                if (proxySession != null && proxySession.Segments.Count > 0 && _hasPausedDvrPos)
                {
                    HlsProxyEngine.Instance.SetSessionPaused(proxySession, false);
                    long resumeTargetMs = _pausedDvrPosMs;
                    _hasPausedDvrPos = false;
                    _pausedDvrPosMs = 0;

                    if (pauseDurationMs < 8000 && _mpv.Status == MpvPlaybackStatus.Paused)
                    {
                        _isTimeshiftMode = true;
                        _timeshiftStartDvrMs = resumeTargetMs;
                        _timeshiftAccumulatedPlayMs = 0;
                        _timeshiftPlayAnchorUtc = DateTime.UtcNow;
                        try { _mpv.Play(); } catch { }
                    }
                    else
                    {
                        SeekAbsolute(resumeTargetMs);
                    }
                }
                else if (!isVod)
                {
                    _accumulatedLiveLagMs += pauseDurationMs;
                    if ((_mpv.Status == MpvPlaybackStatus.Ended || _mpv.Status == MpvPlaybackStatus.Stopped) && !string.IsNullOrEmpty(_currentPlayingUrl))
                    {
                        _accumulatedLiveLagMs = 0;
                        _mpv.Open(_currentPlayingUrl);
                    }
                    else
                    {
                        try { _mpv.Play(); } catch { }
                    }
                }
                else
                {
                    try { _mpv.Play(); } catch { }
                }

                UpdatePlayPauseIcon(true);
                RefreshOsdPlaybackState();
            }
        }

        public void SeekRelative(int deltaMs)
        {
            if (_mpv == null || ViewModel.CurrentChannel == null) return;

            if (ViewModel.IsVod(ViewModel.CurrentChannel))
            {
                long curMs = ((DateTime.Now - _lastSeekTime).TotalSeconds < 2.0 && _effectivePositionMs > 0)
                    ? _effectivePositionMs
                    : _mpv.PositionMs;
                SeekAbsolute(curMs + deltaMs);
                return;
            }

            var proxySession = GetActiveProxySession();
            if (proxySession != null && proxySession.Segments.Count > 0)
            {
                var (_, _, currentMs) = ComputeHlsLiveState(proxySession, _userPaused || _mpv.Status == MpvPlaybackStatus.Paused);
                if ((DateTime.Now - _lastSeekTime).TotalSeconds < 2.0 && _effectivePositionMs > 0 && !_userPaused)
                    currentMs = _effectivePositionMs;
                SeekAbsolute(currentMs + deltaMs);
            }
            else if (_mpv.DurationMs > 0)
            {
                long lengthMs = _mpv.DurationMs;
                SeekAbsolute(Math.Clamp(_mpv.PositionMs + deltaMs, 0, lengthMs));
            }
            else
            {
                ShowOsdToast("Bu canlı akış formatı sadece duraklatmayı destekliyor", "GeoReplay10");
            }
        }

        public void SeekAbsolute(long targetMs)
        {
            if (_mpv == null || ViewModel.CurrentChannel == null) return;

            if (ViewModel.IsVod(ViewModel.CurrentChannel))
            {
                long lengthMs = _mpv.DurationMs;
                if (lengthMs <= 0) return;
                targetMs = Math.Clamp(targetMs, 0, lengthMs);
                try
                {
                    _mpv.SeekMs(targetMs);
                    _lastSeekTime = DateTime.Now;
                    _effectivePositionMs = targetMs;
                    RefreshOsdPlaybackState();
                    ShowOsdTemporary();
                }
                catch { _effectivePositionMs = _mpv.PositionMs; }
                return;
            }

            var proxySession = GetActiveProxySession();
            if (proxySession != null && proxySession.Segments.Count > 0)
            {
                var (earliestMs, latestMs, _) = ComputeHlsLiveState(proxySession, _userPaused);
                targetMs = Math.Clamp(targetMs, earliestMs, latestMs);

                if (latestMs - targetMs <= 2500)
                {
                    GoLive();
                    return;
                }

                string sourceUrl = (!string.IsNullOrEmpty(_currentPlayingUrl) && _currentPlayingUrl.Contains("session="))
                    ? _currentPlayingUrl
                    : (!string.IsNullOrEmpty(proxySession.OriginalUrl) ? proxySession.OriginalUrl : (ViewModel.CurrentChannel.Url ?? ""));
                if (string.IsNullOrEmpty(sourceUrl)) return;

                HlsProxyEngine.Instance.SetSessionPaused(proxySession, false);
                ResetTimeshiftState();
                _isTimeshiftMode = true;
                _timeshiftStartDvrMs = targetMs;
                _timeshiftPlayAnchorUtc = DateTime.UtcNow;
                _lastSeekTime = DateTime.Now;
                _effectivePositionMs = targetMs;

                string timeshiftUrl = HlsProxyEngine.Instance.GetProxyPlaybackUrl(sourceUrl, (int)(targetMs / 1000));
                _mpv.Open(timeshiftUrl);
                UpdatePlayPauseIcon(true);
                RefreshOsdPlaybackState();
                ShowOsdTemporary();
            }
            else if (_mpv.DurationMs > 0)
            {
                long lengthMs = _mpv.DurationMs;
                targetMs = Math.Clamp(targetMs, 0, lengthMs);
                _userPaused = false;
                _livePauseStartedUtc = DateTime.MinValue;
                _accumulatedLiveLagMs = Math.Max(0, lengthMs - targetMs);
                _mpv.SeekMs(targetMs);
                if (_mpv.Status == MpvPlaybackStatus.Paused) _mpv.Play();
                UpdatePlayPauseIcon(true);
                _lastSeekTime = DateTime.Now;
                _effectivePositionMs = targetMs;
                RefreshOsdPlaybackState();
                ShowOsdTemporary();
            }
        }

        public void Rewind(int ms) => SeekRelative(-ms);
        public void Forward(int ms) => SeekRelative(ms);
        public void ChangeVolume(int delta) { if (_mpv != null) _mpv.SetVolume(_mpv.Volume + delta); ShowOsdTemporary(); }

        public void ToggleMute()
        {
            if (_mpv != null)
            {
                _mpv.SetMute(!_mpv.IsMuted);
                UpdateMuteIcon(_mpv.IsMuted);
            }
            ShowOsdTemporary();
        }

        private void TimeSlider_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => _isUserDraggingSlider = true;

        private void TimeSlider_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isUserDraggingSlider = false;
            SeekAbsolute((long)TimeSlider.Value);
        }

        private void TimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isUserDraggingSlider || ViewModel.CurrentChannel == null) return;

            if (ViewModel.IsVod(ViewModel.CurrentChannel))
            {
                SetTextIfChanged(TimeCurrentText, ref _lastTimeCurrentText, TimeSpan.FromMilliseconds((long)e.NewValue).ToString(@"hh\:mm\:ss"));
                return;
            }

            var proxySession = GetActiveProxySession();
            if (proxySession != null && proxySession.Segments.Count > 0)
            {
                UpdateOsdTimeAndBadge((long)e.NewValue, (long)(proxySession.TotalDurationSeconds * 1000), proxySession.StartWallClockTime, _userPaused);
            }
            else if (_mpv != null && _mpv.DurationMs > 0)
            {
                long lenMs = _mpv.DurationMs;
                long delayMs = Math.Max(0, lenMs - (long)e.NewValue);
                string delayStr = FormatDelay(delayMs);
                DateTime aired = DateTime.Now.AddMilliseconds(-delayMs);
                SetLiveBadgeState(delayMs > 2500 ? DelayedAmberBrush : LiveRedBrush,
                    delayMs > 2500 ? delayStr : "🔴 CANLI",
                    delayMs > 2500 ? $"⚡ {delayStr}" : "CANLI",
                    delayMs > 2500 ? $"GERİDE ({delayStr})" : "CANLI YAYIN",
                    aired.ToString("HH:mm:ss"));
            }
        }

        public void Rewind10_Click(object sender, RoutedEventArgs e) => Rewind(10000);
        public void Forward10_Click(object sender, RoutedEventArgs e) => Forward(10000);

        public void GoLive()
        {
            if (_mpv == null || ViewModel.CurrentChannel == null) return;

            var proxySession = GetActiveProxySession();
            HlsProxyEngine.Instance.SetSessionPaused(proxySession, false);
            ResetTimeshiftState();

            if (proxySession != null && proxySession.Segments.Count > 0)
            {
                long totalMs = (long)(proxySession.TotalDurationSeconds * 1000);
                _effectivePositionMs = totalMs;
                string liveSourceUrl = (!string.IsNullOrEmpty(_currentPlayingUrl) && _currentPlayingUrl.Contains("session="))
                    ? _currentPlayingUrl
                    : (!string.IsNullOrEmpty(proxySession.OriginalUrl) ? proxySession.OriginalUrl : (ViewModel.CurrentChannel.Url ?? ""));
                _mpv.Open(HlsProxyEngine.Instance.GetProxyPlaybackUrl(liveSourceUrl, -1));
            }
            else if (!ViewModel.IsVod(ViewModel.CurrentChannel))
            {
                if (_mpv.DurationMs > 0)
                {
                    _mpv.SeekMs(_mpv.DurationMs);
                    if (_mpv.Status == MpvPlaybackStatus.Paused) _mpv.Play();
                }
                else if (!string.IsNullOrEmpty(_currentPlayingUrl))
                {
                    _mpv.Open(_currentPlayingUrl);
                }
            }

            UpdatePlayPauseIcon(true);
            RefreshOsdPlaybackState();
            ShowOsdTemporary();
        }

        public void GoLive_Click(object sender, RoutedEventArgs e) => GoLive();
        public void LiveBadge_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => GoLive();
        public void Mute_Click(object sender, RoutedEventArgs e) => ToggleMute();

        #region Audio Tracks / Language Selection

        public void AudioTrackBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowOsdTemporary();
            if (AudioTracksFlyout == null) return;
            AudioTracksFlyout.Visibility = AudioTracksFlyout.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (AudioTracksFlyout.Visibility == Visibility.Visible) RefreshAudioTracks();
        }

        public void CloseAudioTracksFlyout_Click(object sender, RoutedEventArgs e)
        {
            if (AudioTracksFlyout != null) AudioTracksFlyout.Visibility = Visibility.Collapsed;
        }

        public void AudioTrackItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn || btn.Tag is not AudioTrackModel item) return;

            _selectedAudioTrackId = item.TrackId;
            bool switched = _mpv?.SwitchAudioTrack(item.TrackId) == true;
            if (!string.IsNullOrEmpty(_lastLoadedChannelId))
                ViewModel.SavePreferredLanguageForChannel(_lastLoadedChannelId, item.LangCode);

            foreach (var t in _detectedAudioTracks) t.IsSelected = (t.TrackId == item.TrackId);
            if (AudioTracksItemsList != null)
            {
                AudioTracksItemsList.ItemsSource = null;
                AudioTracksItemsList.ItemsSource = _detectedAudioTracks;
            }
            if (AudioTrackBtnText != null) AudioTrackBtnText.Text = item.LangCode;

            ShowOsdToast(switched ? $"🌐 Ses İzi: {item.DisplayName} aktif" : $"⚠️ Ses izi seçilemedi: {item.DisplayName}", "GeoLanguage");
            if (AudioTracksFlyout != null) AudioTracksFlyout.Visibility = Visibility.Collapsed;
        }

        public void RefreshAudioTracks()
        {
            Dispatcher.InvokeAsync(() =>
            {
                _detectedAudioTracks.Clear();
                var tracks = _mpv?.GetAudioTracks();
                if (tracks != null && tracks.Count > 0)
                {
                    for (int i = 0; i < tracks.Count; i++)
                    {
                        var s = tracks[i];
                        var (langName, langCode) = ParseLanguageInfo(s.Lang, s.Title, i);
                        string codecStr = string.IsNullOrWhiteSpace(s.Codec) ? "AAC" : s.Codec.ToUpperInvariant();
                        string details = $"{codecStr} • {(s.Channels == 6 ? "5.1 Surround" : s.Channels == 2 ? "Stereo" : $"{s.Channels} Kanal")}";
                        if (s.BitRate > 0) details += $" • {s.BitRate / 1000} kbps";

                        _detectedAudioTracks.Add(new AudioTrackModel
                        {
                            Index = i,
                            TrackId = s.Id,
                            DisplayName = $"{i + 1}. {langName}",
                            Details = details,
                            LangCode = langCode,
                            IsSelected = s.Selected
                        });
                    }
                }

                if (_detectedAudioTracks.Count == 0)
                {
                    _detectedAudioTracks.Add(new AudioTrackModel
                    {
                        Index = 0,
                        TrackId = 1,
                        DisplayName = "1. Ana Yayın Sesi",
                        Details = "Varsayılan Ses Akışı",
                        LangCode = "TR",
                        IsSelected = true
                    });
                }

                string preferredLang = ViewModel.GetPreferredLanguageForChannel(_lastLoadedChannelId);
                if (_selectedAudioTrackId < 0 && !string.IsNullOrEmpty(preferredLang) && _detectedAudioTracks.Count > 1)
                {
                    var matchingTrack = _detectedAudioTracks.FirstOrDefault(t => string.Equals(t.LangCode, preferredLang, StringComparison.OrdinalIgnoreCase))
                                        ?? _detectedAudioTracks.FirstOrDefault(t => t.DisplayName.Contains(preferredLang, StringComparison.OrdinalIgnoreCase));
                    if (matchingTrack != null && !matchingTrack.IsSelected && _mpv != null)
                    {
                        _selectedAudioTrackId = matchingTrack.TrackId;
                        _mpv.SwitchAudioTrack(matchingTrack.TrackId);
                        foreach (var t in _detectedAudioTracks) t.IsSelected = (t.TrackId == matchingTrack.TrackId);
                    }
                }

                if (!_detectedAudioTracks.Any(t => t.IsSelected))
                    _detectedAudioTracks[0].IsSelected = true;

                if (AudioTracksItemsList != null)
                {
                    AudioTracksItemsList.ItemsSource = null;
                    AudioTracksItemsList.ItemsSource = _detectedAudioTracks;
                }

                var selected = _detectedAudioTracks.FirstOrDefault(t => t.IsSelected) ?? _detectedAudioTracks[0];
                if (AudioTrackBtnText != null) AudioTrackBtnText.Text = selected.LangCode;

                if (AudioTrackCountBadge != null && AudioTrackCountText != null)
                {
                    bool multi = _detectedAudioTracks.Count > 1;
                    AudioTrackCountBadge.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
                    AudioTrackCountText.Text = _detectedAudioTracks.Count.ToString();
                    if (AudioTracksInfoFooter != null)
                        AudioTracksInfoFooter.Text = multi ? $"🎉 Çoklu Dil: {_detectedAudioTracks.Count} ses izi mevcut" : "Tek ses izi algılandı";
                }
            });
        }

        private static (string name, string code) ParseLanguageInfo(string? lang, string? title, int index)
        {
            string l = (lang ?? "").Trim().ToLowerInvariant();
            string t = (title ?? "").Trim().ToLowerInvariant();
            string combined = $"{l} {t}";

            if (l is "deu" or "ger" or "de" or "german" or "deutsch" || combined.Contains("german") || combined.Contains("almanca") || combined.Contains("deutsch"))
                return ("Almanca", "DE");
            if (l is "eng" or "en" or "english" || combined.Contains("english") || combined.Contains("ingilizce"))
                return ("İngilizce", "EN");
            if (l is "tur" or "tr" or "turkish" || combined.Contains("turkish") || combined.Contains("türkçe") || combined.Contains("turkce"))
                return ("Türkçe", "TR");
            if (l is "fra" or "fre" or "fr" or "french" || combined.Contains("french") || combined.Contains("fransızca"))
                return ("Fransızca", "FR");
            if (l is "ita" or "it" or "italian" || combined.Contains("italian") || combined.Contains("italyanca"))
                return ("İtalyanca", "IT");
            if (l is "spa" or "es" or "spanish" || combined.Contains("spanish") || combined.Contains("ispanyolca"))
                return ("İspanyolca", "ES");
            if (l is "rus" or "ru" or "russian" || combined.Contains("russian") || combined.Contains("rusça"))
                return ("Rusça", "RU");
            if (l is "ara" or "ar" or "arabic" || combined.Contains("arabic") || combined.Contains("arapça"))
                return ("Arapça", "AR");
            if (l is "aze" or "az" or "azerbaijani" || combined.Contains("azerbaijani") || combined.Contains("azerice"))
                return ("Azerice", "AZ");
            if (l is "nld" or "dut" or "nl" or "dutch" || combined.Contains("dutch") || combined.Contains("felemenkçe"))
                return ("Felemenkçe", "NL");
            if (l is "por" or "pt" or "portuguese" || combined.Contains("portuguese") || combined.Contains("portekizce"))
                return ("Portekizce", "PT");
            if (l is "pol" or "pl" or "polish" || combined.Contains("polish") || combined.Contains("lehçe"))
                return ("Lehçe", "PL");

            if (!string.IsNullOrWhiteSpace(l) && l is not "und" and not "unknown" and not "mis" and not "zxx")
            {
                string code = l.Length <= 3 ? l.ToUpperInvariant() : l.Substring(0, 2).ToUpperInvariant();
                string label = !string.IsNullOrWhiteSpace(title) ? title : code;
                return (label, code);
            }
            if (!string.IsNullOrWhiteSpace(title))
                return (title, title.Length <= 3 ? title.ToUpperInvariant() : "SES");

            return ($"Ses İzi {index + 1}", $"S{index + 1}");
        }

        #endregion

        #region Audio Normalization & Video Enhancer (Instant Zero-Reconnect Runtime DSP)

        private void UpdateAudioNormButtonUI()
        {
            if (AudioNormBtn == null) return;
            AudioNormBtn.Background = _isAudioNormEnabled ? NormActiveBrush : SlateBtnBrush;
            AudioNormBtn.BorderBrush = _isAudioNormEnabled ? NormBorderBrush : SlateBorderBrush;
            AudioNormBtn.ToolTip = _isAudioNormEnabled
                ? "Ses Normalizasyonu: AÇIK (Anlık Dinamik DSP Seviye Dengeleme)"
                : "Ses Normalizasyonu: KAPALI (Ham Ses)";
        }

        private void UpdateVideoEnhanceButtonUI()
        {
            if (VideoEnhanceBtn == null) return;
            VideoEnhanceBtn.Background = _isVideoEnhanceEnabled ? SharpenActiveBrush : SlateBtnBrush;
            VideoEnhanceBtn.BorderBrush = _isVideoEnhanceEnabled ? SharpenBorderBrush : SlateBorderBrush;
            VideoEnhanceBtn.ToolTip = _isVideoEnhanceEnabled
                ? "Görüntü Netleştirici: AÇIK (Anlık GPU Keskinlik & Kontrast)"
                : "Görüntü Netleştirici: KAPALI (Standart Görüntü)";
        }

        public void AudioNormBtn_Click(object sender, RoutedEventArgs e)
        {
            _isAudioNormEnabled = !_isAudioNormEnabled;
            string val = _isAudioNormEnabled ? "true" : "false";
            _db.SetSetting("AudioNormEnabled", val);
            _db.SetSetting("FlyleafAudioNorm", val);
            UpdateAudioNormButtonUI();
            _mpv?.SetAudioNormalization(_isAudioNormEnabled);
            ShowOsdToast(_isAudioNormEnabled ? "🔊 Ses Normalizasyonu: AÇIK (Kesintisiz)" : "🔇 Ses Normalizasyonu: KAPALI", "GeoSoundWave");
            ShowOsdTemporary();
        }

        public void VideoEnhanceBtn_Click(object sender, RoutedEventArgs e)
        {
            _isVideoEnhanceEnabled = !_isVideoEnhanceEnabled;
            string val = _isVideoEnhanceEnabled ? "true" : "false";
            _db.SetSetting("VideoEnhanceEnabled", val);
            _db.SetSetting("FlyleafSharpen", val);
            UpdateVideoEnhanceButtonUI();
            _mpv?.SetVideoEnhancement(_isVideoEnhanceEnabled);
            ShowOsdToast(_isVideoEnhanceEnabled ? "✨ Görüntü Netleştirici: AÇIK (Kesintisiz)" : "📺 Görüntü Netleştirici: KAPALI", "GeoSparkle");
            ShowOsdTemporary();
        }

        public void ShowOsdToast(string message, string iconGeoKey)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (OsdToastBorder == null || OsdToastText == null || OsdToastIcon == null) return;
                OsdToastText.Text = message;
                if (TryFindResource(iconGeoKey) is Geometry geo) OsdToastIcon.Data = geo;

                OsdToastBorder.Visibility = Visibility.Visible;
                _toastTimer?.Stop();
                _toastTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                _toastTimer.Tick += (_, _) =>
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
            var mainWin = Window.GetWindow(this) as UI.Windows.MainWindow ?? UI.Windows.MainWindow.Instance;
            if (mainWin != null) { mainWin.ToggleFullscreen(); return; }

            var win = Window.GetWindow(this);
            if (win == null) return;
            win.WindowStyle = win.WindowStyle == WindowStyle.None ? WindowStyle.SingleBorderWindow : WindowStyle.None;
            win.WindowState = win.WindowStyle == WindowStyle.None ? WindowState.Maximized : WindowState.Normal;
        }

        private void UpdateOsdEpgForTime(DateTime targetTime)
        {
            if (ViewModel.CurrentChannel == null || OsdCurrentEpg == null || OsdNextEpg == null) return;
            string curText = "Yayın akışı bilgisi yok";
            string nextText = "Sıradaki: --:-- Bilgi yok";

            if (ViewModel.EpgList?.Count > 0)
            {
                var matching = ViewModel.EpgList.FirstOrDefault(p => targetTime >= p.StartTime && targetTime <= p.EndTime);
                if (matching != null) curText = $"{matching.StartTime:HH:mm} - {matching.EndTime:HH:mm} {matching.Title}";

                var future = ViewModel.EpgList.FirstOrDefault(p => p.StartTime > targetTime);
                if (future != null) nextText = $"Sıradaki: {future.StartTime:HH:mm} {future.Title}";
            }

            SetTextIfChanged(OsdCurrentEpg, ref _lastCurrentEpgText, curText);
            SetTextIfChanged(OsdNextEpg, ref _lastNextEpgText, nextText);
        }

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

                if (foundAccount == null) { DeadAccountAlertCard.Visibility = Visibility.Collapsed; return; }

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

            if (System.Windows.MessageBox.Show(
                $"'{acc.Username}' (@{acc.HostWithPort}) hesabına ait tüm IPTV yayınları ve bu hesaba özel kanallar veritabanından kalıcı olarak silinecektir.\n\nOnaylıyor musunuz?",
                "Ölü IPTV Hesabını Temizle", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

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

        private void BtnCloseDeadAccountAlert_Click(object sender, RoutedEventArgs e) => DeadAccountAlertCard.Visibility = Visibility.Collapsed;

        public void Dispose()
        {
            if (_isRecording) StopRecording();
            _recordCts?.Dispose();
            _recordCts = null;
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;
            _positionTimer?.Stop();
            _osdTimer?.Stop();
            _viewersTimer?.Stop();
            _toastTimer?.Stop();

            if (_mpv != null)
            {
                try
                {
                    var ch = ViewModel.CurrentChannel;
                    if (ch != null && ViewModel.IsVod(ch))
                        ViewModel.SaveVodPosition(ch, _mpv.PositionMs);
                    _mpv.Stop();

                    if (ch?.SourceType == "ACESTREAM" || ch?.Url?.Contains("acestream://") == true)
                        Task.Run(async () => await _ace.StopAllStreamsAsync());

                    _mpv.Dispose();
                }
                catch { }
                _mpv = null;
            }

            try { VideoPlayer.Dispose(); } catch { }
        }
    }

    public class AudioTrackModel
    {
        private static readonly SolidColorBrush SelectedBg = CreateFrozen(60, 56, 189, 248);
        private static readonly SolidColorBrush DefaultBg = CreateFrozen(20, 255, 255, 255);
        private static readonly SolidColorBrush SelectedBorder = CreateFrozen(255, 56, 189, 248);
        private static readonly SolidColorBrush DefaultBorder = CreateFrozen(40, 255, 255, 255);
        private static readonly SolidColorBrush SelectedBadge = CreateFrozen(255, 56, 189, 248);
        private static readonly SolidColorBrush DefaultBadge = CreateFrozen(255, 30, 41, 59);

        private static SolidColorBrush CreateFrozen(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        public int Index { get; set; }
        public int TrackId { get; set; } = 1;
        public string DisplayName { get; set; } = "";
        public string Details { get; set; } = "";
        public string LangCode { get; set; } = "TR";
        public bool IsSelected { get; set; }
        public Visibility CheckmarkVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;
        public System.Windows.Media.Brush BackgroundBrush => IsSelected ? SelectedBg : DefaultBg;
        public System.Windows.Media.Brush BorderBrush => IsSelected ? SelectedBorder : DefaultBorder;
        public System.Windows.Media.Brush BadgeBackground => IsSelected ? SelectedBadge : DefaultBadge;
        public System.Windows.Media.Brush BadgeForeground => IsSelected ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White;
    }
}
