using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text.RegularExpressions;

namespace StreamMesh.Models
{
    public class Channel : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _url = string.Empty;
        private string _logoUrl = string.Empty;
        private string _groupTitle = "Genel";
        private string _category = "TV";
        private string _language = "und";
        private string _sourceType = "M3U"; // M3U, YOUTUBE, ACESTREAM
        private string _playlistUrl = string.Empty;
        private string _epgId = string.Empty;
        private string _epgUrl = string.Empty;
        private string _currentEpgTitle = "Yükleniyor...";
        private string _currentEpgTime = "--:--";
        private bool _isFavorite = false;
        private bool _isVerified = false;
        private bool _isLocked = false;
        private bool _isPremium = false;
        private string _notes = string.Empty;
        private DateTime _createdAt = DateTime.Now;
        private int _personalWatchCount = 0;
        private int _viewersCount = 0;

        public static bool IsPosterMode { get; set; } = true;

        // Metadata Fields
        private string _imdbId = string.Empty;
        private string _overview = string.Empty;
        private string _backdropUrl = string.Empty;
        private string _cast = string.Empty;

        public string ImdbId
        {
            get => _imdbId;
            set { if (_imdbId != value) { _imdbId = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasImdb)); } }
        }

        public string Overview
        {
            get => _overview;
            set { if (_overview != value) { _overview = value; OnPropertyChanged(); } }
        }

        public string BackdropUrl
        {
            get => _backdropUrl;
            set { if (_backdropUrl != value) { _backdropUrl = value; OnPropertyChanged(); } }
        }

        public string Cast
        {
            get => _cast;
            set { if (_cast != value) { _cast = value; OnPropertyChanged(); } }
        }

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public bool IsPremium
        {
            get => _isPremium;
            set { if (_isPremium != value) { _isPremium = value; OnPropertyChanged(); } }
        }

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

        public int PersonalWatchCount
        {
            get => _personalWatchCount;
            set { if (_personalWatchCount != value) { _personalWatchCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPersonalWatch)); } }
        }

        public int ViewersCount
        {
            get => _viewersCount;
            set { if (_viewersCount != value) { _viewersCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasViewers)); } }
        }

        public bool HasViewers => _viewersCount > 0;
        public bool HasPersonalWatch => _personalWatchCount > 0;

        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(CleanName)); } }
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
            set { if (_category != value) { _category = value; OnPropertyChanged(); OnPropertyChanged(nameof(CleanName)); } }
        }

        public string Language
        {
            get => _language;
            set
            {
                string normalized = NormalizeLanguage(value);
                if (_language != normalized)
                {
                    _language = normalized;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LanguageDisplayName));
                }
            }
        }

        public string LanguageDisplayName => GetLanguageDisplayName(Language);

        private static readonly Dictionary<string, string> IsoToDisplayName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "tr", "Türkçe" }, { "en", "English" }, { "de", "Deutsch" }, { "fr", "Français" }, { "es", "Español" },
            { "ru", "Русский" }, { "it", "Italiano" }, { "ar", "العربية" }, { "ku", "Kurdî" }, { "az", "Azərbaycan" },
            { "nl", "Nederlands" }, { "pt", "Português" }, { "zh", "中文" }, { "ja", "日本語" }, { "ko", "한국어" },
            { "pl", "Polski" }, { "uk", "Українська" }, { "el", "Ελληνικά" }, { "sv", "Svenska" }, { "ro", "Română" },
            { "hu", "Magyar" }, { "cs", "Čeština" }, { "bg", "Български" }, { "sr", "Srpski" }, { "hr", "Hrvatski" },
            { "und", "Bilinmiyor" }
        };

        public static string GetLanguageDisplayName(string isoCode)
        {
            if (string.IsNullOrWhiteSpace(isoCode)) return "Bilinmiyor";
            var parts = isoCode.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var names = new List<string>();
            foreach (var part in parts)
            {
                string p = part.Trim().ToLowerInvariant();
                if (IsoToDisplayName.TryGetValue(p, out var name)) names.Add(name);
                else names.Add(p.ToUpperInvariant());
            }
            return names.Count > 0 ? string.Join(", ", names) : "Bilinmiyor";
        }

        public static string NormalizeLanguage(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return "und";
            var parts = lang.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var normalizedParts = new List<string>();
            foreach (var part in parts)
            {
                string norm = NormalizeSingleLanguage(part);
                if (norm != "und" && !normalizedParts.Contains(norm)) normalizedParts.Add(norm);
            }
            return normalizedParts.Count > 0 ? string.Join(",", normalizedParts) : "und";
        }

        private static string NormalizeSingleLanguage(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return "und";
            string lower = lang.ToLower(new System.Globalization.CultureInfo("tr-TR")).Trim();
            int parenIndex = lower.IndexOf('(');
            if (parenIndex > 0) lower = lower.Substring(0, parenIndex).Trim();
            string baseCode = lower;
            int separatorIndex = lower.IndexOfAny(new char[] { '-', '_', ' ' });
            if (separatorIndex > 0) baseCode = lower.Substring(0, separatorIndex).Trim();

            if (IsoToDisplayName.ContainsKey(baseCode)) return baseCode.ToLowerInvariant();
            if (lower.Contains("türk") || lower.Contains("turk")) return "tr";
            if (lower.Contains("ingil") || lower.Contains("english")) return "en";
            if (lower.Contains("alman") || lower.Contains("deutsch")) return "de";
            if (lower.Contains("fransiz") || lower.Contains("french") || lower.Contains("français")) return "fr";
            return "und";
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

        public bool HasImdb => !string.IsNullOrEmpty(ImdbId);
        public bool HasMovieYear => !string.IsNullOrEmpty(MovieYear);
        public bool HasMovieGenre => !string.IsNullOrEmpty(MovieGenre);

        // Smart Extraction Logic
        public string CleanName
        {
            get
            {
                if (Category != "Film") return Name;
                return ParsedMovieDetails.CleanName;
            }
        }

        public string ImdbRating => ParsedMovieDetails.ImdbRating;
        public string MovieYear => ParsedMovieDetails.MovieYear;
        public string MovieGenre => ParsedMovieDetails.MovieGenre;

        private string? _lastNameForParsing;
        private MovieDetails? _cachedDetails;

        private MovieDetails ParsedMovieDetails
        {
            get
            {
                if (_lastNameForParsing == Name && _cachedDetails != null) return _cachedDetails;
                _lastNameForParsing = Name;
                _cachedDetails = ParseNameDetails(Name);
                return _cachedDetails;
            }
        }

        private class MovieDetails
        {
            public string CleanName { get; set; } = "";
            public string ImdbRating { get; set; } = "";
            public string MovieYear { get; set; } = "";
            public string MovieGenre { get; set; } = "";
        }

        private MovieDetails ParseNameDetails(string rawName)
        {
            var details = new MovieDetails { CleanName = rawName ?? "" };
            if (string.IsNullOrEmpty(rawName)) return details;

            string working = rawName;

            // IMDb
            var imdbMatch = Regex.Match(working, @"\((?:[★\*]|imdb)[:\-\s]?(\d+(?:\.\d+)?)\)", RegexOptions.IgnoreCase);
            if (imdbMatch.Success) { details.ImdbRating = imdbMatch.Groups[1].Value; working = working.Replace(imdbMatch.Value, ""); }

            // Year
            var yearMatch = Regex.Match(working, @"\((19\d{2}|20\d{2})\)");
            if (yearMatch.Success) { details.MovieYear = yearMatch.Groups[1].Value; working = working.Replace(yearMatch.Value, ""); }

            working = Regex.Replace(working, @"\s+", " ").Trim(' ', ':', '-', '(', ')');
            details.CleanName = string.IsNullOrWhiteSpace(working) ? rawName : working;
            return details;
        }

        public override string ToString() => $"{Name} ({GroupTitle})";

        // Advanced Multi-Source Helpers
        public List<string> GetUrlList() => (Url ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(u => u.Trim()).ToList();
        public List<string> GetLogoList() => (LogoUrl ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(u => u.Trim()).ToList();
        public List<string> GetEpgIdList() => (EpgId ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(u => u.Trim()).ToList();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
