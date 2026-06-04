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
using StreamMesh.Services.P2P;

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
        private List<Channel> _allChannels = new List<Channel>();
        private DispatcherTimer _adBannerTimer;

        // Radio Visualizer Elements & Timers
        private DispatcherTimer _radioTimer;
        private List<string> _rssNews = new List<string>();
        private int _currentNewsIndex = 0;
        private double _currentVinylAngle = 0;
        private Random _rng = new Random();
        private WeatherService _weatherService = new WeatherService();
        private int _rssTickCounter = 0;

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

            _adBannerTimer = new DispatcherTimer();
            _adBannerTimer.Interval = TimeSpan.FromSeconds(30);
            _adBannerTimer.Tick += AdBannerTimer_Tick;

            _radioTimer = new DispatcherTimer();
            _radioTimer.Interval = TimeSpan.FromMilliseconds(100);
            _radioTimer.Tick += RadioTimer_Tick;

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
            _allChannels = _databaseService.GetAllChannels();
            FilterChannels();
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
        }

        public async void LoadChannel(Channel channel, List<Channel> playlist = null)
        {
            if (channel == null) return;
            try
            {
                _currentChannel = channel;
                
                _databaseService.IncrementPersonalWatchCount(channel.Id);
                channel.PersonalWatchCount++;

                // Sync OSD Category Combobox with the dragged/played channel to provide continuous context
                if (OsdCategoryBox != null)
                {
                    OsdCategoryBox.SelectionChanged -= OsdCategoryBox_SelectionChanged;

                    if (playlist != null && playlist.All(c => c.IsFavorite)) 
                    {
                        OsdCategoryBox.SelectedIndex = 1; // Favoriler
                    }
                    else if (channel.Category != null)
                    {
                        string cat = channel.Category.ToLower();
                        if (cat.Contains("film") || cat.Contains("movie")) OsdCategoryBox.SelectedIndex = 3;
                        else if (cat.Contains("dizi") || cat.Contains("series")) OsdCategoryBox.SelectedIndex = 4;
                        else if (cat.Contains("radyo") || cat.Contains("radio")) OsdCategoryBox.SelectedIndex = 5;
                        else OsdCategoryBox.SelectedIndex = 2; // TV
                    }
                    else
                    {
                        OsdCategoryBox.SelectedIndex = 0;
                    }

                    OsdCategoryBox.SelectionChanged += OsdCategoryBox_SelectionChanged;
                    FilterChannels(); // Apply filter based on this new selection
                }

                if (ChannelListView != null)
                {
                    ChannelListView.SelectedItem = channel;
                    try { ChannelListView.ScrollIntoView(channel); } catch { }
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
                
                string category = (channel.Category ?? "").ToLower();
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
                MessageBox.Show($"Kanal yüklenirken hata oluştu: {ex.Message}", "Yükleme Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                        // Web arayüzünde kullanılan Hızlı (Muxed) stream metoduna geçildi.
                        // Adaptive stream (ayrı ses ve görüntü) VLC'de yavaş arabelleğe alınıyor, süre göstermiyor ve ileri sarmada sorun yaşatıyordu.
                        var directUrl = await _youtubeService.GetSingleMuxedStreamUrlAsync(finalUrl);
                        if (!string.IsNullOrEmpty(directUrl))
                        {
                            finalUrl = directUrl;
                            _currentYtAudioUrl = null; // Ayrı ses akışı kullanılmıyor
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
        
        private void AdBannerTimer_Tick(object sender, EventArgs e)
        {
            _adBannerTimer.Stop();
            if (OsdAdBanner != null) OsdAdBanner.Visibility = Visibility.Collapsed;
        }

        public void StopPlayback()
        {
            _mediaPlayer?.Stop();
            _radioTimer?.Stop();
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
    }
}
