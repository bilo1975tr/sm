using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StreamMesh.Services;
using StreamMesh.Services.P2P;

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
                    CountryCombo.ItemsSource = LocalizationManager.AllCountries;
                    Lang1Combo.ItemsSource = LocalizationManager.KnownLanguagesList;
                    Lang2Combo.ItemsSource = LocalizationManager.KnownLanguagesList;

                    CountryCombo.SelectedItem = string.IsNullOrEmpty(profile.Country) ? "Türkiye" : profile.Country;
                    
                    if (profile.Languages != null)
                    {
                        Lang1Combo.SelectedItem = profile.Languages.Count > 1 ? profile.Languages[1] : "Tümü (Tüm Ülkeler)";
                        Lang2Combo.SelectedItem = profile.Languages.Count > 2 ? profile.Languages[2] : "Tümü (Tüm Ülkeler)";
                    }
                }
            }
            catch { }
        }

        private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!this.IsLoaded) return; // Ignore events during initialization
            var profile = UserService.GetProfile();
            if (profile != null)
            {
                string country = CountryCombo.SelectedItem as string ?? "Türkiye";
                string lang1 = Lang1Combo.SelectedItem as string ?? "Tümü (Tüm Ülkeler)";
                string lang2 = Lang2Combo.SelectedItem as string ?? "Tümü (Tüm Ülkeler)";

                profile.Country = country;
                
                var langs = new System.Collections.Generic.List<string> { "Türkçe" }; // Varsayılanı Türkçe bırakabiliriz veya değiştirebiliriz. Fakat P2P'de Türkiye/Türkçe bazlıydı
                if (lang1 != "Tümü (Tüm Ülkeler)" && !string.IsNullOrEmpty(lang1)) langs.Add(lang1);
                if (lang2 != "Tümü (Tüm Ülkeler)" && !string.IsNullOrEmpty(lang2)) langs.Add(lang2);
                
                profile.Languages = langs;

                UserService.SaveProfile(profile);
            }
        }

        private async void DownloadComponentsBtn_Click(object sender, RoutedEventArgs e)
        {
            DownloadComponentsBtn.IsEnabled = false;
            DownloadStatusText.Text = "Kontrol ediliyor...";

            string githubAceStreamUrl = "https://github.com/bilo1975tr/sm/releases/download/v1.0/AceStream.zip"; 
            
            await Task.Run(async () =>
            {
                await InventoryService.DownloadComponentsManuallyAsync(githubAceStreamUrl, (message) => 
                {
                    Dispatcher.Invoke(() => DownloadStatusText.Text = message);
                });
            });

            Dispatcher.Invoke(() => DownloadComponentsBtn.IsEnabled = true);
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
                    string resultStr = _databaseService.SaveChannels(channels, url);
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
                }
                else
                {
                    ServerControlBtn.Content = "Sunucuyu Başlat";
                    ServerStatusText.Text = "Durum: Kapalı";
                    ServerStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(252, 165, 165)); // Red
                    ServerUrlBox.Text = "";
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
                    string resultStr = _databaseService.SaveChannels(channels, url);
                    
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

            var channels = _databaseService.GetAllChannels();
            
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
                foreach(var c in channels)
                {
                    if (c.Url != null && c.Url.StartsWith("BROKEN_STREAM_"))
                    {
                        _databaseService.DeleteChannel(c.Id);
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
    }
}
