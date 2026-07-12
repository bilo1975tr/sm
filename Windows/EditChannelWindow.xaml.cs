using System;
using System.Windows;
using System.Collections.ObjectModel;
using System.Linq;
using StreamMesh.Models;

namespace StreamMesh.Windows
{
    public partial class EditChannelWindow : Window
    {
        private Channel _channel;
        private System.Collections.Generic.List<string> _allEpgNames = new System.Collections.Generic.List<string>();
        public ObservableCollection<string> Urls { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<string> Logos { get; set; } = new ObservableCollection<string>();
        public EditChannelWindow(Channel channel)
        {
            InitializeComponent();
            _channel = channel;
            this.Title = $"Kanal Düzenle - {channel.Name}";

            // Populate Languages
            LangCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "Hiçbiri" });
            LangCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "Bilinmiyor" });
            var cultures = System.Globalization.CultureInfo.GetCultures(System.Globalization.CultureTypes.SpecificCultures)
                .Select(c => c.NativeName)
                .Distinct()
                .OrderBy(n => n)
                .ToList();
            foreach (var lang in cultures) LangCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = lang });

            // EPG İsimlerini yükle
            try {
                var epgService = new StreamMesh.Services.EpgService();
                _allEpgNames = epgService.GetUniqueEpgChannelNames();
                EpgIdCombo.ItemsSource = _allEpgNames;
            } catch { }

            // Doldur
            NameTxt.Text = channel.Name;
            EpgIdCombo.Text = channel.EpgId;
            
            if (!string.IsNullOrWhiteSpace(channel.Url))
            {
                var split = channel.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var u in split) Urls.Add(u.Trim());
            }
            UrlList.ItemsSource = Urls;

            if (!string.IsNullOrWhiteSpace(channel.LogoUrl))
            {
                var split = channel.LogoUrl.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var l in split) Logos.Add(l.Trim());
            }
            LogoList.ItemsSource = Logos;

            CatCombo.Text = channel.Category;
            LangCombo.Text = channel.Language;
            GroupTxt.Text = channel.GroupTitle;
            SourceTxt.Text = channel.SourceType;
            FavoriteChk.IsChecked = channel.IsFavorite;
            LockedChk.IsChecked = channel.IsLocked;
            NotesTxt.Text = channel.Notes;
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            _channel.Name = NameTxt.Text.Trim();
            _channel.EpgId = EpgIdCombo.Text.Trim();
            _channel.Url = string.Join(",", Urls);
            _channel.Category = CatCombo.Text.Trim();
            _channel.LogoUrl = string.Join(",", Logos);
            _channel.Language = LangCombo.Text?.Trim() ?? "";
            _channel.GroupTitle = GroupTxt.Text.Trim();
            _channel.SourceType = SourceTxt.Text.Trim();
            _channel.IsFavorite = FavoriteChk.IsChecked ?? false;
            _channel.IsLocked = LockedChk.IsChecked ?? false;
            _channel.Notes = NotesTxt.Text?.Trim() ?? string.Empty;

            DialogResult = true;
            Close();
        }

        private void AddLogo_Click(object sender, RoutedEventArgs e)
        {
            string url = NewLogoTxt.Text?.Trim();
            if (string.IsNullOrEmpty(url)) return;
            
            if (!Logos.Contains(url))
            {
                Logos.Add(url);
                NewLogoTxt.Clear();
            }
        }

        private async void SearchOnlineLogo_Click(object sender, RoutedEventArgs e)
        {
            string query = NewLogoTxt.Text?.Trim();
            if (string.IsNullOrEmpty(query))
            {
                query = NameTxt.Text?.Trim();
            }

            if (string.IsNullOrEmpty(query)) return;

            try
            {
                var results = await StreamMesh.Services.LogoSearchService.SearchLogosAsync(query);
                if (results != null && results.Count > 0)
                {
                    LogoSearchResultsList.ItemsSource = results;
                    LogoSearchResultsList.Visibility = Visibility.Visible;
                }
                else
                {
                    LogoSearchResultsList.Visibility = Visibility.Collapsed;
                    MessageBox.Show("Eşleşen herhangi bir logo bulunamadı.", "Logo Arama", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                StreamMesh.Services.LogService.LogError("Logo search error", ex);
                MessageBox.Show("İşlem sırasında beklenmeyen bir hata oluştu. Lütfen tekrar deneyiniz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SelectLogoSearchResult_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is StreamMesh.Services.LogoSearchResult result)
            {
                string url = result.LogoUrl;
                if (!string.IsNullOrEmpty(url) && !Logos.Contains(url))
                {
                    Logos.Add(url);
                    NewLogoTxt.Clear();
                    LogoSearchResultsList.Visibility = Visibility.Collapsed;
                    MessageBox.Show($"'{result.Name}' logosu listeye eklendi.", "Logo Eklendi", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void RemoveLogo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is string url)
            {
                Logos.Remove(url);
            }
        }

        private void MakeDefaultLogo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is string url)
            {
                int index = Logos.IndexOf(url);
                if (index > 0)
                {
                    Logos.Move(index, 0);
                    MessageBox.Show("Bu logo varsayılan olarak ayarlandı.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void AddUrl_Click(object sender, RoutedEventArgs e)
        {
            string url = NewUrlTxt.Text?.Trim();
            if (string.IsNullOrEmpty(url)) return;
            
            if (!Urls.Contains(url))
            {
                Urls.Add(url);
                NewUrlTxt.Clear();
            }
        }

        private void RemoveUrl_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is string url)
            {
                Urls.Remove(url);
            }
        }

        private void MakeDefaultUrl_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is string url)
            {
                int index = Urls.IndexOf(url);
                if (index > 0)
                {
                    Urls.Move(index, 0);
                    MessageBox.Show("Bu yayın varsayılan (öncelikli) olarak ayarlandı.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void FindEpgBtn_Click(object sender, RoutedEventArgs e)
        {
            string channelName = NameTxt.Text?.Trim();
            if (string.IsNullOrEmpty(channelName) || _allEpgNames.Count == 0) return;

            // Fuzzy Match - Levenshtein Benzerlik Araması
            string bestMatch = null;
            double bestScore = 0;

            foreach (var epgName in _allEpgNames)
            {
                double score = CalculateSimilarity(channelName, epgName);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = epgName;
                }
            }

            if (bestMatch != null && bestScore > 0.4) // %40'tan fazla benzerlik varsa
            {
                EpgIdCombo.Text = bestMatch;
            }
            else
            {
                MessageBox.Show("Uygun bir EPG eşleşmesi bulunamadı.", "EPG Bul", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private double CalculateSimilarity(string source, string target)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return 0;
            source = source.ToLowerInvariant();
            target = target.ToLowerInvariant();

            // Basit içeriyor kontrolü
            if (source == target) return 1.0;
            if (target.Contains(source)) return 0.8;
            if (source.Contains(target)) return 0.7;

            // Levenshtein Mesafesi
            int n = source.Length;
            int m = target.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }

            double maxLen = Math.Max(n, m);
            return 1.0 - (d[n, m] / maxLen);
        }
    }
}
