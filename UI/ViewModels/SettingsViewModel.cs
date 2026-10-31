using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using StreamMesh.Models;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media;
using StreamMesh.Core.Plugins;
using StreamMesh.Core.Utils;

namespace StreamMesh.UI.ViewModels
{
    public class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseEngine _db = new DatabaseEngine();
        private readonly GitHubSyncEngine _sync = new GitHubSyncEngine();
        private readonly AiEngine _ai = new AiEngine();

        public ObservableCollection<M3uSourceDisplay> Sources { get; } = new ObservableCollection<M3uSourceDisplay>();
        public ObservableCollection<IptvAccount> IptvAccounts { get; } = new ObservableCollection<IptvAccount>();
        public ObservableCollection<string> ValidationLogs { get; } = new ObservableCollection<string>();

        // Plugins Management Collections
        public ObservableCollection<IPlugin> DisplayedPlugins { get; } = new ObservableCollection<IPlugin>();
        public ObservableCollection<PluginSettingItem> SelectedPluginSettings { get; } = new ObservableCollection<PluginSettingItem>();
        public ObservableCollection<KeyValuePair<string, string>> SelectedPluginStats { get; } = new ObservableCollection<KeyValuePair<string, string>>();
        public ObservableCollection<string> PluginCategories { get; } = new ObservableCollection<string>
        {
            "Tümü",
            "Yayın & Oynatıcı",
            "Ağ & Paylaşım",
            "Veri & Rehber",
            "Yapay Zeka & Meta",
            "Sistem & Bakım"
        };

