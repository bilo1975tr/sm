using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StreamMesh.Core.Media;
using StreamMesh.Models;
using StreamMesh.UI.Windows;
using UserControl = System.Windows.Controls.UserControl;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;

namespace StreamMesh.UI.Views
{
    public partial class SearchAceStreamView : UserControl
    {
        private readonly GlobalSearchEngine _searchEngine = new GlobalSearchEngine();

        public SearchAceStreamView()
        {
            InitializeComponent();
        }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteSearchAsync();
        }

        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await ExecuteSearchAsync();
            }
        }

        private readonly StreamMesh.Core.Database.DatabaseEngine _db = new StreamMesh.Core.Database.DatabaseEngine();

        private async Task ExecuteSearchAsync()
        {
            string query = TxtSearchQuery.Text.Trim();
            string filter = (CmbSourceFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Tümü";

            if (string.IsNullOrWhiteSpace(query))
            {
                TxtResultCount.Text = "Lütfen aramak için bir kanal adı veya etiket ([DE], [TR], vb.) girin.";
                BtnAddAllToLibrary.Visibility = Visibility.Collapsed;
                return;
            }

            SearchProgressBar.Visibility = Visibility.Visible;
            TxtResultCount.Text = "Sonuçlar taranıyor...";
            BtnAddAllToLibrary.Visibility = Visibility.Collapsed;

            try
            {
                var results = await _searchEngine.SearchGlobalAsync(query, filter);

                // Kütüphanede zaten var olan kanalları tespit et ve butonlarını güncelle
                try
                {
                    var existingChannels = await _db.GetAllChannelsAsync();
                    var existingUrls = new HashSet<string>(existingChannels.Where(c => !string.IsNullOrEmpty(c.Url)).Select(c => c.Url!.Trim()), StringComparer.OrdinalIgnoreCase);

                    foreach (var res in results)
                    {
                        if (!string.IsNullOrWhiteSpace(res.Url) && existingUrls.Contains(res.Url.Trim()))
                        {
                            res.IsAdded = true;
                            res.AddButtonText = "✔ Eklendi";
                        }
                    }
                }
                catch { }

                ResultsList.ItemsSource = results;
                TxtResultCount.Text = $"{results.Count} sonuç bulundu.";

                if (results.Count > 0)
                {
                    int unaddedCount = results.Count(x => !x.IsAdded);
                    BtnAddAllToLibrary.Visibility = Visibility.Visible;
                    BtnAddAllToLibrary.IsEnabled = unaddedCount > 0;
                    BtnAddAllToLibrary.Content = unaddedCount > 0 
                        ? $"➕ Tümünü Kütüphaneye Ekle ({results.Count} Kanal)" 
                        : "✔ Tümü Zaten Kütüphanede";
                    BtnAddAllToLibrary.Background = unaddedCount > 0
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(2, 132, 199))
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
                }
                else
                {
                    BtnAddAllToLibrary.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                TxtResultCount.Text = $"Arama sırasında hata: {ex.Message}";
                BtnAddAllToLibrary.Visibility = Visibility.Collapsed;
            }
            finally
            {
                SearchProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private async void AddAllToLibrary_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsList.ItemsSource is not IEnumerable<SearchResultItem> items) return;

            var unaddedList = items.Where(x => !x.IsAdded && !string.IsNullOrWhiteSpace(x.Url)).ToList();
            if (unaddedList.Count == 0)
            {
                MessageBox.Show("Listelenen tüm kanallar zaten kütüphanenizde ekli.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            BtnAddAllToLibrary.IsEnabled = false;
            BtnAddAllToLibrary.Content = "⏳ Kanallar Ekleniyor...";

            try
            {
                var channelsToAdd = new List<Channel>();

                foreach (var item in unaddedList)
                {
                    var ch = new Channel
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = item.Name,
                        Url = item.Url,
                        SourceType = item.Source.Contains("AceStream") ? "ACESTREAM" : "M3U",
                        GroupTitle = string.IsNullOrEmpty(item.GroupTitle) ? item.Source : item.GroupTitle,
                        Category = string.IsNullOrEmpty(item.Category) ? "Canlı TV" : item.Category,
                        LogoUrl = item.LogoUrl
                    };
                    channelsToAdd.Add(ch);
                }

                await _db.SaveChannelsBatchAsync(channelsToAdd, clearFirst: false, notifyUpdated: true);

                foreach (var item in unaddedList)
                {
                    item.IsAdded = true;
                    item.AddButtonText = "✔ Eklendi";
                }

                BtnAddAllToLibrary.Content = $"✔ Tümü Eklendi ({channelsToAdd.Count} Kanal)";
                BtnAddAllToLibrary.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
                TxtResultCount.Text = $"{channelsToAdd.Count} yeni kanal başarıyla kütüphanenize eklendi!";
            }
            catch (Exception ex)
            {
                BtnAddAllToLibrary.IsEnabled = true;
                BtnAddAllToLibrary.Content = "➕ Tekrar Dene";
                MessageBox.Show($"Kanallar eklenirken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PlayContentId_Click(object sender, RoutedEventArgs e)
        {
            string raw = TxtContentId.Text.Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                MessageBox.Show("Lütfen geçerli bir AceStream Content-ID veya linki girin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string playUrl = raw;
            if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                playUrl = raw;
            }
            else if (raw.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase))
            {
                playUrl = raw;
            }
            else
            {
                playUrl = $"acestream://{raw}";
            }

            var channel = new Channel
            {
                Name = $"AceStream P2P ({(raw.Length > 16 ? raw.Substring(0, 12) + "..." : raw)})",
                Url = playUrl,
                SourceType = "ACESTREAM",
                GroupTitle = "AceStream P2P",
                Category = "TV"
            };

            MainWindow.Instance?.LoadChannelToPlayer(channel);
        }

        private void PreviewResultItem_Click(object sender, RoutedEventArgs e)
        {
            PlayResultItem_Click(sender, e);
        }

        private void PlayResultItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is SearchResultItem item)
            {
                var channel = new Channel
                {
                    Name = item.Name,
                    Url = item.Url,
                    SourceType = item.Source.Contains("AceStream") ? "ACESTREAM" : "M3U",
                    GroupTitle = string.IsNullOrEmpty(item.GroupTitle) ? item.Source : item.GroupTitle,
                    Category = string.IsNullOrEmpty(item.Category) ? "TV" : item.Category,
                    LogoUrl = item.LogoUrl
                };

                MainWindow.Instance?.LoadChannelToPlayer(channel);
            }
        }

        private async void AddToLibrary_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is SearchResultItem item)
            {
                if (item.IsAdded) return;

                try
                {
                    var channel = new Channel
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = item.Name,
                        Url = item.Url,
                        SourceType = item.Source.Contains("AceStream") ? "ACESTREAM" : "M3U",
                        GroupTitle = string.IsNullOrEmpty(item.GroupTitle) ? item.Source : item.GroupTitle,
                        Category = string.IsNullOrEmpty(item.Category) ? "TV" : item.Category,
                        LogoUrl = item.LogoUrl
                    };

                    var db = new StreamMesh.Core.Database.DatabaseEngine();
                    await db.SaveChannelAsync(channel);

                    item.IsAdded = true;
                    item.AddButtonText = "✔ Eklendi";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Kütüphaneye eklenirken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void StartAceEngine_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool started = await _searchEngine.StartAceEngineAsync();
                if (started)
                {
                    MessageBox.Show("AceEngine P2P akış motoru başarıyla başlatıldı ve hazır.", "AceEngine", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("AceEngine başlatılamadı. Sisteminizde AceStream kurulu olduğundan emin olun.", "AceEngine Uyarısı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"AceEngine başlatılırken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
