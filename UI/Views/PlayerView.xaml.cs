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

        private void InitializePositionTimer()
        {
            _positionTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _positionTimer.Tick += (s, e) =>
            {
                if (_mediaPlayer == null || !_mediaPlayer.IsPlaying) return;
                try
                {
                    long timeMs = _mediaPlayer.Time;
                    long lengthMs = _mediaPlayer.Length;

                    if (timeMs >= 0)
                    {
                        TimeSpan ts = TimeSpan.FromMilliseconds(timeMs);
                        TimeCurrentText.Text = ts.ToString(@"hh\:mm\:ss");
                    }

                    if (lengthMs > 0)
                    {
                        TimeTotalText.Text = TimeSpan.FromMilliseconds(lengthMs).ToString(@"hh\:mm\:ss");
                        TimeSlider.Value = (double)timeMs / lengthMs * 100.0;
                    }
                    else
                    {
                        TimeTotalText.Text = "CANLI YAYIN";
                    }
                }
                catch { }
            };
            _positionTimer.Start();
        }

        private void Player_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (OsdPanel != null) OsdPanel.Visibility = Visibility.Visible;
            _osdTimer?.Stop();
            _osdTimer?.Start();
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
                    $"--http-user-agent={userAgent}"
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
            await _playSemaphore.WaitAsync();

            try
            {
                _mediaPlayer.Stop();
                Dispatcher.Invoke(() => {
                    OsdTitle.Text = channel.PrimaryName;
                    OsdCategory.Text = channel.Category;

                    try {
                        var convertedLogo = LogoConverter.Convert(channel.LogoUrl, typeof(ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture);
                        if (convertedLogo != null) OsdLogo.Source = (ImageSource)convertedLogo;
                    } catch { }
                });

                // Fetch EPG for OSD
                var epgDict = await _epgService.GetCurrentEpgsAsync(new List<Channel> { channel });
                Dispatcher.Invoke(() => {
                    if (epgDict.TryGetValue(channel.Id, out var currentProg))
                    {
                        OsdCurrentEpg.Text = $"{currentProg.StartTime:HH:mm} - {currentProg.EndTime:HH:mm} {currentProg.Title}";
                    }
                    else
                    {
                        OsdCurrentEpg.Text = "Yayın akışı bilgisi yok";
                    }
                });

                var nextProg = await _epgService.GetNextEpgAsync(channel);
                Dispatcher.Invoke(() => {
                    if (nextProg != null)
                    {
                        OsdNextEpg.Text = $"Sıradaki: {nextProg.StartTime:HH:mm} {nextProg.Title}";
                    }
                    else
                    {
                        OsdNextEpg.Text = "Sıradaki: --:-- Bilgi yok";
                    }
                });

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
                        finalUrlsToTry.Add(u);
                    }
                }

                bool success = false;
                foreach (var tryUrl in finalUrlsToTry)
                {
                    LogService.LogInfo($"Player: Deneniyor -> {tryUrl}");
                    using var media = new Media(_libVLC, new Uri(tryUrl));
                    _mediaPlayer.Play(media);

                    // V1.8.8: Wait longer for streams to buffer
                    int waitMs = tryUrl.Contains("127.0.0.1") ? 8000 : 6000;

                    int checkInterval = 500;
                    for (int t = 0; t < waitMs; t += checkInterval)
                    {
                        await System.Threading.Tasks.Task.Delay(checkInterval);
                        if (_mediaPlayer.IsPlaying) { success = true; break; }
                    }

                    if (success) break;
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
                // V1.8.8: Auto-play next episode for series
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
            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Pause();
                if (PlayPauseBtn != null) PlayPauseBtn.Content = "▶ Oynat";
            }
            else
            {
                _mediaPlayer.Play();
                if (PlayPauseBtn != null) PlayPauseBtn.Content = "⏸ Durdur";
            }
        }

        public void Rewind10_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer == null) return;
            _mediaPlayer.Time = Math.Max(0, _mediaPlayer.Time - 10000);
        }

        public void Forward10_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer == null) return;
            _mediaPlayer.Time = _mediaPlayer.Time + 10000;
        }

        public void GoLive_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer == null) return;
            if (_mediaPlayer.Length > 0)
            {
                _mediaPlayer.Time = _mediaPlayer.Length - 1000;
            }
            else if (_currentChannel != null)
            {
                LoadChannel(_currentChannel);
            }
        }

        public void Mute_Click(object sender, RoutedEventArgs e)
        {
            ToggleMute();
        }

        public void ToggleMute()
        {
            if (_mediaPlayer == null) return;
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

        private void TimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_mediaPlayer == null || !_mediaPlayer.IsSeekable || _mediaPlayer.Length <= 0) return;
            if (TimeSlider.IsMouseOver)
            {
                long newTime = (long)(_mediaPlayer.Length * (e.NewValue / 100.0));
                _mediaPlayer.Time = newTime;
            }
        }

        public void Dispose()
        {
            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();
            if (_bufferPtr != IntPtr.Zero) Marshal.FreeHGlobal(_bufferPtr);
        }
    }
}
