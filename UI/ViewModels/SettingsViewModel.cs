using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using StreamMesh.Models;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media;
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
            LoadSettings();
            _sync.OnProgress += (p, msg) => {
                SyncProgress = p;
                SyncStatus = msg;
                if (p >= 100 || msg.StartsWith("Hata") || msg.StartsWith("🎉"))
                {
                    RefreshSources();
                }
            };
        }

        public void LoadSettings()
        {
            AiUrl = _db.GetSetting("AiUrl", "http://localhost:11434/api/chat");
            AiModel = _db.GetSetting("AiModel", "llama3");
            TmdbApiKey = _db.GetSetting("TmdbApiKey", "3fd2be6f0c70a2a598f084dd23308883");
            ServerPort = _db.GetSetting("ServerPort", "8080");
            RefreshSources();
            RefreshIptvAccounts();
        }

        public void SaveSettings()
        {
            _db.SetSetting("AiUrl", AiUrl);
            _db.SetSetting("AiModel", AiModel);
            _db.SetSetting("TmdbApiKey", TmdbApiKey);
            _db.SetSetting("ServerPort", ServerPort);
        }

        public void RefreshSources()
        {
            Sources.Clear();
            var list = _db.GetM3uSources();
            foreach (var s in list)
            {
                bool isCloud = s.Contains("github") || s.Contains("raw.githubusercontent");
                Sources.Add(new M3uSourceDisplay {
                    Url = s, Origin = isCloud ? "Bulut" : "Yerel",
                    Color = isCloud ? "#0369a1" : "#1e293b",
                    ChannelCount = _db.GetChannelCountBySource(s)
                });
            }
        }

        public void RefreshIptvAccounts()
        {
            IptvAccounts.Clear();
            var list = _db.GetAllIptvAccounts();
            foreach (var a in list) IptvAccounts.Add(a);
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
    }
}
