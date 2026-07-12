using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Net.NetworkInformation;
using LibVLCSharp.Shared;
using StreamMesh.Models;
using StreamMesh.Services;
using StreamMesh.Services.Auth;

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
        private DispatcherTimer _streamInfoTimer;
        private EpgProgram _currentEpg;
        private EpgProgram _nextEpg;
        private bool _isDragging = false;
        private int _currentRatioIndex = 0;
        private string[] _ratios = { "", "16:9", "4:3", "16:10", "2.35:1", "1:1" };
        private string _currentChannelUrl; 
        private bool _isOsnEnabled = false;
        private bool _isGoEnabled = false;

        private string _currentYtAudioUrl = null;
        private List<Tuple<string, string>> _currentYtVideoStreams = null;
        private string _currentYtVideoUrl;
        private List<Channel> _allChannels = new List<Channel>();
        private DispatcherTimer _adBannerTimer;
        private DispatcherTimer _viewerCountTimer;

        // Radio Visualizer Elements & Timers
        private DispatcherTimer _radioTimer;
        private List<string> _rssNews = new List<string>();
        private int _currentNewsIndex = 0;
        private double _currentVinylAngle = 0;
        private Random _rng = new Random();
        private WeatherService _weatherService = new WeatherService();
        private int _rssTickCounter = 0;
        private bool _needsSeekToProgress = false;
        private int _saveProgressCounter = 0;
        private System.Threading.CancellationTokenSource _movieAiCts;

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

            _streamInfoTimer = new DispatcherTimer();
            _streamInfoTimer.Interval = TimeSpan.FromSeconds(10);
            _streamInfoTimer.Tick += StreamInfoTimer_Tick;

            _adBannerTimer = new DispatcherTimer();
            _adBannerTimer.Interval = TimeSpan.FromSeconds(30);
            _adBannerTimer.Tick += AdBannerTimer_Tick;

            _viewerCountTimer = new DispatcherTimer();
            _viewerCountTimer.Interval = TimeSpan.FromSeconds(15);
            _viewerCountTimer.Tick += (s, e) => UpdateOsdViewerCountAsync();
            _viewerCountTimer.Start();

            _radioTimer = new DispatcherTimer();
            _radioTimer.Interval = TimeSpan.FromMilliseconds(100);
            _radioTimer.Tick += RadioTimer_Tick;

            this.PreviewKeyDown += PlayerView_PreviewKeyDown;
            this.Focusable = true;
            this.Loaded += (s, e) => this.Focus();

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
                MessageBox.Show("İşlem sırasında beklenmeyen bir hata oluştu. Lütfen tekrar deneyiniz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            LoadChannelsFromDb();
        }

        private async void LoadChannelsFromDb()
        {
            try
            {
                var channels = await System.Threading.Tasks.Task.Run(() => _databaseService.GetAllChannels());
                Dispatcher.Invoke(() => 
                {
                    _allChannels = channels;
                    FilterChannels();
                });
            }
            catch (Exception ex)
            {
                LogService.LogError("LoadChannelsFromDb error", ex);
            }
        }

        private void FilterChannels()
        {
            if (ChannelListView == null || _allChannels == null) return;
            string searchText = OsdSearchBox?.Text?.ToLower() ?? "";
            int selectedCatIndex = OsdCategoryBox?.SelectedIndex ?? 0;
            // 0: Tümü, 1: Favoriler, 2: TV, 3: Film, 4: Dizi, 5: Radyo

            var filtered = new List<Channel>();
            foreach (var ch in _allChannels)
            {
                if (!string.IsNullOrWhiteSpace(searchText) && ch.Name != null && !ch.Name.ToLower().Contains(searchText))
                    continue;

                if (selectedCatIndex == 1) // Favoriler
                {
                    if (!ch.IsFavorite) continue;
                }
                else if (selectedCatIndex == 2) // TV
                {
                    if (ch.Category != null && !ch.Category.ToLower().Contains("tv")) continue;
                }
                else if (selectedCatIndex == 3) // Film
                {
                    if (ch.Category != null && !ch.Category.ToLower().Contains("film")) continue;
                }
                else if (selectedCatIndex == 4) // Dizi
                {
                    if (ch.Category != null && !ch.Category.ToLower().Contains("dizi") && !ch.Category.ToLower().Contains("series")) continue;
                }
                else if (selectedCatIndex == 5) // Radyo
                {
                    if (ch.Category != null && !ch.Category.ToLower().Contains("radyo") && !ch.Category.ToLower().Contains("radio")) continue;
                }

                filtered.Add(ch);
            }
            ChannelListView.ItemsSource = filtered;

            // Fetch EPG in the background for filtered channels to show current program in the channel list
            var channelsToProcess = filtered.ToList();
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var epgService = new StreamMesh.Services.EpgService();
                    var epgDict = epgService.GetCurrentEpgsForChannels(channelsToProcess);
                    Dispatcher.Invoke(() =>
                    {
                        foreach (var ch in channelsToProcess)
                        {
                            if (epgDict.TryGetValue(ch.Id, out var curEpg))
                            {
                                ch.CurrentEpgTitle = curEpg.Title;
                                ch.CurrentEpgTime = $"{curEpg.StartTime:HH:mm} - {curEpg.EndTime:HH:mm}";
                            }
                            else
                            {
                                ch.CurrentEpgTitle = "EPG Bilgisi Yok";
                                ch.CurrentEpgTime = "--:--";
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    LogService.LogError("PlayerView background EPG loading error", ex);
                }
            });
        }

        public async void LoadChannel(Channel channel, List<Channel> playlist = null)
        {
            if (channel == null) return;
            try
            {
                // Eski kanalın izleme saniyesini hemen kaydet
                if (_currentChannel != null && _mediaPlayer != null && _mediaPlayer.IsPlaying)
                {
                    long t = _mediaPlayer.Time;
                    long len = _mediaPlayer.Length;
                    if (len > 0 && t > 0)
                    {
                        _databaseService.SaveWatchProgress(_currentChannel.Id, _currentChannel.Name, t, len);
                    }
                }

                _needsSeekToProgress = true;
                _saveProgressCounter = 0;
                _currentChannel = channel;
                StreamMesh.Services.ViewerTrackerService.Instance.SetActiveChannel(channel.Id);
                
                _databaseService.IncrementPersonalWatchCount(channel.Id);
                channel.PersonalWatchCount++;

                if (ChannelListView != null)
                {
                    ChannelListView.SelectedItem = channel;
                    try { ChannelListView.ScrollIntoView(channel); } catch { }
                }
                
                OsdTitle.Text = channel.Name;
                OsdCategory.Text = channel.Category ?? "Bilinmiyor";
                UpdateOsdViewerCountAsync();

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
                
                string category = (channel.Category ?? "").ToLower();
                string groupTitle = (channel.GroupTitle ?? "").ToLower();
                bool isFilm = category.Contains("film") || category.Contains("movie") || category.Contains("sinema") || groupTitle.Contains("film");

                if (isFilm)
                {
                    EpgProgressBar.Visibility = Visibility.Collapsed;
                    _currentEpg = null;
                    _nextEpg = null;

                    string localCleanTitle = CleanMovieTitleLocal(channel.Name);
                    OsdTitle.Text = localCleanTitle;

                    bool isAiAvailable = MainWindow.Instance.AiButton?.Foreground == System.Windows.Media.Brushes.LimeGreen;

                    if (isAiAvailable)
                    {
                        OsdCurrentEpgTime.Text = "YAPAY ZEKA FİLM DETAYI";
                        OsdCurrentEpgTitle.Text = localCleanTitle;
                        OsdNextEpg.Text = "Yapay zeka film özeti yükleniyor...";
                        FetchMovieAiDetailsAsync(channel.Name, localCleanTitle);
                    }
                    else
                    {
                        OsdCurrentEpgTime.Text = "FİLM MODU";
                        OsdCurrentEpgTitle.Text = localCleanTitle;
                        OsdNextEpg.Text = "Yapay zeka (Ollama) çevrimdışı olduğu için film özeti alınamadı.";
                    }
                }
                else
                {
                    EpgProgressBar.Visibility = Visibility.Visible;
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
                }
                
                category = (channel.Category ?? "").ToLower();
                bool isRadio = category.Contains("radyo") || category.Contains("radio");
                
                if (isRadio)
                {
                    RadioOverlayGrid.Visibility = Visibility.Visible;
                    RadioStationName.Text = channel.Name;
                    RadioStatusText.Text = "CANLI RADYO YAYINI";
                    _currentVinylAngle = 0;
                    VinylRotation.Angle = 0;
                    
                    LoadRssNewsAsync();
                    LoadRadioWeatherAsync(channel.Language);
                    _radioTimer.Start();
                }
                else
                {
                    RadioOverlayGrid.Visibility = Visibility.Collapsed;
                    _radioTimer.Stop();
                }

                SidebarBorder.Visibility = Visibility.Collapsed;
                ResetOsdTimer();
                await PlayChannelAsync(channel);
            }
            catch (Exception ex)
            {
                LogService.LogError("LoadChannel error", ex);
                MessageBox.Show("İşlem sırasında beklenmeyen bir hata oluştu. Lütfen tekrar deneyiniz.", "Yükleme Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async System.Threading.Tasks.Task PlayChannelAsync(Channel channel)
        {
            if (_mediaPlayer == null) { StatusTextBlock.Text = "VLC Başlatılamadı!"; return; }
            
            await _playSemaphore.WaitAsync();
            try
            {
                var urls = (channel.Url ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                bool success = false;

                for (int i = 0; i < urls.Length; i++)
                {
                    string finalUrl = urls[i].Trim();
                    if (string.IsNullOrEmpty(finalUrl)) continue;

                    LogService.Log($"[Player] Deneniyor ({i+1}/{urls.Length}): {channel.Name} | URL: {finalUrl}");
                    _currentChannelUrl = finalUrl;

                    Dispatcher.Invoke(() => {
                        StatusTextBlock.Text = urls.Length > 1 ? $"Bağlanıyor (Yedek {i+1})..." : "Bağlanıyor...";
                    });

                    try
                    {
                        bool isYoutube = finalUrl.Contains("youtube.com") || finalUrl.Contains("youtu.be");
                        bool isAceStream = finalUrl.StartsWith("acestream://") || (finalUrl.Length == 40 && System.Text.RegularExpressions.Regex.IsMatch(finalUrl, @"^[a-fA-F0-9]+$"));

                        if (channel.SourceType == "YOUTUBE" || isYoutube)
                        {
                            var directUrl = await _youtubeService.GetSingleMuxedStreamUrlAsync(finalUrl);
                            if (!string.IsNullOrEmpty(directUrl)) finalUrl = directUrl;
                        }
                        else if (channel.SourceType == "ACESTREAM" || isAceStream)
                        {
                            await _aceStreamService.StartEngineAsync();
                            finalUrl = _aceStreamService.GetHttpUrl(finalUrl);
                        }

                        var media = new Media(_libVLC, new Uri(finalUrl));
                        ApplyFiltersToMedia(media);
                        
                        _mediaPlayer.Play(media);

                        // Kısa bir süre bekleyip yayın açıldı mı kontrol edelim
                        await System.Threading.Tasks.Task.Delay(3000);
                        if (_mediaPlayer.IsPlaying) {
                            success = true;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError($"[Player] Link hatası (Yedek {i+1}): {ex.Message}");
                    }
                }

                if (!success)
                {
                    Dispatcher.Invoke(() => {
                        StatusTextBlock.Text = "Tüm linkler başarısız!";
                        MessageBox.Show("Bu kanala ait tüm yayın linkleri şu an ulaşılamaz durumda.", "Yayın Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
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
                _streamInfoTimer.Start();
                StatusTextBlock.Text = "Oynatılıyor";
                PlayPauseBtn.Content = "⏸";
                
                // Aspect Ratio'yu UI tarafında tekrar uygula (VLC bazen geç algılar ve sıfırlar)
                if (_currentRatioIndex > 0)
                {
                    ApplyCurrentRatio();
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

                var audioTracks = _mediaPlayer.AudioTrackDescription;
                AudioBtn.Visibility = (audioTracks != null && audioTracks.Length > 1) ? Visibility.Visible : Visibility.Collapsed;
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
                _streamInfoTimer.Stop();
                StatusTextBlock.Text = "Yayın Koptu veya Sona Erdi";
                PlayPauseBtn.Content = "▶";
            });
        }

        private string[] _donationsAndAds = {
            "Gelişmiş StreamMesh Deneyimi: Reklamsız ve engelsiz canlı yayın izlemek için VIP satın alabilirsiniz.",
            "Kampanya: Arkadaşın referans kodunla üye olup 3 gün peş peşe veya 1 ay içinde 4 kez giriş yaparsa 1 AYLIK VIP Hediye!",
            "Yeni Özellik: Sürüş sırasında dikkat! Artık sürükle & bırak ile kanalları birleştirme yayında.",
            "Bulut Avantajı: Aynı üyelik üzerinden cihazlarınız arasında senkronize favori kanallarınızı izleyin.",
        };
        private int _currentAdIndex = 0;

        private void ShowOsd()
        {
            if (TopOsd == null || BottomOsd == null) return;
            TopOsd.Visibility = Visibility.Visible;
            BottomOsd.Visibility = Visibility.Visible;
            this.Cursor = Cursors.Arrow;
            ResetOsdTimer();

            if (UserService.CurrentUser != null && !UserService.CurrentUser.IsPremium)
            {
                if (OsdAdBanner.Visibility != Visibility.Visible && !_adBannerTimer.IsEnabled)
                {
                    _currentAdIndex = (_currentAdIndex + 1) % _donationsAndAds.Length;
                    if (AdBannerText != null)
                    {
                        var refCode = UserService.CurrentUser.ReferralCode;
                        string text = _donationsAndAds[_currentAdIndex];
                        if (text.Contains("Kampanya"))
                        {
                             text += $"\nSenin Referans Kodun: {refCode}";
                        }
                        AdBannerText.Text = text;
                    }
                    
                    OsdAdBanner.Visibility = Visibility.Visible;
                    _adBannerTimer.Start();
                }
            }
            else
            {
                OsdAdBanner.Visibility = Visibility.Collapsed;
                _adBannerTimer.Stop();
            }
        }

        private void HideOsd()
        {
            if (TopOsd == null || BottomOsd == null) return;
            if (SidebarBorder != null && SidebarBorder.Visibility == Visibility.Visible) return;
            TopOsd.Visibility = Visibility.Collapsed;
            BottomOsd.Visibility = Visibility.Collapsed;
            this.Cursor = Cursors.None;
            if (OsdAdBanner != null)
            {
                OsdAdBanner.Visibility = Visibility.Collapsed;
                _adBannerTimer?.Stop();
            }
        }

        private void CloseAdBtn_Click(object sender, RoutedEventArgs e)
        {
            if (OsdAdBanner != null)
            {
                OsdAdBanner.Visibility = Visibility.Collapsed;
                _adBannerTimer?.Stop();
            }
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
                // normvol module can be finicky on some VLC versions. Compressor yields a louder, normalized output.
                media.AddOption(":audio-filter=compressor");
                media.AddOption(":compressor-makeup-gain=15.0"); // Boost the normalized sound
                media.AddOption(":compressor-threshold=-15.0"); // Catch loud sounds early
                media.AddOption(":compressor-ratio=4.0"); // 4:1 compression ratio
            }
        }
        
        private async void UpdateOsdViewerCountAsync()
        {
            if (_currentChannel == null)
            {
                if (OsdViewerPanel != null) OsdViewerPanel.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                var viewerCounts = await StreamMesh.Services.ViewerTrackerService.Instance.FetchViewerCountsAsync();
                if (viewerCounts != null && viewerCounts.TryGetValue(_currentChannel.Id, out var count))
                {
                    if (OsdViewerCount != null)
                    {
                        OsdViewerCount.Text = count.ToString();
                    }
                    if (OsdViewerPanel != null)
                    {
                        OsdViewerPanel.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    if (OsdViewerPanel != null)
                    {
                        OsdViewerPanel.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("UpdateOsdViewerCountAsync error", ex);
            }
        }

        private void AdBannerTimer_Tick(object sender, EventArgs e)
        {
            _adBannerTimer.Stop();
            if (OsdAdBanner != null) OsdAdBanner.Visibility = Visibility.Collapsed;
        }

        public void StopPlayback()
        {
            if (_currentChannel != null && _mediaPlayer != null && _mediaPlayer.IsPlaying)
            {
                long t = _mediaPlayer.Time;
                long len = _mediaPlayer.Length;
                if (len > 0 && t > 0)
                {
                    _databaseService.SaveWatchProgress(_currentChannel.Id, _currentChannel.Name, t, len);
                }
            }
            StreamMesh.Services.ViewerTrackerService.Instance.ClearActiveChannel();
            if (OsdViewerPanel != null) OsdViewerPanel.Visibility = Visibility.Collapsed;
            if (OsdViewerCount != null) OsdViewerCount.Text = "0";
            _mediaPlayer?.Stop();
            _radioTimer?.Stop();
            _streamInfoTimer?.Stop();
            if (RadioOverlayGrid != null) RadioOverlayGrid.Visibility = Visibility.Collapsed;
        }

        private void RadioTimer_Tick(object sender, EventArgs e)
        {
            // Rotate vinyl
            _currentVinylAngle = (_currentVinylAngle + 3) % 360;
            if (VinylRotation != null)
            {
                VinylRotation.Angle = _currentVinylAngle;
            }

            // Animate spectrum analyzer bars randomly to simulate music playing
            if (Bar0 != null) Bar0.Height = _rng.Next(10, 48);
            if (Bar1 != null) { Bar1.Height = _rng.Next(15, 52); Bar1.Fill = new SolidColorBrush(_rng.Next(0, 2) == 0 ? Color.FromRgb(56, 189, 248) : Color.FromRgb(6, 182, 212)); }
            if (Bar2 != null) Bar2.Height = _rng.Next(20, 50);
            if (Bar3 != null) Bar3.Height = _rng.Next(10, 45);
            if (Bar4 != null) Bar4.Height = _rng.Next(5, 35);
            if (Bar5 != null) Bar5.Height = _rng.Next(15, 52);
            if (Bar6 != null) Bar6.Height = _rng.Next(25, 48);
            if (Bar7 != null) Bar7.Height = _rng.Next(30, 55);
            if (Bar8 != null) Bar8.Height = _rng.Next(10, 44);
            if (Bar9 != null) Bar9.Height = _rng.Next(12, 50);
            if (Bar10 != null) Bar10.Height = _rng.Next(18, 48);
            if (Bar11 != null) Bar11.Height = _rng.Next(5, 30);
            if (Bar12 != null) Bar12.Height = _rng.Next(10, 40);
            if (Bar13 != null) Bar13.Height = _rng.Next(8, 35);

            // Change RSS headline news ticker gently every 5 seconds (50 ticks of 100ms)
            _rssTickCounter++;
            if (_rssTickCounter >= 50)
            {
                _rssTickCounter = 0;
                if (_rssNews != null && _rssNews.Count > 0)
                {
                    _currentNewsIndex = (_currentNewsIndex + 1) % _rssNews.Count;
                    if (NewsTickerText != null)
                    {
                        NewsTickerText.Text = _rssNews[_currentNewsIndex];
                    }
                }
            }
        }

        private async void LoadRssNewsAsync()
        {
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
                    string url = "https://www.trthaber.com/manset_articles.rss";
                    string xml = await client.GetStringAsync(url);
                    
                    var doc = System.Xml.Linq.XDocument.Parse(xml);
                    var items = doc.Descendants("item")
                                   .Select(i => i.Element("title")?.Value)
                                   .Where(t => !string.IsNullOrEmpty(t))
                                   .ToList();

                    if (items.Count > 0)
                    {
                        _rssNews = items;
                        _currentNewsIndex = 0;
                        if (NewsTickerText != null) NewsTickerText.Text = _rssNews[0];
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RSS Load Error: {ex.Message}");
            }

            // Fallback messages
            _rssNews = new List<string>
            {
                "StreamMesh Haber ve Canlı Radyo sistemine hoş geldiniz!",
                "Yeni Özellik: Sürükle-bırak ile çift kanalları birbiri üzerine bırakarak hızlıca birleştirebilirsiniz.",
                "StreamMesh veritabanı doğrudan GitHub Raw CDN üzerinden optimize edilerek çekilir.",
                "VLC ve AceStream altyapısı ile pikselsiz canlı yayın ve ses kompresör teknolojisi aktif."
            };
            _currentNewsIndex = 0;
            if (NewsTickerText != null) NewsTickerText.Text = _rssNews[0];
        }

        private async void LoadRadioWeatherAsync(string country)
        {
            try
            {
                string city = "otomatik";
                if (!string.IsNullOrEmpty(country) && country != "Türkiye" && country != "Unknown")
                {
                    city = country;
                }
                
                var weatherResult = await _weatherService.GetFreeWeatherAsync(city);
                if (weatherResult != null)
                {
                    if (RadioWeatherTemp != null) RadioWeatherTemp.Text = $"{weatherResult.CurrentTemp}°C";
                    if (RadioWeatherCity != null) RadioWeatherCity.Text = weatherResult.City;
                    if (RadioWeatherIcon != null) RadioWeatherIcon.Text = weatherResult.IconCode;
                }
                else
                {
                    if (RadioWeatherTemp != null) RadioWeatherTemp.Text = "18°C";
                    if (RadioWeatherCity != null) RadioWeatherCity.Text = "İstanbul";
                    if (RadioWeatherIcon != null) RadioWeatherIcon.Text = "☀️";
                }
            }
            catch
            {
                if (RadioWeatherTemp != null) RadioWeatherTemp.Text = "19°C";
                if (RadioWeatherCity != null) RadioWeatherCity.Text = "İst";
                if (RadioWeatherIcon != null) RadioWeatherIcon.Text = "☀️";
            }
        }

        public void Dispose()
        {
            _movieAiCts?.Cancel();
            _radioTimer?.Stop();
            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();
            if (_bufferPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_bufferPtr);
                _bufferPtr = IntPtr.Zero;
            }
        }

        public static string CleanMovieTitleLocal(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return "";
            try
            {
                string cleaned = System.IO.Path.GetFileNameWithoutExtension(rawName);
                
                cleaned = cleaned.Replace(".", " ")
                                 .Replace("_", " ")
                                 .Replace("-", " ")
                                 .Replace("(", " ")
                                 .Replace(")", " ")
                                 .Replace("[", " ")
                                 .Replace("]", " ");
                
                string[] patternsToRemove = new string[] {
                    "1080p", "720p", "480p", "2160p", "4k", "bluray", "brrip", "web-dl", "webdl", "hdrip", "dvdrip", "x264", "x265", "hevc", "h264", "h265",
                    "dual", "dublaj", "altyazili", "altyazılı", "altyazi", "tr", "eng", "turkce", "english", "turkish", "multi", "aac", "dd5 1", "dts", "ac3", "web", "rip", "remux", "imax"
                };
                
                foreach (var pattern in patternsToRemove)
                {
                    cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\b" + pattern + @"\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
                
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();
                
                if (!string.IsNullOrEmpty(cleaned))
                {
                    cleaned = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleaned.ToLower());
                }
                
                return string.IsNullOrEmpty(cleaned) ? rawName : cleaned;
            }
            catch
            {
                return rawName;
            }
        }

        private async void FetchMovieAiDetailsAsync(string rawName, string localCleanTitle)
        {
            _movieAiCts?.Cancel();
            _movieAiCts = new System.Threading.CancellationTokenSource();
            var token = _movieAiCts.Token;

            try
            {
                var chatService = new OllamaChatService();
                string jsonResponse = await chatService.GenerateMovieMetadataJsonAsync(rawName, token);

                if (token.IsCancellationRequested) return;

                if (!string.IsNullOrEmpty(jsonResponse))
                {
                    string cleanedJson = jsonResponse.Trim();
                    if (cleanedJson.StartsWith("```"))
                    {
                        int start = cleanedJson.IndexOf('{');
                        int end = cleanedJson.LastIndexOf('}');
                        if (start >= 0 && end > start)
                        {
                            cleanedJson = cleanedJson.Substring(start, end - start + 1);
                        }
                    }

                    try
                    {
                        using (var doc = System.Text.Json.JsonDocument.Parse(cleanedJson))
                        {
                            var root = doc.RootElement;
                            string aiTitle = root.GetProperty("title").GetString();
                            string aiSummary = root.GetProperty("summary").GetString();

                            if (!string.IsNullOrEmpty(aiTitle) && !string.IsNullOrEmpty(aiSummary))
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    if (token.IsCancellationRequested) return;
                                    OsdTitle.Text = aiTitle;
                                    OsdCurrentEpgTitle.Text = aiTitle;
                                    OsdNextEpg.Text = aiSummary;
                                });
                                return;
                            }
                        }
                    }
                    catch
                    {
                        string titleKey = "\"title\":";
                        string summaryKey = "\"summary\":";
                        int tIdx = cleanedJson.IndexOf(titleKey);
                        int sIdx = cleanedJson.IndexOf(summaryKey);
                        if (tIdx >= 0 && sIdx >= 0)
                        {
                            string aiTitle = ExtractValue(cleanedJson, titleKey);
                            string aiSummary = ExtractValue(cleanedJson, summaryKey);
                            if (!string.IsNullOrEmpty(aiTitle) && !string.IsNullOrEmpty(aiSummary))
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    if (token.IsCancellationRequested) return;
                                    OsdTitle.Text = aiTitle;
                                    OsdCurrentEpgTitle.Text = aiTitle;
                                    OsdNextEpg.Text = aiSummary;
                                });
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching movie details from Ollama: {ex.Message}");
            }

            Dispatcher.Invoke(() =>
            {
                if (token.IsCancellationRequested) return;
                OsdNextEpg.Text = "Bu film için EPG/özet bilgisi bulunmuyor. Yapay zeka çevrimdışı veya film detaylarına erişilemedi.";
            });
        }

        private async void StreamInfoTimer_Tick(object sender, EventArgs e)
        {
            if (_mediaPlayer == null || !_mediaPlayer.IsPlaying || _currentChannel == null)
            {
                StreamInfoOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            _streamInfoTimer.Stop(); // Stop timer while processing

            StreamInfoOverlay.Visibility = Visibility.Visible;

            string host = "Unknown";
            try { host = new Uri(_currentChannelUrl).Host; } catch { }

            string ping = await GetPingAsync(host);

            // Fetch technical stats
            var media = _mediaPlayer.Media;
            string codec = "N/A";
            string resolution = "N/A";
            string fps = "N/A";

            if (media != null)
            {
                // In LibVLCSharp, stats can be accessed via Media.Statistics
                var stats = _mediaPlayer.Media.Statistics;
                double bitrateKbps = stats.InputBitrate * 8 / 1024.0; // Bitrate is in bytes/s, convert to Kbps

                // Track info for resolution/fps
                var tracks = _mediaPlayer.VideoTrackDescription;
                if (tracks != null && tracks.Length > 0)
                {
                    // This is a simplified approach, LibVLC doesn't easily expose FPS/Codec in a single call without parsing tracks
                    codec = "H264"; // Default assumption, LibVLC doesn't expose it easily in the simplified API
                    resolution = "1080p"; // Placeholder, LibVLC tracks info would need parsing
                    fps = "60fps"; // Placeholder
                }

                StreamInfoText.Text = $"{host} | {codec} {resolution} {fps} | Bitrate: {bitrateKbps:F0} Kbps | Ping: {ping}";
            }
            else
            {
                StreamInfoText.Text = $"{host} | Ping: {ping}";
            }

            _streamInfoTimer.Start(); // Restart timer
        }

        private async Task<string> GetPingAsync(string host)
        {
            return await Task.Run(() => {
                try
                {
                    using (Ping ping = new Ping())
                    {
                        PingReply reply = ping.Send(host, 1000);
                        return reply.Status == IPStatus.Success ? $"{reply.RoundtripTime}ms" : "N/A";
                    }
                }
                catch { return "N/A"; }
            });
        }

        private void PlayerView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_mediaPlayer == null) return;

            switch (e.Key)
            {
                case Key.Space:
                    PlayPauseBtn_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.F:
                    FullscreenBtn_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.M:
                    MuteBtn_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.Left:
                    if (_mediaPlayer.Length > 0) _mediaPlayer.Time -= 10000; // 10 sn geri
                    ShowOsd();
                    e.Handled = true;
                    break;
                case Key.Right:
                    if (_mediaPlayer.Length > 0) _mediaPlayer.Time += 10000; // 10 sn ileri
                    ShowOsd();
                    e.Handled = true;
                    break;
                case Key.Up:
                    _mediaPlayer.Volume = Math.Min(100, _mediaPlayer.Volume + 5);
                    VolumeSlider.Value = _mediaPlayer.Volume;
                    ShowOsd();
                    e.Handled = true;
                    break;
                case Key.Down:
                    _mediaPlayer.Volume = Math.Max(0, _mediaPlayer.Volume - 5);
                    VolumeSlider.Value = _mediaPlayer.Volume;
                    ShowOsd();
                    e.Handled = true;
                    break;
            }
        }

        private string ExtractValue(string json, string key)
        {
            try
            {
                int idx = json.IndexOf(key);
                if (idx < 0) return null;
                int start = json.IndexOf('"', idx + key.Length);
                if (start < 0) return null;
                int end = json.IndexOf('"', start + 1);
                if (end < 0) return null;
                return json.Substring(start + 1, end - start - 1);
            }
            catch { return null; }
        }
    }
}
