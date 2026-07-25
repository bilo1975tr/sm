using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StreamMesh.Services;
using StreamMesh.Models;

namespace StreamMesh.Views
{
    public partial class HomeView : UserControl
    {
        private DatabaseService _databaseService;
        private List<Channel> _allChannels;

        private string _selectedCategoryTag = "All";
        private string _selectedCategory = "Tümü";
        private int _currentPage = 1;
        private int _pageSize = 14;
        private int _totalPages = 1;
        private System.Windows.Threading.DispatcherTimer _viewerCountTimer;

        private bool _isChannelsLoaded = false;

        public HomeView()
        {
            InitializeComponent();
            _databaseService = new DatabaseService();

            _viewerCountTimer = new System.Windows.Threading.DispatcherTimer();
            _viewerCountTimer.Interval = TimeSpan.FromSeconds(20);
            _viewerCountTimer.Tick += (s, e) => UpdateViewerCountsAsync();
            _viewerCountTimer.Start();

            this.Unloaded += (s, e) =>
            {
                if (_viewerCountTimer != null)
                {
                    _viewerCountTimer.Stop();
                }
            };
        }

        public async void LoadChannels()
        {
            try
            {
                var channels = await System.Threading.Tasks.Task.Run(() => 
                {
                    _databaseService.SyncAndCleanPremiumChannels();
                    return _databaseService.GetAllChannels();
                });
                
                Dispatcher.Invoke(() => 
                {
                    _allChannels = channels;
                    _isChannelsLoaded = true;
                    UpdateViewerCountsAsync();
                    if (_currentPage < 1) _currentPage = 1;

                    if (StreamMesh.Services.Auth.UserService.CurrentUser != null)
                    {
                        if (StreamMesh.Services.Auth.UserService.CurrentUser.IsPremium)
                        {
                            SponsorBannerBorder.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            SponsorBannerBorder.Visibility = Visibility.Visible;
                            var refCode = StreamMesh.Services.Auth.UserService.CurrentUser.ReferralCode;
                            HomeAdText.Text = $"Arkadaşını Getir VIP Kazan!\nReferans Kodun: {refCode}";
                        }
                    }
                    else
                    {
                        SponsorBannerBorder.Visibility = Visibility.Visible;
                    }

                    bool isMovie = _selectedCategoryTag == "Movies";
                    if (isMovie && MovieFiltersPanel != null)
                    {
                        PopulateMovieFilters();
                    }

                    FilterChannels();
                });
            }
            catch (Exception ex)
            {
                LogService.LogError("HomeView LoadChannels error", ex);
            }
        }

        private async void UpdateViewerCountsAsync()
        {
            try
            {
                var viewerCounts = await ViewerTrackerService.Instance.FetchViewerCountsAsync();
                if (_allChannels != null && viewerCounts != null)
                {
                    foreach (var ch in _allChannels)
                    {
                        ch.ViewersCount = viewerCounts.TryGetValue(ch.Id, out var count) ? count : 0;
                    }

                    // UI'daki gruplanmış dizi kartlarını da güncelle
                    if (ChannelGrid.ItemsSource is IEnumerable<Channel> currentDisplay)
                    {
                        foreach (var displayCh in currentDisplay)
                        {
                            if (displayCh.IsSeriesGroup && displayCh.SeriesEpisodes != null)
                            {
                                displayCh.ViewersCount = displayCh.SeriesEpisodes.Sum(e => e.ViewersCount);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("UpdateViewerCountsAsync error", ex);
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isChannelsLoaded || _allChannels == null)
            {
                LoadChannels();
            }
        }

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.Visibility == Visibility.Visible && (!_isChannelsLoaded || _allChannels == null))
            {
                LoadChannels();
            }
        }

        private void SponsorBannerBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                var donationWindow = new Views.DonationWindow();
                donationWindow.Owner = window;
                donationWindow.ShowDialog();
                // Reload channels to hide banner if activated
                LoadChannels();
            }
        }

        private List<Channel> _filteredChannels;

