using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Linq;
using LibVLCSharp.Shared;
using StreamMesh.Models;
using StreamMesh.Core.Media;
using StreamMesh.Converters;
using StreamMesh.Core.Utils;
using StreamMesh.Core.Database;

namespace StreamMesh.UI.Views
{
    public partial class PlayerView : System.Windows.Controls.UserControl, IDisposable
    {
        private LibVLC? _libVLC;
        private LibVLCSharp.Shared.MediaPlayer? _mediaPlayer;
        private WriteableBitmap? _bitmap;
        private IntPtr _bufferPtr = IntPtr.Zero;
        private int _bufferSize = 0;

        private readonly YoutubeEngine _yt = new YoutubeEngine();
        private readonly AceEngine _ace = new AceEngine();
        private readonly StreamMesh.Core.Database.DatabaseEngine _db = new StreamMesh.Core.Database.DatabaseEngine();
        private static readonly LogoCacheConverter LogoConverter = new LogoCacheConverter();
        private readonly System.Threading.SemaphoreSlim _playSemaphore = new System.Threading.SemaphoreSlim(1, 1);

        private readonly EpgService _epgService = new EpgService();
        private System.Windows.Threading.DispatcherTimer? _osdTimer;
        private System.Windows.Threading.DispatcherTimer? _positionTimer;
        private Channel? _currentChannel;
        private List<EpgProgram> _currentChannelEpgList = new();
        private string _lastDisplayedEpgCurrent = "";
        private string _lastDisplayedEpgNext = "";

