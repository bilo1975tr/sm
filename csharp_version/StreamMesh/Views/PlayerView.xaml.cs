using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using StreamMesh.Models;
using StreamMesh.Services;

namespace StreamMesh.Views
{
    public partial class PlayerView : UserControl, IDisposable
    {
        private LibVLC _libVLC;
        private LibVLCSharp.Shared.MediaPlayer _mediaPlayer;
        private WriteableBitmap _bitmap;
        private IntPtr _bufferPtr = IntPtr.Zero;
        private int _bufferSize = 0;

        private M3uService _m3uService;
        private YoutubeService _youtubeService;
        private AceStreamService _aceStreamService;
        private DatabaseService _databaseService;

        private Channel _currentChannel;
        private System.Threading.SemaphoreSlim _playSemaphore = new System.Threading.SemaphoreSlim(1, 1);
        private DispatcherTimer _osdTimer;
        private DispatcherTimer _updateTimer;
        private EpgProgram _currentEpg;
        private EpgProgram _nextEpg;
        private bool _isDragging = false;
        private int _currentRatioIndex = 0;
        private string[] _ratios = { "", "16:9", "4:3", "16:10", "2.35:1", "1:1" };
        private string _currentChannelUrl; 
        private bool _isOsnEnabled = false;
        private bool _isGoEnabled = false;

        private string _currentYtAudioUrl;
        private List<Tuple<string, string>> _currentYtVideoStreams;
        private string _currentYtVideoUrl;

        public PlayerView()
        {
            InitializeComponent();

            _m3uService = new M3uService();
            _youtubeService = new YoutubeService();
            _aceStreamService = new AceStreamService();
            _databaseService = new DatabaseService();

            _osdTimer = new DispatcherTimer();
            _osdTimer.Interval = TimeSpan.FromSeconds(3);
            _osdTimer.Tick += OsdTimer_Tick;

            _updateTimer = new DispatcherTimer();
            _updateTimer.Interval = TimeSpan.FromMilliseconds(500);
            _updateTimer.Tick += UpdateTimer_Tick;
            _updateTimer.Start();

            try
            {
                // Ensure LibVLC is initialized
                Core.Initialize();

                // VLC Configuration
                var vlcArgs = new string[] 
                {
                    "--http-user-agent=Mozilla/5.0",
                    "--network-caching=1500",
                    "--live-caching=1500",
                    "--avcodec-hw=any",
                    "--no-osd"
                };

                _libVLC = new LibVLC(vlcArgs);
                _libVLC.Log += (s, ev) => LogService.Log($"VLC Native: {ev.Message}", "VLC");
                
                _mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);
                _mediaPlayer.SetVideoFormat("RV32", 1920, 1080, 1920 * 4);
                _mediaPlayer.SetVideoCallbacks(LockVideo, null, DisplayVideo);

                _mediaPlayer.Playing += MediaPlayer_Playing;
                _mediaPlayer.EncounteredError += MediaPlayer_EncounteredError;
                _mediaPlayer.EndReached += MediaPlayer_EndReached;
                
                LogService.Log("VLC Player Initialized Successfully.");
            }
            catch (Exception ex)
            {
                LogService.LogError("VLC could not be initialized", ex);
                MessageBox.Show($"VLC başlatılamadı: {ex.Message}\nStack: {ex.StackTrace}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            LoadChannelsFromDb();
        }

        private void LoadChannelsFromDb()
        {
            var channels = _databaseService.GetAllChannels();
            ChannelListView.ItemsSource = channels;
        }

        public async void LoadChannel(Channel channel, List<Channel> playlist = null)
        {
            if (channel == null) return;
            _currentChannel = channel;

            if (playlist != null)
            {
                ChannelListView.ItemsSource = playlist;
                ChannelListView.SelectedItem = channel;
                ChannelListView.ScrollIntoView(channel);
            }
            
            OsdTitle.Text = channel.Name;
            OsdCategory.Text = channel.Category ?? "Bilinmiyor";

            if (!string.IsNullOrEmpty(channel.LogoUrl))
            {
                try
                {
                    var converter = new StreamMesh.Converters.LogoCacheConverter();
                    var localPath = converter.Convert(channel.LogoUrl, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture) as string;
                    if (!string.IsNullOrEmpty(localPath) && (localPath.StartsWith("http") || System.IO.File.Exists(localPath)))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(localPath, UriKind.RelativeOrAbsolute);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        OsdChannelLogo.Source = bitmap;
                    }
                    else OsdChannelLogo.Source = null;
                }
                catch { OsdChannelLogo.Source = null; }
            }
            else OsdChannelLogo.Source = null;
            
            var epgService = new EpgService();
            _currentEpg = epgService.GetCurrentEpgForChannel(channel);
            _nextEpg = epgService.GetNextEpgForChannel(channel);

            if (_currentEpg != null)
            {
                OsdCurrentEpgTime.Text = $"{_currentEpg.StartTime:HH:mm} - {_currentEpg.EndTime:HH:mm}";
                OsdCurrentEpgTitle.Text = _currentEpg.Title;
            }
            else
            {
                OsdCurrentEpgTime.Text = "";
                OsdCurrentEpgTitle.Text = "EPG Bilgisi Yok";
                EpgProgressBar.Value = 0;
            }

            if (_nextEpg != null) OsdNextEpg.Text = $"Sonraki: {_nextEpg.StartTime:HH:mm} {_nextEpg.Title}";
            else OsdNextEpg.Text = "Sonraki Program Yok";
            
            SidebarBorder.Visibility = Visibility.Collapsed;
            ResetOsdTimer();
            await PlayChannelAsync(channel);
        }

