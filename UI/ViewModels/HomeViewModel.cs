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
        private readonly DatabaseEngine _db = DatabaseEngine.Instance;
        private readonly EpgService _epg = new EpgService();
        private List<Channel> _allChannels = new List<Channel>();
        private List<Channel> _filteredUnsorted = new List<Channel>();
        private List<Channel> _filteredChannels = new List<Channel>();
        private List<IptvAccountInfo> _cachedAccountInfos = new List<IptvAccountInfo>();
        private Dictionary<string, IptvAccountInfo> _cachedAccountInfosById = new Dictionary<string, IptvAccountInfo>(StringComparer.OrdinalIgnoreCase);

        private static readonly System.Text.RegularExpressions.Regex SeriesEpisodeRegex = new System.Text.RegularExpressions.Regex(
            @"\b(s\d+\s*e\d+|bölüm|bolum|sezon)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

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
                _ = DebouncedSearchAsync();
            }
        }

        private System.Threading.CancellationTokenSource? _searchCts;
        private async Task DebouncedSearchAsync()
        {
            _searchCts?.Cancel();
            _searchCts = new System.Threading.CancellationTokenSource();
            try
            {
                await Task.Delay(250, _searchCts.Token);
                _currentPage = 1;
                await ApplyFiltersAsync(rebuildGroups: false);
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
                    _ = ApplyFiltersAsync(rebuildGroups: true);
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
        public ObservableCollection<GroupCategoryItem> FilteredGroupCategories { get; set; } = new ObservableCollection<GroupCategoryItem>();

        public bool HasGroupCategories => GroupCategories.Count > 1;
        public bool CanToggleGroupSidebar => GroupCategories.Count > 1;
        public bool ShowGroupSidebar => _isGroupSidebarOpen && GroupCategories.Count > 1;

        private bool _isGroupSidebarOpen = true;
        public bool IsGroupSidebarOpen
        {
            get => _isGroupSidebarOpen;
            set
            {
                if (_isGroupSidebarOpen != value)
                {
                    _isGroupSidebarOpen = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ShowGroupSidebar));
                }
            }
        }

        private string _groupSearchText = "";
        public string GroupSearchText
        {
            get => _groupSearchText;
            set
            {
                if (_groupSearchText != value)
                {
                    _groupSearchText = value ?? "";
                    OnPropertyChanged();
                    RebuildFilteredGroups();
                }
            }
        }

        public void ToggleGroupSidebar() => IsGroupSidebarOpen = !IsGroupSidebarOpen;

        private void RebuildFilteredGroups()
        {
            string query = (_groupSearchText ?? "").Trim();
            var matches = string.IsNullOrEmpty(query)
                ? GroupCategories.ToList()
                : GroupCategories.Where(g =>
                    string.Equals(g.Name, "All", StringComparison.OrdinalIgnoreCase) ||
                    (g.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

            void Apply()
            {
                FilteredGroupCategories.Clear();
                foreach (var g in matches) FilteredGroupCategories.Add(g);
            }

            if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true)
                Apply();
            else
                System.Windows.Application.Current?.Dispatcher?.Invoke(Apply);
        }

        public string ActiveCategory => _activeCategory;

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
                    _ = ApplyFiltersAsync(rebuildGroups: false);
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

        private Channel? _selectedHeroItem;
        private System.Threading.CancellationTokenSource? _heroEnrichCts;

        public Channel? SelectedHeroItem
        {
            get => _selectedHeroItem;
            set
            {
                if (ReferenceEquals(_selectedHeroItem, value)) return;
                if (_selectedHeroItem != null)
                {
                    _selectedHeroItem.IsSelected = false;
                    _selectedHeroItem.PropertyChanged -= SelectedHeroItem_PropertyChanged;
                }

                _selectedHeroItem = value;

                if (_selectedHeroItem != null)
                {
                    _selectedHeroItem.IsSelected = true;
                    _selectedHeroItem.PropertyChanged += SelectedHeroItem_PropertyChanged;
                    CurrentBackdrop = !string.IsNullOrWhiteSpace(_selectedHeroItem.BackdropUrl)
                        ? _selectedHeroItem.BackdropUrl
                        : _selectedHeroItem.PrimaryLogoUrl;
                }

                NotifyHeroPropertiesChanged();
            }
        }

        private void SelectedHeroItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Channel.Overview) ||
                e.PropertyName == nameof(Channel.BackdropUrl) ||
                e.PropertyName == nameof(Channel.LogoUrl) ||
                e.PropertyName == nameof(Channel.PrimaryLogoUrl) ||
                e.PropertyName == nameof(Channel.Cast) ||
                e.PropertyName == nameof(Channel.ImdbId) ||
                e.PropertyName == nameof(Channel.IsFavorite) ||
                e.PropertyName == nameof(Channel.LastPositionMs))
            {
                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(NotifyHeroPropertiesChanged);
            }
        }

        private void NotifyHeroPropertiesChanged()
        {
            OnPropertyChanged(nameof(SelectedHeroItem));
            OnPropertyChanged(nameof(ShowVodHero));
            OnPropertyChanged(nameof(HeroBackdropUrl));
            OnPropertyChanged(nameof(HeroPosterUrl));
            OnPropertyChanged(nameof(HeroOverviewText));
            OnPropertyChanged(nameof(IsSelectedHeroSeries));
            OnPropertyChanged(nameof(IsSelectedHeroResume));
            OnPropertyChanged(nameof(HeroPlayButtonText));
            OnPropertyChanged(nameof(HeroDetailsButtonText));
        }

        public bool ShowVodHero =>
            SelectedHeroItem != null &&
            !string.Equals(_activeCategory, "TV", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(_activeCategory, "Radio", StringComparison.OrdinalIgnoreCase) &&
            (SelectedHeroItem.IsVodMedia ||
             string.Equals(_activeCategory, "Movies", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(_activeCategory, "Series", StringComparison.OrdinalIgnoreCase));

        public string HeroBackdropUrl =>
            !string.IsNullOrWhiteSpace(SelectedHeroItem?.BackdropUrl)
                ? SelectedHeroItem.BackdropUrl
                : (SelectedHeroItem?.PrimaryLogoUrl ?? "");

        public string HeroPosterUrl => SelectedHeroItem?.PrimaryLogoUrl ?? "";

        public string HeroOverviewText =>
            !string.IsNullOrWhiteSpace(SelectedHeroItem?.Overview)
                ? SelectedHeroItem.Overview
                : "Bu yapım için özet bilgisi bulunmuyor. İsterseniz detay penceresinden kontrol edebilir veya doğrudan oynatabilirsiniz.";

        public bool IsSelectedHeroSeries => SelectedHeroItem is SeriesGroup;
        public bool IsSelectedHeroResume => (SelectedHeroItem?.LastPositionMs ?? 0) > 0;
        public string HeroPlayButtonText => IsSelectedHeroResume ? "⏯ Devam Et" : (IsSelectedHeroSeries ? "▶ Bölümleri Aç" : "▶ Şimdi İzle");
        public string HeroDetailsButtonText => IsSelectedHeroSeries ? "📋 Sezon & Bölümler" : "ℹ Detay & Fragman";

        public void SelectHeroItem(Channel? item, bool enrichIfMissing = true)
        {
            SelectedHeroItem = item;

            if (!enrichIfMissing || item == null || !item.IsVodMedia || MetadataEngine.IsLikelyLiveTv(item))
                return;

            bool needsMeta = string.IsNullOrWhiteSpace(item.Overview) ||
                             string.IsNullOrWhiteSpace(item.BackdropUrl) ||
                             string.IsNullOrWhiteSpace(item.Cast);
            if (!needsMeta) return;

            _heroEnrichCts?.Cancel();
            var cts = new System.Threading.CancellationTokenSource();
            _heroEnrichCts = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(180, cts.Token).ConfigureAwait(false);
                    if (cts.Token.IsCancellationRequested) return;

                    await new MetadataEngine().EnrichChannelAsync(item).ConfigureAwait(false);

                    if (item is SeriesGroup sg && sg.Episodes.Count > 0)
                    {
                        var ep0 = sg.Episodes[0];
                        bool epChanged = false;
                        if (string.IsNullOrWhiteSpace(ep0.Overview) && !string.IsNullOrWhiteSpace(sg.Overview)) { ep0.Overview = sg.Overview; epChanged = true; }
                        if (string.IsNullOrWhiteSpace(ep0.BackdropUrl) && !string.IsNullOrWhiteSpace(sg.BackdropUrl)) { ep0.BackdropUrl = sg.BackdropUrl; epChanged = true; }
                        if (string.IsNullOrWhiteSpace(ep0.Cast) && !string.IsNullOrWhiteSpace(sg.Cast)) { ep0.Cast = sg.Cast; epChanged = true; }
                        if (string.IsNullOrWhiteSpace(ep0.ImdbId) && !string.IsNullOrWhiteSpace(sg.ImdbId)) { ep0.ImdbId = sg.ImdbId; epChanged = true; }
                        if (epChanged) await _db.SaveChannelAsync(ep0).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { LogService.LogWarning($"Hero metadata enrich skipped: {ex.Message}"); }
            });
        }

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
                targetChannels = _filteredChannels.ToList();
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
                    await _db.DeleteChannelsAsync(deadChannelIds.ToList());
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

        public async Task RefreshSourceAsync(string sourceId)
        {
            if (string.IsNullOrWhiteSpace(sourceId) || !sourceId.StartsWith("IPTV_", StringComparison.OrdinalIgnoreCase)) return;

            string accountId = sourceId.Substring(5);
            var acc = _db.GetAllIptvAccounts().FirstOrDefault(a => a.Id == accountId);
            if (acc == null) return;

            var accInfo = IptvAccountInfo.FromAccount(acc);
            IsSyncing = true;
            SyncStatusMessage = $"{acc.Name} sağlayıcısından tüm kanallar ve içerikler indiriliyor...";
            SyncProgressPercent = 10;

            try
            {
                var (success, count, msg) = await IptvAccountHelper.FetchAndImportFullAccountAsync(accInfo, (m, p) =>
                {
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        SyncStatusMessage = $"[{acc.Username}] {m}";
                        SyncProgressPercent = (int)p;
                    });
                }).ConfigureAwait(false);

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (success)
                    {
                        System.Windows.MessageBox.Show($"{acc.Name} sağlayıcısından {count} kanal ve içerik başarıyla güncellendi!", "Sağlayıcı Güncellendi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show($"Güncelleme uyarısı: {msg}", "Bilgi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    }
                });
            }
            catch (Exception ex)
            {
                LogService.LogError("HomeViewModel.RefreshSourceAsync hatası", ex);
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    System.Windows.MessageBox.Show($"Hata: {ex.Message}", "Hata", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                });
            }
            finally
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    IsSyncing = false;
                    SyncStatusMessage = "";
                });
                await LoadDataAsync();
            }
        }

        public async Task PurgeSourceAsync(string sourceId)
        {
            if (string.IsNullOrWhiteSpace(sourceId) || !sourceId.StartsWith("IPTV_", StringComparison.OrdinalIgnoreCase)) return;

            string accountId = sourceId.Substring(5);
            var acc = _db.GetAllIptvAccounts().FirstOrDefault(a => a.Id == accountId);
            if (acc == null) return;

            if (System.Windows.MessageBox.Show($"{acc.Name} sağlayıcısı ve bu sağlayıcıya ait tüm kanallar veritabanından kalıcı olarak silinecek. Emin misiniz?", "Sağlayıcıyı Sil", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes)
            {
                await IptvAccountHelper.PurgeAccountFromDatabaseAsync(IptvAccountInfo.FromAccount(acc));
                await LoadDataAsync();
            }
        }

        public string CurrentPageText => $"Sayfa {_currentPage} / {_totalPages}";

        private readonly System.Threading.SemaphoreSlim _loadSemaphore = new System.Threading.SemaphoreSlim(1, 1);
        private readonly object _debounceLock = new object();
        private System.Threading.CancellationTokenSource? _loadDebounceCts;
        private bool _hasPendingReload = false;

        public HomeViewModel()
        {
            RequestLoadData(0);

            GitHubSyncEngine.OnSyncStarted += () =>
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    IsSyncing = true;
                    SyncStatusMessage = "Kanal listeniz hazırlanıyor...";
                    SyncProgressPercent = 5;
                });
            };

            GitHubSyncEngine.OnStaticProgress += (pct, msg) =>
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    IsSyncing = true;
                    SyncProgressPercent = pct;
                    SyncStatusMessage = msg;
                });
            };

            GitHubSyncEngine.OnSyncCompleted += () =>
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
                {
                    SyncProgressPercent = 100;
                    SyncStatusMessage = "Kütüphane hazırlandı!";
                    await Task.Delay(1500);
                    IsSyncing = false;
                    SyncStatusMessage = "";
                });
                RequestLoadData(0);
            };

            DatabaseEngine.OnDatabaseUpdated += (s, e) =>
            {
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
                try
                {
                    using var cts = new System.Threading.CancellationTokenSource(4000);
                    await Task.WhenAny(DatabaseEngine.WaitForInitAsync(), Task.Delay(4000, cts.Token));
                }
                catch { }

                LogService.LogInfo("HomeViewModel: Kütüphane yükleniyor...");
                var channels = await _db.GetAllChannelsAsync();
                LogService.LogInfo($"HomeViewModel: {channels.Count} kanal veritabanından okundu.");

                _allChannels = channels;

                // Cache normalized IPTV accounts once per data load so filtering never queries SQLite
                var rawIptvAccounts = _db.GetAllIptvAccounts();
                var seenAccKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var registeredAccounts = new List<(IptvAccount Account, IptvAccountInfo Info)>();
                var accById = new Dictionary<string, IptvAccountInfo>(StringComparer.OrdinalIgnoreCase);

                foreach (var acc in rawIptvAccounts)
                {
                    var info = IptvAccountInfo.FromAccount(acc);
                    if (!string.IsNullOrEmpty(acc.Id)) accById[acc.Id] = info;
                    if (seenAccKeys.Add(info.SignatureKey))
                    {
                        registeredAccounts.Add((acc, info));
                    }
                }

                _cachedAccountInfos = registeredAccounts.Select(x => x.Info).ToList();
                _cachedAccountInfosById = accById;

                var sourceList = BuildSourceFilters(channels, registeredAccounts);

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    SourceFilters.Clear();
                    foreach (var s in sourceList) SourceFilters.Add(s);
                    OnPropertyChanged(nameof(HasMultipleSources));
                });

                await ApplyFiltersAsync(rebuildGroups: true);

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (channels.Count == 0)
                    {
                        TotalCountText = IsSyncing
                            ? "Kütüphane boş - İlk kurulum yapılıyor, yayın listesi indiriliyor..."
                            : "Kütüphanenizde henüz kanal yok.";
                    }
                    OnPropertyChanged(nameof(HasNoChannels));
                });

                _ = Task.Run(() =>
                {
                    try
                    {
                        var stats = _db.GetDailyQueryStats();
                        System.Windows.Application.Current?.Dispatcher.Invoke(() => DailyApiCount = stats.count);
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                LogService.LogError("HomeViewModel.LoadData failed", ex);
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    TotalCountText = "Hata: İçerik yüklenemedi. Logları kontrol edin.";
                });
            }
        }

        private List<SourceFilterItem> BuildSourceFilters(List<Channel> channels, List<(IptvAccount Account, IptvAccountInfo Info)> registeredAccounts)
        {
            var sourceList = new List<SourceFilterItem>();
            if (registeredAccounts.Count == 0) return sourceList;

            sourceList.Add(new SourceFilterItem
            {
                Id = "ALL",
                Name = "Tüm Liste",
                Icon = "📁",
                Count = channels.Count,
                IsSelected = string.Equals(_selectedSourceId, "ALL", StringComparison.OrdinalIgnoreCase)
            });

            var accountCounts = new int[registeredAccounts.Count];
            int matchedTotal = 0;

            foreach (var ch in channels)
            {
                for (int i = 0; i < registeredAccounts.Count; i++)
                {
                    if (IptvAccountHelper.ChannelBelongsToAccount(ch, registeredAccounts[i].Info))
                    {
                        accountCounts[i]++;
                        matchedTotal++;
                        break;
                    }
                }
            }

            for (int i = 0; i < registeredAccounts.Count; i++)
            {
                if (accountCounts[i] <= 0) continue;
                var (acc, info) = registeredAccounts[i];
                string displayName = !string.IsNullOrWhiteSpace(acc.Name) ? acc.Name : $"{acc.Username} ({info.HostWithPort})";
                sourceList.Add(new SourceFilterItem
                {
                    Id = $"IPTV_{acc.Id}",
                    Name = displayName,
                    SourceUrl = acc.ServerUrl,
                    Icon = "⚡",
                    Count = accountCounts[i],
                    IsSelected = string.Equals(_selectedSourceId, $"IPTV_{acc.Id}", StringComparison.OrdinalIgnoreCase)
                });
            }

            int generalCount = channels.Count - matchedTotal;
            if (generalCount > 0)
            {
                sourceList.Insert(1, new SourceFilterItem
                {
                    Id = "GENERAL_M3U",
                    Name = "Genel Liste",
                    Icon = "🌐",
                    Count = generalCount,
                    IsSelected = string.Equals(_selectedSourceId, "GENERAL_M3U", StringComparison.OrdinalIgnoreCase)
                });
            }

            return sourceList;
        }

        public async Task MergeChannelsAsync(Channel source, Channel target)
        {
            if (source == null || target == null || source.Id == target.Id) return;

            var existingUrls = target.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(u => u.Trim()).ToList();
            foreach (var u in source.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = u.Trim();
                if (!existingUrls.Contains(trimmed)) existingUrls.Add(trimmed);
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
            await ApplyFiltersAsync(rebuildGroups: _activeCategory == "Favorites");
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
            _selectedGroup = "All";
            _currentPage = 1;
            OnPropertyChanged(nameof(ActiveCategory));
            if (tag == "TV" || tag == "Radio")
            {
                SelectedHeroItem = null;
            }
            else
            {
                NotifyHeroPropertiesChanged();
            }

            if (tag == "ContinueWatching")
            {
                RequestLoadData(0);
            }
            else
            {
                _ = ApplyFiltersAsync(rebuildGroups: true);
            }
        }

        public void SetGroup(string group) => SelectedGroup = group;
        public void SetSource(string sourceId) => SelectedSourceId = sourceId;

        public void SetSort(int index)
        {
            _sortIndex = index;
            _currentPage = 1;
            _ = SortAndRenderAsync();
        }

        public void NextPage()
        {
            if (_currentPage < _totalPages)
            {
                _currentPage++;
                _ = RenderCurrentPageAsync();
            }
        }

        public void PrevPage()
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                _ = RenderCurrentPageAsync();
            }
        }

        private System.Threading.CancellationTokenSource? _enrichmentCts;

        private async Task ApplyFiltersAsync(bool rebuildGroups)
        {
            string searchText = _searchText;
            string category = _activeCategory;
            string selectedGroup = _selectedGroup;
            string selectedSourceId = _selectedSourceId;
            var sourceChannels = _allChannels;
            var allAccountInfos = _cachedAccountInfos;
            var accountInfosById = _cachedAccountInfosById;

            await Task.Run(async () =>
            {
                IEnumerable<Channel> filtered = sourceChannels;

                // 1. Source filter (uses cached account infos - zero DB roundtrips)
                if (!string.Equals(selectedSourceId, "ALL", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(selectedSourceId, "GENERAL_M3U", StringComparison.OrdinalIgnoreCase))
                    {
                        filtered = filtered.Where(c => !allAccountInfos.Any(info => IptvAccountHelper.ChannelBelongsToAccount(c, info)));
                    }
                    else if (selectedSourceId.StartsWith("IPTV_", StringComparison.OrdinalIgnoreCase))
                    {
                        string targetAccId = selectedSourceId.Substring(5);
                        if (accountInfosById.TryGetValue(targetAccId, out var targetInfo))
                        {
                            filtered = filtered.Where(c => IptvAccountHelper.ChannelBelongsToAccount(c, targetInfo));
                        }
                    }
                }

                // 2. Category filter
                filtered = category switch
                {
                    "Favorites" => filtered.Where(c => c.IsFavorite),
                    "ContinueWatching" => filtered.Where(c => c.LastPositionMs > 0 || c.PersonalWatchCount > 0),
                    "TV" => filtered.Where(c => !IsMovieCategory(c.Category) && !IsSeriesCategory(c.Category) && !IsRadioCategory(c.Category)),
                    "Movies" => filtered.Where(c => IsMovieCategory(c.Category)),
                    "Series" => filtered.Where(c => IsSeriesCategory(c.Category)),
                    "Radio" => filtered.Where(c => IsRadioCategory(c.Category)),
                    _ => filtered
                };

                var categoryList = filtered.ToList();

                // 3. Rebuild group chips only when source/category/library changed
                if (rebuildGroups)
                {
                    var groupCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var ch in categoryList)
                    {
                        string grp = string.IsNullOrWhiteSpace(ch.GroupTitle) ? "Genel" : ch.GroupTitle.Trim();
                        groupCounts[grp] = groupCounts.TryGetValue(grp, out int cnt) ? cnt + 1 : 1;
                    }

                    if (!string.Equals(selectedGroup, "All", StringComparison.OrdinalIgnoreCase) && !groupCounts.ContainsKey(selectedGroup))
                    {
                        selectedGroup = "All";
                        _selectedGroup = "All";
                    }

                    var groupList = new List<GroupCategoryItem>
                    {
                        new GroupCategoryItem
                        {
                            Name = "All",
                            Count = categoryList.Count,
                            IsSelected = string.Equals(selectedGroup, "All", StringComparison.OrdinalIgnoreCase)
                        }
                    };

                    foreach (var kvp in groupCounts.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Take(150))
                    {
                        groupList.Add(new GroupCategoryItem
                        {
                            Name = kvp.Key,
                            Count = kvp.Value,
                            IsSelected = string.Equals(selectedGroup, kvp.Key, StringComparison.OrdinalIgnoreCase)
                        });
                    }

                    if (!string.Equals(selectedGroup, "All", StringComparison.OrdinalIgnoreCase) &&
                        !groupList.Any(g => string.Equals(g.Name, selectedGroup, StringComparison.OrdinalIgnoreCase)) &&
                        groupCounts.TryGetValue(selectedGroup, out int selectedCount))
                    {
                        groupList.Add(new GroupCategoryItem { Name = selectedGroup, Count = selectedCount, IsSelected = true });
                    }

                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        GroupCategories.Clear();
                        foreach (var g in groupList) GroupCategories.Add(g);
                        RebuildFilteredGroups();
                        OnPropertyChanged(nameof(HasGroupCategories));
                        OnPropertyChanged(nameof(CanToggleGroupSidebar));
                        OnPropertyChanged(nameof(ShowGroupSidebar));
                    });
                }

                // 4. Group & search filter
                IEnumerable<Channel> narrowed = categoryList;
                if (!string.Equals(selectedGroup, "All", StringComparison.OrdinalIgnoreCase))
                {
                    narrowed = narrowed.Where(c =>
                    {
                        string g = string.IsNullOrWhiteSpace(c.GroupTitle) ? "Genel" : c.GroupTitle.Trim();
                        return string.Equals(g, selectedGroup, StringComparison.OrdinalIgnoreCase);
                    });
                }

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    narrowed = narrowed.Where(c => ChannelUtils.MatchesQueryFilter(c, searchText));
                }

                // 5. Group series episodes into SeriesGroup cards
                var groupedItems = GroupSeriesItems(narrowed);
                _filteredUnsorted = groupedItems;

                await SortAndRenderCoreAsync();
            });
        }

        private static List<Channel> GroupSeriesItems(IEnumerable<Channel> items)
        {
            var result = new List<Channel>();
            var seriesItems = new List<Channel>();

            foreach (var c in items)
            {
                if (string.Equals(c.Category, "Dizi", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Category, "Series", StringComparison.OrdinalIgnoreCase))
                {
                    seriesItems.Add(c);
                }
                else
                {
                    result.Add(c);
                }
            }

            if (seriesItems.Count == 0) return result;

            foreach (var g in seriesItems.GroupBy(s => !string.IsNullOrWhiteSpace(s.SeriesBaseName) ? s.SeriesBaseName : s.CleanName))
            {
                bool isRealSeries = g.Count() > 1 && g.Any(ep =>
                    ep.SeasonNumber > 0 || ep.EpisodeNumber > 0 || SeriesEpisodeRegex.IsMatch(ep.Name ?? ""));

                if (string.IsNullOrWhiteSpace(g.Key) || !isRealSeries)
                {
                    result.AddRange(g);
                }
                else
                {
                    result.Add(new SeriesGroup(g.Key, g.ToList()));
                }
            }

            return result;
        }

        private async Task SortAndRenderAsync()
        {
            await Task.Run(SortAndRenderCoreAsync);
        }

        private async Task SortAndRenderCoreAsync()
        {
            var source = _filteredUnsorted;
            string searchText = _searchText;
            string category = _activeCategory;
            int sort = _sortIndex;

            List<Channel> sorted;
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                sorted = source
                    .OrderByDescending(c => ChannelUtils.CalculateSearchScore(c, searchText))
                    .ThenBy(c => c.CleanName ?? c.Name ?? "")
                    .ToList();
            }
            else if (category == "ContinueWatching")
            {
                sorted = source
                    .OrderByDescending(c => c.LastPositionMs > 0)
                    .ThenByDescending(c => c.PersonalWatchCount)
                    .ThenBy(c => c.CleanName ?? c.Name ?? "")
                    .ToList();
            }
            else
            {
                sorted = sort switch
                {
                    1 => source.OrderByDescending(c => c.CleanName ?? c.Name ?? "").ToList(),
                    2 => source.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).ToList(),
                    3 => source.OrderByDescending(c => c.IsFavorite).ThenBy(c => c.CleanName ?? c.Name ?? "").ToList(),
                    _ => source.OrderBy(c => c.CleanName ?? c.Name ?? "").ToList()
                };
            }

            _filteredChannels = sorted;
            await RenderCurrentPageCoreAsync();
        }

        private async Task RenderCurrentPageAsync()
        {
            await Task.Run(RenderCurrentPageCoreAsync);
        }

        private async Task RenderCurrentPageCoreAsync()
        {
            var finalItems = _filteredChannels;
            int pageSize = _pageSize;
            int totalCount = finalItems.Count;
            int totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            int page = Math.Clamp(_currentPage, 1, totalPages);

            var pageItems = finalItems.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                _totalPages = totalPages;
                _currentPage = page;
                TotalCountText = $"Toplam: {totalCount} İçerik";
                OnPropertyChanged(nameof(CurrentPageText));

                DisplayedChannels.Clear();
                foreach (var ch in pageItems) DisplayedChannels.Add(ch);
                OnPropertyChanged(nameof(HasNoChannels));

                if (string.Equals(_activeCategory, "TV", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(_activeCategory, "Radio", StringComparison.OrdinalIgnoreCase))
                {
                    if (SelectedHeroItem != null) SelectedHeroItem = null;
                }
                else
                {
                    var vodCandidate = pageItems.FirstOrDefault(c => c.IsVodMedia);
                    bool keepCurrent = SelectedHeroItem != null && pageItems.Any(c => c.Id == SelectedHeroItem.Id) && SelectedHeroItem.IsVodMedia;
                    if (!keepCurrent)
                    {
                        SelectHeroItem(vodCandidate, enrichIfMissing: true);
                    }
                }
            });

            System.Threading.CancellationToken token;
            lock (_debounceLock)
            {
                _enrichmentCts?.Cancel();
                _enrichmentCts = new System.Threading.CancellationTokenSource();
                token = _enrichmentCts.Token;
            }

            if (token.IsCancellationRequested || pageItems.Count == 0) return;

            try
            {
                var epgChannels = pageItems.SelectMany(c => c is SeriesGroup sg ? sg.Episodes : new List<Channel> { c }).ToList();
                await _epg.EnrichBatchEpgAsync(epgChannels);

                if (token.IsCancellationRequested) return;

                var missingLogos = pageItems.Where(c => string.IsNullOrWhiteSpace(c.LogoUrl)).ToList();
                if (missingLogos.Count > 0)
                {
                    await new ChannelEnricher().EnrichChannelsAsync(missingLogos);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LogService.LogWarning($"HomeViewModel: Enrichment background task error: {ex.Message}"); }
        }

        private static bool IsMovieCategory(string? cat)
        {
            if (string.IsNullOrWhiteSpace(cat)) return false;
            string c = cat.Trim().ToLowerInvariant();
            return c == "film" || c == "movie" || c == "filmler" || c == "movies" || c == "vod" || c.Contains("film") || c.Contains("movie");
        }

        private static bool IsSeriesCategory(string? cat)
        {
            if (string.IsNullOrWhiteSpace(cat)) return false;
            string c = cat.Trim().ToLowerInvariant();
            return c == "dizi" || c == "series" || c == "diziler" || c.Contains("dizi") || c.Contains("series");
        }

        private static bool IsRadioCategory(string? cat)
        {
            if (string.IsNullOrWhiteSpace(cat)) return false;
            string c = cat.Trim().ToLowerInvariant();
            return c == "radyo" || c == "radio" || c.Contains("radyo") || c.Contains("radio");
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