        private bool _isUserDraggingSlider = false;
        private bool _isSeekingDvr = false;
        private double _dvrCurrentOffsetSec = -1; // -1 means live mode
        private long _liveElapsedMs = 0;
        private DateTime _streamStartTime = DateTime.UtcNow;
        private HlsSessionInfo? _currentHlsSession = null;
        private bool _isLivePaused = false;
        private DateTime? _livePauseStartUtc = null;
        private double _accumulatedDelaySec = 0;
        private double _pausedDvrSec = -1;
        private static readonly SolidColorBrush LiveRedBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));
        private static readonly SolidColorBrush DelayedAmberBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11));
        private static readonly SolidColorBrush VodBlueBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235));

        public PlayerView()
        {
            InitializeComponent();
            InitializePlayer();
            InitializeOsdTimer();
            InitializePositionTimer();
            this.Focusable = true;
        }

        private void InitializeOsdTimer()
        {
            _osdTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            _osdTimer.Tick += (s, e) =>
            {
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
                if (_mediaPlayer == null) return;
                try
                {
                    long timeMs = _mediaPlayer.Time;
                    long lengthMs = _mediaPlayer.Length;
                    bool isVod = IsCurrentStreamVod();

                    // Case 1: Stream is VOD
                    if (isVod && lengthMs > 0)
                    {
                        if (LiveBadge != null) LiveBadge.Background = VodBlueBrush;
                        if (LiveBadgeText != null) LiveBadgeText.Text = "🎬 VOD";

                        if (timeMs >= 0)
                        {
                            TimeSpan curTs = TimeSpan.FromMilliseconds(timeMs);
                            TimeCurrentText.Text = curTs.ToString(@"hh\:mm\:ss");
                        }

                        TimeSpan totalTs = TimeSpan.FromMilliseconds(lengthMs);
                        TimeTotalText.Text = totalTs.ToString(@"hh\:mm\:ss");

                        TimeSlider.Minimum = 0;
                        TimeSlider.Maximum = lengthMs;
                        if (!_isUserDraggingSlider && timeMs >= 0)
                        {
                            TimeSlider.Value = Math.Clamp(timeMs, 0, lengthMs);
                        }
                    }
                    // Case 2: Live TV Stream (HLS Proxy Timeshift or Direct)
                    else
                    {
                        double currentPauseSeconds = (_isLivePaused && _livePauseStartUtc.HasValue)
                            ? (DateTime.UtcNow - _livePauseStartUtc.Value).TotalSeconds
                            : 0;

                        double totalDelaySec = _accumulatedDelaySec + currentPauseSeconds;

                        // Check if explicit DVR session has total duration
                        double totalDvrSec = _currentHlsSession != null ? _currentHlsSession.TotalDurationSeconds : 0;
                        if (totalDvrSec <= 0)
                        {
                            totalDvrSec = (DateTime.UtcNow - _streamStartTime).TotalSeconds;
                        }

                        if (_isLivePaused)
                        {
                            if (LiveBadge != null) LiveBadge.Background = DelayedAmberBrush;
                            TimeSpan delayTs = TimeSpan.FromSeconds(Math.Max(1, totalDelaySec));
                            string delayStr = delayTs.TotalHours >= 1 ? delayTs.ToString(@"hh\:mm\:ss") : delayTs.ToString(@"mm\:ss");
                            if (LiveBadgeText != null) LiveBadgeText.Text = $"⏸ -{delayStr}";

                            DateTime broadcastTime = DateTime.Now.AddSeconds(-totalDelaySec);
                            TimeCurrentText.Text = broadcastTime.ToString("HH:mm:ss");
                            TimeTotalText.Text = $"🔴 CANLI: {DateTime.Now:HH:mm:ss}";
                            UpdateOsdEpgForTime(broadcastTime);
                        }
                        else if (totalDelaySec > 3.0)
                        {
                            if (LiveBadge != null) LiveBadge.Background = DelayedAmberBrush;
                            TimeSpan delayTs = TimeSpan.FromSeconds(totalDelaySec);
                            string delayStr = delayTs.TotalHours >= 1 ? delayTs.ToString(@"hh\:mm\:ss") : delayTs.ToString(@"mm\:ss");
                            if (LiveBadgeText != null) LiveBadgeText.Text = $"⏳ -{delayStr}";

                            DateTime broadcastTime = DateTime.Now.AddSeconds(-totalDelaySec);
                            TimeCurrentText.Text = broadcastTime.ToString("HH:mm:ss");
                            TimeTotalText.Text = $"🔴 CANLI: {DateTime.Now:HH:mm:ss}";
                            UpdateOsdEpgForTime(broadcastTime);
                        }
                        else
                        {
                            if (LiveBadge != null) LiveBadge.Background = LiveRedBrush;
                            if (LiveBadgeText != null) LiveBadgeText.Text = "🔴 CANLI";

                            TimeCurrentText.Text = DateTime.Now.ToString("HH:mm:ss");
                            TimeTotalText.Text = "CANLI YAYIN";
                            UpdateOsdEpgForTime(DateTime.Now);
                        }

                        TimeSlider.Minimum = 0;
                        TimeSlider.Maximum = Math.Max(10, totalDvrSec);
                        if (!_isUserDraggingSlider)
                        {
                            double currentPointSec = Math.Max(0, totalDvrSec - totalDelaySec);
                            TimeSlider.Value = Math.Clamp(currentPointSec, 0, Math.Max(10, totalDvrSec));
                        }
                    }
                }
                catch { }
            };
            _positionTimer.Start();
        }

        private void Player_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            ShowOsdTemporary();
        }

        private void InitializePlayer()
        {
            try
            {
                // V1.8.8: Dynamic LibVLC discovery logic
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] possiblePaths = {
                    Path.Combine(baseDir, "libvlc", "win-x64"),
                    Path.Combine(baseDir, "libvlc"),
                    @"C:\Program Files\VideoLAN\VLC",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\StreamMesh\libvlc\win-x64"),
                    baseDir
                };

                string? foundPath = possiblePaths.FirstOrDefault(p => File.Exists(Path.Combine(p, "libvlc.dll")));

                if (foundPath != null)
                {
                    LogService.LogInfo($"Player: LibVLC found at {foundPath}");
                    LibVLCSharp.Shared.Core.Initialize(foundPath);
                }
                else
                {
                    LogService.LogInfo("Player: LibVLC not found in standard paths, trying default initialization...");
                    LibVLCSharp.Shared.Core.Initialize();
                }

                string caching = _db.GetSetting("VlcCaching", "3000");
                string userAgent = _db.GetSetting("VlcUserAgent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                bool hwAccel = _db.GetSetting("VlcHwAccel", "true") == "true";
                bool audioNorm = _db.GetSetting("VlcAudioNorm", "false") == "true";
                bool videoSharpen = _db.GetSetting("VlcVideoSharpen", "false") == "true";

                var vlcArgs = new List<string> {
                    "--no-osd",
                    $"--network-caching={caching}",
                    $"--live-caching={caching}",
                    $"--http-user-agent={userAgent}",
                    "--clock-jitter=0",
                    "--clock-synchro=0"
                };
                if (hwAccel) vlcArgs.Add("--avcodec-hw=any");
                else vlcArgs.Add("--avcodec-hw=none");

                if (audioNorm)
                {
                    vlcArgs.Add("--audio-filter=normvol");
                    vlcArgs.Add("--norm-max-level=2.0");
                }

                if (videoSharpen)
                {
                    vlcArgs.Add("--video-filter=sharpen");
                    vlcArgs.Add("--sharpen-sigma=0.08");
                }

                _libVLC = new LibVLC(vlcArgs.ToArray());
                _mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);
                _mediaPlayer.EndReached += OnEndReached;
                _mediaPlayer.EncounteredError += (s, e) => LogService.LogError("Player: LibVLC Error encountered");
                _mediaPlayer.SetVideoFormat("RV32", 1920, 1080, 1920 * 4);
                _mediaPlayer.SetVideoCallbacks(LockVideo, null, DisplayVideo);
                LogService.LogInfo("Player: Initialization Success");
            }
            catch (Exception ex)
            {
                LogService.LogError("Player: Initialization Failed", ex);
            }
        }

        private IntPtr LockVideo(IntPtr opaque, IntPtr planes)
        {
            if (_bufferPtr == IntPtr.Zero)
            {
                _bufferSize = 1920 * 1080 * 4;
                _bufferPtr = Marshal.AllocHGlobal(_bufferSize);
            }
            Marshal.WriteIntPtr(planes, _bufferPtr);
            return IntPtr.Zero;
        }

        private void DisplayVideo(IntPtr opaque, IntPtr picture)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_bitmap == null)
                {
                    _bitmap = new WriteableBitmap(1920, 1080, 96, 96, PixelFormats.Bgr32, null);
                    VideoImage.Source = _bitmap;
                }
                _bitmap.Lock();
                unsafe { Buffer.MemoryCopy(_bufferPtr.ToPointer(), _bitmap.BackBuffer.ToPointer(), _bufferSize, _bufferSize); }
                _bitmap.AddDirtyRect(new Int32Rect(0, 0, 1920, 1080));
                _bitmap.Unlock();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        public async void LoadChannel(Channel channel)
        {
            if (_mediaPlayer == null || _libVLC == null) return;
            _currentChannel = channel;
            _liveElapsedMs = 0;
            _dvrCurrentOffsetSec = -1;
            _isLivePaused = false;
            _livePauseStartUtc = null;
            _accumulatedDelaySec = 0;
            _pausedDvrSec = -1;
            _streamStartTime = DateTime.UtcNow;
            _currentHlsSession = null;

            // Clear previous channel cache from proxy
            HlsProxyEngine.Instance.ClearChannelCache();

            await _playSemaphore.WaitAsync();

            try
            {
                _mediaPlayer.Stop();
                Dispatcher.Invoke(() => {
                    OsdTitle.Text = channel.PrimaryName;
                    OsdCategory.Text = channel.Category;
                    if (PlayPauseBtn != null) PlayPauseBtn.Content = "⏸";

                    try {
                        var convertedLogo = LogoConverter.Convert(channel.LogoUrl, typeof(ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture);
                        if (convertedLogo != null) OsdLogo.Source = (ImageSource)convertedLogo;
                    } catch { }
                });

                // Fetch full EPG for OSD and rewind timeline
                _currentChannelEpgList = await _epgService.GetChannelEpgHistoryAsync(channel);
                _lastDisplayedEpgCurrent = "";
                _lastDisplayedEpgNext = "";
                Dispatcher.Invoke(() => UpdateOsdEpgForTime(DateTime.Now));

                var rawUrls = (channel.Url ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                var finalUrlsToTry = new List<string>();

                // Prepare URLs
                foreach(var raw in rawUrls)
                {
                    string u = raw.Trim();
                    if (u.StartsWith("acestream://") || channel.SourceType == "ACESTREAM")
                    {
                        await _ace.StartEngineAsync();
                        finalUrlsToTry.AddRange(_ace.GetHttpUrls(u));
                    }
                    else if (u.Contains("youtube.com") || channel.SourceType == "YOUTUBE")
                    {
                        var direct = await _yt.GetStreamUrlAsync(u);
                        if (direct != null) finalUrlsToTry.Add(direct);
                    }
                    else
                    {
                        // Check if HLS stream to parse manifest and prepare proxy with DVR timeline
                        if (u.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) || !u.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var hlsSession = await HlsProxyEngine.Instance.InspectAndPrepareHlsAsync(u);
                                if (hlsSession != null && hlsSession.Segments.Count > 0)
                                {
                                    _currentHlsSession = hlsSession;
                                    string proxyUrl = HlsProxyEngine.Instance.GetProxyPlaybackUrl(u);
                                    finalUrlsToTry.Add(proxyUrl);
                                    LogService.LogInfo($"HlsProxy: Manifest çözüldü ({hlsSession.Segments.Count} segment, {hlsSession.TotalDurationSeconds:F0}s DVR). Proxy aktif: {proxyUrl}");
                                }
                            }
                            catch (Exception ex)
                            {
                                LogService.LogError("HlsProxy: HLS parse hatası, doğrudan URL denenecek", ex);
                            }
                        }

                        finalUrlsToTry.Add(u);
                    }
                }

                bool success = false;
                string caching = _db.GetSetting("VlcCaching", "1500");

                foreach (var tryUrl in finalUrlsToTry)
                {
                    LogService.LogInfo($"Player: Deneniyor -> {tryUrl}");
                    using var media = new Media(_libVLC, new Uri(tryUrl));
                    media.AddOption($":network-caching={caching}");
                    media.AddOption($":live-caching={caching}");
                    media.AddOption(":clock-jitter=0");

                    _mediaPlayer.Play(media);

                    // Wait for stream buffering
                    int waitMs = tryUrl.Contains("127.0.0.1") ? 5000 : 6000;
                    int checkInterval = 400;
                    for (int t = 0; t < waitMs; t += checkInterval)
                    {
                        await System.Threading.Tasks.Task.Delay(checkInterval);
                        if (_mediaPlayer.IsPlaying) { success = true; break; }
                    }

                    if (success)
                    {
                        _streamStartTime = DateTime.UtcNow;
                        _liveElapsedMs = 0;
                        break;
                    }
                    LogService.LogInfo($"Player: Adres yanıt vermedi ({tryUrl}), sonraki deneniyor...");
                }

                if (!success) System.Windows.MessageBox.Show("Yayın başlatılamadı. Tüm yedek linkler denendi.", "Oynatma Hatası");
            }
            catch (Exception ex) { LogService.LogError("Player: Playback error", ex); }
            finally { _playSemaphore.Release(); }
        }

        private void OnEndReached(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(async () =>
            {
                // Auto-play next episode for series
                if (_currentChannel != null && _currentChannel.Category == "Dizi")
                {
                    // Mark as watched
                    _currentChannel.IsWatched = true;
                    await _db.SaveChannelAsync(_currentChannel);

                    var seriesItems = await _db.GetSeriesEpisodesAsync(_currentChannel.SeriesBaseName);
                    int currentIdx = seriesItems.FindIndex(c => c.Id == _currentChannel.Id);
                    if (currentIdx >= 0 && currentIdx < seriesItems.Count - 1)
                    {
                        var nextEp = seriesItems[currentIdx + 1];
                        LogService.LogInfo($"Oynatma bitti. Sonraki bölüme geçiliyor: {nextEp.Name}");
                        LoadChannel(nextEp);
                        return;
                    }
                }

                _mediaPlayer?.Stop();
            });
        }

        public void Stop()
        {
            _mediaPlayer?.Stop();
        }

        public void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            TogglePause();
        }

        public void TogglePause()
        {
            if (_mediaPlayer == null) return;
            ShowOsdTemporary();

            bool isVod = IsCurrentStreamVod() && _mediaPlayer.Length > 0;

            if (isVod)
            {
                if (_mediaPlayer.IsPlaying)
                {
                    _mediaPlayer.Pause();
                    if (PlayPauseBtn != null) PlayPauseBtn.Content = "▶";
                }
                else
                {
                    _mediaPlayer.Play();
                    if (PlayPauseBtn != null) PlayPauseBtn.Content = "⏸";
                }
            }
            else
            {
                // Live Stream Pause / Resume Timeshift logic
                if (!_isLivePaused)
                {
                    // PAUSE LIVE:
                    _isLivePaused = true;
                    _livePauseStartUtc = DateTime.UtcNow;

                    if (_currentHlsSession != null && _currentHlsSession.TotalDurationSeconds > 0)
                    {
                        _pausedDvrSec = _dvrCurrentOffsetSec >= 0 ? _dvrCurrentOffsetSec : _currentHlsSession.TotalDurationSeconds;
                    }
                    else
                    {
                        _pausedDvrSec = (DateTime.UtcNow - _streamStartTime).TotalSeconds;
                    }

                    _mediaPlayer.SetPause(true);
                    if (PlayPauseBtn != null) PlayPauseBtn.Content = "▶";
                }
                else
                {
                    // RESUME LIVE from paused point:
                    _isLivePaused = false;
                    if (_livePauseStartUtc.HasValue)
                    {
                        double pauseDuration = (DateTime.UtcNow - _livePauseStartUtc.Value).TotalSeconds;
                        _accumulatedDelaySec += pauseDuration;
                        _livePauseStartUtc = null;
                    }

                    if (PlayPauseBtn != null) PlayPauseBtn.Content = "⏸";

                    // If paused for more than 2 seconds and we have an HLS session, resume from buffered TS playlist
                    if (_accumulatedDelaySec > 2.0 && _currentChannel != null && _currentHlsSession != null)
                    {
                        double targetSec = Math.Max(0, _pausedDvrSec);
                        _ = SeekDvrToSecondAsync(targetSec);
                    }
                    else
                    {
                        _mediaPlayer.SetPause(false);
                    }
                }
            }
        }

        public void Rewind(int ms = 10000)
        {
            if (_mediaPlayer == null) return;
            ShowOsdTemporary();

            if (IsCurrentStreamVod() && _mediaPlayer.Length > 0)
            {
                _mediaPlayer.Time = Math.Max(0, _mediaPlayer.Time - ms);
            }
            else if (_currentHlsSession != null && _currentHlsSession.HasDvrWindow && _currentHlsSession.TotalDurationSeconds > 0)
            {
                long totalMs = (long)(_currentHlsSession.TotalDurationSeconds * 1000);
                long timeMs = _mediaPlayer.Time;
                long cur = _dvrCurrentOffsetSec >= 0 ? (long)(_dvrCurrentOffsetSec * 1000) + (timeMs >= 0 ? timeMs : 0) : totalMs;
                long target = Math.Max(0, cur - ms);
                ApplySliderSeek(target);
            }
            else
            {
                long cur = _mediaPlayer.Time >= 0 ? _mediaPlayer.Time : _liveElapsedMs;
                long target = Math.Max(0, cur - ms);
                if (_mediaPlayer.IsSeekable)
                {
                    _mediaPlayer.Time = target;
                }
                else if (_liveElapsedMs > 0)
                {
                    float pos = (float)target / _liveElapsedMs;
                    _mediaPlayer.Position = Math.Clamp(pos, 0.0f, 1.0f);
                }
            }
        }

        public void Forward(int ms = 10000)
        {
            if (_mediaPlayer == null) return;
            ShowOsdTemporary();

            if (IsCurrentStreamVod() && _mediaPlayer.Length > 0)
            {
                _mediaPlayer.Time = Math.Min(_mediaPlayer.Length, _mediaPlayer.Time + ms);
            }
            else if (_currentHlsSession != null && _currentHlsSession.HasDvrWindow && _currentHlsSession.TotalDurationSeconds > 0)
            {
                long totalMs = (long)(_currentHlsSession.TotalDurationSeconds * 1000);
                long timeMs = _mediaPlayer.Time;
                long cur = _dvrCurrentOffsetSec >= 0 ? (long)(_dvrCurrentOffsetSec * 1000) + (timeMs >= 0 ? timeMs : 0) : totalMs;
                long target = cur + ms;
                if (target >= totalMs - 3500)
                {
                    GoLive();
                }
                else
                {
                    ApplySliderSeek(target);
                }
            }
            else
            {
                long cur = _mediaPlayer.Time >= 0 ? _mediaPlayer.Time : _liveElapsedMs;
                long target = cur + ms;
                if (target >= _liveElapsedMs - 2000)
                {
                    GoLive();
                }
                else
                {
                    if (_mediaPlayer.IsSeekable)
                    {
                        _mediaPlayer.Time = target;
                    }
                    else if (_liveElapsedMs > 0)
                    {
                        float pos = (float)target / _liveElapsedMs;
                        _mediaPlayer.Position = Math.Clamp(pos, 0.0f, 1.0f);
                    }
                }
            }
        }

        public void Rewind10_Click(object sender, RoutedEventArgs e)
        {
            Rewind(10000);
        }

        public void Forward10_Click(object sender, RoutedEventArgs e)
        {
            Forward(10000);
        }

        public void GoLive_Click(object sender, RoutedEventArgs e)
        {
            GoLive();
        }

        public void GoLive()
        {
            if (_mediaPlayer == null) return;
            ShowOsdTemporary();

            _isLivePaused = false;
            _livePauseStartUtc = null;
            _accumulatedDelaySec = 0;
            _dvrCurrentOffsetSec = -1;

            if (IsCurrentStreamVod() && _mediaPlayer.Length > 0)
            {
                if (!_mediaPlayer.IsPlaying)
                {
                    _mediaPlayer.Play();
                    if (PlayPauseBtn != null) PlayPauseBtn.Content = "⏸";
                }
                _mediaPlayer.Time = Math.Max(0, _mediaPlayer.Length - 1000);
            }
            else if (_currentHlsSession != null)
            {
                string rawUrl = (_currentChannel?.Url ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(rawUrl))
                {
                    string liveProxyUrl = HlsProxyEngine.Instance.GetProxyPlaybackUrl(rawUrl, -1);
                    string caching = _db.GetSetting("VlcCaching", "1500");
                    using var media = new Media(_libVLC!, new Uri(liveProxyUrl));
                    media.AddOption($":network-caching={caching}");
                    media.AddOption($":live-caching={caching}");
                    media.AddOption(":clock-jitter=0");
                    _mediaPlayer.Play(media);
                    if (PlayPauseBtn != null) PlayPauseBtn.Content = "⏸";
                }
                else
                {
                    if (!_mediaPlayer.IsPlaying)
                    {
                        _mediaPlayer.Play();
                        if (PlayPauseBtn != null) PlayPauseBtn.Content = "⏸";
                    }
                }
            }
            else
            {
                if (!_mediaPlayer.IsPlaying)
                {
                    _mediaPlayer.Play();
                    if (PlayPauseBtn != null) PlayPauseBtn.Content = "⏸";
                }

                if (_liveElapsedMs > 0 && _mediaPlayer.IsSeekable)
                {
                    _mediaPlayer.Time = _liveElapsedMs;
                }
                else if (_mediaPlayer.Position < 0.99f)
                {
                    _mediaPlayer.Position = 1.0f;
                }
            }

            if (LiveBadge != null) LiveBadge.Background = LiveRedBrush;
            if (LiveBadgeText != null) LiveBadgeText.Text = "🔴 CANLI";
        }

        public void ChangeVolume(int deltaPercent)
        {
            if (_mediaPlayer == null) return;
            ShowOsdTemporary();

            if (_mediaPlayer.Mute) _mediaPlayer.Mute = false;
            int newVol = Math.Clamp(_mediaPlayer.Volume + deltaPercent, 0, 150);
            _mediaPlayer.Volume = newVol;
            if (MuteBtn != null) MuteBtn.Content = newVol == 0 ? "🔇" : "🔊";
        }

        public void Mute_Click(object sender, RoutedEventArgs e)
        {
            ToggleMute();
        }

        public void ToggleMute()
        {
            if (_mediaPlayer == null) return;
            ShowOsdTemporary();
            _mediaPlayer.Mute = !_mediaPlayer.Mute;
            if (MuteBtn != null) MuteBtn.Content = _mediaPlayer.Mute ? "🔇" : "🔊";
        }

        public void Fullscreen_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullscreen();
        }

        public void ToggleFullscreen()
        {
            StreamMesh.UI.Windows.MainWindow.Instance?.ToggleFullscreen();
        }

        private void TimeSlider_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isUserDraggingSlider = true;
            ShowOsdTemporary();
        }

        private void TimeSlider_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isUserDraggingSlider = false;
            ApplySliderSeek(TimeSlider.Value);
        }

        private void TimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUserDraggingSlider)
            {
                ApplySliderSeek(e.NewValue);
            }
        }

        private void ApplySliderSeek(double value)
        {
            if (_mediaPlayer == null) return;

            if (IsCurrentStreamVod() && _mediaPlayer.Length > 0)
            {
                _mediaPlayer.Time = (long)Math.Clamp(value, 0, _mediaPlayer.Length);
            }
            else if (_currentHlsSession != null && _currentHlsSession.HasDvrWindow && _currentHlsSession.TotalDurationSeconds > 0)
            {
                long totalMs = (long)(_currentHlsSession.TotalDurationSeconds * 1000);
                long targetTimeMs = (long)Math.Clamp(value, 0, totalMs);

                if (targetTimeMs >= totalMs - 3500)
                {
                    GoLive();
                }
                else
                {
                    double targetSec = targetTimeMs / 1000.0;
                    _ = SeekDvrToSecondAsync(targetSec);
                }
            }
            else
            {
                long targetTime = (long)Math.Clamp(value, 0, Math.Max(1000, _liveElapsedMs));
                if (targetTime >= _liveElapsedMs - 2000)
                {
                    GoLive();
                }
                else
                {
                    if (_mediaPlayer.IsSeekable)
                    {
                        _mediaPlayer.Time = targetTime;
                    }
                    else if (_liveElapsedMs > 0)
                    {
                        float pos = (float)targetTime / _liveElapsedMs;
                        _mediaPlayer.Position = Math.Clamp(pos, 0.0f, 1.0f);
                    }
                }
            }
        }

        private async Task SeekDvrToSecondAsync(double targetSec)
        {
            if (_currentChannel == null || _currentHlsSession == null || _libVLC == null || _mediaPlayer == null) return;
            if (_isSeekingDvr) return;

            _isSeekingDvr = true;
            try
            {
                _dvrCurrentOffsetSec = targetSec;
                string rawUrl = (_currentChannel.Url ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
                if (string.IsNullOrEmpty(rawUrl)) return;

                string proxyVodUrl = HlsProxyEngine.Instance.GetProxyPlaybackUrl(rawUrl, targetSec);
                string caching = _db.GetSetting("VlcCaching", "1500");

                LogService.LogInfo($"HlsProxy DVR Seek: {targetSec:F1}s konumuna atlanıyor -> {proxyVodUrl}");

                using var media = new Media(_libVLC, new Uri(proxyVodUrl));
                media.AddOption($":network-caching={caching}");
                media.AddOption($":live-caching={caching}");
                media.AddOption(":clock-jitter=0");

                _mediaPlayer.Play(media);

                if (LiveBadge != null) LiveBadge.Background = DelayedAmberBrush;
                if (LiveBadgeText != null)
                {
                    double delaySec = Math.Max(0, _currentHlsSession.TotalDurationSeconds - targetSec);
                    TimeSpan delayTs = TimeSpan.FromSeconds(delaySec);
                    string delayStr = delayTs.TotalHours >= 1 ? delayTs.ToString(@"hh\:mm\:ss") : delayTs.ToString(@"mm\:ss");
                    LiveBadgeText.Text = $"⏳ -{delayStr}";
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("DVR Seek Hatası", ex);
            }
            finally
            {
                await Task.Delay(400);
                _isSeekingDvr = false;
            }
        }

        private void UpdateOsdEpgForTime(DateTime targetTime)
        {
            if (_currentChannel == null || OsdCurrentEpg == null || OsdNextEpg == null) return;

            if (_currentChannelEpgList != null && _currentChannelEpgList.Count > 0)
            {
                var chEpgUrls = (_currentChannel.EpgUrl ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(u => u.Trim()).ToList();

                // 1. Find program airing at targetTime
                var matching = _currentChannelEpgList.Where(p => targetTime >= p.StartTime && targetTime <= p.EndTime).ToList();
                EpgProgram? currentProg = null;
                if (matching.Count > 0)
                {
                    if (chEpgUrls.Count > 0)
                    {
                        currentProg = matching.FirstOrDefault(p => chEpgUrls.Any(u => (p.SourceUrl ?? "").Contains(u))) ?? matching[0];
                    }
                    else
                    {
                        currentProg = matching[0];
                    }
                }

                // 2. Find next program right after targetTime
                DateTime nextSearchTime = currentProg != null ? currentProg.EndTime : targetTime;
                var future = _currentChannelEpgList.Where(p => p.StartTime >= nextSearchTime).OrderBy(p => p.StartTime).ToList();
                EpgProgram? nextProg = null;
                if (future.Count > 0)
                {
                    if (chEpgUrls.Count > 0)
                    {
                        nextProg = future.FirstOrDefault(p => chEpgUrls.Any(u => (p.SourceUrl ?? "").Contains(u))) ?? future[0];
                    }
                    else
                    {
                        nextProg = future[0];
                    }
                }

                string currentText = currentProg != null
                    ? $"{currentProg.StartTime:HH:mm} - {currentProg.EndTime:HH:mm} {currentProg.Title}"
                    : "Yayın akışı bilgisi yok";

                string nextText = nextProg != null
                    ? $"Sıradaki: {nextProg.StartTime:HH:mm} {nextProg.Title}"
                    : "Sıradaki: --:-- Bilgi yok";

                if (_lastDisplayedEpgCurrent != currentText)
                {
                    _lastDisplayedEpgCurrent = currentText;
                    OsdCurrentEpg.Text = currentText;
                }

                if (_lastDisplayedEpgNext != nextText)
                {
                    _lastDisplayedEpgNext = nextText;
                    OsdNextEpg.Text = nextText;
                }
            }
            else
            {
                string currentText = "Yayın akışı bilgisi yok";
                string nextText = "Sıradaki: --:-- Bilgi yok";
                if (_lastDisplayedEpgCurrent != currentText)
                {
                    _lastDisplayedEpgCurrent = currentText;
                    OsdCurrentEpg.Text = currentText;
                }
                if (_lastDisplayedEpgNext != nextText)
                {
                    _lastDisplayedEpgNext = nextText;
                    OsdNextEpg.Text = nextText;
                }
            }
        }

        public void Dispose()
        {
            _positionTimer?.Stop();
            _osdTimer?.Stop();
            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();
            HlsProxyEngine.Instance.ClearChannelCache();
            if (_bufferPtr != IntPtr.Zero) Marshal.FreeHGlobal(_bufferPtr);
        }
    }
}
