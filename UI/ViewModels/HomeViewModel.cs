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
                if (_searchText == value) return;
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
                _ = RefreshDisplayAsync();
            }
            catch (TaskCanceledException) { }
        }

        private int _currentPage = 1;
        private int _pageSize = 24;
        private int _totalPages = 1;

        private string _activeCategory = "All";
        private string _selectedGroup = "All";
        private string _selectedSourceId = "ALL";

        public ObservableCollection<SourceFilterItem> SourceFilters { get; set; } = new ObservableCollection<SourceFilterItem>();
        public bool HasMultipleSources => SourceFilters.Count > 1;

        public string SelectedSourceId
        {
            get => _selectedSourceId;
            set
            {
                if (_selectedSourceId != value)
                {
                    _selectedSourceId = value;
                    OnPropertyChanged();
                    UpdateSelectedSourceInList();
                    _currentPage = 1;
                    _ = RefreshDisplayAsync();
                }
            }
        }

        private void UpdateSelectedSourceInList()
        {
            foreach (var item in SourceFilters)
            {
                item.IsSelected = string.Equals(item.Id, _selectedSourceId, StringComparison.OrdinalIgnoreCase);
            }
        }

        public ObservableCollection<GroupCategoryItem> GroupCategories { get; set; } = new ObservableCollection<GroupCategoryItem>();

        public bool HasGroupCategories => GroupCategories.Count > 1;

        public string SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                if (_selectedGroup != value)
                {
                    _selectedGroup = value;
                    OnPropertyChanged();
                    UpdateSelectedGroupInList();
                    _currentPage = 1;
                    _ = RefreshDisplayAsync();
                }
            }
        }

        private void UpdateSelectedGroupInList()
        {
            foreach (var item in GroupCategories)
            {
                item.IsSelected = string.Equals(item.Name, _selectedGroup, StringComparison.OrdinalIgnoreCase);
            }
        }

        public ObservableCollection<Channel> DisplayedChannels { get; set; } = new ObservableCollection<Channel>();

        private bool _isSyncing;
        public bool IsSyncing
        {
            get => _isSyncing;
            set
            {
                _isSyncing = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasNoChannels));
            }
        }

        private string _syncStatusMessage = "";
        public string SyncStatusMessage
        {
            get => _syncStatusMessage;
            set { _syncStatusMessage = value; OnPropertyChanged(); }
        }

        private int _syncProgressPercent;
        public int SyncProgressPercent
        {
            get => _syncProgressPercent;
            set { _syncProgressPercent = value; OnPropertyChanged(); }
        }

        public bool HasNoChannels => DisplayedChannels.Count == 0 && !IsSyncing;

        private bool _validateAllInGroup;
        public bool ValidateAllInGroup
        {
            get => _validateAllInGroup;
            set { _validateAllInGroup = value; OnPropertyChanged(); }
        }

        private bool _isValidatingChannels;
        public bool IsValidatingChannels
        {
            get => _isValidatingChannels;
            set { _isValidatingChannels = value; OnPropertyChanged(); }
        }

        private string _validationStatusText = "";
        public string ValidationStatusText
        {
            get => _validationStatusText;
            set { _validationStatusText = value; OnPropertyChanged(); }
        }

        private int _validationProgressPercent;
        public int ValidationProgressPercent
        {
            get => _validationProgressPercent;
            set { _validationProgressPercent = value; OnPropertyChanged(); }
        }

        private System.Threading.CancellationTokenSource? _validationCts;

        public void CancelValidation()
        {
            _validationCts?.Cancel();
        }

        public async Task StartValidationAsync()
        {
            if (IsValidatingChannels) return;

            List<Channel> targetChannels;
            string scopeDescription;

            if (ValidateAllInGroup)
            {
                lock (_filteredChannels)
                {
                    targetChannels = _filteredChannels.ToList();
                }
                scopeDescription = $"seçili filtrenin/grubun tamamındaki ({targetChannels.Count} içerik)";
            }
            else
            {
                targetChannels = DisplayedChannels.ToList();
                scopeDescription = $"ekrandaki sayfada görüntülenen ({targetChannels.Count} içerik)";
            }

            if (targetChannels.Count == 0)
            {
                System.Windows.MessageBox.Show("Doğrulanacak herhangi bir kanal bulunamadı.", "Kanal Yok", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            var confirm = System.Windows.MessageBox.Show(
                $"Kanal Canlılık Doğrulaması Başlatılsın mı?\n\n" +
                $"• Kapsam: {scopeDescription}\n" +
                $"• Kural: Yayın akışı açılmayan veya çökmüş ölü kanallar kütüphaneden kalıcı olarak SİLİNECEKTİR.\n" +
                $"• Yalnızca aktif, canlı kanallar kalacaktır.\n\n" +
                $"Devam etmek istiyor musunuz?",
                "Doğrulama Onayı",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (confirm != System.Windows.MessageBoxResult.Yes) return;

            _validationCts?.Cancel();
            _validationCts = new System.Threading.CancellationTokenSource();
            var token = _validationCts.Token;

            IsValidatingChannels = true;
            ValidationProgressPercent = 0;
            ValidationStatusText = $"{targetChannels.Count} kanal taranıyor...";

            int total = targetChannels.Count;
            int tested = 0;
            int aliveCount = 0;
            int deadCount = 0;

            var deadChannelIds = new System.Collections.Concurrent.ConcurrentBag<string>();
            var validator = new StreamValidator();

            try
            {
                using var semaphore = new System.Threading.SemaphoreSlim(8);

                var tasks = targetChannels.Select(async ch =>
                {
                    await semaphore.WaitAsync(token);
                    try
                    {
                        if (token.IsCancellationRequested) return;

                        var res = await validator.ValidateAsync(ch, ValidationLevel.Fast, null, token);
                        if (res.IsOnline)
                        {
                            System.Threading.Interlocked.Increment(ref aliveCount);
                        }
                        else
                        {
                            deadChannelIds.Add(ch.Id);
                            System.Threading.Interlocked.Increment(ref deadCount);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch
                    {
                        deadChannelIds.Add(ch.Id);
                        System.Threading.Interlocked.Increment(ref deadCount);
                    }
                    finally
                    {
                        semaphore.Release();
                        int currentTested = System.Threading.Interlocked.Increment(ref tested);
                        int pct = (int)((double)currentTested / total * 100);

                        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                        {
                            ValidationProgressPercent = pct;
                            ValidationStatusText = $"{currentTested} / {total} test edildi (Aktif: {aliveCount}, Silinecek: {deadCount})";
                        });
                    }
                });

                await Task.WhenAll(tasks);

                if (deadChannelIds.Count > 0)
                {
                    ValidationStatusText = $"{deadChannelIds.Count} ölü kanal kütüphaneden temizleniyor...";
                    foreach (var id in deadChannelIds)
                    {
                        _db.DeleteChannelById(id);
                    }
                }

                await LoadDataAsync();

                System.Windows.MessageBox.Show(
                    $"Kanal Doğrulama Tamamlandı!\n\n" +
                    $"• Test Edilen: {tested}\n" +
                    $"• Aktif Kalan: {aliveCount}\n" +
                    $"• Temizlenen / Silinen: {deadCount}",
                    "Doğrulama Bitti",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                System.Windows.MessageBox.Show("Doğrulama işlemi durduruldu.", "İptal Edildi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                LogService.LogError("HomeViewModel.StartValidationAsync failed", ex);
                System.Windows.MessageBox.Show($"Doğrulama sırasında hata oluştu: {ex.Message}", "Hata", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                validator.Dispose();
                IsValidatingChannels = false;
                ValidationProgressPercent = 0;
                ValidationStatusText = "";
            }
        }

        public string CurrentPageText => $"Sayfa {_currentPage} / {_totalPages}";

        private readonly System.Threading.SemaphoreSlim _loadSemaphore = new System.Threading.SemaphoreSlim(1, 1);
        private readonly object _debounceLock = new object();
        private System.Threading.CancellationTokenSource? _loadDebounceCts;
        private bool _hasPendingReload = false;

        public HomeViewModel()
        {
            _ = LoadDataAsync();

            GitHubSyncEngine.OnSyncStarted += () =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    IsSyncing = true;
                    SyncStatusMessage = "Kanal listeniz boş. İlk kurulum için yayın listesi indiriliyor...";
                    SyncProgressPercent = 5;
                });
            };

            GitHubSyncEngine.OnStaticProgress += (pct, msg) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    IsSyncing = true;
                    SyncProgressPercent = pct;
                    SyncStatusMessage = msg;
                });
            };

            GitHubSyncEngine.OnSyncCompleted += () => {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    IsSyncing = false;
                    SyncStatusMessage = "";
                });
                RequestLoadData(0);
            };

            DatabaseEngine.OnDatabaseUpdated += (s, e) => {
                // Coalesce / debounce database updates (300ms) so rapid batch writes trigger a single clean UI update
                RequestLoadData(300);
            };
        }

        public void RequestLoadData(int delayMs = 300)
        {
            System.Threading.CancellationToken token;
            lock (_debounceLock)
            {
                _loadDebounceCts?.Cancel();
                _loadDebounceCts = new System.Threading.CancellationTokenSource();
                token = _loadDebounceCts.Token;
            }

            Task.Run(async () =>
            {
                try
                {
                    if (delayMs > 0) await Task.Delay(delayMs, token);
                    if (token.IsCancellationRequested) return;

                    if (!await _loadSemaphore.WaitAsync(0))
                    {
                        // Load in progress - mark that a reload is required once current load finishes
                        _hasPendingReload = true;
                        return;
                    }

                    try
                    {
                        do
                        {
                            _hasPendingReload = false;
                            await LoadDataAsync();
                        } while (_hasPendingReload);
                    }
                    finally
                    {
                        _loadSemaphore.Release();
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    LogService.LogError("HomeViewModel.RequestLoadData error", ex);
                }
            });
        }

        public async Task LoadDataAsync()
        {
            try
            {
                await Task.Delay(200);
                LogService.LogInfo("HomeViewModel: Kütüphane veritabanından yükleniyor (SQL Paged)...");
                int totalCount = await _db.GetTotalChannelCountAsync();
                LogService.LogInfo($"HomeViewModel: Toplam {totalCount} kanal mevcut.");

                var registeredIptvAccounts = _db.GetAllIptvAccounts();
                var sourceList = new List<SourceFilterItem>();

                if (registeredIptvAccounts.Count > 0)
                {
                    sourceList.Add(new SourceFilterItem
                    {
                        Id = "ALL",
                        Name = "Tüm Liste",
                        Icon = "📁",
                        Count = totalCount,
                        IsSelected = string.Equals(_selectedSourceId, "ALL", StringComparison.OrdinalIgnoreCase)
                    });
                }

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    SourceFilters.Clear();
                    foreach (var s in sourceList) SourceFilters.Add(s);
                    OnPropertyChanged(nameof(HasMultipleSources));
                });

                await RefreshDisplayAsync();

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (totalCount == 0)
                    {
                        if (IsSyncing)
                        {
                            TotalCountText = "Kütüphane boş - İlk kurulum yapılıyor, yayın listesi indiriliyor...";
                        }
                        else
                        {
                            TotalCountText = "Kütüphanenizde henüz kanal yok.";
                        }
                    }
                    OnPropertyChanged(nameof(HasNoChannels));
                });

                _ = Task.Run(() =>
                {
                    try
                    {
                        var stats = _db.GetDailyQueryStats();
                        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                        {
                            DailyApiCount = stats.count;
                        });
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                LogService.LogError("HomeViewModel.LoadData failed", ex);
                System.Windows.Application.Current?.Dispatcher.Invoke(() => {
                    TotalCountText = "Hata: İçerik yüklenemedi. Logları kontrol edin.";
                });
            }
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
            await LoadDataAsync();
        }

        public async Task ToggleFavoriteAsync(Channel ch)
        {
            if (ch == null) return;
            ch.IsFavorite = !ch.IsFavorite;
            await _db.SaveChannelAsync(ch);
            await RefreshDisplayAsync();
        }

        public async Task DeleteChannelAsync(Channel ch)
        {
            if (ch == null) return;
            _db.DeleteChannelById(ch.Id);
            await LoadDataAsync();
        }

        private int _sortIndex = 0;

        public void SetCategory(string tag)
        {
            _activeCategory = tag;
            _selectedGroup = "All"; // Reset sub-category filter on main category switch
            _currentPage = 1;
            _ = RefreshDisplayAsync();
        }

        public void SetGroup(string group)
        {
            SelectedGroup = group;
        }

        public void SetSource(string sourceId)
        {
            SelectedSourceId = sourceId;
        }

        public void SetSort(int index)
        {
            _sortIndex = index;
            _currentPage = 1;
            _ = RefreshDisplayAsync();
        }
        public void NextPage() { if (_currentPage < _totalPages) { _currentPage++; _ = RefreshDisplayAsync(); } }
        public void PrevPage() { if (_currentPage > 1) { _currentPage--; _ = RefreshDisplayAsync(); } }

        private System.Threading.CancellationTokenSource? _enrichmentCts;

        private async Task RefreshDisplayAsync()
        {
            var searchText = _searchText;
            var category = _activeCategory;
            var selectedGroup = _selectedGroup;
            var selectedSourceId = _selectedSourceId;
            var sort = _sortIndex;
            var page = _currentPage;
            var pageSize = _pageSize;

            try
            {
                var groupList = await _db.GetGroupCategoriesForCategoryAsync(category, selectedSourceId);
                foreach (var g in groupList)
                {
                    g.IsSelected = string.Equals(g.Name, selectedGroup, StringComparison.OrdinalIgnoreCase);
                }

                var (pageItems, totalCount) = await _db.GetPagedChannelsAsync(searchText, category, selectedGroup, selectedSourceId, sort, page, pageSize);

                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                if (totalPages < 1) totalPages = 1;

                if (page > totalPages) page = totalPages;
                if (page < 1) page = 1;

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    GroupCategories.Clear();
                    foreach (var g in groupList) GroupCategories.Add(g);
                    OnPropertyChanged(nameof(HasGroupCategories));

                    _totalPages = totalPages;
                    _currentPage = page;
                    TotalCountText = $"Toplam: {totalCount} İçerik";
                    OnPropertyChanged(nameof(CurrentPageText));

                    DisplayedChannels.Clear();
                    foreach (var ch in pageItems) DisplayedChannels.Add(ch);
                    OnPropertyChanged(nameof(HasNoChannels));
                });

                System.Threading.CancellationToken token;
                lock (_debounceLock)
                {
                    _enrichmentCts?.Cancel();
                    _enrichmentCts = new System.Threading.CancellationTokenSource();
                    token = _enrichmentCts.Token;
                }

                if (token.IsCancellationRequested) return;

                // Asynchronously enrich missing logos and EPG for visible page items only
                try
                {
                    // 1. Enrich EPG (New On-Demand Logic)
                    var epgChannels = pageItems.SelectMany(c => (c is SeriesGroup sg) ? sg.Episodes : new List<Channel> { c }).ToList();
                    await _epg.EnrichBatchEpgAsync(epgChannels);

                    if (token.IsCancellationRequested) return;

                    // 2. Enrich Logos
                    var missingLogos = pageItems.Where(c => string.IsNullOrWhiteSpace(c.LogoUrl)).ToList();
                    if (missingLogos.Count > 0)
                    {
                        var enricher = new ChannelEnricher();
                        await enricher.EnrichChannelsAsync(missingLogos);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { LogService.LogWarning($"HomeViewModel: Enrichment background task error: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                LogService.LogError("HomeViewModel.RefreshDisplayAsync error", ex);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
