using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StreamMesh.Services;
using StreamMesh.Services.Auth;

namespace StreamMesh.Views
{
    public partial class SettingsView : UserControl
    {
        private DatabaseService _databaseService;
        private M3uService _m3uService;
        private ServerService _serverService;
        private AceStreamService _aceStreamService;
        private bool _isServerRunning = false;

        public class EpgSourceInfo
        {
            public string Url { get; set; }
            public string Stats { get; set; }
        }

        public class M3uSourceInfo
        {
            public string Url { get; set; }
            public string Stats { get; set; }
        }

        public SettingsView()
        {
            InitializeComponent();
            _databaseService = new DatabaseService();
            _m3uService = new M3uService();
            _serverService = ServerService.Instance;
            _aceStreamService = new AceStreamService();
            
            _serverService.OnStatusChanged += ServerService_OnStatusChanged;

            // Güncel durumu butona yansıt
            if (_serverService.IsRunning)
            {
                ServerService_OnStatusChanged(true, _serverService.LocalIp, _serverService.Port.ToString());
            }

            LoadM3uSources();
            LoadEpgSources();
            
            SetCurrentLanguageInCombo();
            UpdateComponentStatusUI();

            // Auto Update link listesini yukle
            LoadAutoUpdateLinks();

            // Wire up TunnelService events
            TunnelService.Instance.OnStatusMessage += (msg) => 
            {
                Dispatcher.Invoke(() => 
                {
                    TunnelLogsBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
                    TunnelLogsBox.ScrollToEnd();
                    
                    // Update NAT status display
                    NatTypeStatusText.Text = TunnelService.Instance.CurrentNatType switch
                    {
                        NatType.ConeNAT => "Cone NAT (Hole Punching Destekleniyor)",
                        NatType.SymmetricNAT => "Simetrik NAT (Hole Punching İmkansız)",
                        _ => "Bilinmiyor (Analiz edilmedi)"
                    };
                    
                    // Update connection mode display
                    ConnectionModeText.Text = TunnelService.Instance.ActiveMode switch
                    {
                        ConnectionMode.StunP2P => "STUN P2P",
                        _ => "Doğrudan Yerel IP"
                    };
                });
            };
        }

        private async void DetectNatBtn_Click(object sender, RoutedEventArgs e)
        {
            DetectNatBtn.IsEnabled = false;
            TunnelLogsBox.AppendText($"[{DateTime.Now:HH:mm:ss}] NAT analizi başlatıldı...{Environment.NewLine}");
            var nat = await TunnelService.Instance.DetectNatTypeAsync();
            DetectNatBtn.IsEnabled = true;
        }

