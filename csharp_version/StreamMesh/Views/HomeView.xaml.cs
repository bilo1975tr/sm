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

        private string _selectedCategory = "Tümü";
        private int _currentPage = 1;
        private int _pageSize = 14;
        private int _totalPages = 1;

        public HomeView()
        {
            InitializeComponent();
            _databaseService = new DatabaseService();
            LoadChannels();
        }

        public void LoadChannels()
        {
            _allChannels = _databaseService.GetAllChannels();
            _currentPage = 1;

            if (StreamMesh.Services.P2P.UserService.CurrentUser != null)
            {
                if (StreamMesh.Services.P2P.UserService.CurrentUser.IsPremium)
                {
                    SponsorBannerBorder.Visibility = Visibility.Collapsed;
                }
                else
                {
                    SponsorBannerBorder.Visibility = Visibility.Visible;
                    var refCode = StreamMesh.Services.P2P.UserService.CurrentUser.ReferralCode;
                    HomeAdText.Text = $"Arkadaşını Getir VIP Kazan!\nReferans Kodun: {refCode}";
                }
            }
            else
            {
                SponsorBannerBorder.Visibility = Visibility.Visible;
            }

            bool isMovie = _selectedCategory != null && 
                           (_selectedCategory.ToUpper().Contains("FİLM") || 
                            _selectedCategory.ToUpper().Contains("FILM") || 
                            _selectedCategory.ToUpper().Contains("MOVIE"));
            if (isMovie && MovieFiltersPanel != null)
            {
                PopulateMovieFilters();
            }

            FilterChannels();
        }

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.Visibility == Visibility.Visible)
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

        private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_filteredChannels != null)
            {
                _currentPage = 1;
                FilterChannels();
            }
        }

        private void FilterChannels()
        {
            if (_allChannels == null) return;

            string searchText = SearchBox.Text.ToLower();
            _filteredChannels = string.IsNullOrWhiteSpace(searchText) 
                ? _allChannels 
                : _allChannels.Where(c => 
                    (c.Name != null && c.Name.ToLower().Contains(searchText)) || 
                    (c.GroupTitle != null && c.GroupTitle.ToLower().Contains(searchText))).ToList();

            if (_selectedCategory == "Favoriler ⭐")
            {
                _filteredChannels = _filteredChannels.Where(c => c.IsFavorite).ToList();
            }
            else if (_selectedCategory != "Tümü" && _selectedCategory != "All")
            {
                // Kategori ismini daha esnek kontrol et (örn: "Belgesel" hem "Belgesel" hem "Belgesel [TV]" için çalışsın)
                string catUpper = _selectedCategory.ToUpper().Trim();
                if (catUpper.Contains("RADYO") || catUpper.Contains("RADIO"))
                {
                    _filteredChannels = _filteredChannels.Where(c => 
                        c.Category != null && 
                        (c.Category.ToUpper().Contains("RADYO") || c.Category.ToUpper().Contains("RADIO"))
                    ).ToList();
                }
                else
                {
                    _filteredChannels = _filteredChannels.Where(c => 
                        c.Category != null && 
                        (c.Category.ToUpper().Contains(catUpper) || catUpper.Contains(c.Category.ToUpper()))
                    ).ToList();
                }
            }

            // Film özel filtrelerini uygula
            bool isMovieCat = _selectedCategory != null && 
                              (_selectedCategory.ToUpper().Contains("FİLM") || 
                               _selectedCategory.ToUpper().Contains("FILM") || 
                               _selectedCategory.ToUpper().Contains("MOVIE"));
            
            if (isMovieCat)
            {
                // 1. Film Türü Filtresi
                if (MovieGenreComboBox != null && MovieGenreComboBox.SelectedItem is ComboBoxItem genreItem)
                {
                    string selectedGenre = genreItem.Content.ToString();
                    if (selectedGenre != "Hepsi")
                    {
                        _filteredChannels = _filteredChannels.Where(c => c.MovieGenre != null && c.MovieGenre.Equals(selectedGenre, StringComparison.OrdinalIgnoreCase)).ToList();
                    }
                }

                // 2. Yapım Yılı Filtresi
                if (MovieYearComboBox != null && MovieYearComboBox.SelectedItem is ComboBoxItem yearItem)
                {
                    string selectedYear = yearItem.Content.ToString();
                    if (selectedYear != "Hepsi")
                    {
                        _filteredChannels = _filteredChannels.Where(c => c.MovieYear != null && c.MovieYear.Equals(selectedYear, StringComparison.OrdinalIgnoreCase)).ToList();
                    }
                }

                // 3. Minimum IMDb Puanı Filtresi (IMDb süzgeci)
                if (MovieImdbComboBox != null && MovieImdbComboBox.SelectedItem is ComboBoxItem imdbItem)
                {
                    string selectedImdbText = imdbItem.Content.ToString();
                    if (selectedImdbText != "Hepsi")
                    {
                        double minImdb = 0.0;
                        if (selectedImdbText.Contains("8.5")) minImdb = 8.5;
                        else if (selectedImdbText.Contains("8.0")) minImdb = 8.0;
                        else if (selectedImdbText.Contains("7.0")) minImdb = 7.0;
                        else if (selectedImdbText.Contains("6.0")) minImdb = 6.0;

                        _filteredChannels = _filteredChannels.Where(c => 
                        {
                            if (double.TryParse(c.ImdbRating, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double rating))
                            {
                                return rating >= minImdb;
                            }
                            return false;
                        }).ToList();
                    }
                }
            }

            // Dil filtresini (profile.Languages) ve dil dengelemeyi/normalizasyonu uygula
            var profile = StreamMesh.Services.P2P.UserService.GetProfile();
            if (profile != null && profile.Languages != null && profile.Languages.Count > 0)
            {
                var activeLangs = profile.Languages
                    .Where(l => !string.IsNullOrEmpty(l) && l != "Hiçbiri")
                    .Select(NormalizeLanguage)
                    .ToList();

                if (activeLangs.Count > 0)
                {
                    _filteredChannels = _filteredChannels.Where(c => 
                        string.IsNullOrEmpty(c.Language) || 
                        c.Language == "Bilinmiyor" || 
                        activeLangs.Contains(NormalizeLanguage(c.Language))
                    ).ToList();
                }
            }

            // Apply Sorting
            if (SortComboBox != null && SortComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string sortType = selectedItem.Content.ToString();
                switch (sortType)
                {
                    case "Alfabetik (A-Z)":
                        _filteredChannels = _filteredChannels.OrderBy(c => c.Name).ToList();
                        break;
                    case "Alfabetik (Z-A)":
                        _filteredChannels = _filteredChannels.OrderByDescending(c => c.Name).ToList();
                        break;
                    case "Eklenme (Yeni)":
                        _filteredChannels = _filteredChannels.OrderByDescending(c => c.CreatedAt).ToList();
                        break;
                    case "Eklenme (Eski)":
                        _filteredChannels = _filteredChannels.OrderBy(c => c.CreatedAt).ToList();
                        break;
                    case "Favoriler Önce":
                        _filteredChannels = _filteredChannels.OrderByDescending(c => c.IsFavorite).ThenBy(c => c.Name).ToList();
                        break;
                    case "Çok İzlenenler":
                        _filteredChannels = _filteredChannels.OrderByDescending(c => c.ViewersCount).ToList();
                        break;
                    case "Sizin Çok İzledikleriniz":
                        _filteredChannels = _filteredChannels.OrderByDescending(c => c.PersonalWatchCount).ToList();
                        break;
                }
            }
            else
            {
                _filteredChannels = _filteredChannels.OrderBy(c => c.Name).ToList();
            }

            _totalPages = (int)Math.Ceiling(_filteredChannels.Count / (double)_pageSize);
            if (_totalPages == 0) _totalPages = 1;
            if (_currentPage > _totalPages) _currentPage = 1;

            var paged = _filteredChannels.Skip((_currentPage - 1) * _pageSize).Take(_pageSize).ToList();

            var epgService = new StreamMesh.Services.EpgService();
            StreamMesh.Services.LogService.Log($"[HomeView] Sayfadaki {paged.Count} kanal için TOPLU EPG aranıyor...");
            
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

            ChannelGrid.ItemsSource = paged;
            TotalCountText.Text = string.Format(LocalizationManager.Instance["Home_Total"], _filteredChannels.Count);
            
            // Eğer Localization içinde Home_Page yoksa null dönebiliyor bu yüzden direkt gösteriyoruz
            if (PageInfoText != null)
            {
                PageInfoText.Text = $"{_currentPage} / {_totalPages}";
            }
            PrevPageBtn.IsEnabled = _currentPage > 1;
            NextPageBtn.IsEnabled = _currentPage < _totalPages;
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
                _selectedCategory = btn.Content.ToString();

                bool isMovie = _selectedCategory.ToUpper().Contains("FİLM") || 
                               _selectedCategory.ToUpper().Contains("FILM") || 
                               _selectedCategory.ToUpper().Contains("MOVIE");

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

                // Türleri çekelim
                var genres = movieChannels
                    .Where(c => !string.IsNullOrEmpty(c.MovieGenre))
                    .Select(c => c.MovieGenre.Trim())
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
                    OnChannelSelected(channel, _filteredChannels);
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
                OnChannelSelected(channel, _filteredChannels);
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
            if (string.IsNullOrEmpty(lang)) return "";
            string lower = lang.ToLower(new System.Globalization.CultureInfo("tr-TR")).Trim();
            
            if (lower.Contains("türkçe") || lower.Contains("turkce") || lower == "tr" || lower == "tur" || lower.Contains("turkish")) return "türkçe";
            if (lower.Contains("ingilizce") || lower.Contains("english") || lower == "en" || lower == "eng" || lower == "usa" || lower == "uk") return "ingilizce";
            if (lower.Contains("almanca") || lower.Contains("deutsch") || lower.Contains("german") || lower == "de" || lower == "ger") return "almanca";
            if (lower.Contains("fransızca") || lower.Contains("french") || lower.Contains("français") || lower == "fr" || lower == "fra") return "fransızca";
            if (lower.Contains("ispanyolca") || lower.Contains("spanish") || lower.Contains("español") || lower == "es" || lower == "esp") return "ispanyolca";
            if (lower.Contains("rusça") || lower.Contains("russian") || lower.Contains("русский") || lower == "ru" || lower == "rus") return "rusça";
            if (lower.Contains("italyanca") || lower.Contains("italian") || lower.Contains("italiano") || lower == "it" || lower == "ita") return "italyanca";
            if (lower.Contains("arapça") || lower.Contains("arabic") || lower == "ar" || lower == "ara") return "arapça";
            if (lower.Contains("kurtçe") || lower.Contains("kürtçe") || lower.Contains("kurdish") || lower == "ku" || lower == "kur") return "kürtçe";
            if (lower.Contains("azerice") || lower.Contains("azerbaijani") || lower.Contains("azeri") || lower == "az" || lower == "aze") return "azerice";
            if (lower == "bilinmiyor" || lower == "unknown") return "bilinmiyor";

            int parenIndex = lower.IndexOf('(');
            if (parenIndex > 0)
            {
                lower = lower.Substring(0, parenIndex).Trim();
            }
            return lower;
        }
    }
}
