using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StreamMesh.Models
{
    public class IptvAccount : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        private string _name = "Yeni IPTV Hesabı";
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }

        private string _serverUrl = "";
        public string ServerUrl { get => _serverUrl; set { _serverUrl = value; OnPropertyChanged(); } }

        private string _username = "";
        public string Username { get => _username; set { _username = value; OnPropertyChanged(); } }

        private string _password = "";
        public string Password { get => _password; set { _password = value; OnPropertyChanged(); } }

        private string _status = "Bilinmiyor";
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

        private DateTime _expiryDate;
        public DateTime ExpiryDate { get => _expiryDate; set { _expiryDate = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExpiryDateFormatted)); OnPropertyChanged(nameof(DaysRemainingText)); } }

        private int? _maxConnections = null;
        public int? MaxConnections { get => _maxConnections; set { _maxConnections = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConnectionsText)); } }

        private int _activeConnections = 0;
        public int ActiveConnections { get => _activeConnections; set { _activeConnections = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConnectionsText)); } }

        private int _totalLiveStreams = 0;
        public int TotalLiveStreams { get => _totalLiveStreams; set { _totalLiveStreams = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalContentCount)); } }

        private int _totalVodStreams = 0;
        public int TotalVodStreams { get => _totalVodStreams; set { _totalVodStreams = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalContentCount)); } }

        private int _totalSeriesStreams = 0;
        public int TotalSeriesStreams { get => _totalSeriesStreams; set { _totalSeriesStreams = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalContentCount)); } }

        private int _localChannelsCount = 0;
        public int LocalChannelsCount { get => _localChannelsCount; set { _localChannelsCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasLocalDifference)); } }

        private bool _hasServerIndex = false;
        public bool HasServerIndex { get => _hasServerIndex; set { _hasServerIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(IndexStatusText)); OnPropertyChanged(nameof(IndexStatusColor)); } }

        private string _serverVersion = "";
        public string ServerVersion { get => _serverVersion; set { _serverVersion = value; OnPropertyChanged(); } }

        private string _serverTimezone = "";
        public string ServerTimezone { get => _serverTimezone; set { _serverTimezone = value; OnPropertyChanged(); } }

        private string _allowedFormats = "";
        public string AllowedFormats { get => _allowedFormats; set { _allowedFormats = value; OnPropertyChanged(); } }

        private bool _isTrial = false;
        public bool IsTrial { get => _isTrial; set { _isTrial = value; OnPropertyChanged(); } }

        private DateTime _lastChecked = DateTime.MinValue;
        public DateTime LastChecked { get => _lastChecked; set { _lastChecked = value; OnPropertyChanged(); } }

        // Helper UI formatters
        public string ExpiryDateFormatted => ExpiryDate > DateTime.MinValue && ExpiryDate < DateTime.MaxValue.AddYears(-10) 
            ? ExpiryDate.ToString("dd.MM.yyyy HH:mm") 
            : "Süresiz / Sınırsız";

        public string DaysRemainingText
        {
            get
            {
                if (ExpiryDate <= DateTime.MinValue || ExpiryDate > DateTime.MaxValue.AddYears(-10)) return "Süresiz / Sınırsız";
                var rem = ExpiryDate - DateTime.Now;
                if (rem.TotalDays < 0) return "Süresi Doldu!";
                if (rem.TotalDays < 1) return $"Son {(int)rem.TotalHours} saat";
                return $"{(int)rem.TotalDays} gün kaldı";
            }
        }

        public string ConnectionsText
        {
            get
            {
                if (!MaxConnections.HasValue || MaxConnections.Value <= 0)
                {
                    return ActiveConnections > 0 ? $"{ActiveConnections} Aktif (Sınırsız)" : "Sınırsız / Belirtilmemiş";
                }
                return $"{ActiveConnections} / {MaxConnections.Value}";
            }
        }

        public string IndexStatusText => HasServerIndex ? "🌐 Ana İndeks Dosyası Mevcut (Sunucu Havuzu)" : "📁 İndekse Ulaşılamadı (Yalnızca Kütüphane Verisi)";
        public string IndexStatusColor => HasServerIndex ? "#10b981" : "#f59e0b";

        public bool HasLocalDifference => LocalChannelsCount > 0;
        public int TotalContentCount => TotalLiveStreams + TotalVodStreams + TotalSeriesStreams;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