        private void UpdateComponentStatusUI()
        {
            Dispatcher.Invoke(() => 
            {
                bool hasFFmpeg = InventoryService.IsFFmpegInstalled();
                bool hasAce = InventoryService.IsAceStreamInstalled();

                if (hasFFmpeg)
                {
                    FFmpegStatusIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)); // Green
                    FFmpegStatusText.Text = "FFmpeg Yüklü";
                }
                else
                {
                    FFmpegStatusIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // Red
                    FFmpegStatusText.Text = "FFmpeg Yüklü Değil";
                }

                if (hasAce)
                {
                    AceStreamStatusIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)); // Green
                    AceStreamStatusText.Text = "AceStream Yüklü";
                }
                else
                {
                    AceStreamStatusIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // Red
                    AceStreamStatusText.Text = "AceStream Yüklü Değil";
                }
            });
        }

        private void SetCurrentLanguageInCombo()
        {
            try
            {
                string currentLang = LocalizationManager.Instance.CurrentLanguage;
                foreach (ComboBoxItem item in LanguageCombo.Items)
                {
                    if (item.Tag?.ToString() == currentLang)
                    {
                        LanguageCombo.SelectedItem = item;
                        break;
                    }
                }

                var profile = UserService.GetProfile();
                if (profile != null)
                {
                    CountryCombo.ItemsSource = LocalizationManager.SystemCultures;
                    Lang1Combo.ItemsSource = LocalizationManager.SystemLanguagesWithNone;
                    Lang2Combo.ItemsSource = LocalizationManager.SystemLanguagesWithNone;

                    var defaultCountry = LocalizationManager.SystemCultures.FirstOrDefault(c => c.Contains("Türkçe")) ?? LocalizationManager.SystemCultures.FirstOrDefault();
                    CountryCombo.SelectedItem = string.IsNullOrEmpty(profile.Country) ? defaultCountry : profile.Country;
                    
                    if (profile.Languages != null)
                    {
                        string lang1 = profile.Languages.Count > 1 ? profile.Languages[1] : "Hiçbiri";
                        string lang2 = profile.Languages.Count > 2 ? profile.Languages[2] : "Hiçbiri";

                        SelectLanguageInCombo(Lang1Combo, lang1);
                        SelectLanguageInCombo(Lang2Combo, lang2);
                    }
                }
            }
            catch { }
        }

        private void SelectLanguageInCombo(ComboBox combo, string target)
        {
            if (string.IsNullOrEmpty(target) || target == "Hiçbiri")
            {
                combo.SelectedItem = "Hiçbiri";
                return;
            }

            // Önce birebir eşleşme ara
            foreach (var item in combo.Items)
            {
                if (item?.ToString() == target)
                {
                    combo.SelectedItem = item;
                    return;
                }
            }

            // Normalleştirilmiş eşleşme ara
            string targetNorm = StreamMesh.Models.Channel.NormalizeLanguage(target).ToLower(new System.Globalization.CultureInfo("tr-TR"));
            foreach (var item in combo.Items)
            {
                string itemStr = item?.ToString();
                if (!string.IsNullOrEmpty(itemStr))
                {
                    string itemNorm = StreamMesh.Models.Channel.NormalizeLanguage(itemStr).ToLower(new System.Globalization.CultureInfo("tr-TR"));
                    if (itemNorm == targetNorm)
                    {
                        combo.SelectedItem = item;
                        return;
                    }
                }
            }

            // Bulunamadıysa dinamik olarak ekle ve seç
            var list = combo.ItemsSource as System.Collections.Generic.List<string>;
            if (list != null)
            {
                var newList = new System.Collections.Generic.List<string>(list);
                newList.Add(target);
                combo.ItemsSource = newList;
                combo.SelectedItem = target;
            }
            else
            {
                combo.SelectedItem = "Hiçbiri";
            }
        }

        private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!this.IsLoaded) return; // Ignore events during initialization
            var profile = UserService.GetProfile();
            if (profile != null)
            {
                var defaultCountry = LocalizationManager.SystemCultures.FirstOrDefault(c => c.Contains("Türkçe")) ?? LocalizationManager.SystemCultures.FirstOrDefault();
                string country = CountryCombo.SelectedItem as string ?? defaultCountry;
                string lang1 = Lang1Combo.SelectedItem as string ?? "Hiçbiri";
                string lang2 = Lang2Combo.SelectedItem as string ?? "Hiçbiri";

                profile.Country = country;
                
                var langs = new System.Collections.Generic.List<string> { country };
                if (lang1 != "Hiçbiri" && !string.IsNullOrEmpty(lang1)) langs.Add(lang1);
                if (lang2 != "Hiçbiri" && !string.IsNullOrEmpty(lang2)) langs.Add(lang2);
                
                profile.Languages = langs.Distinct().ToList();

                UserService.SaveProfile(profile);
            }
        }

        private async void DownloadAllComponentsBtn_Click(object sender, RoutedEventArgs e)
        {
            DownloadAllComponentsBtn.IsEnabled = false;
            DownloadStatusText.Text = "Kontrol ediliyor...";

            string githubAceStreamUrl = "https://github.com/bilo1975tr/sm/releases/download/v1.0/AceStream.zip"; 
            
            await Task.Run(async () =>
            {
                await InventoryService.DownloadComponentsManuallyAsync(githubAceStreamUrl, (message) => 
                {
                    Dispatcher.Invoke(() => DownloadStatusText.Text = message);
                });
            });

            UpdateComponentStatusUI();
            Dispatcher.Invoke(() => DownloadAllComponentsBtn.IsEnabled = true);
        }

        private async void DownloadFFmpegBtn_Click(object sender, RoutedEventArgs e)
        {
            DownloadFFmpegBtn.IsEnabled = false;
            DownloadStatusText.Text = "FFmpeg kontrol ediliyor...";

            await Task.Run(async () =>
            {
                await InventoryService.DownloadFFmpegManuallyAsync((message) => 
                {
                    Dispatcher.Invoke(() => DownloadStatusText.Text = message);
                });
            });

            UpdateComponentStatusUI();
            Dispatcher.Invoke(() => DownloadFFmpegBtn.IsEnabled = true);
        }

        private async void DownloadAceStreamBtn_Click(object sender, RoutedEventArgs e)
        {
            DownloadAceStreamBtn.IsEnabled = false;
            DownloadStatusText.Text = "AceStream kontrol ediliyor...";

            string githubAceStreamUrl = "https://github.com/bilo1975tr/sm/releases/download/v1.0/AceStream.zip"; 

            await Task.Run(async () =>
            {
                await InventoryService.DownloadAceStreamManuallyAsync(githubAceStreamUrl, (message) => 
                {
                    Dispatcher.Invoke(() => DownloadStatusText.Text = message);
                });
            });

            UpdateComponentStatusUI();
            Dispatcher.Invoke(() => DownloadAceStreamBtn.IsEnabled = true);
        }

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageCombo.SelectedItem is ComboBoxItem selectedItem)
            {
                string lang = selectedItem.Tag?.ToString();
                if (!string.IsNullOrEmpty(lang))
                {
                    LocalizationManager.Instance.LoadTranslations(lang);
                    var userProfile = UserService.GetProfile();
                    if (userProfile != null)
                    {
                        userProfile.AppLanguage = lang;
                        UserService.SaveProfile(userProfile);
                    }
                }
            }
        }

        private void LoadM3uSources()
        {
            try
            {
                var sources = _databaseService.GetM3uSources();
                var m3uItems = new System.Collections.Generic.List<M3uSourceInfo>();
                foreach (var url in sources)
                {
                    var (total, verified) = _databaseService.GetChannelCountsBySource(url);
                    m3uItems.Add(new M3uSourceInfo
                    {
                        Url = url,
                        Stats = $"Kanal Sayısı: {total} | Onaylı: {verified}"
                    });
                }
                M3uSourcesList.ItemsSource = m3uItems;
            }
            catch (Exception ex)
            {
                LogService.LogError("LoadM3uSources error", ex);
            }
        }

        private void LoadEpgSources()
        {
            try
            {
                var sources = _databaseService.GetEpgSources();
                var epgItems = new System.Collections.Generic.List<EpgSourceInfo>();
                foreach (var url in sources)
                {
                    int channels = _databaseService.GetEpgSourceChannelCount(url);
                    int programs = _databaseService.GetEpgSourceProgramCount(url);
                    string lastUpdated = _databaseService.GetSetting($"epg_updated_{url}", "Bilinmiyor");
                    epgItems.Add(new EpgSourceInfo
                    {
                        Url = url,
                        Stats = $"Durum: Çalışıyor | Eşleşen Kanal: {channels} | Toplam Program: {programs} | Son Güncelleme: {lastUpdated}"
                    });
                }
                EpgSourcesList.ItemsSource = epgItems;
            }
            catch (Exception ex)
            {
                LogService.LogError("LoadEpgSources error", ex);
            }
        }

        private void EditSource_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = M3uSourcesList.SelectedItem as M3uSourceInfo;
            string selected = selectedItem?.Url;
            if (string.IsNullOrEmpty(selected))
            {
                MessageBox.Show("Lütfen düzenlemek için bir kaynak seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var editorWin = new StreamMesh.Windows.SourceEditorWindow(selected);
            editorWin.ShowDialog();
        }

        private async void ReloadSource_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = (sender as FrameworkElement)?.DataContext as M3uSourceInfo;
            if (selectedItem == null) 
            {
                selectedItem = M3uSourcesList.SelectedItem as M3uSourceInfo;
            }

            if (selectedItem == null || string.IsNullOrEmpty(selectedItem.Url)) return;

            string url = selectedItem.Url;
            if (url == "Manuel Eklenen Linkler") return;

            M3uStatusText.Text = $"{url} yeniden yükleniyor...";
            try
            {
                System.Collections.Generic.List<StreamMesh.Models.Channel> channels;
                if (url.EndsWith(".dpl", StringComparison.OrdinalIgnoreCase))
                {
                    var dplService = new DplService();
                    channels = await dplService.ParseDplAsync(url, "Otomatik");
                }
                else if (url.Contains("youtube.com") || url.Contains("youtu.be"))
                {
                    var ytService = new StreamMesh.Services.YoutubeService();
                    channels = await ytService.GetChannelsFromUrlAsync(url);
                }
                else
                {
                    channels = await _m3uService.ParseM3uAsync(url, "Otomatik");
                }

                if (channels.Count > 0)
                {
                    M3uStatusText.Text = "Kanallar veritabanına işleniyor...";
                    string resultStr = await Task.Run(() => _databaseService.SaveChannels(channels, url));
                    LoadM3uSources();
                    M3uStatusText.Text = resultStr;
                    LogService.Log($"{url} başarıyla güncellendi.");
                }
                else
                {
                    M3uStatusText.Text = "Uyarı: Kaynakta kanal bulunamadı.";
                }
            }
            catch (Exception ex)
            {
                M3uStatusText.Text = $"Hata: {ex.Message}";
            }
        }

        private void RemoveM3uSourceBtn_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = M3uSourcesList.SelectedItem as M3uSourceInfo;
            string selected = selectedItem?.Url;
            if (string.IsNullOrEmpty(selected))
            {
                MessageBox.Show("Lütfen silmek için bir kaynak seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show($"Bu kaynağı ve bağlı tüm kanalları silmek istediğinize emin misiniz?\n{selected}", "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _databaseService.RemoveM3uSource(selected);
                LoadM3uSources();
                MessageBox.Show("Kaynak ve kanalları silindi.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ServerControlBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isServerRunning)
            {
                _serverService.StopServer();
            }
            else
            {
                _serverService.StartServer();
            }
        }

        private void ServerService_OnStatusChanged(bool isRunning, string ip, string port)
        {
            // Thread safety for UI update
            Dispatcher.Invoke(() =>
            {
                _isServerRunning = isRunning;
                if (isRunning)
                {
                    ServerControlBtn.Content = "Sunucuyu Durdur";
                    ServerStatusText.Text = "Durum: Çalışıyor";
                    ServerStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)); // Green
                    ServerUrlBox.Text = $"http://{ip}:{port}/playlist.m3u";
                    WebPlayerUrlBox.Text = $"http://{ip}:{port}/";
                }
                else
                {
                    ServerControlBtn.Content = "Sunucuyu Başlat";
                    ServerStatusText.Text = "Durum: Kapalı";
                    ServerStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(252, 165, 165)); // Red
                    ServerUrlBox.Text = "";
                    WebPlayerUrlBox.Text = "";
                }
            });
        }

        private void BrowseM3uBtn_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "Oynatma Listeleri (*.m3u;*.m3u8;*.dpl)|*.m3u;*.m3u8;*.dpl|Tüm Dosyalar (*.*)|*.*";
            if (openFileDialog.ShowDialog() == true)
            {
                M3uUrlTextBox.Text = openFileDialog.FileName;
            }
        }

        private void BrowseEpgBtn_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "XMLTV Dosyaları (*.xml;*.gz)|*.xml;*.gz|Tüm Dosyalar (*.*)|*.*";
            if (openFileDialog.ShowDialog() == true)
            {
                EpgUrlTextBox.Text = openFileDialog.FileName;
            }
        }

        private async void AddM3uButton_Click(object sender, RoutedEventArgs e)
        {
            string url = M3uUrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;

            if (AutoUpdateService.IsUrlInAutoUpdate(url))
            {
                MessageBox.Show("Bu link otomatik güncelleme listesinde (auto_update.json) zaten mevcut olduğu için manuel olarak eklenemez!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                M3uStatusText.Text = "Bu kaynak otomatik güncelleme listesinde tanımlıdır.";
                return;
            }

            // Seçili kategoriyi al
            string categoryHint = (DefaultCategoryCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Otomatik";

            AddM3uButton.IsEnabled = false;
            M3uStatusText.Text = "Kaynak analiz ediliyor...";
            LogService.Log($"Smart Import started for URL: {url} (Category Hint: {categoryHint})");

            try
            {
                System.Collections.Generic.List<StreamMesh.Models.Channel> channels = new System.Collections.Generic.List<StreamMesh.Models.Channel>();

                // 1. YouTube Kontrolü (Servis zaten video vs playlist ayrımını kendi yapıyor)
                if (url.Contains("youtube.com") || url.Contains("youtu.be"))
                {
                    var ytService = new StreamMesh.Services.YoutubeService();
                    channels = await ytService.GetChannelsFromUrlAsync(url);
                    
                    if (channels.Count > 0)
                    {
                        var dialog = new StreamMesh.Windows.YoutubeMetadataWindow();
                        if (dialog.ShowDialog() == true && dialog.IsConfirmed)
                        {
                            int epCounter = dialog.StartEpisode;
                            foreach (var ch in channels)
                            {
                                ch.GroupTitle = dialog.GroupTitle;
                                ch.Language = dialog.StreamLanguage;
                                ch.Category = dialog.SelectedType == "Movie" ? "Film" : (dialog.SelectedType == "Series" ? "Dizi" : "TV");

                                if (dialog.SelectedType == "Series" && dialog.AutoNumbering)
                                {
                                    string suffix = $"S{dialog.StartSeason:D2}E{epCounter:D2}";
                                    ch.Name = $"{dialog.GroupTitle} - {suffix} ({ch.Name})";
                                    epCounter++;
                                }
                            }
                        }
                        else
                        {
                            M3uStatusText.Text = "Kayıt işlemi kullanıcı tarafından iptal edildi.";
                            AddM3uButton.IsEnabled = true;
                            M3uUrlTextBox.Text = "";
                            return;
                        }
                    }
                }
                // 2. AceStream Kontrolü (Direct Hash or URL)
                else if (url.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase) || 
                         (url.Length == 40 && System.Text.RegularExpressions.Regex.IsMatch(url, @"^[a-fA-F0-9]+$")))
                {
                    string aceUrl = url.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase) ? url : "acestream://" + url;
                    var ch = new StreamMesh.Models.Channel 
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Name = "AceStream Yayın " + DateTime.Now.ToString("HH:mm"),
                        Url = aceUrl,
                        Category = categoryHint == "Otomatik" ? "TV" : categoryHint,
                        SourceType = "ACESTREAM",
                        GroupTitle = "Manuel Eklenenler",
                        Language = "Bilinmiyor",
                        PlaylistUrl = "DIRECT_LINK_" + Guid.NewGuid().ToString("N").Substring(0, 4)
                    };

                    // Manuel sorma penceresi aç
                    var editWin = new StreamMesh.Windows.EditChannelWindow(ch);
                    if (editWin.ShowDialog() == true)
                    {
                        channels.Add(ch);
                    }
                    else
                    {
                        M3uStatusText.Text = "Ekleme iptal edildi.";
                        AddM3uButton.IsEnabled = true;
                        return;
                    }
                }
                // 3. DPL Playlist Kontrolü
                else if (url.EndsWith(".dpl", StringComparison.OrdinalIgnoreCase))
                {
                    var dplService = new DplService();
                    channels = await dplService.ParseDplAsync(url, categoryHint);
                }
                // 4. Doğrudan Video Linki veya Playlist (m3u8, mp4, etc.)
                else if (url.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) || 
                         url.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                         url.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
                {
                    // Önce bir M3U playlist olarak ayrıştırmayı dene
                    channels = await _m3uService.ParseM3uAsync(url, categoryHint);
                    
                    // Eğer kanal çıkmadıysa ve bir URL ise (dosya değilse), bunu doğrudan bir yayın linki sayalım
                    if (channels.Count == 0 && (url.StartsWith("http") || url.StartsWith("https")))
                    {
                        var ch = new StreamMesh.Models.Channel 
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Name = "Manuel Yayın " + Path.GetFileName(url),
                            Url = url,
                            Category = categoryHint == "Otomatik" ? "TV" : categoryHint,
                            SourceType = "M3U",
                            GroupTitle = "Manuel Eklenenler",
                            Language = "Bilinmiyor",
                            PlaylistUrl = "DIRECT_LINK_" + Guid.NewGuid().ToString("N").Substring(0, 4)
                        };

                        // Manuel sorma penceresi aç
                        var editWin = new StreamMesh.Windows.EditChannelWindow(ch);
                        if (editWin.ShowDialog() == true)
                        {
                            channels.Add(ch);
                        }
                        else
                        {
                            M3uStatusText.Text = "Ekleme iptal edildi.";
                            AddM3uButton.IsEnabled = true;
                            return;
                        }
                    }
                }
                // 5. YouTube Tekil Video Kontrolü (Smart Import kapsamında ek kanal sorma)
                else if ((url.Contains("youtube.com/watch") || url.Contains("youtu.be/")) && !url.Contains("list="))
                {
                    var ytService = new StreamMesh.Services.YoutubeService();
                    var ytChannels = await ytService.GetChannelsFromUrlAsync(url);
                    if (ytChannels.Count == 1)
                    {
                        var ch = ytChannels[0];
                        var editWin = new StreamMesh.Windows.EditChannelWindow(ch);
                        if (editWin.ShowDialog() == true)
                        {
                            channels.Add(ch);
                        }
                        else
                        {
                            M3uStatusText.Text = "Ekleme iptal edildi.";
                            AddM3uButton.IsEnabled = true;
                            return;
                        }
                    }
                    else
                    {
                        channels.AddRange(ytChannels);
                    }
                }
                // 5. Varsayılan: M3U Playlist olarak dene
                else
                {
                    channels = await _m3uService.ParseM3uAsync(url, categoryHint);
                }

                // SONUÇ KAYIT
                if (channels.Count > 0)
                {
                    M3uStatusText.Text = "Kanallar veritabanına işleniyor...";
                    string resultStr = await Task.Run(() => _databaseService.SaveChannels(channels, url));
                    
                    // Eğer direct link değilse kaynağı listeye ekle (direct linkler her seferinde farklı Source ID alabilir)
                    if (!channels.Any(c => c.PlaylistUrl.StartsWith("DIRECT_LINK_")))
                    {
                        _databaseService.AddM3uSource(url);
                    }
                    else
                    {
                        // Direct link ise playlist_url olarak bir tane sanal "Manuel Eklenenler" girdisi ekleyebiliriz
                        _databaseService.AddM3uSource("Manuel Eklenen Linkler");
                    }
                    
                    LoadM3uSources();
                    M3uStatusText.Text = resultStr; 
                    LogService.Log($"{channels.Count} kanalları başarıyla akıllı içe aktarma ile işlendi.");
                }
                else
                {
                    M3uStatusText.Text = "Uyarı: Listede geçerli kanal bulunamadı veya dosya boş.";
                    LogService.Log("No channels found in source.", "WARN");
                }
            }
            catch (Exception ex)
            {
                M3uStatusText.Text = $"Hata: {ex.Message}";
                LogService.LogError("Smart Import failed", ex);
            }

            AddM3uButton.IsEnabled = true;
            M3uUrlTextBox.Text = "";
        }

        private async void AddEpgButton_Click(object sender, RoutedEventArgs e)
        {
            string url = EpgUrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;

            if (AutoUpdateService.IsUrlInAutoUpdate(url))
            {
                MessageBox.Show("Bu link otomatik güncelleme listesinde (auto_update.json) zaten mevcut olduğu için manuel olarak eklenemez!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                EpgStatusText.Text = "Bu kaynak otomatik güncelleme listesinde tanımlıdır.";
                return;
            }

            AddEpgButton.IsEnabled = false;
            EpgStatusText.Text = "EPG indiriliyor ve ayrıştırılıyor (Büyük dosyalarda zaman alabilir)...";

            try
            {
                var epgService = new EpgService();
                bool result = await epgService.ParseEpgUrlAsync(url);
                if (result)
                {
                    _databaseService.SetSetting($"epg_updated_{url}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                    EpgStatusText.Text = "EPG başarıyla yüklendi ve veri tabanına kaydedildi.";
                    LoadEpgSources();
                }
                else
                {
                    EpgStatusText.Text = "EPG yüklenirken bir hata oluştu veya dosya boş.";
                }
            }
            catch (Exception ex)
            {
                EpgStatusText.Text = $"Hata: {ex.Message}";
            }

            AddEpgButton.IsEnabled = true;
        }

        private async void ReloadEpgSource_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = (sender as FrameworkElement)?.DataContext as EpgSourceInfo;
            if (selectedItem == null) 
            {
                selectedItem = EpgSourcesList.SelectedItem as EpgSourceInfo;
            }

            if (selectedItem == null || string.IsNullOrEmpty(selectedItem.Url)) return;

            string url = selectedItem.Url;
            EpgStatusText.Text = $"{url} yeniden yükleniyor...";
            try
            {
                var epgService = new EpgService();
                bool result = await epgService.ParseEpgUrlAsync(url);
                if (result)
                {
                    _databaseService.SetSetting($"epg_updated_{url}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                    EpgStatusText.Text = "EPG başarıyla güncellendi.";
                    LoadEpgSources();
                }
                else
                {
                    EpgStatusText.Text = "EPG yüklenirken bir hata oluştu veya dosya boş.";
                }
            }
            catch (Exception ex)
            {
                EpgStatusText.Text = $"Hata: {ex.Message}";
            }
        }

        private void RemoveEpgSourceBtn_Click(object sender, RoutedEventArgs e)
        {
            var selected = EpgSourcesList.SelectedItem as EpgSourceInfo;
            if (selected == null)
            {
                MessageBox.Show("Lütfen silmek için bir kaynak seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show($"Bu EPG kaynağını ve içerdiği tüm program verilerini silmek istediğinize emin misiniz?\n{selected.Url}", "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _databaseService.RemoveEpgSource(selected.Url);
                LoadEpgSources();
                MessageBox.Show("EPG kaynağı ve verileri silindi.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ClearEpgsButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Tüm EPG verileri ve EPG kaynakları silinecek ve bu işlem geri alınamaz. Emin misiniz?", "Uyarı", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                var sources = _databaseService.GetEpgSources();
                foreach (var url in sources)
                {
                    _databaseService.RemoveEpgSource(url);
                }
                _databaseService.ClearEpg();
                LoadEpgSources();
                MessageBox.Show("Tüm EPG listeleri ve verileri başarıyla silindi.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ClearChannelsButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Tüm kanallar ve M3U kaynakları silinecek ve bu işlem geri alınamaz. Emin misiniz?", "Uyarı", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                _databaseService.ClearAllChannels();
                LoadM3uSources(); // Will refresh the list
                MessageBox.Show("Tüm kanallar ve kaynaklar başarıyla silindi.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void OptimizeLibraryBtn_Click(object sender, RoutedEventArgs e)
        {
            OptimizeLibraryBtn.IsEnabled = false;
            M3uStatusText.Text = "Kütüphane optimize ediliyor, lütfen bekleyin...";
            
            await System.Threading.Tasks.Task.Run(() => 
            {
                _databaseService.OptimizeAllChannels();
            });

            LoadM3uSources();
            M3uStatusText.Text = "Kütüphane optimizasyonu ve URL temizliği tamamlandı.";
            OptimizeLibraryBtn.IsEnabled = true;
            
            MessageBox.Show("Kütüphane tarandı, çift URL'ler birleştirildi ve liste temizlendi.", "Optimizasyon Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private System.Threading.CancellationTokenSource _streamCheckCts;

        private async void CheckAllStreamsBtn_Click(object sender, RoutedEventArgs e)
        {
            await RunStreamCheckAsync(false);
        }

        private async void CheckUnverifiedStreamsBtn_Click(object sender, RoutedEventArgs e)
        {
            await RunStreamCheckAsync(true);
        }

        private async System.Threading.Tasks.Task RunStreamCheckAsync(bool unapprovedOnly)
        {
            if (_streamCheckCts != null)
            {
                _streamCheckCts.Cancel();
                _streamCheckCts = null;
                StreamCheckStatusText.Text = "Kontrol iptal edildi.";
                CheckAllStreamsBtn.IsEnabled = true;
                CheckUnverifiedStreamsBtn.IsEnabled = true;
                return;
            }

            var channels = await System.Threading.Tasks.Task.Run(() => _databaseService.GetAllChannels());
            
            // Eğer onaysız ise, sadece son 24 saat içinde eklenenleri filtresine takılabilir.
            // Şimdilik test amaçlı HEPSİNİ alıyoruz veya 'unapprovedOnly' mantığıyla bölüyoruz.
            // Asıl DB'de onay durumu ("Verified") tutmak en doğrusudur.
            
            if (channels.Count == 0)
            {
                StreamCheckStatusText.Text = "Kayıtlı kanal bulunamadı.";
                return;
            }

            CheckAllStreamsBtn.Content = unapprovedOnly ? "Tüm Kanalları Kontrol Et" : "İptal Et";
            CheckUnverifiedStreamsBtn.Content = unapprovedOnly ? "İptal Et" : "Onaysız Yayınları Kontrol Et";
            CheckAllStreamsBtn.IsEnabled = !unapprovedOnly;
            CheckUnverifiedStreamsBtn.IsEnabled = unapprovedOnly;
            
            _streamCheckCts = new System.Threading.CancellationTokenSource();
            var checker = new StreamCheckerService();

            StreamCheckStatusText.Text = $"Kontrol başlatıldı... Toplam Kanal: {channels.Count}";

            try
            {
                var result = await checker.CheckChannelsAsync(channels, unapprovedOnly, _streamCheckCts.Token, (stats) => 
                {
                    Dispatcher.Invoke(() => 
                    {
                        StreamCheckStatusText.Text = $"İşlenen: {stats.Processed} / {stats.Total}\n\n" +
                                                     $"AceStream: {stats.AceStreamWorking}/{stats.AceStreamTotal} doğrulandı\n" +
                                                     $"YouTube: {stats.YouTubeWorking}/{stats.YouTubeTotal} doğrulandı\n" +
                                                     $"M3U8: {stats.M3u8Working}/{stats.M3u8Total} doğrulandı";
                    });
                });

                // Post process: delete broken channels
                int deletedCount = 0;
                if (result.BrokenChannelIds != null)
                {
                    foreach (var id in result.BrokenChannelIds)
                    {
                        _databaseService.DeleteChannel(id);
                        deletedCount++;
                    }
                }

                int totalWorking = result.AceStreamWorking + result.YouTubeWorking + result.M3u8Working;
                int totalBroken = (result.AceStreamTotal + result.YouTubeTotal + result.M3u8Total) - totalWorking;

                StreamCheckStatusText.Text = $"Kontrol tamamlandı. {totalWorking} çalışan, {totalBroken} çalışmayan kanal bulundu. {deletedCount} kanal silindi.\n\n" +
                                             $"AceStream: {result.AceStreamWorking}/{result.AceStreamTotal} doğrulandı\n" +
                                             $"YouTube: {result.YouTubeWorking}/{result.YouTubeTotal} doğrulandı\n" +
                                             $"M3U8: {result.M3u8Working}/{result.M3u8Total} doğrulandı";
            }
            catch (System.OperationCanceledException)
            {
                StreamCheckStatusText.Text = "Kontrol iptal edildi.";
            }
            catch (Exception ex)
            {
                StreamCheckStatusText.Text = $"Hata: {ex.Message}";
            }
            finally
            {
                _streamCheckCts = null;
                CheckAllStreamsBtn.Content = "Tüm Kanalları Kontrol Et";
                CheckUnverifiedStreamsBtn.Content = "Onaysız Yayınları Kontrol Et";
                CheckAllStreamsBtn.IsEnabled = true;
                CheckUnverifiedStreamsBtn.IsEnabled = true;
            }
        }

        private async void AutoMatchLogosBtn_Click(object sender, RoutedEventArgs e)
        {
            AutoMatchLogosBtn.IsEnabled = false;
            LogoMatchStatusText.Text = "Eksik logolar taranıyor...";

            try
            {
                int matched = await StreamMesh.Services.LogoSearchService.AutoMatchAllMissingLogosAsync((msg) =>
                {
                    Dispatcher.Invoke(() => LogoMatchStatusText.Text = msg);
                });

                MessageBox.Show($"Otomatik logo eşleştirme tamamlandı!\nToplam {matched} kanala yeni logo eklendi.", "İşlem Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogService.LogError("AutoMatchLogos error", ex);
                LogoMatchStatusText.Text = "Hata oluştu";
                MessageBox.Show("İşlem sırasında beklenmeyen bir hata oluştu. Lütfen tekrar deneyiniz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AutoMatchLogosBtn.IsEnabled = true;
            }
        }

        private void OpenEpgMatchWindowBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var parentWindow = Window.GetWindow(this);
                var matchWindow = new EpgMatchWindow();
                matchWindow.Owner = parentWindow;
                matchWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                LogService.LogError("OpenEpgMatchWindow error", ex);
                MessageBox.Show("İşlem sırasında beklenmeyen bir hata oluştu. Lütfen tekrar deneyiniz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void LoadAutoUpdateLinks()
        {
            try
            {
                var config = await AutoUpdateService.FetchConfigAsync();
                var items = new System.Collections.Generic.List<object>();
                
                if (config != null)
                {
                    foreach (var url in config.Tv) items.Add(new { Category = "TV", Url = url });
                    foreach (var url in config.Film) items.Add(new { Category = "Film", Url = url });
                    foreach (var url in config.Dizi) items.Add(new { Category = "Dizi", Url = url });
                    foreach (var url in config.Epg) items.Add(new { Category = "EPG", Url = url });
                }

                Dispatcher.Invoke(() => 
                {
                    AutoUpdateLinksListBox.ItemsSource = items;
                });
            }
            catch (Exception ex)
            {
                LogService.LogError("LoadAutoUpdateLinks failed", ex);
            }
        }

        private async void PerformAutoUpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            PerformAutoUpdateBtn.IsEnabled = false;
            AutoUpdateStatusText.Text = "Otomatik güncelleme başlatılıyor...";
            
            await AutoUpdateService.PerformAutoUpdateAsync((status) => 
            {
                Dispatcher.Invoke(() => 
                {
                    AutoUpdateStatusText.Text = status;
                });
            });

            PerformAutoUpdateBtn.IsEnabled = true;
            
            // Reload settings lists as sources might have been added
            LoadM3uSources();
            LoadEpgSources();
        }
    }
}
