using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using StreamMesh.Models;
using StreamMesh.Services;

namespace StreamMesh.Windows
{
    public class ChannelSelectionItem : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected 
        { 
            get => _isSelected; 
            set 
            { 
                if (_isSelected != value) 
                { 
                    _isSelected = value; 
                    OnPropertyChanged(nameof(IsSelected)); 
                } 
            } 
        }

        private Channel _channel;
        public Channel Channel 
        { 
            get => _channel; 
            set 
            { 
                if (_channel != value) 
                {
                    _channel = value; 
                    OnPropertyChanged(nameof(Channel)); 
                    RefreshComputedProperties();
                }
            } 
        }

        public int UrlCount => (Channel?.Url ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Length;
        public string FirstLogoUrl => (Channel?.LogoUrl ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        private string _tempSnapshotUrl;
        public string TempSnapshotUrl
        {
            get => _tempSnapshotUrl;
            set
            {
                if (_tempSnapshotUrl != value)
                {
                    _tempSnapshotUrl = value;
                    OnPropertyChanged(nameof(TempSnapshotUrl));
                    OnPropertyChanged(nameof(DisplayImageUrl));
                }
            }
        }

        public string DisplayImageUrl => !string.IsNullOrEmpty(TempSnapshotUrl) ? TempSnapshotUrl : FirstLogoUrl;

        public void RefreshComputedProperties()
        {
            OnPropertyChanged(nameof(UrlCount));
            OnPropertyChanged(nameof(FirstLogoUrl));
            OnPropertyChanged(nameof(DisplayImageUrl));
            OnPropertyChanged(nameof(Channel));
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    public class ChannelGroupItem
    {
        public string GroupName { get; set; }
        public int Count { get; set; }
        public string DisplayText => $"{GroupName} ({Count} kanal)";
        public List<Channel> Channels { get; set; }
    }

    public partial class AdvancedChannelEditorWindow : Window
    {
        private DatabaseService _databaseService;
        private List<Channel> _allChannels;
        private List<ChannelSelectionItem> _currentList;
        private bool _isProcessing = false;
        private List<string> _tempSnapshots = new List<string>();

        public AdvancedChannelEditorWindow()
        {
            InitializeComponent();
            _databaseService = new DatabaseService();
            _allChannels = _databaseService.GetAllChannels();

            // Populate Languages
            PopulateLanguages();
            
            // Popülasyon Başlangıcı
            _currentList = _allChannels.Take(200).Select(c => new ChannelSelectionItem { Channel = c, IsSelected = false }).ToList();
            ChannelsList.ItemsSource = _currentList;
        }

        private void PopulateLanguages()
        {
            BulkLanguageCombo.Items.Add(new ComboBoxItem { Content = "Değiştirme" });
            BulkLanguageCombo.Items.Add(new ComboBoxItem { Content = "Hiçbiri" });
            BulkLanguageCombo.Items.Add(new ComboBoxItem { Content = "Bilinmiyor" });
            
            var cultures = System.Globalization.CultureInfo.GetCultures(System.Globalization.CultureTypes.SpecificCultures)
                .Select(c => c.NativeName)
                .Distinct()
                .OrderBy(n => n)
                .ToList();
                
            foreach (var lang in cultures)
            {
                BulkLanguageCombo.Items.Add(new ComboBoxItem { Content = lang });
            }
            BulkLanguageCombo.SelectedIndex = 0;
        }

        private void RefreshChannelsList()
        {
            if (_allChannels == null) return;

            string query = SearchTxt.Text.ToLower().Trim();
            string selectedCat = (FilterCategoryCombo?.SelectedItem as ComboBoxItem)?.Content.ToString();
            if (string.IsNullOrEmpty(selectedCat)) selectedCat = "Tümü";

            var filtered = _allChannels.AsEnumerable();

            // 1. Kategori Filtresi
            if (selectedCat != "Tümü")
            {
                filtered = filtered.Where(c => c.Category != null && c.Category.Equals(selectedCat, StringComparison.OrdinalIgnoreCase));
            }

            // 2. Arama Sorgusu (Min 2 karakter koşulu kaldırıldı/veya esnetildi ama text search için hızlı filtreleme)
            if (query.Length >= 2)
            {
                filtered = filtered.Where(c => c.Name != null && c.Name.ToLower().Contains(query));
            }

            var list = filtered.Take(200).Select(c => new ChannelSelectionItem { Channel = c, IsSelected = false }).ToList();

            _currentList = list;
            ChannelsList.ItemsSource = _currentList;
        }

        private void FilterCategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshChannelsList();
        }

        private void SearchTxt_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshChannelsList();
        }

        private void SearchBtn_Click(object sender, RoutedEventArgs e)
        {
            string query = SearchTxt.Text.ToLower().Trim();
            if (string.IsNullOrEmpty(query)) return;

            string selectedCat = (FilterCategoryCombo?.SelectedItem as ComboBoxItem)?.Content.ToString();
            if (string.IsNullOrEmpty(selectedCat)) selectedCat = "Tümü";

            var baseChannels = _allChannels.AsEnumerable();
            if (selectedCat != "Tümü")
            {
                baseChannels = baseChannels.Where(c => c.Category != null && c.Category.Equals(selectedCat, StringComparison.OrdinalIgnoreCase));
            }

            // Simple string matching / Fuzzy replacement
            var filtered = baseChannels
                .Where(c => c.Name != null)
                .Select(c => new { Channel = c, Score = Math.Max(CalculateSimilarity(query, c.Name.ToLower()), c.Name.ToLower().Contains(query) ? 1.0 : 0.0) })
                .Where(x => x.Score >= 0.5) // At least 50% similar or contains
                .OrderByDescending(x => x.Score)
                .Take(200)
                .Select(x => x.Channel)
                .ToList();

            _currentList = filtered.Select(c => new ChannelSelectionItem { Channel = c, IsSelected = false }).ToList();
            ChannelsList.ItemsSource = _currentList;
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (ChannelsList.SelectedItem is ChannelSelectionItem selectionItem && selectionItem.Channel != null)
            {
                var result = MessageBox.Show($"'{selectionItem.Channel.Name}' isimli kanalı silmek istediğinize emin misiniz?", "Kanalı Sil", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    _databaseService.DeleteChannel(selectionItem.Channel.Id);
                    _allChannels.Remove(selectionItem.Channel);
                    RefreshChannelsList();
                    MessageBox.Show("Kanal başarıyla silindi.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show("Silmek için lütfen listeden bir kanal seçin.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BulkDeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentList == null) return;

            var selected = _currentList.Where(x => x.IsSelected).Select(x => x.Channel).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Lütfen silmek istediğiniz kanalları seçin.", "Uyan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show($"Seçili {selected.Count} kanalı tamamen ve kalıcı olarak silmek istediğinize emin misiniz?", "Toplu Kanal Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                foreach (var ch in selected)
                {
                    _databaseService.DeleteChannel(ch.Id);
                    _allChannels.Remove(ch);
                }

                RefreshChannelsList();
                MessageBox.Show("Seçilen kanallar başarıyla silindi.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private string CleanChannelName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string lower = name.ToLowerInvariant();
            
            string[] exactReplacements = { " fhd", " hd", " sd", " 4k", " 1080p", " 720p", " 480p", " 576p", " backup", " yedek", " vip", " hevc", " h265", " raw", " 50fps", " 60fps" };
            foreach (var rep in exactReplacements)
            {
                lower = lower.Replace(rep, " ");
            }

            string[] charsToRemove = { "!", "?", "-", "+", "(", ")", "[", "]", "_" };
            foreach (var ch in charsToRemove)
            {
                lower = lower.Replace(ch, " ");
            }

            return string.Join(" ", lower.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }

        private void SmartGroupBtn_Click(object sender, RoutedEventArgs e)
        {
            var groups = _allChannels.Where(c => c.Name != null)
                                     .GroupBy(c => CleanChannelName(c.Name))
                                     .Where(g => g.Count() > 1 && !string.IsNullOrEmpty(g.Key) && g.Key.Length > 2)
                                     .Select(g => new ChannelGroupItem
                                     {
                                         GroupName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(g.Key),
                                         Count = g.Count(),
                                         Channels = g.ToList()
                                     })
                                     .OrderByDescending(g => g.Count)
                                     .ToList();

            GroupList.ItemsSource = groups;
            GroupList.Visibility = Visibility.Visible;
            GroupColumn.Width = new GridLength(250);
            
            MessageBox.Show($"{groups.Count} adet farklı grup bulundu!\nSol listeden bir gruba tıklayarak içeriğini görebilirsiniz.", "Akıllı Gruplama", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void GroupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GroupList.SelectedItem is ChannelGroupItem selectedGroup)
            {
                _currentList = selectedGroup.Channels.Select(c => new ChannelSelectionItem { Channel = c, IsSelected = true }).ToList();
                ChannelsList.ItemsSource = _currentList;
            }
        }

        private double CalculateSimilarity(string source, string target)
        {
            if (string.IsNullOrEmpty(source)) return string.IsNullOrEmpty(target) ? 1.0 : 0.0;
            if (string.IsNullOrEmpty(target)) return 0.0;

            int stepsToSame = ComputeLevenshteinDistance(source, target);
            return 1.0 - ((double)stepsToSame / (double)Math.Max(source.Length, target.Length));
        }

        private int ComputeLevenshteinDistance(string source, string target)
        {
            int n = source.Length;
            int m = target.Length;
            int[,] d = new int[n + 1, m + 1];

            if (n == 0) return m;
            if (m == 0) return n;

            for (int i = 0; i <= n; d[i, 0] = i++) { }
            for (int j = 0; j <= m; d[0, j] = j++) { }

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }

        private void BulkUpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentList == null) return;

            var selected = _currentList.Where(x => x.IsSelected).Select(x => x.Channel).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Lütfen güncellenecek kanalları seçin.");
                return;
            }

            string cat = (BulkCategoryCombo.SelectedItem as ComboBoxItem)?.Content.ToString();
            string lang = (BulkLanguageCombo.SelectedItem as ComboBoxItem)?.Content.ToString();

            bool anyChanged = false;
            foreach (var ch in selected)
            {
                bool localChanged = false;
                if (cat != "Değiştirme" && !string.IsNullOrEmpty(cat))
                {
                    ch.Category = cat;
                    localChanged = true;
                    anyChanged = true;
                }
                
                if (lang != "Değiştirme" && !string.IsNullOrEmpty(lang))
                {
                    string normalizedLang = Channel.NormalizeLanguage(lang);
                    ch.Language = normalizedLang;
                    localChanged = true;
                    anyChanged = true;
                }
                
                if (localChanged) 
                {
                    _databaseService.SaveChannel(ch);
                    if (ch.IsVerified) _ = StreamMesh.Services.GitHubSyncService.PushNewChannelsToFirebasePoolAsync(new List<StreamMesh.Models.Channel> { ch });
                }
            }

            if (anyChanged)
            {
                MessageBox.Show("Seçili kanallar güncellendi.");
                ChannelsList.Items.Refresh();
            }
        }

        private void MergeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentList == null) return;

            var selected = _currentList.Where(x => x.IsSelected).Select(x => x.Channel).ToList();
            if (selected.Count < 2)
            {
                MessageBox.Show("Birleştirmek için en az 2 kanal seçmelisiniz.");
                return;
            }

            var result = MessageBox.Show($"{selected.Count} kanalı tek bir kanalda birleştirmek istediğinize emin misiniz?", "Kanalları Birleştir", MessageBoxButton.YesNo);
            if (result == MessageBoxResult.Yes)
            {
                var mainChannel = selected.First();
                
                var urlSet = new HashSet<string>(mainChannel.Url?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>());
                var logoSet = new HashSet<string>(mainChannel.LogoUrl?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>());

                foreach (var ch in selected.Skip(1))
                {
                    var uList = ch.Url?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                    foreach (var u in uList) urlSet.Add(u);
                    
                    var lList = ch.LogoUrl?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                    foreach (var l in lList) logoSet.Add(l);

                    _databaseService.DeleteChannel(ch.Id); // Diğerlerini sil
                }

                mainChannel.Url = string.Join(",", urlSet);
                mainChannel.LogoUrl = string.Join(",", logoSet);
                _databaseService.SaveChannel(mainChannel);
                if (mainChannel.IsVerified) _ = StreamMesh.Services.GitHubSyncService.PushNewChannelsToFirebasePoolAsync(new List<StreamMesh.Models.Channel> { mainChannel });

                MessageBox.Show("Kanal birleştirme tamamlandı.");
                _allChannels = _databaseService.GetAllChannels(); // Reload
                SearchTxt_TextChanged(null, null); // Refilter
            }
        }

        private async void SnapshotBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;
            if (_currentList == null) return;

            var selectedItems = _currentList.Where(x => x.IsSelected).ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("Görüntü çekilecek kanalları seçin.");
                return;
            }

            string ffmpegPath = StreamMesh.Services.InventoryService.FFmpegPath;
            if (!File.Exists(ffmpegPath))
            {
                MessageBox.Show("ffmpeg.exe bulunamadı. Lütfen Ayarlar > Bileşen Yönetimi kısmından indirin.");
                return;
            }

            _isProcessing = true;
            SnapshotBtn.IsEnabled = false;
            
            int successCount = 0;
            string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StreamMesh", "Thumbnails");
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

            for (int i = 0; i < selectedItems.Count; i++)
            {
                var item = selectedItems[i];
                var channel = item.Channel;
                
                JobStatusTxt.Text = $"İşleniyor ({i+1}/{selectedItems.Count}): {channel.Name}";
                
                string streamUrl = channel.Url?.Split(',').FirstOrDefault();
                if (string.IsNullOrEmpty(streamUrl) || streamUrl.StartsWith("acestream://"))
                {
                    continue; // YouTube can be resolved, but we skip AceStream or empty here for now
                }

                string imgFileName = $"{Guid.NewGuid()}.jpg";
                string imgPath = Path.Combine(cacheDir, imgFileName);

                try
                {
                    await Task.Run(() => 
                    {
                        var proc = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = ffmpegPath,
                                Arguments = $"-y -i \"{streamUrl}\" -vframes 1 -q:v 2 -s 320x180 \"{imgPath}\"",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        proc.Start();
                        // wait max 10 seconds for a frame
                        if (proc.WaitForExit(10000)) 
                        {
                            if (proc.ExitCode != 0 && File.Exists(imgPath))
                            {
                                // it might still have created a thumbnail even if it exited with non-zero
                            }
                        }
                        else
                        {
                            proc.Kill();
                        }
                    });

                    if (File.Exists(imgPath))
                    {
                        item.TempSnapshotUrl = imgPath;
                        _tempSnapshots.Add(imgPath);
                        successCount++;
                        item.RefreshComputedProperties();
                    }
                }
                catch (Exception ex)
                {
                    LogService.Log($"Snapshot hatası: {ex.Message}");
                }
            }

            JobStatusTxt.Text = $"Tamamlandı! {successCount} kanalın anlık görüntüsü alındı.";
            ChannelsList.Items.Refresh();

            _isProcessing = false;
            SnapshotBtn.IsEnabled = true;
        }