        private async System.Threading.Tasks.Task PlayChannelAsync(Channel channel)
        {
            if (_mediaPlayer == null) { StatusTextBlock.Text = "VLC Başlatılamadı!"; return; }
            
            await _playSemaphore.WaitAsync();
            try
            {
                _mediaPlayer.Stop();
                
                string finalUrl = (channel.Url ?? "").Split(',')[0].Trim();
                if (string.IsNullOrEmpty(finalUrl))
                {
                    StatusTextBlock.Text = "Hata: Yayın URL'si bulunamadı!";
                    return;
                }

                _currentYtAudioUrl = null;
                _currentYtVideoStreams = null;
                _currentYtVideoUrl = null;

                LogService.Log($"Playing channel: {channel.Name} | URL: {finalUrl}");
                _currentChannelUrl = finalUrl;
                StatusTextBlock.Text = "Bağlanıyor...";
                ShowOsd();
                
                try
                {
                    // URL bazlı otomatik tespit (SourceType yanlış olsa bile düzelt)
                    bool isYoutube = finalUrl.Contains("youtube.com") || finalUrl.Contains("youtu.be");
                    bool isAceStream = finalUrl.StartsWith("acestream://");

                    if (channel.SourceType == "YOUTUBE" || isYoutube)
                    {
                        StatusTextBlock.Text = "YouTube bağlantısı çözülüyor...";
                        var directUrl = await _youtubeService.GetDirectStreamUrlAsync(finalUrl);
                        if (!string.IsNullOrEmpty(directUrl))
                        {
                            if (directUrl.StartsWith("YTCUSTOM::::"))
                            {
                                var segments = directUrl.Split(new[]{"::::"}, StringSplitOptions.RemoveEmptyEntries);
                                if (segments.Length > 2)
                                {
                                    _currentYtAudioUrl = segments[1];
                                    _currentYtVideoStreams = new List<Tuple<string, string>>();
                                    for (int i = 2; i < segments.Length; i++)
                                    {
                                        var sp = segments[i].Split(new[]{"|||"}, StringSplitOptions.RemoveEmptyEntries);
                                        if (sp.Length == 2) _currentYtVideoStreams.Add(new Tuple<string, string>(sp[0], sp[1]));
                                    }
                                    if (_currentYtVideoStreams.Count > 0)
                                    {
                                        finalUrl = _currentYtVideoStreams[0].Item2;
                                        _currentYtVideoUrl = finalUrl;
                                    }
                                }
                            }
                            else finalUrl = directUrl;
                        }
                        else throw new Exception("YouTube stream çözülemedi.");
                    }
                    else if (channel.SourceType == "ACESTREAM" || isAceStream)
                    {
                        StatusTextBlock.Text = "AceStream motoru hazırlanıyor...";
                        // Engine kontrolü ve başlatılması
                        await _aceStreamService.StartEngineAsync();
                        
                        // HTTP proxy URL'ine dönüştür
                        finalUrl = _aceStreamService.GetHttpUrl(finalUrl);
                        LogService.Log($"AceStream converted to: {finalUrl}");
                    }

                    // URL hala "acestream://" ise ve dönüştürülemediyse VLC kilitlenmeyi önlemek için engelle
                    if (finalUrl.StartsWith("acestream://"))
                    {
                        throw new Exception("AceStream motoru hazır değil veya yüklü değil!");
                    }

                    var media = new Media(_libVLC, new Uri(finalUrl));
                    if (!string.IsNullOrEmpty(_currentYtAudioUrl)) media.AddOption($":input-slave={_currentYtAudioUrl}");
                    ApplyFiltersToMedia(media);
                    
                    _mediaPlayer.Play(media);
                    PlayPauseBtn.Content = "⏸";
                    StatusTextBlock.Text = "Yükleniyor...";
                }
                catch(Exception ex)
                {
                    LogService.LogError($"Stream Play Error for channel: {channel.Name}", ex);
                    StatusTextBlock.Text = "Hata: " + ex.Message;
                    MessageBox.Show($"Yayın açılamadı!\n\nDetay: {ex.Message}", "Oynatma Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            finally
            {
                _playSemaphore.Release();
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
                var source = _bufferPtr;
                var destination = _bitmap.BackBuffer;
                unsafe { Buffer.MemoryCopy(source.ToPointer(), destination.ToPointer(), _bufferSize, _bufferSize); }
                _bitmap.AddDirtyRect(new Int32Rect(0, 0, 1920, 1080));
                _bitmap.Unlock();
            }), DispatcherPriority.Render);
        }

        private void MediaPlayer_Playing(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() => {
                StatusTextBlock.Text = "Oynatılıyor";
                PlayPauseBtn.Content = "⏸";
                
                // Ratio'yu tekrar uygula (VLC bazen geç algılar)
                if (_currentRatioIndex > 0)
                {
                    string ratio = _ratios[_currentRatioIndex];
                    if (ratio == "1:1") VideoImage.Stretch = Stretch.Fill;
                    else _mediaPlayer.AspectRatio = ratio;
                }

                // GO (Görüntü Onarıcı) filtresini yeni yayına taşı
                if (_isGoEnabled && _mediaPlayer != null)
                {
                    try
                    {
                        _mediaPlayer.SetAdjustInt(VideoAdjustOption.Enable, 1);
                        _mediaPlayer.SetAdjustFloat(VideoAdjustOption.Contrast, 1.25f);
                        _mediaPlayer.SetAdjustFloat(VideoAdjustOption.Brightness, 1.05f);
                        _mediaPlayer.SetAdjustFloat(VideoAdjustOption.Saturation, 1.15f);
                    }
                    catch { }
                }

                var tracks = _mediaPlayer.VideoTrackDescription;
                bool hasYtStreams = _currentYtVideoStreams != null && _currentYtVideoStreams.Count > 1;
                QualityBtn.Visibility = (hasYtStreams || (tracks != null && tracks.Length > 1)) ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        private void MediaPlayer_EncounteredError(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() => {
                string mediaUrl = _mediaPlayer?.Media?.Mrl ?? "Bilinmiyor";
                StatusTextBlock.Text = $"Bağlantı Hatası! URL: {mediaUrl}";
            });
        }

        private void MediaPlayer_EndReached(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() => {
                StatusTextBlock.Text = "Yayın Koptu veya Sona Erdi";
                PlayPauseBtn.Content = "▶";
            });
        }

        private void ShowOsd()
        {
            if (TopOsd == null || BottomOsd == null) return;
            TopOsd.Visibility = Visibility.Visible;
            BottomOsd.Visibility = Visibility.Visible;
            this.Cursor = Cursors.Arrow;
            ResetOsdTimer();
        }

        private void HideOsd()
        {
            if (TopOsd == null || BottomOsd == null) return;
            if (SidebarBorder != null && SidebarBorder.Visibility == Visibility.Visible) return;
            TopOsd.Visibility = Visibility.Collapsed;
            BottomOsd.Visibility = Visibility.Collapsed;
            this.Cursor = Cursors.None;
        }

        private void ResetOsdTimer()
        {
            if (_osdTimer == null) return;
            _osdTimer.Stop();
            _osdTimer.Start();
        }

        private void ApplyFiltersToMedia(Media media)
        {
            if (_isOsnEnabled)
            {
                media.AddOption(":audio-filter=normvol");
                media.AddOption(":normvol-buff-size=20");
                media.AddOption(":normvol-max-lvol=2.0");
            }
        }

        public void StopPlayback()
        {
            _mediaPlayer?.Stop();
        }

        public void Dispose()
        {
            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();
            if (_bufferPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_bufferPtr);
                _bufferPtr = IntPtr.Zero;
            }
        }
    }
}
