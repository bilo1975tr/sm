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
        private int _clockLogTickCount = 0;
        private long _lastReportedCurTimeMs = -1;

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
            var ch = ViewModel.CurrentChannel;
            if (ch == null) return false;
            string cat = (ch.Category ?? "").Trim().ToLowerInvariant();
            if (cat.Contains("dizi") || cat.Contains("film") || cat.Contains("vod") || cat.Contains("sinema") || cat.Contains("movie") || cat.Contains("series"))
            {
                return true;
            }
            string url = (ch.Url ?? "").ToLowerInvariant();
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
                if (_player == null || _player.Status == Status.Stopped || _isUserDraggingSlider) return;

                try
                {
                    long timeMs = _player.CurTime / 10000;
                    long lengthMs = _player.Duration / 10000;
                    bool isVod = ViewModel.IsVod(ViewModel.CurrentChannel);

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
                            double totalSec = proxySession.TotalDurationSeconds;
                            long totalMsDvr = (long)(totalSec * 1000);

                            TimeSlider.IsEnabled = true;
                            TimeSlider.Minimum = 0;
                            TimeSlider.Maximum = Math.Max(1000, totalMsDvr);

                            long currentDvrPosMs;
                            if (_player.Status == Status.Paused && _pausedDvrPosMs > 0)
                            {
                                currentDvrPosMs = _pausedDvrPosMs;
                            }
                            else if (_isTimeshiftMode)
                            {
                                currentDvrPosMs = Math.Clamp(_timeshiftStartDvrMs + timeMs, 0, totalMsDvr);
                                long delayFromLive = totalMsDvr - currentDvrPosMs;
                                if (delayFromLive <= 3000)
                                {
                                    _isTimeshiftMode = false;
                                    _timeshiftStartDvrMs = 0;
                                    currentDvrPosMs = totalMsDvr;
                                }
                            }
                            else
                            {
                                currentDvrPosMs = totalMsDvr;
                            }

                            _effectivePositionMs = currentDvrPosMs;
                            if (!_isUserDraggingSlider)
                            {
                                TimeSlider.Value = currentDvrPosMs;
                            }

                            UpdateOsdTimeAndBadge(currentDvrPosMs, totalMsDvr, proxySession.StartWallClockTime);
                            long offsetMs = Math.Max(0, totalMsDvr - currentDvrPosMs);
                            TimeTotalText.Text = offsetMs > 3000 ? "GERİDEN YAYIN" : "CANLI YAYIN";
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

                if (LiveBadge != null) LiveBadge.Background = DelayedAmberBrush;
                if (LiveBadgeText != null)
                {
                    TimeSpan delay = TimeSpan.FromMilliseconds(offsetMs);
                    LiveBadgeText.Text = delay.TotalHours >= 1 
                        ? $"-{delay:hh\\:mm\\:ss}" 
                        : $"-{delay:mm\\:ss}";
                }
                UpdateOsdEpgForTime(airedTime);
            }
            else
            {
                TimeCurrentText.Text = DateTime.Now.ToString("HH:mm:ss");
                if (LiveBadge != null) LiveBadge.Background = LiveRedBrush;
                if (LiveBadgeText != null) LiveBadgeText.Text = "🔴 CANLI";
                UpdateOsdEpgForTime(DateTime.Now);
            }
        }

        private async void InitializePlayer()
        {
            if (_player != null)
            {
                VideoPlayer.Player = _player;
                return;
            }

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
                    // Demuxer Network & User Agent Configurations
                    _config.Demuxer.FormatOpt["user_agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

                    _config.Demuxer.BufferDuration = 2000 * 10000; // 2.0s initial buffer for stable stream start
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
                            }
                            else if (_player.Status == Status.Paused || _player.Status == Status.Stopped)
                            {
                                UpdatePlayPauseIcon(false);
                            }
                            else if (_player.Status == Status.Failed)
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
                _initTcs.TrySetException(ex);
            }
        }

        private async void UpdateChannelLogo(Channel channel)
        {
            if (channel == null || OsdLogo == null || OsdLogoFallback == null) return;

            try
            {
                ImageSource? converted = null;
                if (!string.IsNullOrWhiteSpace(channel.PrimaryLogoUrl))
                {
                    converted = LogoConverter.Convert(channel.PrimaryLogoUrl, typeof(ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture) as ImageSource;
                }

                // Fallback: If no logo or conversion failed, search locally/online via LogoSearchEngine
                if (converted == null)
                {
                    string cleanName = ChannelUtils.GetCleanName(channel.PrimaryName ?? channel.Name ?? "TV");
                    var searchResults = await LogoSearchEngine.SearchLogosAsync(cleanName);
                    if (searchResults != null && searchResults.Count > 0)
                    {
                        var firstMatch = searchResults.FirstOrDefault(r => !string.IsNullOrEmpty(r.Url));
                        if (firstMatch != null)
                        {
                            channel.LogoUrl = string.IsNullOrEmpty(channel.LogoUrl) ? firstMatch.Url : $"{channel.LogoUrl},{firstMatch.Url}";
                            try { _db.SaveChannelSync(channel); } catch { }
                            converted = LogoConverter.Convert(firstMatch.Url, typeof(ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture) as ImageSource;
                        }
                    }
                }

                if (converted != null)
                {
                    OsdLogo.Source = converted;
                    OsdLogo.Visibility = Visibility.Visible;
                    OsdLogoFallback.Visibility = Visibility.Collapsed;
                }
                else
                {
                    string cleanName = ChannelUtils.GetCleanName(channel.PrimaryName ?? "TV");
                    string monogram = GenerateMonogram(cleanName);
                    OsdLogoFallbackText.Text = monogram;
                    int hash = Math.Abs(cleanName.GetHashCode());
                    OsdLogoFallback.Background = MonogramPalette[hash % MonogramPalette.Length];
                    OsdLogo.Visibility = Visibility.Collapsed;
                    OsdLogoFallback.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                OsdLogo.Visibility = Visibility.Collapsed;
                OsdLogoFallback.Visibility = Visibility.Visible;
            }
        }

        private static string GenerateMonogram(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "TV";
            var parts = name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                return parts[0].Length <= 3 ? parts[0].ToUpperInvariant() : parts[0].Substring(0, 2).ToUpperInvariant();
            }
            else if (parts.Length == 2)
            {
                return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
            }
            else
            {
                return $"{parts[0][0]}{parts[1][0]}{parts[2][0]}".ToUpperInvariant();
            }
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

            _currentPlayingUrl = null;
            _streamStartTime = DateTime.UtcNow;
            _isTimeshiftMode = false;
            _timeshiftStartDvrMs = 0;
            _pausedDvrPosMs = 0;
            _effectivePositionMs = 0;

            Dispatcher.Invoke(() => {
                OsdTitle.Text = $"{channel.PrimaryName} (Bağlanıyor...)";
                OsdCategory.Text = channel.Category;
                OsdCurrentEpg.Text = "Yayın akışı bilgisi yükleniyor...";
                OsdNextEpg.Text = "Sıradaki: --:--";
                ShowOsdTemporary();
                UpdatePlayPauseIcon(true);
                UpdateChannelLogo(channel);
            });

            Task.Run(async () =>
            {
                if (token.IsCancellationRequested) return;

                // Wait for Player initialization (Flyleaf/FFmpeg setup)
                try
                {
                    LogService.LogInfo("[PLAYBACK] Waiting for Player Init...");
                    using var initTimeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
                    using var linkedInit = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(token, initTimeout.Token);
                    await _initTcs.Task.WaitAsync(linkedInit.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested) return;
                    LogService.LogError("[PLAYBACK] Player Init TIMEOUT");
                    _ = Dispatcher.InvokeAsync(() => {
                        OsdTitle.Text = "Görüntü motoru başlatılamadı (Zaman aşımı)";
                        ShowOsdTemporary();
                    });
                    return;
                }

                if (_player == null) return;

                LogService.LogInfo("[PLAYBACK] Waiting for Play Semaphore...");
                bool acquired = await _playSemaphore.WaitAsync(10000, token).ConfigureAwait(false);
                if (!acquired || token.IsCancellationRequested)
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

                    if (channel.SourceType == "ACESTREAM" || (channel.Url ?? "").Contains("acestream://"))
                    {
                        await _ace.StopAllStreamsAsync().ConfigureAwait(false);
                    }
                    _player.Stop();
                    HlsProxyEngine.Instance.ClearChannelCache();

                    if (token.IsCancellationRequested) return;

                    _selectedAudioTrackIndex = -1;
                    _selectedAudioTrackLangCode = "";

                    await ViewModel.RefreshEpgAsync(channel);
                    _ = Dispatcher.InvokeAsync(() => UpdateOsdEpgForTime(DateTime.Now));

                    ApplyAudioNormalization();
                    ApplyVideoEnhancement();

                    LogService.LogInfo("[PLAYBACK] Preparing stream URL...");
                    string tryUrl = await ViewModel.PrepareStreamAsync(channel, token, (status) => {
                        _ = Dispatcher.InvokeAsync(() => { OsdTitle.Text = status; ShowOsdTemporary(); });
                    }).ConfigureAwait(false);

                    if (token.IsCancellationRequested || string.IsNullOrEmpty(tryUrl))
                    {
                        if (!token.IsCancellationRequested)
                        {
                            LogService.LogWarning("[PLAYBACK] Stream URL preparation failed or empty");
                            _ = Dispatcher.InvokeAsync(() => {
                                OsdTitle.Text = $"{channel.PrimaryName} (Sinyal Zayıf veya Adres Geçersiz)";
                                UpdatePlayPauseIcon(false);
                                ShowOsdTemporary();
                            });
                        }
                        return;
                    }

                    LogService.LogInfo($"[PLAYBACK] Calling Player.Open -> {tryUrl}");

                    // Open with a safer watchdog (30s for IPTV, 40s for AceStream)
                    int watchdogMs = (channel.SourceType == "ACESTREAM") ? 40000 : 30000;

                    var swOpen = System.Diagnostics.Stopwatch.StartNew();
                    var openTask = Task.Run(() => {
                        try { _player.Open(tryUrl); } catch (Exception ex) { LogService.LogError("[PLAYBACK] Player.Open Exception", ex); }
                    }, token);

                    var completed = await Task.WhenAny(openTask, Task.Delay(watchdogMs, token)).ConfigureAwait(false);
                    swOpen.Stop();

                    if (token.IsCancellationRequested) return;

                    if (completed != openTask)
                    {
                        LogService.LogWarning($"[PLAYBACK] Player.Open TIMEOUT after {swOpen.ElapsedMilliseconds}ms for {tryUrl}");
                        _ = Dispatcher.InvokeAsync(() => {
                            OsdTitle.Text = $"{channel.PrimaryName} (Bağlantı Zaman Aşımı)";
                            UpdatePlayPauseIcon(false);
                            ShowOsdTemporary();
                        });
                        return;
                    }

                    LogService.LogInfo($"[PLAYBACK] Player.Open RETURNED in {swOpen.ElapsedMilliseconds}ms (Player.Status: {_player?.Status})");
                    _currentPlayingUrl = tryUrl;
                    _ = Dispatcher.InvokeAsync(() => {
                        if (ViewModel.CurrentChannel != null) OsdTitle.Text = ViewModel.CurrentChannel.PrimaryName;
                    });

                    if (ViewModel.IsVod(channel) && channel.LastPositionMs > 0 && _player != null)
                    {
                        try { _player.Seek((int)channel.LastPositionMs); _lastSeekTime = DateTime.Now; _effectivePositionMs = channel.LastPositionMs; } catch { }
                    }

                    _ = Dispatcher.InvokeAsync(() => _positionTimer?.Start());
                }
                catch (Exception ex)
                {
                    LogService.LogError("[PLAYBACK] CRITICAL ERROR", ex);
                    if (!token.IsCancellationRequested)
                    {
                        _ = Dispatcher.InvokeAsync(() => {
                            OsdTitle.Text = $"{channel.PrimaryName} (Hata: {ex.Message})";
                            ShowOsdTemporary();
                        });
                    }
                }
                finally { _playSemaphore.Release(); LogService.LogInfo("[PLAYBACK] Play Semaphore Released"); }
            }, token);
        }

        public async void Stop()
        {
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
                        long totalMsDvr = (long)(proxySession.TotalDurationSeconds * 1000);
                        if (_isTimeshiftMode)
                        {
                            long curMs = _player.CurTime / 10000;
                            _pausedDvrPosMs = Math.Clamp(_timeshiftStartDvrMs + curMs, 0, totalMsDvr);
                        }
                        else
                        {
                            _pausedDvrPosMs = _effectivePositionMs > 0 ? _effectivePositionMs : totalMsDvr;
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
                    long totalMsDvr = (long)(proxySession.TotalDurationSeconds * 1000);
                    targetMs = Math.Clamp(targetMs, 0, totalMsDvr);
                    long offsetMs = totalMsDvr - targetMs;

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
                        UpdateOsdTimeAndBadge(targetMs, totalMsDvr, proxySession.StartWallClockTime);
                        TimeTotalText.Text = "GERİDEN YAYIN";
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

                string liveUrl = HlsProxyEngine.Instance.GetProxyPlaybackUrl(ViewModel.CurrentChannel.Url ?? "", -1);
                _player.Open(liveUrl);

                TimeSlider.Value = totalMs;
                TimeCurrentText.Text = DateTime.Now.ToString("HH:mm:ss");
                if (LiveBadge != null) LiveBadge.Background = LiveRedBrush;
                if (LiveBadgeText != null) LiveBadgeText.Text = "🔴 CANLI";
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

                if (_player.Status == Status.Playing || _player.Status == Status.Paused || _player.Status == Status.Opening)
                {
                    LogService.LogInfo($"Player: Fast reloading current stream for '{ViewModel.CurrentChannel.PrimaryName}' to apply filters immediately (VOD pos: {currentPosMs}ms)");
                    LoadChannel(ViewModel.CurrentChannel);
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
