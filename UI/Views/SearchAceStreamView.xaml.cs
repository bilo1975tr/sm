using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StreamMesh.Models;
using StreamMesh.Core.Media;
using StreamMesh.Core.Database;
using StreamMesh.Core.Utils;
using StreamMesh.UI.Windows;

namespace StreamMesh.UI.Views
{
    public partial class SearchAceStreamView : System.Windows.Controls.UserControl
    {
        private readonly GlobalSearchEngine _searchEngine = new GlobalSearchEngine();
        private readonly DatabaseEngine _db = new DatabaseEngine();
        public ObservableCollection<SearchResultItem> Results { get; set; } = new ObservableCollection<SearchResultItem>();

        public SearchAceStreamView()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += SearchAceStreamView_Loaded;
        }

        private async void SearchAceStreamView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Auto-start AceStream Engine when search view is opened
                await _searchEngine.StartAceEngineAsync();
            }
            catch { }
        }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            await PerformSearchAsync();
        }

        private async void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                await PerformSearchAsync();
            }
        }

        private async Task PerformSearchAsync()
        {
            string selectedSource = (SourceComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Tüm Kaynaklar";
            string selectedCategory = (CategoryComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Tüm Kategoriler";
            string selectedLanguage = (LanguageComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Tüm Diller";

            if (string.IsNullOrWhiteSpace(SearchBox.Text) && selectedCategory.Contains("Tüm") && selectedLanguage.Contains("Tüm"))
            {
                System.Windows.MessageBox.Show("Lütfen bir arama terimi girin veya bir kategori / dil filtresi seçin.", "Arama Uyarısı", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Results.Clear();
            AddAllButton.Visibility = Visibility.Collapsed;
            LoadingBar.Visibility = Visibility.Visible;

            try
            {
                var list = await _searchEngine.SearchGlobalAsync(SearchBox.Text, selectedSource, selectedCategory, selectedLanguage);
                foreach (var item in list)
                {
                    Results.Add(item);
                }

                if (list.Count > 0)
                {
                    AddAllButton.Visibility = Visibility.Visible;
                    // Dynamic message for result count
                    if (Results.Count > 100)
                    {
                        System.Windows.MessageBox.Show($"Harika! Kriterlerinize uygun toplam {Results.Count} kanal listelendi.", "Geniş Arama Sonucu", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    System.Windows.MessageBox.Show("Aranan kriterlere uygun kanal veya medya içeriği bulunamadı.", "Arama Sonucu", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Arama sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingBar.Visibility = Visibility.Collapsed;
            }
        }

        private async void AddAllToLibrary_Click(object sender, RoutedEventArgs e)
        {
            if (Results.Count == 0) return;

            var result = System.Windows.MessageBox.Show($"Listelenen {Results.Count} kanalın tamamı kütüphanenize eklensin mi?", "Toplu Kütüphaneye Ekle", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                var channels = Results.Select(item => new Channel
                {
                    Name = item.Name,
                    Url = item.Url,
                    Category = item.Category,
                    GroupTitle = string.IsNullOrEmpty(item.GroupTitle) ? "Arama Sonuçları" : item.GroupTitle,
                    LogoUrl = item.LogoUrl,
                    SourceType = item.Source.Contains("AceStream") ? "ACESTREAM" : "M3U"
                }).ToList();

                await _db.SyncIncomingChannelsAsync(channels);
                System.Windows.MessageBox.Show($"{channels.Count} adet kanal ve medya akışı başarıyla kütüphanenize eklendi.", "Toplu Ekleme Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Ekleme sırasında bir hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PlayResult_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.CommandParameter is SearchResultItem item)
            {
                var ch = new Channel
                {
                    Name = item.Name,
                    Url = item.Url,
                    Category = item.Category,
                    GroupTitle = item.GroupTitle,
                    LogoUrl = item.LogoUrl,
                    SourceType = item.Source.Contains("AceStream") ? "ACESTREAM" : "M3U"
                };
                MainWindow.Instance?.LoadChannelToPlayer(ch);
            }
        }

        private async void AddToLibrary_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.CommandParameter is SearchResultItem item)
            {
                var ch = new Channel
                {
                    Name = item.Name,
                    Url = item.Url,
                    Category = item.Category,
                    GroupTitle = string.IsNullOrEmpty(item.GroupTitle) ? "Eklenen Kanallar" : item.GroupTitle,
                    LogoUrl = item.LogoUrl,
                    SourceType = item.Source.Contains("AceStream") ? "ACESTREAM" : "M3U"
                };

                await _db.SaveChannelAsync(ch);
                System.Windows.MessageBox.Show($"'{ch.Name}' başarıyla kütüphanenize eklendi.", "Kütüphaneye Eklendi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
