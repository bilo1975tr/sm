using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using StreamMesh.Models;
using StreamMesh.Core.Media;
using StreamMesh.Converters;
using StreamMesh.Core.Utils;

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

        public PlayerView()
        {
            InitializeComponent();
            InitializePlayer();
            this.Focusable = true;
        }

        private void InitializePlayer()
        {
            try
            {
                LibVLCSharp.Shared.Core.Initialize();

                string caching = _db.GetSetting("VlcCaching", "2000");
                string userAgent = _db.GetSetting("VlcUserAgent", "Mozilla/5.0");
                bool hwAccel = _db.GetSetting("VlcHwAccel", "true") == "true";

                var vlcArgs = new List<string> {
                    "--no-osd",
                    $"--network-caching={caching}",
                    $"--http-user-agent={userAgent}"
                };
                if (hwAccel) vlcArgs.Add("--avcodec-hw=any");
                else vlcArgs.Add("--avcodec-hw=none");

                _libVLC = new LibVLC(vlcArgs.ToArray());
                _mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);
                _mediaPlayer.SetVideoFormat("RV32", 1920, 1080, 1920 * 4);
                _mediaPlayer.SetVideoCallbacks(LockVideo, null, DisplayVideo);
            }
            catch { }
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
            await _playSemaphore.WaitAsync();

            try
            {
                OsdTitle.Text = channel.Name;
                OsdCategory.Text = channel.Category;
                OsdLogo.Source = (ImageSource)LogoConverter.Convert(channel.LogoUrl, typeof(ImageSource), null, null);

                var urls = (channel.Url ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
                bool success = false;

                for (int i = 0; i < urls.Length; i++)
                {
                    string finalUrl = urls[i].Trim();
                    LogService.LogInfo($"Player: Deneniyor ({i+1}/{urls.Length}) -> {finalUrl}");

                    if (channel.SourceType == "YOUTUBE" || finalUrl.Contains("youtube.com"))
                    {
                        var direct = await _yt.GetStreamUrlAsync(finalUrl);
                        if (direct != null) finalUrl = direct;
                    }
                    else if (channel.SourceType == "ACESTREAM" || finalUrl.StartsWith("acestream://"))
                    {
                        await _ace.StartEngineAsync();
                        finalUrl = _ace.GetHttpUrl(finalUrl);
                    }

                    using var media = new Media(_libVLC, new Uri(finalUrl));
                    _mediaPlayer.Play(media);

                    await System.Threading.Tasks.Task.Delay(3000);
                    if (_mediaPlayer.IsPlaying) { success = true; break; }
                }

                if (!success) System.Windows.MessageBox.Show("Yayın başlatılamadı. Tüm yedek linkler denendi.");
            }
            catch (Exception ex) { LogService.LogError("Player: Playback error", ex); }
            finally { _playSemaphore.Release(); }
        }

        public void Dispose()
        {
            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();
            if (_bufferPtr != IntPtr.Zero) Marshal.FreeHGlobal(_bufferPtr);
        }

        public void TogglePause()
        {
            if (_mediaPlayer == null) return;
            if (_mediaPlayer.IsPlaying) _mediaPlayer.Pause();
            else _mediaPlayer.Play();
        }

        public void ToggleMute()
        {
            if (_mediaPlayer == null) return;
            _mediaPlayer.Mute = !_mediaPlayer.Mute;
        }

        public void ToggleFullscreen()
        {
            var win = Window.GetWindow(this);
            if (win == null) return;

            if (win.WindowState == WindowState.Maximized && win.WindowStyle == WindowStyle.None)
            {
                win.WindowStyle = WindowStyle.SingleBorderWindow;
                win.WindowState = WindowState.Normal;
                OsdPanel.Visibility = Visibility.Visible;
            }
            else
            {
                win.WindowStyle = WindowStyle.None;
                win.WindowState = WindowState.Maximized;
                OsdPanel.Visibility = Visibility.Collapsed;
            }
        }
    }
}
