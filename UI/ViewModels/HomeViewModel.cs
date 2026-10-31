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

        public async Task RefreshSourceAsync(string sourceId)
        {
            if (string.IsNullOrWhiteSpace(sourceId)) return;

            if (sourceId.StartsWith("IPTV_", StringComparison.OrdinalIgnoreCase))
            {
                string accountId = sourceId.Substring(5);
                var allAccs = _db.GetAllIptvAccounts();
                var acc = allAccs.FirstOrDefault(a => a.Id == accountId);
                if (acc != null)
                {
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
            }
        }

        public async Task PurgeSourceAsync(string sourceId)
        {
            if (string.IsNullOrWhiteSpace(sourceId)) return;

            if (sourceId.StartsWith("IPTV_", StringComparison.OrdinalIgnoreCase))
            {
                string accountId = sourceId.Substring(5);
                var allAccs = _db.GetAllIptvAccounts();
                var acc = allAccs.FirstOrDefault(a => a.Id == accountId);
                if (acc != null)
                {
                    if (System.Windows.MessageBox.Show($"{acc.Name} sağlayıcısı ve bu sağlayıcıya ait tüm kanallar veritabanından kalıcı olarak silinecek. Emin misiniz?", "Sağlayıcıyı Sil", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes)
                    {
                        var accInfo = IptvAccountInfo.FromAccount(acc);
                        await IptvAccountHelper.PurgeAccountFromDatabaseAsync(accInfo);
                        await LoadDataAsync();
                    }
                }
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

            GitHubSyncEngine.OnSyncCompleted += () => {
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
                // Veritabanı tablolarının oluşturulup hazır hale gelmesini bekle
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

                // Dinamik Kaynak (Playlist / Xtream) Filtrelerini Hazırla:
                var rawIptvAccounts = _db.GetAllIptvAccounts();
                var seenAccKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var registeredIptvAccounts = new List<IptvAccount>();

                foreach (var acc in rawIptvAccounts)
                {
                    string host = IptvAccountHelper.ExtractHostAndPort(acc.ServerUrl).ToLowerInvariant();
                    string user = (acc.Username ?? "").Trim().ToLowerInvariant();
                    string key = $"{user}@{host}";

                    if (seenAccKeys.Add(key))
                    {
                        registeredIptvAccounts.Add(acc);
                    }
                }

                var sourceList = new List<SourceFilterItem>();

                if (registeredIptvAccounts.Count > 0)
                {
                    sourceList.Add(new SourceFilterItem
                    {
                        Id = "ALL",
                        Name = "Tüm Liste",
                        Icon = "📁",
                        Count = channels.Count,
                        IsSelected = string.Equals(_selectedSourceId, "ALL", StringComparison.OrdinalIgnoreCase)
                    });

                    // Hızlı tek geçişli eşleme (Single-pass account mapping)
                    var accInfos = registeredIptvAccounts.Select(acc =>
                    {
                        string host = IptvAccountHelper.ExtractHostAndPort(acc.ServerUrl).ToLowerInvariant();
                        string user = (acc.Username ?? "").Trim().ToLowerInvariant();
                        return new
                        {
                            Account = acc,
                            Host = host,
                            User = user,
                            DisplayName = !string.IsNullOrWhiteSpace(acc.Name) ? acc.Name : $"{acc.Username} ({host})",
                            Count = 0
                        };
                    }).ToList();

                    var accountCounts = new int[accInfos.Count];
                    var accountMatchedChannelIds = new HashSet<string>();

                    foreach (var ch in channels)
                    {
                        string pUrl = (ch.PlaylistUrl ?? "").ToLowerInvariant();
                        string chUrl = (ch.Url ?? "").ToLowerInvariant();

                        for (int i = 0; i < accInfos.Count; i++)
                        {
                            var info = accInfos[i];
                            bool matches = false;

                            if (!string.IsNullOrEmpty(info.Host) && (pUrl.Contains(info.Host) || chUrl.Contains(info.Host)))
                            {
                                if (string.IsNullOrEmpty(info.User) || pUrl.Contains(info.User) || chUrl.Contains(info.User))
                                {
                                    matches = true;
                                }
                            }

                            if (matches)
                            {
                                accountCounts[i]++;
                                accountMatchedChannelIds.Add(ch.Id);
                                break;
                            }
                        }
                    }

                    for (int i = 0; i < accInfos.Count; i++)
                    {
                        if (accountCounts[i] > 0)
                        {
                            var info = accInfos[i];
                            sourceList.Add(new SourceFilterItem
                            {
                                Id = $"IPTV_{info.Account.Id}",
                                Name = info.DisplayName,
                                SourceUrl = info.Account.ServerUrl,
                                Icon = "⚡",
                                Count = accountCounts[i],
                                IsSelected = string.Equals(_selectedSourceId, $"IPTV_{info.Account.Id}", StringComparison.OrdinalIgnoreCase)
                            });
                        }
                    }

                    int generalCount = channels.Count(c => !accountMatchedChannelIds.Contains(c.Id));
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
                    if (channels.Count == 0)
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
            var sourceChannels = _allChannels.ToList(); // Take a snapshot

            await Task.Run(async () =>
            {
                var baseSourceFiltered = sourceChannels.AsEnumerable();

                // 1. Kaynak Filtresi (Tüm Liste, Genel M3U veya Xtream Hesabı)
                if (!string.Equals(selectedSourceId, "ALL", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(selectedSourceId, "GENERAL_M3U", StringComparison.OrdinalIgnoreCase))
                    {
                        var allAccInfos = _db.GetAllIptvAccounts().Select(IptvAccountInfo.FromAccount).ToList();
                        baseSourceFiltered = baseSourceFiltered.Where(c => !allAccInfos.Any(info => IptvAccountHelper.ChannelBelongsToAccount(c, info)));
                    }
                    else if (selectedSourceId.StartsWith("IPTV_", StringComparison.OrdinalIgnoreCase))
                    {
                        // Belirli bir Xtream hesabı seçildi
                        string targetAccId = selectedSourceId.Substring(5);
                        var targetAcc = _db.GetAllIptvAccounts().FirstOrDefault(a => a.Id == targetAccId);
                        if (targetAcc != null)
                        {
                            var targetInfo = IptvAccountInfo.FromAccount(targetAcc);
                            baseSourceFiltered = baseSourceFiltered.Where(c => IptvAccountHelper.ChannelBelongsToAccount(c, targetInfo));
                        }
                    }
                }

                var baseCategoryFiltered = baseSourceFiltered;

                if (category == "Favorites") baseCategoryFiltered = baseCategoryFiltered.Where(c => c.IsFavorite);
                else if (category == "TV") baseCategoryFiltered = baseCategoryFiltered.Where(c => string.Equals(c.Category, "TV", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(c.Category));
                else if (category == "Movies") baseCategoryFiltered = baseCategoryFiltered.Where(c => string.Equals(c.Category, "Film", StringComparison.OrdinalIgnoreCase) || string.Equals(c.Category, "Movie", StringComparison.OrdinalIgnoreCase));
                else if (category == "Series") baseCategoryFiltered = baseCategoryFiltered.Where(c => string.Equals(c.Category, "Dizi", StringComparison.OrdinalIgnoreCase) || string.Equals(c.Category, "Series", StringComparison.OrdinalIgnoreCase));
                else if (category == "Radio") baseCategoryFiltered = baseCategoryFiltered.Where(c => string.Equals(c.Category, "Radyo", StringComparison.OrdinalIgnoreCase) || string.Equals(c.Category, "Radio", StringComparison.OrdinalIgnoreCase));

                var baseCategoryList = baseCategoryFiltered.ToList();

                // Compute GroupTitle categories for this active category
                var groupCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var ch in baseCategoryList)
                {
                    string grp = (ch.GroupTitle ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(grp)) grp = "Genel";
                    groupCounts[grp] = groupCounts.TryGetValue(grp, out int count) ? count + 1 : 1;
                }

                var groupList = new List<GroupCategoryItem>();
                groupList.Add(new GroupCategoryItem
                {
                    Name = "All",
                    Count = baseCategoryList.Count,
                    IsSelected = string.Equals(selectedGroup, "All", StringComparison.OrdinalIgnoreCase)
                });

                foreach (var kvp in groupCounts.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
                {
                    groupList.Add(new GroupCategoryItem
                    {
                        Name = kvp.Key,
                        Count = kvp.Value,
                        IsSelected = string.Equals(selectedGroup, kvp.Key, StringComparison.OrdinalIgnoreCase)
                    });
                }

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    GroupCategories.Clear();
                    foreach (var g in groupList) GroupCategories.Add(g);
                    OnPropertyChanged(nameof(HasGroupCategories));
                });

                // Apply group filter if specific group selected
                var filtered = baseCategoryList.AsEnumerable();
                if (!string.Equals(selectedGroup, "All", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = filtered.Where(c =>
                    {
                        string g = (c.GroupTitle ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(g)) g = "Genel";
                        return string.Equals(g, selectedGroup, StringComparison.OrdinalIgnoreCase);
                    });
                }

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
                    // YALNIZCA GERÇEKTEN BÖLÜM NUMARASI VEYA SEZON OLAN İÇERİKLERİ SeriesGroup YAP!
                    // Eğer bölüm numarası yoksa, bunlar bağımsız film veya canlı kanallardır; ASLA tek bir karta birleştirilmemelidir!
                    bool isRealSeriesWithEpisodes = g.Count() > 1 && g.Any(ep => ep.SeasonNumber > 0 || ep.EpisodeNumber > 0 ||
                        System.Text.RegularExpressions.Regex.IsMatch(ep.Name ?? "", @"(?i)\b(s\d+\s*e\d+|bölüm|bolum|sezon)\b"));

                    if (string.IsNullOrWhiteSpace(g.Key) || !isRealSeriesWithEpisodes)
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
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
