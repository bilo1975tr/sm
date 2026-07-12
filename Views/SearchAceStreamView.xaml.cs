using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StreamMesh.Models;
using StreamMesh.Services;

namespace StreamMesh.Views
{
    public partial class SearchAceStreamView : UserControl
    {
        private readonly HttpClient _httpClient;

        public SearchAceStreamView()
        {
            InitializeComponent();
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            await PerformSearchAsync();
        }

        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await PerformSearchAsync();
            }
        }

        private async Task PerformSearchAsync()
        {
            string query = SearchBox.Text?.Trim();
            if (string.IsNullOrEmpty(query)) return;

            LoadingText.Visibility = Visibility.Visible;
            EmptyText.Visibility = Visibility.Collapsed;
            ResultsList.ItemsSource = null;
            SearchButton.IsEnabled = false;
            AddAllButton.Visibility = Visibility.Collapsed;

            try
            {
                var allResults = new List<AceSearchResult>();
                int sourceIndex = SourceCombo.SelectedIndex; // 0: All, 1: AceStream, 2: FreetuxTV, 3: IPTVcat

                if (sourceIndex == 0 || sourceIndex == 1)
                {
                    var aceResults = await SearchAceStreamAsync(query);
                    allResults.AddRange(aceResults);
                }

                if (sourceIndex == 0 || sourceIndex == 2)
                {
                    var freetuxResults = await SearchFreetuxTVAsync(query);
                    allResults.AddRange(freetuxResults);
                }

                if (sourceIndex == 3)
                {
                    // IPTVcat API hides stream URLs. Redirect user to browser securely.
                    string url = $"https://iptvcat.net/s/{Uri.EscapeDataString(query)}";
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    LoadingText.Visibility = Visibility.Collapsed;
                    SearchButton.IsEnabled = true;
                    return;
                }

                if (allResults.Count > 0)
                {
                    ResultsList.ItemsSource = allResults;
                    AddAllButton.Visibility = Visibility.Visible;
                }
                else
                {
                    EmptyText.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("Global Search Error", ex);
                MessageBox.Show("İşlem sırasında beklenmeyen bir hata oluştu. Lütfen tekrar deneyiniz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                EmptyText.Visibility = Visibility.Visible;
            }
            finally
            {
                LoadingText.Visibility = Visibility.Collapsed;
                SearchButton.IsEnabled = true;
            }
        }

        private async Task<List<AceSearchResult>> SearchAceStreamAsync(string query)
        {
            var list = new List<AceSearchResult>();
            try
            {
                _httpClient.DefaultRequestHeaders.Remove("Accept");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                string url = $"https://search-ace.stream/search?query={Uri.EscapeDataString(query)}";
                string responseJson = await _httpClient.GetStringAsync(url);
                var results = JsonSerializer.Deserialize<List<AceSearchResult>>(responseJson);
                if (results != null)
                {
                    foreach (var r in results)
                    {
                        r.SourceName = "AceStream";
                        list.Add(r);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("AceStream Search Error", ex);
            }
            return list;
        }

        private async Task<List<AceSearchResult>> SearchFreetuxTVAsync(string query)
        {
            var list = new List<AceSearchResult>();
            try
            {
                _httpClient.DefaultRequestHeaders.Remove("Accept");
                _httpClient.DefaultRequestHeaders.Add("Accept", "text/html");

                string url = $"https://database.freetuxtv.net/WebStream/index?WebStreamSearchForm%5BName%5D={Uri.EscapeDataString(query)}&WebStreamSearchForm%5BStatus%5D=2";
                string html = await _httpClient.GetStringAsync(url);

                var match = Regex.Match(html, @"<td>(.*?)<br />=&gt; <a href=""([^""]+)"">");
                while (match.Success)
                {
                    string name = match.Groups[1].Value.Trim();
                    string streamUrl = match.Groups[2].Value.Trim();

                    list.Add(new AceSearchResult
                    {
                        Name = name,
                        ContentId = streamUrl, 
                        TranslatedName = "FreetuxTV M3U",
                        SourceName = "FreetuxTV"
                    });

                    match = match.NextMatch();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("FreetuxTV Search Error", ex);
            }
            return list;
        }

        private void AddToList_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is AceSearchResult result)
            {
                try
                {
                    button.IsEnabled = false;
                    button.Content = "Eklendi ✓";
                    button.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 211, 153));

                    string streamUrl = result.SourceName == "AceStream" ? $"acestream://{result.ContentId}" : result.ContentId;

                    var profile = StreamMesh.Services.Auth.UserService.GetProfile();
                    string defaultLang = (profile?.Languages != null && profile.Languages.Count > 0) ? profile.Languages[0] : "Türkçe";

                    var newChannel = new Channel
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Name = result.Name,
                        Url = streamUrl,
                        Category = "TV",
                        GroupTitle = result.SourceName + " Arama",
                        CreatedAt = DateTime.UtcNow,
                        IsFavorite = false,
                        SourceType = result.SourceName,
                        Language = defaultLang
                    };

                    new DatabaseService().SaveChannel(newChannel);
                    LogService.Log($"Kanal eklendi: {newChannel.Name}");

                    // Auto-refresh the home view list and categories
                    MainWindow.Instance?.HomeView?.LoadChannels();
                }
                catch (Exception ex)
                {
                    LogService.LogError("Error adding searched channel", ex);
                    MessageBox.Show("İşlem sırasında beklenmeyen bir hata oluştu. Lütfen tekrar deneyiniz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void AddAllButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var items = ResultsList.ItemsSource as List<AceSearchResult>;
                if (items == null || items.Count == 0) return;

                var db = new DatabaseService();
                var profile = StreamMesh.Services.Auth.UserService.GetProfile();
                string defaultLang = (profile?.Languages != null && profile.Languages.Count > 0) ? profile.Languages[0] : "Türkçe";

                int added = 0;
                foreach (var result in items)
                {
                    string streamUrl = result.SourceName == "AceStream" ? $"acestream://{result.ContentId}" : result.ContentId;

                    var newChannel = new Channel
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Name = result.Name,
                        Url = streamUrl,
                        Category = "TV",
                        GroupTitle = result.SourceName + " Arama",
                        CreatedAt = DateTime.UtcNow,
                        IsFavorite = false,
                        SourceType = result.SourceName,
                        Language = defaultLang
                    };
                    db.SaveChannel(newChannel);
                    added++;
                }

                // Auto-refresh the home view list and categories
                MainWindow.Instance?.HomeView?.LoadChannels();

                MessageBox.Show($"{added} adet içerik başarıyla kütüphanenize eklendi.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                AddAllButton.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                LogService.LogError("Error adding all searched channels", ex);
                MessageBox.Show("İşlem sırasında beklenmeyen bir hata oluştu. Lütfen tekrar deneyiniz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
