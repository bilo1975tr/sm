using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StreamMesh.Models
{
    public class Channel : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _url = string.Empty;
        private string _logoUrl = string.Empty;
        private string _groupTitle = "Genel";
        private string _category = "TV";
        private string _language = "English";
        private string _sourceType = "M3U"; // M3U, YOUTUBE, ACESTREAM
        private string _playlistUrl = string.Empty;
        private string _epgId = string.Empty;
        private string _epgUrl = string.Empty;
        private string _currentEpgTitle;
        private string _currentEpgTime;
        private bool _isFavorite = false;
        private bool _isVerified = false;
        private bool _isLocked = false;
        private string _notes = string.Empty;
        private DateTime _createdAt = DateTime.Now;

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public bool IsVerified
        {
            get => _isVerified;
            set { if (_isVerified != value) { _isVerified = value; OnPropertyChanged(); } }
        }

        public bool IsLocked
        {
            get => _isLocked;
            set { if (_isLocked != value) { _isLocked = value; OnPropertyChanged(); } }
        }

        public string Notes
        {
            get => _notes;
            set { if (_notes != value) { _notes = value; OnPropertyChanged(); } }
        }

        public DateTime CreatedAt
        {
            get => _createdAt;
            set { if (_createdAt != value) { _createdAt = value; OnPropertyChanged(); } }
        }

        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
        }

        public string EpgId
        {
            get => _epgId;
            set { if (_epgId != value) { _epgId = value; OnPropertyChanged(); } }
        }

        public string EpgUrl
        {
            get => _epgUrl;
            set { if (_epgUrl != value) { _epgUrl = value; OnPropertyChanged(); } }
        }

        public bool IsFavorite
        {
            get => _isFavorite;
            set { if (_isFavorite != value) { _isFavorite = value; OnPropertyChanged(); } }
        }

        public string Url
        {
            get => _url;
            set { if (_url != value) { _url = value; OnPropertyChanged(); OnPropertyChanged(nameof(SourcesCount)); } }
        }

        public int SourcesCount => string.IsNullOrEmpty(_url) ? 0 : _url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Length;
        public bool HasMultipleSources => SourcesCount > 1;


        public string LogoUrl
        {
            get => _logoUrl;
            set { if (_logoUrl != value) { _logoUrl = value; OnPropertyChanged(); } }
        }

        public string GroupTitle
        {
            get => _groupTitle;
            set { if (_groupTitle != value) { _groupTitle = value; OnPropertyChanged(); } }
        }

        public string Category
        {
            get => _category;
            set { if (_category != value) { _category = value; OnPropertyChanged(); } }
        }

        public string Language
        {
            get => _language;
            set { if (_language != value) { _language = value; OnPropertyChanged(); } }
        }

        public string SourceType
        {
            get => _sourceType;
            set { if (_sourceType != value) { _sourceType = value; OnPropertyChanged(); } }
        }

        public string PlaylistUrl
        {
            get => _playlistUrl;
            set { if (_playlistUrl != value) { _playlistUrl = value; OnPropertyChanged(); } }
        }

        // EPG Info for UI
        public string CurrentEpgTitle
        {
            get => _currentEpgTitle;
            set { if (_currentEpgTitle != value) { _currentEpgTitle = value; OnPropertyChanged(); } }
        }

        public string CurrentEpgTime
        {
            get => _currentEpgTime;
            set { if (_currentEpgTime != value) { _currentEpgTime = value; OnPropertyChanged(); } }
        }

        private int _viewersCount = 0;
        public int ViewersCount
        {
            get => _viewersCount;
            set { if (_viewersCount != value) { _viewersCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasViewers)); } }
        }

        public bool HasViewers => _viewersCount > 0;

        private int _personalWatchCount = 0;
        public int PersonalWatchCount
        {
            get => _personalWatchCount;
            set { 
                if (_personalWatchCount != value) { 
                    _personalWatchCount = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(HasPersonalWatch));
                } 
            }
        }

        public bool HasPersonalWatch => PersonalWatchCount > 0;

        public override string ToString()
        {
            return $"{Name} ({GroupTitle})";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