        private void ViewModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModeComboBox?.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                string mode = item.Tag.ToString();
                bool isPoster = mode == "Poster";
                Channel.IsPosterMode = isPoster;
                if (_filteredChannels != null)
                {
                    FilterChannels();
                }
            }
        }

        private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_filteredChannels != null)
            {
                _currentPage = 1;
                FilterChannels();
            }
        }

        private async void FilterChannels()
        {
            if (_allChannels == null) return;

            // UI thread parametrelerini yakala
            string searchText = SearchBox?.Text?.ToLower() ?? "";
            string catTag = _selectedCategoryTag;
            int curPage = _currentPage;
            int pageSize = _pageSize;

            string selectedGenre = "Hepsi";
            string selectedYear = "Hepsi";
            string selectedImdbText = "Hepsi";

            if (catTag == "Movies")
            {
                if (MovieGenreComboBox?.SelectedItem is ComboBoxItem genreItem) selectedGenre = genreItem.Content.ToString();
                if (MovieYearComboBox?.SelectedItem is ComboBoxItem yearItem) selectedYear = yearItem.Content.ToString();
                if (MovieImdbComboBox?.SelectedItem is ComboBoxItem imdbItem) selectedImdbText = imdbItem.Content.ToString();
            }

            string sortType = "Alfabetik (A-Z)";
            if (SortComboBox?.SelectedItem is ComboBoxItem sortItem)
            {
                sortType = sortItem.Content.ToString();
            }

            var profile = StreamMesh.Services.Auth.UserService.GetProfile();
            var recentProgress = _databaseService.GetAllWatchProgress();
            var sourceChannels = _allChannels.ToList();

            await System.Threading.Tasks.Task.Run(() =>
            {
                var filtered = string.IsNullOrWhiteSpace(searchText) 
                    ? sourceChannels 
                    : sourceChannels.Where(c => 
                        (c.Name != null && c.Name.ToLower().Contains(searchText)) || 
                        (c.GroupTitle != null && c.GroupTitle.ToLower().Contains(searchText))).ToList();

                if (catTag == "Favorites")
                {
                    filtered = filtered.Where(c => c.IsFavorite).ToList();
                }
                else if (catTag == "Recent")
                {
                    filtered = filtered
                        .Where(c => recentProgress.ContainsKey(c.Id))
                        .OrderByDescending(c => recentProgress[c.Id].LastWatched)
                        .Take(50)
                        .ToList();
                }
                else if (catTag != "All")
                {
                    string targetCategory = "TV";
                    if (catTag == "Movies") targetCategory = "Film";
                    else if (catTag == "Series") targetCategory = "Dizi";
                    else if (catTag == "Radio") targetCategory = "Radyo";
                    else targetCategory = catTag;

                    string catUpper = targetCategory.ToUpper().Trim();
                    if (catUpper.Contains("RADYO") || catUpper.Contains("RADIO"))
                    {
                        filtered = filtered.Where(c => 
                            c.Category != null && 
                            (c.Category.ToUpper().Contains("RADYO") || c.Category.ToUpper().Contains("RADIO"))
                        ).ToList();
                    }
                    else
                    {
                        filtered = filtered.Where(c => 
                            c.Category != null && 
                            (c.Category.ToUpper().Contains(catUpper) || catUpper.Contains(c.Category.ToUpper()))
                        ).ToList();
                    }
                }

                // Film özel filtreleri
                if (catTag == "Movies")
                {
                    if (selectedGenre != "Hepsi")
                    {
                        filtered = filtered.Where(c => 
                            c.MovieGenre != null && 
                            c.MovieGenre.IndexOf(selectedGenre, StringComparison.OrdinalIgnoreCase) >= 0
                        ).ToList();
                    }

                    if (selectedYear != "Hepsi")
                    {
                        filtered = filtered.Where(c => c.MovieYear != null && c.MovieYear.Equals(selectedYear, StringComparison.OrdinalIgnoreCase)).ToList();
                    }

                    if (selectedImdbText != "Hepsi")
                    {
                        double minImdb = 0.0;
                        if (selectedImdbText.Contains("8.5")) minImdb = 8.5;
                        else if (selectedImdbText.Contains("8.0")) minImdb = 8.0;
                        else if (selectedImdbText.Contains("7.0")) minImdb = 7.0;
                        else if (selectedImdbText.Contains("6.0")) minImdb = 6.0;

                        filtered = filtered.Where(c => 
                        {
                            if (double.TryParse(c.ImdbRating, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double rating))
                            {
                                return rating >= minImdb;
                            }
                            return false;
                        }).ToList();
                    }
                }

                // Dil filtresi (Çoklu dil virgüllü destek)
                if (profile != null && profile.Languages != null && profile.Languages.Count > 0)
                {
                    bool showAllLangs = profile.Languages.Any(l => 
                        !string.IsNullOrEmpty(l) && 
                        (l.Equals("Tümü", StringComparison.OrdinalIgnoreCase) || 
                         l.Equals("Hepsi", StringComparison.OrdinalIgnoreCase) || 
                         l.Equals("All", StringComparison.OrdinalIgnoreCase))
                    );

                    if (!showAllLangs)
                    {
                        var activeLangs = profile.Languages
                            .Where(l => !string.IsNullOrEmpty(l) && l != "Hiçbiri")
                            .Select(Channel.NormalizeLanguage)
                            .ToList();

                        if (activeLangs.Count > 0)
                        {
                            filtered = filtered.Where(c => 
                            {
                                if (string.IsNullOrEmpty(c.Language) || c.Language == "Bilinmiyor" || c.Language == "und")
                                    return true;

                                var chLangs = c.Language.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                        .Select(Channel.NormalizeLanguage);
                                return chLangs.Any(l => activeLangs.Contains(l));
                            }).ToList();
                        }
                    }
                }

                // Dizi gruplama
                var groupedResult = new List<Channel>();
                var diziList = filtered.Where(c => c.Category != null && c.Category.Equals("Dizi", StringComparison.OrdinalIgnoreCase)).ToList();
                var otherList = filtered.Where(c => c.Category == null || !c.Category.Equals("Dizi", StringComparison.OrdinalIgnoreCase)).ToList();

                if (diziList.Count > 0)
                {
                    var groups = diziList.GroupBy(c => {
                        var det = Channel.ParseSeriesDetails(c.Name, c.Url);
                        return (det.SeriesName.ToLowerInvariant().Trim(), det.Year);
                    }).ToList();

                    foreach (var g in groups)
                    {
                        var episodes = g.OrderBy(e => {
                            var det = Channel.ParseSeriesDetails(e.Name, e.Url);
                            return det.Season * 10000 + det.Episode;
                        }).ToList();

                        var firstEp = episodes[0];
                        var seriesDet = Channel.ParseSeriesDetails(firstEp.Name, firstEp.Url);

                        var repChannel = new Channel
                        {
                            Id = firstEp.Id,
                            Name = seriesDet.SeriesName,
                            Category = "Dizi",
                            LogoUrl = firstEp.LogoUrl,
                            GroupTitle = "Dizi",
                            Language = firstEp.Language,
                            IsFavorite = episodes.Any(e => e.IsFavorite),
                            IsVerified = episodes.All(e => e.IsVerified),
                            CreatedAt = episodes.Max(e => e.CreatedAt),
                            Url = firstEp.Url,
                            IsSeriesGroup = true,
                            SeriesEpisodes = episodes,
                            SeriesName = seriesDet.SeriesName,
                            TotalSeasonsCount = episodes.Select(e => Channel.ParseSeriesDetails(e.Name, e.Url).Season).Distinct().Count(),
                            TotalEpisodesCount = episodes.Count,
                            PersonalWatchCount = episodes.Sum(e => e.PersonalWatchCount),
                            ViewersCount = episodes.Sum(e => e.ViewersCount)
                        };

                        groupedResult.Add(repChannel);
                    }
                }
                groupedResult.AddRange(otherList);
                filtered = groupedResult;

                // Sıralama
                switch (sortType)
                {
                    case "Alfabetik (A-Z)":
                        filtered = filtered.OrderBy(c => c.Name).ToList();
                        break;
                    case "Alfabetik (Z-A)":
                        filtered = filtered.OrderByDescending(c => c.Name).ToList();
                        break;
                    case "Eklenme (Yeni)":
                        filtered = filtered.OrderByDescending(c => c.CreatedAt).ToList();
                        break;
                    case "Eklenme (Eski)":
                        filtered = filtered.OrderBy(c => c.CreatedAt).ToList();
                        break;
                    case "Favoriler Önce":
                        filtered = filtered.OrderByDescending(c => c.IsFavorite).ThenBy(c => c.Name).ToList();
                        break;
                    case "Çok İzlenenler":
                        filtered = filtered.OrderByDescending(c => c.ViewersCount).ToList();
                        break;
                    case "Sizin Çok İzledikleriniz":
                        filtered = filtered.OrderByDescending(c => c.PersonalWatchCount).ToList();
                        break;
                    default:
                        filtered = filtered.OrderBy(c => c.Name).ToList();
                        break;
                }

                int totalPages = (int)Math.Ceiling(filtered.Count / (double)pageSize);
                if (totalPages == 0) totalPages = 1;
                if (curPage > totalPages) curPage = totalPages;

                var paged = filtered.Skip((curPage - 1) * pageSize).Take(pageSize).ToList();

                // EPG eşleştirme
                var epgService = new StreamMesh.Services.EpgService();
                var epgDict = epgService.GetCurrentEpgsForChannels(paged);

                foreach (var ch in paged)
                {
                    if (epgDict.TryGetValue(ch.Id, out var curEpg))
                    {
                        ch.CurrentEpgTitle = curEpg.Title;
                        ch.CurrentEpgTime = $"{curEpg.StartTime:HH:mm} - {curEpg.EndTime:HH:mm}";
                    }
                    else
                    {
                        ch.CurrentEpgTitle = "EPG Bulunamadı";
                        ch.CurrentEpgTime = "--:--";
                    }
                }

                // UI güncellemesi
                Dispatcher.Invoke(() =>
                {
                    _filteredChannels = filtered;
                    _totalPages = totalPages;
                    _currentPage = curPage;

                    ChannelGrid.ItemsSource = paged;
                    TotalCountText.Text = string.Format(LocalizationManager.Instance["Home_Total"], _filteredChannels.Count);

                    if (PageInfoText != null)
                    {
                        PageInfoText.Text = $"{_currentPage} / {_totalPages}";
                    }
                    if (PrevPageBtn != null) PrevPageBtn.IsEnabled = _currentPage > 1;
                    if (NextPageBtn != null) NextPageBtn.IsEnabled = _currentPage < _totalPages;
                });

                // Arka plan metadata zenginleştirme
                System.Threading.Tasks.Task.Run(async () =>
                {
                    foreach (var ch in paged)
                    {
                        if (ch.Category == "Film" || ch.Category == "Dizi" || ch.Category == "Movies" || ch.Category == "Series")
                        {
                            await MetadataService.EnrichChannelMetadataAsync(ch, _databaseService);
                        }
                    }
                });
            });
        }

        private void Category_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                // Reset styles
                foreach (UIElement child in CategoryPanel.Children)
                {
                    if (child is Button b)
                    {
                        b.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#334155"));
                    }
                }

                // Set active style
                btn.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#38bdf8")); // Primary color
                _selectedCategoryTag = btn.Tag?.ToString() ?? "All";
                _selectedCategory = btn.Content.ToString();

                bool isMovie = _selectedCategoryTag == "Movies";

                if (MovieFiltersPanel != null)
                {
                    MovieFiltersPanel.Visibility = isMovie ? Visibility.Visible : Visibility.Collapsed;
                    if (isMovie)
                    {
                        PopulateMovieFilters();
                    }
                }
                
                _currentPage = 1;
                FilterChannels();
            }
        }

        private bool _isPopulatingFilters = false;
        private void PopulateMovieFilters()
        {
            if (_allChannels == null || _isPopulatingFilters) return;
            _isPopulatingFilters = true;

            try
            {
                var movieChannels = _allChannels.Where(c => c.Category == "Film").ToList();

                // Türleri tekil olarak bölüp çekelim (örnek: "Aksiyon / Dram" -> "Aksiyon", "Dram")
                var genres = movieChannels
                    .Where(c => !string.IsNullOrEmpty(c.MovieGenre))
                    .SelectMany(c => c.MovieGenre.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
                    .Select(g => g.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g)
                    .ToList();

                if (MovieGenreComboBox != null)
                {
                    var selectedItem = MovieGenreComboBox.SelectedItem as ComboBoxItem;
                    string previousSelectedGenre = selectedItem != null ? selectedItem.Content.ToString() : "Hepsi";

                    MovieGenreComboBox.Items.Clear();
                    var defaultItem = new ComboBoxItem { Content = "Hepsi" };
                    MovieGenreComboBox.Items.Add(defaultItem);
                    MovieGenreComboBox.SelectedItem = defaultItem;

                    foreach (var genre in genres)
                    {
                        var item = new ComboBoxItem { Content = genre };
                        MovieGenreComboBox.Items.Add(item);
                        if (genre.Equals(previousSelectedGenre, StringComparison.OrdinalIgnoreCase))
                        {
                            MovieGenreComboBox.SelectedItem = item;
                        }
                    }
                }

                // Yılları çekelim
                var years = movieChannels
                    .Where(c => !string.IsNullOrEmpty(c.MovieYear))
                    .Select(c => c.MovieYear.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(y => y)
                    .ToList();

                if (MovieYearComboBox != null)
                {
                    var selectedItem = MovieYearComboBox.SelectedItem as ComboBoxItem;
                    string previousSelectedYear = selectedItem != null ? selectedItem.Content.ToString() : "Hepsi";

                    MovieYearComboBox.Items.Clear();
                    var defaultItem = new ComboBoxItem { Content = "Hepsi" };
                    MovieYearComboBox.Items.Add(defaultItem);
                    MovieYearComboBox.SelectedItem = defaultItem;

                    foreach (var year in years)
                    {
                        var item = new ComboBoxItem { Content = year };
                        MovieYearComboBox.Items.Add(item);
                        if (year.Equals(previousSelectedYear, StringComparison.OrdinalIgnoreCase))
                        {
                            MovieYearComboBox.SelectedItem = item;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("PopulateMovieFilters failed", ex);
            }
            finally
            {
                _isPopulatingFilters = false;
            }
        }

        private void MovieFilters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isPopulatingFilters)
            {
                _currentPage = 1;
                FilterChannels();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = SearchBox.Text.ToLower().Trim();
            if (query == "i am prenses")
            {
                SearchBox.Text = "";
                var window = new StreamMesh.Windows.AdvancedChannelEditorWindow();
                window.ShowDialog();
                LoadChannels();
                return;
            }

            _currentPage = 1;
            FilterChannels();
        }

        private async void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Tab && !string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                e.Handled = true;
                string query = SearchBox.Text;

                // Show thinking status
                TotalCountText.Text = "AI Akıllı Arama yapıyor...";

                try
                {
                    var aiService = new OllamaChatService();
                    var result = await aiService.AskOllama(
                        $"Kullanıcı şu aramayı yaptı: '{query}'. Lütfen bu aramaya en uygun kanalları bulmamı sağlayacak bir SQL WHERE cümlesi üret. Sadece WHERE cümlesini döndür. Örn: Name LIKE '%haber%' AND Language = 'Türkçe'",
                        "DATABASE_SEARCH_HELPER");

                    if (result.Contains("[SQL:"))
                    {
                        int start = result.IndexOf("[SQL:") + 5;
                        int end = result.IndexOf("]", start);
                        string sql = result.Substring(start, end - start).Trim();
                        if (sql.ToUpper().StartsWith("SELECT"))
                        {
                            // If AI returned full SELECT, try to extract WHERE part or just use it
                            _filteredChannels = await Task.Run(() => {
                                var rows = _databaseService.ExecuteRawQuery(sql);
                                var ids = rows.Select(r => r["Id"]?.ToString()).Where(id => id != null).ToList();
                                return _allChannels.Where(c => ids.Contains(c.Id)).ToList();
                            });

                            _currentPage = 1;
                            // Skip normal FilterChannels() because we have custom AI results
                            UpdateUiWithFilteredResults();
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("AI Search failed", ex);
                }
            }
        }

        private void UpdateUiWithFilteredResults()
        {
            _totalPages = (int)Math.Ceiling(_filteredChannels.Count / (double)_pageSize);
            if (_totalPages == 0) _totalPages = 1;
            if (_currentPage > _totalPages) _currentPage = _totalPages;

            var paged = _filteredChannels.Skip((_currentPage - 1) * _pageSize).Take(_pageSize).ToList();
            ChannelGrid.ItemsSource = paged;
            TotalCountText.Text = $"AI Sonucu: {_filteredChannels.Count} içerik bulundu.";

            if (PageInfoText != null) PageInfoText.Text = $"{_currentPage} / {_totalPages}";
            PrevPageBtn.IsEnabled = _currentPage > 1;
            NextPageBtn.IsEnabled = _currentPage < _totalPages;
        }

        private void PrevPageBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                FilterChannels();
            }
        }

        private void NextPageBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage < _totalPages)
            {
                _currentPage++;
                FilterChannels();
            }
        }

        private void CardBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) // Only play if user just clicked, not dragged
            {
                if (sender is Border border && border.DataContext is Channel channel)
                {
                    if (channel.IsSeriesGroup)
                    {
                        var win = new StreamMesh.Windows.SeriesSelectionWindow(channel);
                        win.Owner = Window.GetWindow(this);
                        if (win.ShowDialog() == true && win.SelectedEpisode != null)
                        {
                            OnChannelSelected(win.SelectedEpisode, channel.SeriesEpisodes);
                        }
                    }
                    else
                    {
                        OnChannelSelected(channel, _filteredChannels);
                    }
                }
            }
            _isDragging = false;
        }

        public delegate void ChannelSelectedHandler(Channel channel, List<Channel> playlist);
        public event ChannelSelectedHandler ChannelSelectedEvent;

        protected virtual void OnChannelSelected(Channel channel, List<Channel> playlist)
        {
            ChannelSelectedEvent?.Invoke(channel, playlist);
        }

        private void PlayContext_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is Channel channel)
            {
                if (channel.IsSeriesGroup)
                {
                    var win = new StreamMesh.Windows.SeriesSelectionWindow(channel);
                    win.Owner = Window.GetWindow(this);
                    if (win.ShowDialog() == true && win.SelectedEpisode != null)
                    {
                        OnChannelSelected(win.SelectedEpisode, channel.SeriesEpisodes);
                    }
                }
                else
                {
                    OnChannelSelected(channel, _filteredChannels);
                }
            }
        }

        private void ToggleFavoriteContext_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is Channel channel)
            {
                channel.IsFavorite = !channel.IsFavorite;
                _databaseService.SaveChannel(channel);
                FilterChannels(); // Canlı yayında filtreyi güncellemek veya UI'ı tazelemek için
            }
        }

        private void EditContext_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is Channel channel)
            {
                // Show Edit Channel window
                var editWindow = new StreamMesh.Windows.EditChannelWindow(channel);
                if (editWindow.ShowDialog() == true)
                {
                    _databaseService.SaveChannel(channel);
                    LoadChannels(); // Refresh the list
                }
            }
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is Channel channel)
            {
                var editWindow = new StreamMesh.Windows.EditChannelWindow(channel);
                if (editWindow.ShowDialog() == true)
                {
                    _databaseService.SaveChannel(channel);
                    LoadChannels(); // Refresh the list
                }
            }
        }

        private void DeleteContext_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is Channel channel)
            {
                var result = MessageBox.Show($"'{channel.Name}' adlı kanalı silmek istediğinize emin misiniz?", "Kanalı Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    _databaseService.DeleteChannel(channel.Id);
                    LoadChannels(); // Refresh the list
                }
            }
        }

        private Point _startPoint;
        private bool _isDragging = false;

        private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(null);
            _isDragging = false;
        }

        private void DragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _startPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;
                    var border = sender as Border;
                    var channel = border?.DataContext as Channel;

                    if (channel != null)
                    {
                        var data = new DataObject("DraggedChannel", channel);
                        DragDrop.DoDragDrop(border, data, DragDropEffects.Move);
                        _isDragging = false;
                    }
                }
            }
        }

        private void CardBorder_Drop(object sender, DragEventArgs e)
        {
            _isDragging = false;
            // ...
            if (e.Data.GetDataPresent("DraggedChannel"))
            {
                var sourceChannel = e.Data.GetData("DraggedChannel") as Channel;
                var targetBorder = sender as Border;
                var targetChannel = targetBorder?.DataContext as Channel;

                if (sourceChannel != null && targetChannel != null && sourceChannel.Id != targetChannel.Id)
                {
                    var result = MessageBox.Show($"'{sourceChannel.Name}' kanalını '{targetChannel.Name}' kanalı ile birleştirmek istiyor musunuz?", "Kanalları Birleştir", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        if (!targetChannel.Url.Contains(sourceChannel.Url))
                        {
                            targetChannel.Url += "," + sourceChannel.Url;
                        }
                        
                        if (!string.IsNullOrEmpty(sourceChannel.LogoUrl) && !targetChannel.LogoUrl.Contains(sourceChannel.LogoUrl))
                        {
                            targetChannel.LogoUrl += "," + sourceChannel.LogoUrl;
                        }

                        _databaseService.SaveChannel(targetChannel);
                        _databaseService.DeleteChannel(sourceChannel.Id);
                        
                        LoadChannels();
                        MessageBox.Show("Kanallar başarıyla birleştirildi.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }

        private void CardBorder_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("DraggedChannel"))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private string NormalizeLanguage(string lang)
        {
            return StreamMesh.Models.Channel.NormalizeLanguage(lang).ToLower(new System.Globalization.CultureInfo("tr-TR"));
        }
    }
}