        private IPlugin? _selectedPlugin;
        public IPlugin? SelectedPlugin
        {
            get => _selectedPlugin;
            set
            {
                if (_selectedPlugin != value)
                {
                    _selectedPlugin = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasSelectedPlugin));
                    OnPropertyChanged(nameof(SelectedPluginName));
                    OnPropertyChanged(nameof(SelectedPluginDescription));
                    OnPropertyChanged(nameof(SelectedPluginIcon));
                    OnPropertyChanged(nameof(SelectedPluginVersion));
                    OnPropertyChanged(nameof(SelectedPluginAuthor));
                    OnPropertyChanged(nameof(SelectedPluginCategory));
                    OnPropertyChanged(nameof(SelectedPluginStatusText));
                    OnPropertyChanged(nameof(SelectedPluginStatusBadgeColor));
                    RefreshSelectedPluginDetails();
                }
            }
        }

        public bool HasSelectedPlugin => SelectedPlugin != null;
        public string SelectedPluginName => SelectedPlugin?.Name ?? "Eklenti Seçilmedi";
        public string SelectedPluginDescription => SelectedPlugin?.Description ?? "";
        public string SelectedPluginIcon => SelectedPlugin?.Icon ?? "🧩";
        public string SelectedPluginVersion => SelectedPlugin != null ? $"v{SelectedPlugin.Version}" : "";
        public string SelectedPluginAuthor => SelectedPlugin?.Author ?? "";
        public string SelectedPluginCategory => SelectedPlugin?.Category ?? "";
        public string SelectedPluginStatusText => SelectedPlugin?.StatusText ?? "";
        public string SelectedPluginStatusBadgeColor => SelectedPlugin?.StatusBadgeColor ?? "#475569";

        private string _pluginSearchText = "";
        public string PluginSearchText
        {
            get => _pluginSearchText;
            set
            {
                if (_pluginSearchText != value)
                {
                    _pluginSearchText = value;
                    OnPropertyChanged();
                    FilterPlugins();
                }
            }
        }

        private string _selectedCategoryFilter = "Tümü";
        public string SelectedCategoryFilter
        {
            get => _selectedCategoryFilter;
            set
            {
                if (_selectedCategoryFilter != value)
                {
                    _selectedCategoryFilter = value;
                    OnPropertyChanged();
                    FilterPlugins();
                }
            }
        }

        private string _pluginActionFeedback = "";
        public string PluginActionFeedback
        {
            get => _pluginActionFeedback;
            set
            {
                _pluginActionFeedback = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PluginActionFeedbackVisibility));
            }
        }
        public Visibility PluginActionFeedbackVisibility => string.IsNullOrWhiteSpace(_pluginActionFeedback) ? Visibility.Collapsed : Visibility.Visible;

        public int TotalPluginsCount => PluginManager.Instance.Plugins.Count;
        public int ActivePluginsCount => PluginManager.Instance.Plugins.Count(p => p.IsEnabled);

        private string _aiUrl = "";
        public string AiUrl { get => _aiUrl; set { _aiUrl = value; OnPropertyChanged(); } }

        private string _aiModel = "";
        public string AiModel { get => _aiModel; set { _aiModel = value; OnPropertyChanged(); } }

        private string _tmdbApiKey = "";
        public string TmdbApiKey { get => _tmdbApiKey; set { _tmdbApiKey = value; OnPropertyChanged(); } }

        private string _serverPort = "8080";
        public string ServerPort { get => _serverPort; set { _serverPort = value; OnPropertyChanged(); } }

        private int _syncProgress;
        public int SyncProgress { get => _syncProgress; set { _syncProgress = value; OnPropertyChanged(); } }

        private string _syncStatus = "";
        public string SyncStatus { get => _syncStatus; set { _syncStatus = value; OnPropertyChanged(); } }

        public SettingsViewModel()
        {
            // Ultra-lightweight constructor: do NOT execute synchronous blocking queries on the UI thread
            _sync.OnProgress += (p, msg) => {
                SyncProgress = p;
                SyncStatus = msg;
                if (p >= 100 || msg.StartsWith("Hata") || msg.StartsWith("🎉"))
                {
                    _ = RefreshSourcesAsync();
                }
            };

            FilterPlugins();
            if (DisplayedPlugins.Count > 0)
            {
                SelectedPlugin = DisplayedPlugins[0];
            }
        }

        public void LoadSettings()
        {
            _ = LoadSettingsAsync();
        }

        public async Task LoadSettingsAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    string aiUrl = _db.GetSetting("AiUrl", "http://localhost:11434/api/chat");
                    string aiModel = _db.GetSetting("AiModel", "llama3");
                    string tmdbKey = _db.GetSetting("TmdbApiKey", "3fd2be6f0c70a2a598f084dd23308883");
                    string serverPort = _db.GetSetting("ServerPort", "8080");

                    var sourcesList = LoadSourcesInternal();
                    var accountsList = LoadIptvAccountsInternal();

                    // Refresh plugins settings from DB
                    PluginManager.Instance.LoadAllSettings();

                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        AiUrl = aiUrl;
                        AiModel = aiModel;
                        TmdbApiKey = tmdbKey;
                        ServerPort = serverPort;

                        Sources.Clear();
                        foreach (var s in sourcesList) Sources.Add(s);

                        IptvAccounts.Clear();
                        foreach (var a in accountsList) IptvAccounts.Add(a);

                        FilterPlugins();
                        RefreshCounters();
                        RefreshSelectedPluginDetails();
                    });
                }
                catch (Exception ex)
                {
                    LogService.LogError("SettingsViewModel.LoadSettingsAsync error", ex);
                }
            }).ConfigureAwait(false);
        }

        public void SaveSettings()
        {
            _db.SetSetting("AiUrl", AiUrl);
            _db.SetSetting("AiModel", AiModel);
            _db.SetSetting("TmdbApiKey", TmdbApiKey);
            _db.SetSetting("ServerPort", ServerPort);
        }

        public void FilterPlugins()
        {
            var all = PluginManager.Instance.Plugins;
            string search = (PluginSearchText ?? "").Trim().ToLowerInvariant();
            string category = SelectedCategoryFilter ?? "Tümü";

            var filtered = all.Where(p =>
            {
                bool matchesSearch = string.IsNullOrEmpty(search) ||
                                     p.Name.ToLowerInvariant().Contains(search) ||
                                     p.Description.ToLowerInvariant().Contains(search) ||
                                     p.Id.ToLowerInvariant().Contains(search) ||
                                     p.Category.ToLowerInvariant().Contains(search);

                bool matchesCategory = category == "Tümü" || string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase);

                return matchesSearch && matchesCategory;
            }).ToList();

            DisplayedPlugins.Clear();
            foreach (var p in filtered)
            {
                DisplayedPlugins.Add(p);
            }

            if (SelectedPlugin != null && !DisplayedPlugins.Contains(SelectedPlugin) && DisplayedPlugins.Count > 0)
            {
                SelectedPlugin = DisplayedPlugins[0];
            }
            RefreshCounters();
        }

        public void RefreshSelectedPluginDetails()
        {
            SelectedPluginSettings.Clear();
            SelectedPluginStats.Clear();

            if (SelectedPlugin != null)
            {
                foreach (var setting in SelectedPlugin.Settings)
                {
                    SelectedPluginSettings.Add(setting);
                }

                var stats = SelectedPlugin.GetStatistics();
                foreach (var kvp in stats)
                {
                    SelectedPluginStats.Add(kvp);
                }
            }
        }

        public void TogglePlugin(IPlugin? plugin)
        {
            if (plugin == null) return;
            bool newState = !plugin.IsEnabled;
            PluginManager.Instance.SetPluginEnabled(plugin.Id, newState);
            OnPropertyChanged(nameof(SelectedPluginStatusText));
            OnPropertyChanged(nameof(SelectedPluginStatusBadgeColor));
            RefreshCounters();
            RefreshSelectedPluginDetails();
        }

        public void EnableAllPlugins()
        {
            foreach (var p in PluginManager.Instance.Plugins)
            {
                PluginManager.Instance.SetPluginEnabled(p.Id, true);
            }
            RefreshCounters();
            RefreshSelectedPluginDetails();
        }

        public void DisableAllPlugins()
        {
            foreach (var p in PluginManager.Instance.Plugins)
            {
                PluginManager.Instance.SetPluginEnabled(p.Id, false);
            }
            RefreshCounters();
            RefreshSelectedPluginDetails();
        }

        public void SaveSelectedPluginSettings()
        {
            if (SelectedPlugin == null) return;
            PluginManager.Instance.SavePluginSettings(SelectedPlugin);
            PluginActionFeedback = $"💾 '{SelectedPlugin.Name}' ayarları başarıyla kaydedildi.";
            RefreshSelectedPluginDetails();
        }

        public void ResetSelectedPluginSettings()
        {
            if (SelectedPlugin == null) return;
            PluginManager.Instance.ResetPluginSettings(SelectedPlugin);
            PluginActionFeedback = $"↩️ '{SelectedPlugin.Name}' varsayılan fabrika ayarlarına döndürüldü.";
            RefreshSelectedPluginDetails();
        }

        public async Task ExecuteSelectedPluginActionAsync(string actionName)
        {
            if (SelectedPlugin == null) return;
            PluginActionFeedback = "⚡ İşlem yürütülüyor...";
            string result = await PluginManager.Instance.ExecutePluginActionAsync(SelectedPlugin.Id, actionName).ConfigureAwait(false);
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                PluginActionFeedback = result;
                RefreshSelectedPluginDetails();
            });
        }

        public void RefreshCounters()
        {
            OnPropertyChanged(nameof(TotalPluginsCount));
            OnPropertyChanged(nameof(ActivePluginsCount));
        }

        private List<M3uSourceDisplay> LoadSourcesInternal()
        {
            var result = new List<M3uSourceDisplay>();
            try
            {
                var list = _db.GetAllM3uSources();
                string? defaultUrl = _db.GetDefaultM3uSource();
                if (string.IsNullOrEmpty(defaultUrl) && list.Count > 0)
                {
                    defaultUrl = list[0].Url;
                }

                // Batch fetch all channel counts in a single fast query
                var countsMap = _db.GetAllChannelCountsByPlaylist();

                foreach (var s in list)
                {
                    bool isCloud = s.Url.Contains("github") || s.Url.Contains("raw.githubusercontent");
                    bool isDef = s.IsDefault || (defaultUrl != null && string.Equals(s.Url, defaultUrl, StringComparison.OrdinalIgnoreCase));

                    int count = 0;
                    if (countsMap.TryGetValue(s.Url, out int directCount))
                    {
                        count = directCount;
                    }
                    else
                    {
                        string fileName = System.IO.Path.GetFileName(s.Url.Split('?')[0]);
                        foreach (var kvp in countsMap)
                        {
                            if (kvp.Key.Contains(s.Url, StringComparison.OrdinalIgnoreCase) ||
                                (!string.IsNullOrEmpty(fileName) && kvp.Key.Contains(fileName, StringComparison.OrdinalIgnoreCase)))
                            {
                                count += kvp.Value;
                            }
                        }
                    }

                    result.Add(new M3uSourceDisplay
                    {
                        Url = s.Url,
                        Origin = isCloud ? "Bulut" : "Yerel",
                        Color = isDef ? "#059669" : (isCloud ? "#0369a1" : "#1e293b"),
                        ChannelCount = count,
                        IsDefault = isDef
                    });
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("SettingsViewModel.LoadSourcesInternal error", ex);
            }
            return result;
        }

        private List<IptvAccount> LoadIptvAccountsInternal()
        {
            var result = new List<IptvAccount>();
            try
            {
                var raw = _db.GetAllIptvAccounts();
                foreach (var a in raw)
                {
                    if (a.Status != null && (a.Status.Contains("Başarısız") || a.Status.Contains("Hata") || a.Status.Contains("Bağlanılamadı")))
                    {
                        _db.RemoveIptvAccount(a.Id);
                    }
                    else
                    {
                        result.Add(a);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("SettingsViewModel.LoadIptvAccountsInternal error", ex);
            }
            return result;
        }

        public void RefreshSources()
        {
            _ = RefreshSourcesAsync();
        }

        public async Task RefreshSourcesAsync()
        {
            await Task.Run(() =>
            {
                var list = LoadSourcesInternal();
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    Sources.Clear();
                    foreach (var s in list) Sources.Add(s);
                });
            }).ConfigureAwait(false);
        }

        public void RefreshIptvAccounts()
        {
            _ = RefreshIptvAccountsAsync();
        }

        public async Task RefreshIptvAccountsAsync()
        {
            await Task.Run(() =>
            {
                var list = LoadIptvAccountsInternal();
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    IptvAccounts.Clear();
                    foreach (var a in list) IptvAccounts.Add(a);
                });
            }).ConfigureAwait(false);
        }

        public async Task StartCloudSyncAsync()
        {
            await _sync.PullFromGitHubAsync();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class M3uSourceDisplay
    {
        public string Url { get; set; } = "";
        public string Origin { get; set; } = "Yerel";
        public string Color { get; set; } = "#1e293b";
        public int ChannelCount { get; set; } = 0;
        public bool IsDefault { get; set; } = false;

        public Visibility DefaultBadgeVisibility => IsDefault ? Visibility.Visible : Visibility.Collapsed;
        public Visibility MakeDefaultButtonVisibility => IsDefault ? Visibility.Collapsed : Visibility.Visible;
    }
}
