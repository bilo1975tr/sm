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

        public HomeViewModel()
        {
            LoadData();
            GitHubSyncEngine.OnSyncCompleted += () => {
                System.Windows.Application.Current.Dispatcher.Invoke(() => LoadData());
            };
        }

        public async void LoadData()
        {
            var stats = _db.GetDailyQueryStats();
            DailyApiCount = stats.count;

            _allChannels = await _db.GetAllChannelsAsync();
            RefreshDisplay();
            await RefreshEpgAsync();
        }

        private async Task RefreshEpgAsync()
        {
            var epgs = await _epg.GetCurrentEpgsAsync(_allChannels);
            foreach (var ch in _allChannels)
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
            else if (_activeCategory == "TV") filtered = filtered.Where(c => c.Category == "TV");
            else if (_activeCategory == "Movies") filtered = filtered.Where(c => c.Category == "Film");
            else if (_activeCategory == "Series") filtered = filtered.Where(c => c.Category == "Dizi");
            else if (_activeCategory == "Radio") filtered = filtered.Where(c => c.Category == "Radyo");

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                string s = _searchText.ToLowerInvariant();
                filtered = filtered.Where(c => c.Name.ToLowerInvariant().Contains(s) || c.GroupTitle.ToLowerInvariant().Contains(s));
            }

            _filteredChannels = filtered.ToList();
            _totalPages = (int)Math.Ceiling(_filteredChannels.Count / (double)_pageSize);
            if (_totalPages == 0) _totalPages = 1;

            TotalCountText = $"Toplam: {_filteredChannels.Count} İçerik";
            var pageItems = _filteredChannels.Skip((_currentPage - 1) * _pageSize).Take(_pageSize).ToList();

            System.Windows.Application.Current.Dispatcher.Invoke(() => {
                DisplayedChannels.Clear();
                foreach (var ch in pageItems) DisplayedChannels.Add(ch);
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
