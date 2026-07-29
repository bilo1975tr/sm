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
            set { _searchText = value; OnPropertyChanged(); RefreshDisplay(); }
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

                    // Fetch current EPGs fast in background
                    var epgs = await _epg.GetCurrentEpgsAsync(channels);
                    LogService.LogInfo($"HomeViewModel: {epgs.Count} EPG verisi eşleştirildi.");

                    foreach (var ch in channels)
                    {
                        if (epgs.TryGetValue(ch.Id, out var p))
                        {
                            ch.CurrentEpgTitle = p.Title;
                            ch.CurrentEpgTime = $"{p.StartTime:HH:mm} - {p.EndTime:HH:mm}";
                        }
                        else
                        {
                            ch.CurrentEpgTitle = "Yayın akışı bilgisi yok";
                            ch.CurrentEpgTime = "--:--";
                        }
                    }
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

        public void SetCategory(string tag)
        {
            _activeCategory = tag;
            _currentPage = 1;
            RefreshDisplay();
        }

        public void SetSort(int index) { RefreshDisplay(); }
        public void NextPage() { if (_currentPage < _totalPages) { _currentPage++; RefreshDisplay(); } }
        public void PrevPage() { if (_currentPage > 1) { _currentPage--; RefreshDisplay(); } }

        private void RefreshDisplay()
        {
            var filtered = _allChannels.AsEnumerable();

            if (_activeCategory == "Favorites") filtered = filtered.Where(c => c.IsFavorite);
            else if (_activeCategory == "TV") filtered = filtered.Where(c => string.Equals(c.Category, "TV", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(c.Category));
            else if (_activeCategory == "Movies") filtered = filtered.Where(c => string.Equals(c.Category, "Film", StringComparison.OrdinalIgnoreCase) || string.Equals(c.Category, "Movie", StringComparison.OrdinalIgnoreCase));
            else if (_activeCategory == "Series") filtered = filtered.Where(c => string.Equals(c.Category, "Dizi", StringComparison.OrdinalIgnoreCase) || string.Equals(c.Category, "Series", StringComparison.OrdinalIgnoreCase));
            else if (_activeCategory == "Radio") filtered = filtered.Where(c => string.Equals(c.Category, "Radyo", StringComparison.OrdinalIgnoreCase) || string.Equals(c.Category, "Radio", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                filtered = filtered.Where(c => ChannelUtils.MatchesQueryFilter(c, _searchText));
            }

            // V1.8.8: Group Series
            var finalItems = new List<Channel>();
            var nonSeries = filtered.Where(c => c.Category != "Dizi").ToList();
            var seriesItems = filtered.Where(c => c.Category == "Dizi").ToList();

            finalItems.AddRange(nonSeries);

            var groups = seriesItems.GroupBy(s => s.SeriesBaseName).ToList();
            foreach (var g in groups)
            {
                if (string.IsNullOrEmpty(g.Key))
                {
                    finalItems.AddRange(g); // If no base name, don't group
                }
                else if (g.Count() > 1)
                {
                    finalItems.Add(new SeriesGroup(g.Key, g.ToList()));
                }
                else
                {
                    finalItems.Add(g.First());
                }
            }

            _filteredChannels = finalItems.ToList();
            _totalPages = (int)Math.Ceiling(_filteredChannels.Count / (double)_pageSize);
            if (_totalPages < 1) _totalPages = 1;

            if (_currentPage > _totalPages) _currentPage = _totalPages;
            if (_currentPage < 1) _currentPage = 1;

            TotalCountText = $"Toplam: {_filteredChannels.Count} İçerik";
            OnPropertyChanged(nameof(CurrentPageText));

            var pageItems = _filteredChannels.Skip((_currentPage - 1) * _pageSize).Take(_pageSize).ToList();

            System.Windows.Application.Current?.Dispatcher.Invoke(() => {
                DisplayedChannels.Clear();
                foreach (var ch in pageItems) DisplayedChannels.Add(ch);
            });

            // Asynchronously enrich missing logos for visible page items only
            _ = Task.Run(async () =>
            {
                var missingLogos = pageItems.Where(c => string.IsNullOrWhiteSpace(c.LogoUrl)).ToList();
                if (missingLogos.Count > 0)
                {
                    DatabaseEngine.SuppressEvents = true; // Prevent loop
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
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