        private void PreviewChannel_Click(object sender, RoutedEventArgs e)
        {
            if (ChannelsList.SelectedItem is ChannelSelectionItem selectionItem && selectionItem.Channel != null)
            {
                var previewWin = new PreviewPlayerWindow(selectionItem.Channel);
                previewWin.Owner = this;
                previewWin.ShowDialog();
            }
            else
            {
                MessageBox.Show("Önizlemek için lütfen listeden sağ tıklanan kanalı seçin.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void MergeSelected_Click(object sender, RoutedEventArgs e)
        {
            MergeBtn_Click(sender, e);
        }

        private async void GetStreamInfo_Click(object sender, RoutedEventArgs e)
        {
            if (ChannelsList.SelectedItem is ChannelSelectionItem selectionItem && selectionItem.Channel != null)
            {
                var channel = selectionItem.Channel;
                string firstUrl = (channel.Url ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
                if (string.IsNullOrEmpty(firstUrl))
                {
                    MessageBox.Show("Analiz edilecek yayın adresi bulunamadı.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                JobStatusTxt.Text = $"Yayın analiz ediliyor: {channel.Name}...";
                try
                {
                    var checker = new StreamCheckerService();
                    
                    bool isYoutube = firstUrl.Contains("youtube.com") || firstUrl.Contains("youtu.be");
                    if (isYoutube || channel.SourceType == "YOUTUBE")
                    {
                        var yt = new YoutubeService();
                        var resolved = await yt.GetSingleMuxedStreamUrlAsync(firstUrl);
                        if (!string.IsNullOrEmpty(resolved)) firstUrl = resolved;
                    }
                    else if (firstUrl.StartsWith("acestream://") || channel.SourceType == "ACESTREAM")
                    {
                        var ace = new AceStreamService();
                        await ace.StartEngineAsync();
                        firstUrl = ace.GetHttpUrl(firstUrl);
                    }

                    var result = await checker.AnalyzeStreamWithVlcAsync(firstUrl);
                    
                    string statusText = result.working ? "🔴 Aktif (Çalışıyor)" : "⚫ Pasif (Çevrimdışı)";
                    string categoryText = result.category ?? "Bilinmiyor";
                    string resolutionText = result.resolution ?? "Bilinmiyor (Sadece Ses vb.)";
                    
                    MessageBox.Show(
                        $"Kanal: {channel.Name}\n" +
                        $"Bağlantı Durumu: {statusText}\n" +
                        $"Kategori: {categoryText}\n" +
                        $"Çözünürlük: {resolutionText}\n" +
                        $"Yayın Adresi: {firstUrl}",
                        "Kanal/Yayın Analiz Raporu",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
                catch (Exception ex)
                {
                    LogService.LogError("Stream analysis error", ex);
                    MessageBox.Show("İşlem sırasında beklenmeyen bir hata oluştu. Lütfen tekrar deneyiniz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    JobStatusTxt.Text = "Bekliyor...";
                }
            }
            else
            {
                MessageBox.Show("Lütfen analiz etmek istediğiniz kanalı seçin.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            foreach (var file in _tempSnapshots)
            {
                try
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
                catch { }
            }
            base.OnClosed(e);
        }
    }
}
