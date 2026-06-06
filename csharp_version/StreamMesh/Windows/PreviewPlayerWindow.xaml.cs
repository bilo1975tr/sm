using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LibVLCSharp.Shared;
using StreamMesh.Models;
using StreamMesh.Services;

namespace StreamMesh.Windows
{
    public partial class PreviewPlayerWindow : Window
    {
        private Channel _channel;
        private LibVLC _libVLC;
        private MediaPlayer _mediaPlayer;
        private YoutubeService _youtubeService;
        private AceStreamService _aceStreamService;

        public PreviewPlayerWindow(Channel channel)
        {
            InitializeComponent();
            _channel = channel;
            _youtubeService = new YoutubeService();
            _aceStreamService = new AceStreamService();

            ChannelNameTxt.Text = $"Önizleme: {_channel.Name}";
            
            this.Loaded += PreviewPlayerWindow_Loaded;
            this.Closed += PreviewPlayerWindow_Closed;
        }

        private async void PreviewPlayerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // LibVLC Başlatılıyor
                Core.Initialize();
                var vlcArgs = new string[] 
                {
                    "--http-user-agent=Mozilla/5.0",
                    "--network-caching=1500",
                    "--live-caching=1500",
                    "--no-osd"
                };
                _libVLC = new LibVLC(vlcArgs);
                _mediaPlayer = new MediaPlayer(_libVLC);
                
                VlcVideoView.MediaPlayer = _mediaPlayer;

                _mediaPlayer.Playing += MediaPlayer_Playing;
                _mediaPlayer.EncounteredError += MediaPlayer_EncounteredError;

                await PlayChannelAsync();
            }
            catch (Exception ex)
            {
                StatusTxt.Text = "VLC Başlatılamadı!";
                InfoTxt.Text = $"Hata: {ex.Message}";
                LogService.LogError("Önizleme başlatılırken hata", ex);
            }
        }

        private async Task PlayChannelAsync()
        {
            string url = (_channel.Url ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (string.IsNullOrEmpty(url))
            {
                StatusTxt.Text = "Hata: Yayın adresi boş!";
                InfoTxt.Text = "Kanalda geçerli bir yayın adresi bulunamadı.";
                return;
            }

            StatusTxt.Text = "Yayın çözülüyor...";
            InfoTxt.Text = $"Kaynak: {url}";

            try
            {
                bool isYoutube = url.Contains("youtube.com") || url.Contains("youtu.be");
                bool isAceStream = url.StartsWith("acestream://");

                if (_channel.SourceType == "YOUTUBE" || isYoutube)
                {
                    StatusTxt.Text = "YouTube adresi çözümleniyor...";
                    var directUrl = await _youtubeService.GetSingleMuxedStreamUrlAsync(url);
                    if (!string.IsNullOrEmpty(directUrl))
                    {
                        url = directUrl;
                    }
                    else
                    {
                        throw new Exception("YouTube stream çözümlenemedi.");
                    }
                }
                else if (_channel.SourceType == "ACESTREAM" || isAceStream)
                {
                    StatusTxt.Text = "AceStream motoru hazırlanıyor...";
                    await _aceStreamService.StartEngineAsync();
                    url = _aceStreamService.GetHttpUrl(url);
                }

                if (url.StartsWith("acestream://"))
                {
                    throw new Exception("AceStream motoru çalışmıyor veya kurulu değil!");
                }

                using (var media = new Media(_libVLC, new Uri(url)))
                {
                    _mediaPlayer.Play(media);
                }
                StatusTxt.Text = "Bağlanıyor...";
            }
            catch (Exception ex)
            {
                StatusTxt.Text = "Yayın Başlatılamadı!";
                InfoTxt.Text = $"Hata: {ex.Message}";
            }
        }

        private void MediaPlayer_Playing(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                StatusTxt.Visibility = Visibility.Collapsed;
                InfoTxt.Text = $"Canlı yayın oynatılıyor. URL: {(_channel.Url ?? "").Split(',')[0]}";

                // Genişlik ve yüksekliği okuyalım
                try
                {
                    var tracks = _mediaPlayer.Media?.Tracks;
                    if (tracks != null)
                    {
                        var videoTrack = tracks.FirstOrDefault(t => t.TrackType == TrackType.Video);
                        if (videoTrack != null)
                        {
                            ResolutionTxt.Text = $"{videoTrack.Data.Video.Width}x{videoTrack.Data.Video.Height}";
                        }
                    }
                }
                catch { }
            });
        }

        private void MediaPlayer_EncounteredError(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                StatusTxt.Text = "Yayın açılırken bir hata oluştu!";
                StatusTxt.Visibility = Visibility.Visible;
                InfoTxt.Text = "Medya yürütme hatası alındı (VLC).";
            });
        }

        private void PreviewPlayerWindow_Closed(object sender, EventArgs e)
        {
            try
            {
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.Stop();
                    _mediaPlayer.Dispose();
                    _mediaPlayer = null;
                }
                if (_libVLC != null)
                {
                    _libVLC.Dispose();
                    _libVLC = null;
                }
            }
            catch { }
        }

        private void Ufak_Click(object sender, RoutedEventArgs e)
        {
            SetWindowSize(340, 260);
            UpdateButtonStyles(sender as Button);
        }

        private void Orta_Click(object sender, RoutedEventArgs e)
        {
            SetWindowSize(660, 440);
            UpdateButtonStyles(sender as Button);
        }

        private void Buyuk_Click(object sender, RoutedEventArgs e)
        {
            SetWindowSize(980, 620);
            UpdateButtonStyles(sender as Button);
        }

        private void SetWindowSize(double width, double height)
        {
            this.Width = width;
            this.Height = height;
            // Ekranda ortala
            this.Left = (SystemParameters.PrimaryScreenWidth - width) / 2;
            this.Top = (SystemParameters.PrimaryScreenHeight - height) / 2;
        }

        private void UpdateButtonStyles(Button selectedButton)
        {
            if (selectedButton == null) return;

            // Diğer buton stillerini sıfırla
            var parent = selectedButton.Parent as StackPanel;
            if (parent != null)
            {
                foreach (var child in parent.Children)
                {
                    if (child is Button btn)
                    {
                        if (btn == selectedButton)
                        {
                            btn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0xbd, 0xf8)); // mavi tonu
                            btn.FontWeight = FontWeights.Bold;
                        }
                        else
                        {
                            btn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x41, 0x55)); // koyu slate
                            btn.FontWeight = FontWeights.Normal;
                        }
                    }
                }
            }
        }
    }
}
