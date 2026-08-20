using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using StreamMesh.Models;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media;
using StreamMesh.Core.Utils;

namespace StreamMesh.UI.ViewModels
{
    public class HomeViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseEngine _db = new DatabaseEngine();
        private readonly EpgService _epg = new EpgService();
        private List<Channel> _allChannels = new List<Channel>();
        private List<Channel> _filteredChannels = new List<Channel>();

        private string? _currentBackdrop;
        public string? CurrentBackdrop
        {
            get => _currentBackdrop;
            set { _currentBackdrop = value; OnPropertyChanged(); }
        }

        private int _dailyApiCount;
        public int DailyApiCount
        {
            get => _dailyApiCount;
            set { _dailyApiCount = value; OnPropertyChanged(); }
        }

        private string _totalCountText = "Yükleniyor...";
        public string TotalCountText
        {
            get => _totalCountText;
            set { _totalCountText = value; OnPropertyChanged(); }
        }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                _ = DebouncedRefreshDisplayAsync();
            }
        }

        private System.Threading.CancellationTokenSource? _searchCts;
        private async Task DebouncedRefreshDisplayAsync()
        {
            _searchCts?.Cancel();
            _searchCts = new System.Threading.CancellationTokenSource();
            try
            {
                await Task.Delay(300, _searchCts.Token);
                RefreshDisplay();
            }
            catch (TaskCanceledException) { }
        }

        private int _currentPage = 1;
        private int _pageSize = 24;
        private int _totalPages = 1;

        private string _activeCategory = "All";

        public ObservableCollection<Channel> DisplayedChannels { get; set; } = new ObservableCollection<Channel>();

        public string CurrentPageText => $"Sayfa {_currentPage} / {_totalPages}";

        private bool _isSyncing = false;
        private bool _isDataDirty = false;

        public HomeViewModel()
        {
            LoadData();
            GitHubSyncEngine.OnSyncStarted += () => {
                _isSyncing = true;
                _isDataDirty = false;
            };
            GitHubSyncEngine.OnSyncCompleted += () => {
                _isSyncing = false;
                if (_isDataDirty) System.Windows.Application.Current?.Dispatcher.Invoke(() => LoadData());
                _isDataDirty = false;
            };
            DatabaseEngine.OnDatabaseUpdated += (s, e) => {
                if (_isSyncing)
                {
                    _isDataDirty = true;
                }
                else
                {
                    // V1.8.8: Small delay to debounce rapid updates
                    _ = Task.Delay(1000).ContinueWith(_ => System.Windows.Application.Current?.Dispatcher.Invoke(() => LoadData()));
                }
            };
        }

        public async void LoadData()
        {
            await Task.Run(async () =>
            {
                try
                {
                    LogService.LogInfo("HomeViewModel: Kütüphane yükleniyor...");
                    var stats = _db.GetDailyQueryStats();
                    var channels = await _db.GetAllChannelsAsync();
                    LogService.LogInfo($"HomeViewModel: {channels.Count} kanal veritabanından okundu.");

                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        DailyApiCount = stats.count;
                        _allChannels = channels;
                        RefreshDisplay();
                    });
                }
                catch (Exception ex)
                {
                    LogService.LogError("HomeViewModel.LoadData failed", ex);
                    System.Windows.Application.Current?.Dispatcher.Invoke(() => {
                        TotalCountText = "Hata: İçerik yüklenemedi. Logları kontrol edin.";
                    });
                }
            });
        }

        public async Task MergeChannelsAsync(Channel source, Channel target)
        {
            if (source == null || target == null || source.Id == target.Id) return;

            var existingUrls = target.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            var sourceUrls = source.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var u in sourceUrls)
            {
                if (!existingUrls.Contains(u.Trim())) existingUrls.Add(u.Trim());
            }

            target.Url = string.Join(",", existingUrls);
            await _db.SaveChannelAsync(target);
            _db.DeleteChannelById(source.Id);
            LoadData();
        }

        public async Task ToggleFavoriteAsync(Channel ch)
        {
            if (ch == null) return;
            ch.IsFavorite = !ch.IsFavorite;
            await _db.SaveChannelAsync(ch);
            RefreshDisplay();
        }

        public void DeleteChannel(Channel ch)
        {
            if (ch == null) return;
            _db.DeleteChannelById(ch.Id);
            LoadData();
        }

        private int _sortIndex = 0;

        public void SetCategory(string tag)
        {
            _activeCategory = tag;
            _currentPage = 1;
            RefreshDisplay();
        }

        public void SetSort(int index)
        {
            _sortIndex = index;
            _currentPage = 1;
            RefreshDisplay();
        }
        public void NextPage() { if (_currentPage < _totalPages) { _currentPage++; RefreshDisplay(); } }
        public void PrevPage() { if (_currentPage > 1) { _currentPage--; RefreshDisplay(); } }

        private async void RefreshDisplay()
        {
            var searchText = _searchText;
            var category = _activeCategory;
            var sort = _sortIndex;
            var page = _currentPage;
            var pageSize = _pageSize;
            var sourceChannels = _allChannels.ToList(); // Take a snapshot to avoid concurrent modification issues

            await Task.Run(() =>
            {
                var filtered = sourceChannels.AsEnumerable();

                if (category == "Favorites") filtered = filtered.Where(c => c.IsFavorite);
                else if (category == "TV") filtered = filtered.Where(c => string.Equals(c.Category, "TV", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(c.Category));
                else if (category == "Movies") filtered = filtered.Where(c => string.Equals(c.Category, "Film", StringComparison.OrdinalIgnoreCase) || string.Equals(c.Category, "Movie", StringComparison.OrdinalIgnoreCase));
                else if (category == "Series") filtered = filtered.Where(c => string.Equals(c.Category, "Dizi", StringComparison.OrdinalIgnoreCase) || string.Equals(c.Category, "Series", StringComparison.OrdinalIgnoreCase));
                else if (category == "Radio") filtered = filtered.Where(c => string.Equals(c.Category, "Radyo", StringComparison.OrdinalIgnoreCase) || string.Equals(c.Category, "Radio", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    filtered = filtered.Where(c => ChannelUtils.MatchesQueryFilter(c, searchText));
                }

                // Group Series
                var finalItems = new List<Channel>();
                var nonSeries = filtered.Where(c => !string.Equals(c.Category, "Dizi", StringComparison.OrdinalIgnoreCase) && !string.Equals(c.Category, "Series", StringComparison.OrdinalIgnoreCase)).ToList();
                var seriesItems = filtered.Where(c => string.Equals(c.Category, "Dizi", StringComparison.OrdinalIgnoreCase) || string.Equals(c.Category, "Series", StringComparison.OrdinalIgnoreCase)).ToList();

                finalItems.AddRange(nonSeries);

                var groups = seriesItems.GroupBy(s => !string.IsNullOrWhiteSpace(s.SeriesBaseName) ? s.SeriesBaseName : s.CleanName).ToList();
                foreach (var g in groups)
                {
                    if (string.IsNullOrWhiteSpace(g.Key))
                    {
                        finalItems.AddRange(g);
                    }
                    else
                    {
                        finalItems.Add(new SeriesGroup(g.Key, g.ToList()));
                    }
                }

                // Apply Sorting
                bool isSearching = !string.IsNullOrWhiteSpace(searchText);
                if (isSearching)
                {
                    finalItems = finalItems
                        .OrderByDescending(c => ChannelUtils.CalculateSearchScore(c, searchText))
                        .ThenBy(c => c.CleanName ?? c.Name ?? "")
                        .ToList();
                }
                else
                {
                    switch (sort)
                    {
                        case 1: // Alfabetik (Z-A)
                            finalItems = finalItems.OrderByDescending(c => c.CleanName ?? c.Name ?? "").ToList();
                            break;
                        case 2: // Yeni Eklenenler
                            finalItems = finalItems.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).ToList();
                            break;
                        case 3: // Favoriler Önce
                            finalItems = finalItems.OrderByDescending(c => c.IsFavorite).ThenBy(c => c.CleanName ?? c.Name ?? "").ToList();
                            break;
                        case 0: // Alfabetik (A-Z)
                        default:
                            finalItems = finalItems.OrderBy(c => c.CleanName ?? c.Name ?? "").ToList();
                            break;
                    }
                }

                var totalCount = finalItems.Count;
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                if (totalPages < 1) totalPages = 1;

                if (page > totalPages) page = totalPages;
                if (page < 1) page = 1;

                var pageItems = finalItems.Skip((page - 1) * pageSize).Take(pageSize).ToList();

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    _filteredChannels = finalItems;
                    _totalPages = totalPages;
                    _currentPage = page;
                    TotalCountText = $"Toplam: {totalCount} İçerik";
                    OnPropertyChanged(nameof(CurrentPageText));

                    DisplayedChannels.Clear();
                    foreach (var ch in pageItems) DisplayedChannels.Add(ch);
                });

                // Asynchronously enrich missing logos and EPG for visible page items only
                _ = Task.Run(async () =>
                {
                    // 1. Enrich EPG (New On-Demand Logic)
                    var epgChannels = pageItems.SelectMany(c => (c is SeriesGroup sg) ? sg.Episodes : new List<Channel> { c }).ToList();
                    await _epg.EnrichBatchEpgAsync(epgChannels);

                    // 2. Enrich Logos
                    var missingLogos = pageItems.Where(c => string.IsNullOrWhiteSpace(c.LogoUrl)).ToList();
                    if (missingLogos.Count > 0)
                    {
                        DatabaseEngine.SuppressEvents = true;
                        try
                        {
                            var enricher = new ChannelEnricher();
                            await enricher.EnrichChannelsAsync(missingLogos);
                        }
                        finally
                        {
                            DatabaseEngine.SuppressEvents = false;
                        }
                    }
                });
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
