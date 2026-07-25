using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private readonly AceStreamService _aceStreamService;

        public SearchAceStreamView()
        {
            InitializeComponent();
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _aceStreamService = new AceStreamService();

            this.Loaded += SearchAceStreamView_Loaded;
        }

        private async void SearchAceStreamView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Arama sayfasına girer girmez AceStream motorunu otomatik olarak başlat
                await Task.Run(async () =>
                {
                    await _aceStreamService.StartEngineAsync();
                });
            }
            catch (Exception ex)
            {
                LogService.LogError("Arama sayfasında AceStream motoru başlatılırken hata oluştu.", ex);
            }
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
            string query = SearchBox.Text?.Trim() ?? "";
            string category = (CategoryCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
            string language = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
            string sortTag = (SortCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "availability|desc";
            
            string sortBy = "availability";
            string sortOrder = "desc";
            if (!string.IsNullOrEmpty(sortTag) && sortTag.Contains("|"))
            {
                var parts = sortTag.Split('|');
                sortBy = parts[0];
                sortOrder = parts[1];
            }

            if (string.IsNullOrEmpty(query) && category == "all" && language == "all")
            {
                MessageBox.Show("Lütfen bir arama kelimesi yazın veya Kategori / Dil filtresi seçin.", "Arama Bilgisi", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            LoadingText.Visibility = Visibility.Visible;
            EmptyText.Visibility = Visibility.Collapsed;
            ResultsList.ItemsSource = null;
            SearchButton.IsEnabled = false;
            AddAllButton.Visibility = Visibility.Collapsed;

            try
            {
                var allResults = new List<AceSearchResult>();
                string sourceTag = (SourceCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";

                LogService.LogInfo($"[Global Arama] Arama başlatıldı. Sorgu: '{query}', Kategori: '{category}', Dil: '{language}', Kaynak: '{sourceTag}'");

                if (sourceTag == "all" || sourceTag == "ace_engine")
                {
                    var localEngineResults = await SearchLocalEngineAsync(query, category, language);
                    allResults.AddRange(localEngineResults);
                }

                if (sourceTag == "all" || sourceTag == "ace_web")
                {
                    var aceWebResults = await SearchAceWebAsync(query, category, language, sortBy, sortOrder);
                    foreach (var r in aceWebResults)
                    {
                        if (!allResults.Exists(x => x.ContentId == r.ContentId))
                        {
                            allResults.Add(r);
                        }
                    }
                }

                if ((sourceTag == "all" || sourceTag == "freetuxtv") && !string.IsNullOrEmpty(query))
                {
                    var freetuxResults = await SearchFreetuxTVAsync(query);
                    foreach (var r in freetuxResults)
                    {
                        if (!allResults.Exists(x => x.ContentId == r.ContentId))
                        {
                            allResults.Add(r);
                        }
                    }
                }

                if (sourceTag == "iptvcat")
                {
                    // IPTVcat API hides stream URLs. Redirect user to browser securely.
                    string searchUrl = $"https://iptvcat.net/s/{Uri.EscapeDataString(query)}";
                    LogService.LogInfo($"[IPTVcat] Tarayıcıya yönlendiriliyor: {searchUrl}");
                    Process.Start(new ProcessStartInfo(searchUrl) { UseShellExecute = true });
                    LoadingText.Visibility = Visibility.Collapsed;
                    SearchButton.IsEnabled = true;
                    return;
                }

                LogService.LogInfo($"[Global Arama] Arama tamamlandı. Listelenen toplam benzersiz sonuç sayısı: {allResults.Count}");

                if (allResults.Count > 0)
                {
                    ResultsList.ItemsSource = allResults;
                    AddAllButton.Visibility = Visibility.Visible;
                    ResultCountBorder.Visibility = Visibility.Visible;
                    ResultCountText.Text = $"Bulunan içerik sayısı: {allResults.Count}";
                }
                else
                {
                    EmptyText.Visibility = Visibility.Visible;
                    ResultCountBorder.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("[Global Arama] Arama sırasında beklenmeyen bir hata oluştu.", ex);
                MessageBox.Show("İşlem sırasında beklenmeyen bir hata oluştu. Lütfen tekrar deneyiniz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                EmptyText.Visibility = Visibility.Visible;
            }
            finally
            {
                LoadingText.Visibility = Visibility.Collapsed;
                SearchButton.IsEnabled = true;
            }
        }

        private async Task<List<AceSearchResult>> SearchAceWebAsync(string query, string category, string language, string sortBy, string sortOrder)
        {
            var list = new List<AceSearchResult>();

            try
            {
                _httpClient.DefaultRequestHeaders.Remove("Accept");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                string cleanQ = query?.Trim() ?? "";
                LogService.LogInfo($"[AceStream Web Dizini] Dizin araması başlatılıyor... Sorgu: '{cleanQ}', Kategori: '{category}', Dil: '{language}'");

                for (int page = 1; page <= 10; page++)
                {
                    int offset = (page - 1) * 100;

                    var queryParams = new List<string>();
                    if (!string.IsNullOrEmpty(cleanQ))
                    {
                        queryParams.Add($"query={Uri.EscapeDataString(cleanQ)}");
                    }
                    if (!string.IsNullOrEmpty(category) && category != "all")
                    {
                        queryParams.Add($"category={Uri.EscapeDataString(category)}");
                    }
                    if (!string.IsNullOrEmpty(language) && language != "all")
                    {
                        queryParams.Add($"language={Uri.EscapeDataString(language)}");
                    }
                    if (!string.IsNullOrEmpty(sortBy))
                    {
                        queryParams.Add($"sort_by={Uri.EscapeDataString(sortBy)}");
                        if (!string.IsNullOrEmpty(sortOrder))
                        {
                            queryParams.Add($"sort_order={Uri.EscapeDataString(sortOrder)}");
                        }
                    }

                    string baseParams = string.Join("&", queryParams);
                    string paramSep = string.IsNullOrEmpty(baseParams) ? "" : "&";

                    string[] urls = new string[]
                    {
                        $"https://search-ace.stream/search?{baseParams}{paramSep}limit=300&page={page}",
                        $"https://search-ace.stream/search?{baseParams}{paramSep}limit=300&p={page}",
                        $"https://search-ace.stream/search?{baseParams}{paramSep}limit=300&offset={offset}"
                    };

                    bool addedInThisPage = false;

                    foreach (string url in urls)
                    {
                        try
                        {
                            HttpResponseMessage response = await _httpClient.GetAsync(url);
                            if (response.IsSuccessStatusCode)
                            {
                                string responseJson = await response.Content.ReadAsStringAsync();
                                var results = JsonSerializer.Deserialize<List<AceSearchResult>>(responseJson);
                                if (results != null && results.Count > 0)
                                {
                                    foreach (var r in results)
                                    {
                                        if (!list.Exists(x => x.ContentId == r.ContentId))
                                        {
                                            r.SourceName = "AceStream Web";
                                            list.Add(r);
                                            addedInThisPage = true;
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    if (!addedInThisPage) break;
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("[AceStream Web Dizini] Dizin arama hatası", ex);
            }

            LogService.LogInfo($"[AceStream Web Dizini] Arama tamamlandı. Bulunan içerik sayısı: {list.Count}");
            return list;
        }

        private async Task<List<AceSearchResult>> SearchLocalEngineAsync(string query, string category, string language)
        {
            var list = new List<AceSearchResult>();
            try
            {
                if (!_aceStreamService.IsRunning())
                {
                    LogService.LogInfo("[AceStream Yerel Engine] Motor çalışmıyor, otomatik olarak başlatılıyor...");
                    await _aceStreamService.StartEngineAsync();
                }

                string cleanQ = query?.Trim() ?? "";
                var queryParams = new List<string>();
                if (!string.IsNullOrEmpty(cleanQ))
                {
                    queryParams.Add($"query={Uri.EscapeDataString(cleanQ)}");
                }
                if (!string.IsNullOrEmpty(category) && category != "all")
                {
                    queryParams.Add($"category={Uri.EscapeDataString(category)}");
                }
                queryParams.Add("page=0");
                queryParams.Add("page_size=200");

                string url = $"http://127.0.0.1:6878/search?{string.Join("&", queryParams)}";
                LogService.LogInfo($"[AceStream Yerel Engine] Arama isteği gönderiliyor: {url}");

                HttpResponseMessage response = await _httpClient.GetAsync(url);
                LogService.LogInfo($"[AceStream Yerel Engine] Yanıt durumu: HTTP {(int)response.StatusCode} ({response.StatusCode})");

                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        if (doc.RootElement.TryGetProperty("result", out JsonElement resultElem))
                        {
                            if (resultElem.TryGetProperty("results", out JsonElement resultsArray) && resultsArray.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var group in resultsArray.EnumerateArray())
                                {
                                    string groupName = group.TryGetProperty("name", out var gn) ? gn.GetString() : null;

                                    if (group.TryGetProperty("items", out JsonElement itemsArray) && itemsArray.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var item in itemsArray.EnumerateArray())
                                        {
                                            string infohash = item.TryGetProperty("infohash", out var ih) ? ih.GetString() : null;
                                            string itemName = item.TryGetProperty("name", out var iname) ? iname.GetString() : groupName;

                                            if (!string.IsNullOrEmpty(infohash) && !string.IsNullOrEmpty(itemName))
                                            {
                                                if (!string.IsNullOrEmpty(language) && language != "all")
                                                {
                                                    bool langMatch = false;
                                                    if (item.TryGetProperty("languages", out JsonElement langsElem) && langsElem.ValueKind == JsonValueKind.Array)
                                                    {
                                                        foreach (var l in langsElem.EnumerateArray())
                                                        {
                                                            string lStr = l.GetString()?.ToLowerInvariant() ?? "";
                                                            if (lStr.Contains(language) ||
                                                                (language == "tr" && (lStr.Contains("tur") || lStr.Contains("tr"))) ||
                                                                (language == "de" && (lStr.Contains("deu") || lStr.Contains("ger") || lStr.Contains("de"))) ||
                                                                (language == "en" && (lStr.Contains("eng") || lStr.Contains("en"))))
                                                            {
                                                                langMatch = true;
                                                                break;
                                                            }
                                                        }
                                                    }
                                                    else
                                                    {
                                                        string nameLower = itemName.ToLowerInvariant();
                                                        if ((language == "tr" && (nameLower.Contains("tr") || nameLower.Contains("türk"))) ||
                                                            (language == "de" && (nameLower.Contains("de") || nameLower.Contains("ger"))) ||
                                                            (language == "en" && (nameLower.Contains("en") || nameLower.Contains("eng"))))
                                                        {
                                                            langMatch = true;
                                                        }
                                                    }

                                                    if (!langMatch) continue;
                                                }

                                                double avail = 0;
                                                if (item.TryGetProperty("availability", out JsonElement avElem))
                                                {
                                                    avElem.TryGetDouble(out avail);
                                                }

                                                string catLabel = "Yerel Motor";
                                                if (item.TryGetProperty("categories", out JsonElement catsElem) && catsElem.ValueKind == JsonValueKind.Array)
                                                {
                                                    var cats = new List<string>();
                                                    foreach (var c in catsElem.EnumerateArray()) cats.Add(c.GetString());
                                                    if (cats.Count > 0) catLabel = string.Join(", ", cats).ToUpper();
                                                }

                                                string translated = $"{catLabel} | Kullanılabilirlik: %{(int)(avail * 100)}";

                                                if (!list.Exists(x => x.ContentId == infohash))
                                                {
                                                    list.Add(new AceSearchResult
                                                    {
                                                        ContentId = infohash,
                                                        Name = itemName,
                                                        TranslatedName = translated,
                                                        SourceName = "AceEngine"
                                                    });
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    LogService.Log($"[AceStream Yerel Engine] Yerel motor isteği başarısız oldu: HTTP {(int)response.StatusCode}", "WARN");
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("[AceStream Yerel Engine] Yerel motor arama hatası (Motor veya P2P servisi yanıt vermiyor olabilir)", ex);
            }

            LogService.LogInfo($"[AceStream Yerel Engine] Yerel motorda bulunan içerik sayısı: {list.Count}");

            // Eğer yerel motor veritabanı boşsa veya sonuç dönmediyse, otomatik olarak Web AceStream veritabanından sorgula
            if (list.Count == 0)
            {
                LogService.LogInfo("[AceStream Yerel Engine] Yerel AceStream motorunda içerik bulunamadı. Otomatik olarak Web AceStream dizinine (search-ace.stream) geçiliyor...");
                var webBackup = await SearchAceWebAsync(query, category, language, "availability", "desc");
                list.AddRange(webBackup);
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

                string cleanQ = query.Trim();
                LogService.LogInfo($"[FreetuxTV] Arama başlatılıyor... Sorgu: '{cleanQ}'");

                for (int page = 1; page <= 10; page++)
                {
                    string url = $"https://database.freetuxtv.net/WebStream/index?WebStreamSearchForm%5BName%5D={Uri.EscapeDataString(cleanQ)}&page={page}";
                    string html = await _httpClient.GetStringAsync(url);

                    bool foundInPage = false;
                    var match = Regex.Match(html, @"<td>(.*?)<br />=&gt; <a href=""([^""]+)"">", RegexOptions.Singleline);
                    while (match.Success)
                    {
                        string rawName = match.Groups[1].Value;
                        string name = Regex.Replace(rawName, @"<[^>]+>", "").Trim();
                        string streamUrl = match.Groups[2].Value.Trim();

                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(streamUrl))
                        {
                            if (!list.Exists(x => x.ContentId == streamUrl))
                            {
                                list.Add(new AceSearchResult
                                {
                                    Name = name,
                                    ContentId = streamUrl, 
                                    TranslatedName = "FreetuxTV M3U",
                                    SourceName = "FreetuxTV"
                                });
                                foundInPage = true;
                            }
                        }

                        match = match.NextMatch();
                    }

                    if (!foundInPage) break;
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("[FreetuxTV] Arama hatası", ex);
            }

            LogService.LogInfo($"[FreetuxTV] Arama tamamlandı. Bulunan içerik sayısı: {list.Count}");
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

                    string rawContentId = result.ContentId?.Trim() ?? string.Empty;
                    bool isAceSource = (result.SourceName != null && result.SourceName.StartsWith("Ace", StringComparison.OrdinalIgnoreCase)) ||
                                       rawContentId.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase);

                    string streamUrl = rawContentId;
                    if (isAceSource && !rawContentId.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !rawContentId.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!rawContentId.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase))
                        {
                            streamUrl = $"acestream://{rawContentId}";
                        }
                    }

                    string sourceType = isAceSource ? "ACESTREAM" : (result.SourceName ?? "M3U");

                    var profile = StreamMesh.Services.Auth.UserService.GetProfile();
                    string defaultLang = (profile?.Languages != null && profile.Languages.Count > 0) ? profile.Languages[0] : "Türkçe";

                    var newChannel = new Channel
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Name = result.Name,
                        Url = streamUrl,
                        Category = "TV",
                        GroupTitle = (result.SourceName ?? "AceStream") + " Arama",
                        CreatedAt = DateTime.UtcNow,
                        IsFavorite = false,
                        SourceType = sourceType,
                        Language = defaultLang
                    };

                    new DatabaseService().SaveChannel(newChannel);
                    LogService.Log($"Kanal eklendi: {newChannel.Name} (URL: {newChannel.Url})");

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
                    string rawContentId = result.ContentId?.Trim() ?? string.Empty;
                    bool isAceSource = (result.SourceName != null && result.SourceName.StartsWith("Ace", StringComparison.OrdinalIgnoreCase)) ||
                                       rawContentId.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase);

                    string streamUrl = rawContentId;
                    if (isAceSource && !rawContentId.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !rawContentId.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!rawContentId.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase))
                        {
                            streamUrl = $"acestream://{rawContentId}";
                        }
                    }

                    string sourceType = isAceSource ? "ACESTREAM" : (result.SourceName ?? "M3U");

                    var newChannel = new Channel
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Name = result.Name,
                        Url = streamUrl,
                        Category = "TV",
                        GroupTitle = (result.SourceName ?? "AceStream") + " Arama",
                        CreatedAt = DateTime.UtcNow,
                        IsFavorite = false,
                        SourceType = sourceType,
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
