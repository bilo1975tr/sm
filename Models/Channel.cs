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
        private bool _isEpgLocked = false;
        private bool _isPremium = false;
        private bool _isWatched = false;
        private string _notes = string.Empty;
        private DateTime _createdAt = DateTime.Now;
        private int _personalWatchCount = 0;
        private int _viewersCount = 0;
        private long _lastPositionMs = 0;
        private string _urlSpeeds = ""; // JSON string for URL-speed mapping

        public long LastPositionMs { get => _lastPositionMs; set { _lastPositionMs = value; OnPropertyChanged(); } }

        private int _preferredNameIndex = 0;
        private int _preferredLogoIndex = 0;
        private int _preferredEpgIndex = 0;

        public int PreferredNameIndex { get => _preferredNameIndex; set { _preferredNameIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(PrimaryName)); } }
        public int PreferredLogoIndex { get => _preferredLogoIndex; set { _preferredLogoIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(LogoUrl)); } }
        public int PreferredEpgIndex { get => _preferredEpgIndex; set { _preferredEpgIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(EpgId)); } }

        public string UrlSpeeds { get => _urlSpeeds; set { _urlSpeeds = value; OnPropertyChanged(); } }

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

        public bool IsEpgLocked
        {
            get => _isEpgLocked;
            set { if (_isEpgLocked != value) { _isEpgLocked = value; OnPropertyChanged(); } }
        }

        public bool IsWatched
        {
            get => _isWatched;
            set { if (_isWatched != value) { _isWatched = value; OnPropertyChanged(); } }
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
            set { if (_name != value) { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(PrimaryName)); OnPropertyChanged(nameof(CleanName)); OnPropertyChanged(nameof(NamesCount)); OnPropertyChanged(nameof(HasMultipleNames)); } }
        }

        public string PrimaryName
        {
            get
            {
                var list = GetNamesList();
                if (list.Count == 0) return Name ?? "";
                if (PreferredNameIndex >= 0 && PreferredNameIndex < list.Count) return list[PreferredNameIndex];
                return list[0];
            }
        }

        public int NamesCount => GetNamesList().Count;
        public bool HasMultipleNames => NamesCount > 1;

        public string EpgId
        {
            get => _epgId;
            set { if (_epgId != value) { _epgId = value; OnPropertyChanged(); OnPropertyChanged(nameof(EpgsCount)); OnPropertyChanged(nameof(HasMultipleEpgs)); } }
        }

        public int EpgsCount => GetEpgIdList().Count;
        public bool HasMultipleEpgs => EpgsCount > 1;

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
            set { if (_url != value) { _url = value; OnPropertyChanged(); OnPropertyChanged(nameof(SourcesCount)); OnPropertyChanged(nameof(HasMultipleSources)); } }
        }

        public int SourcesCount => string.IsNullOrEmpty(_url) ? 0 : _url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Length;
        public bool HasMultipleSources => SourcesCount > 1;

        public string LogoUrl
        {
            get => _logoUrl;
            set { if (_logoUrl != value) { _logoUrl = value; OnPropertyChanged(); OnPropertyChanged(nameof(LogosCount)); OnPropertyChanged(nameof(HasMultipleLogos)); } }
        }

        public int LogosCount => GetLogoList().Count;
        public bool HasMultipleLogos => LogosCount > 1;

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
            
            string lowerInput = lang.ToLowerInvariant();
            if (lowerInput.Contains("[de]") || lowerInput.Contains("(de)") || lowerInput.Contains(" deutsch ") || lowerInput.EndsWith(" de")) return "de";
            if (lowerInput.Contains("[tr]") || lowerInput.Contains("(tr)") || lowerInput.Contains(" türk ") || lowerInput.Contains(" turkey ")) return "tr";
            if (lowerInput.Contains("[en]") || lowerInput.Contains("(en)") || lowerInput.Contains(" english ")) return "en";
            if (lowerInput.Contains("[fr]") || lowerInput.Contains("(fr)") || lowerInput.Contains(" french ")) return "fr";

            var parts = lang.Split(new[] { ',', ' ', '[', ']', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
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
            string lower = lang.ToLower(new System.Globalization.CultureInfo("tr-TR")).Trim(' ', '[', ']', '(', ')');
            int parenIndex = lower.IndexOf('(');
            if (parenIndex > 0) lower = lower.Substring(0, parenIndex).Trim();
            string baseCode = lower;
            int separatorIndex = lower.IndexOfAny(new char[] { '-', '_', ' ' });
            if (separatorIndex > 0) baseCode = lower.Substring(0, separatorIndex).Trim();

            if (IsoToDisplayName.ContainsKey(baseCode)) return baseCode.ToLowerInvariant();
            if (lower.Contains("türk") || lower.Contains("turk") || lower.Contains("tr")) return "tr";
            if (lower.Contains("ingil") || lower.Contains("english") || lower.Contains("en")) return "en";
            if (lower.Contains("alman") || lower.Contains("deutsch") || lower.Contains("de")) return "de";
            if (lower.Contains("fransiz") || lower.Contains("french") || lower.Contains("français") || lower.Contains("fr")) return "fr";
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
        public string CleanName => string.IsNullOrWhiteSpace(Name) ? "" : StreamMesh.Core.Media.ChannelUtils.GetCleanName(Name);

        public int SeasonNumber => ParsedMovieDetails.Season;
        public int EpisodeNumber => ParsedMovieDetails.Episode;
        public string SeriesBaseName => ParsedMovieDetails.SeriesTitle;

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
            public string SeriesTitle { get; set; } = "";
            public int Season { get; set; } = 0;
            public int Episode { get; set; } = 0;
            public string ImdbRating { get; set; } = "";
            public string MovieYear { get; set; } = "";
            public string MovieGenre { get; set; } = "";
        }

        private MovieDetails ParseNameDetails(string rawName)
        {
            var details = new MovieDetails { CleanName = rawName ?? "", SeriesTitle = rawName ?? "" };
            if (string.IsNullOrEmpty(rawName)) return details;

            string working = rawName;

            // IMDb
            var imdbMatch = Regex.Match(working, @"\((?:[★\*]|imdb)[:\-\s]?(\d+(?:\.\d+)?)\)", RegexOptions.IgnoreCase);
            if (imdbMatch.Success) { details.ImdbRating = imdbMatch.Groups[1].Value; working = working.Replace(imdbMatch.Value, ""); }

            // Year
            var yearMatch = Regex.Match(working, @"\((19\d{2}|20\d{2})\)");
            if (yearMatch.Success) { details.MovieYear = yearMatch.Groups[1].Value; working = working.Replace(yearMatch.Value, ""); }

            // S01E01 or 1x01 pattern
            var seriesMatch = Regex.Match(working, @"(?i)s(\d+)\s?e(\d+)|(\d+)x(\d+)");
            if (seriesMatch.Success)
            {
                if (!string.IsNullOrEmpty(seriesMatch.Groups[1].Value))
                {
                    int.TryParse(seriesMatch.Groups[1].Value, out int s); details.Season = s;
                    int.TryParse(seriesMatch.Groups[2].Value, out int e); details.Episode = e;
                }
                else
                {
                    int.TryParse(seriesMatch.Groups[3].Value, out int s); details.Season = s;
                    int.TryParse(seriesMatch.Groups[4].Value, out int e); details.Episode = e;
                }
                details.SeriesTitle = working.Substring(0, seriesMatch.Index).Trim(' ', '-', '_', ':');
            }

            working = Regex.Replace(working, @"\s+", " ").Trim(' ', ':', '-', '(', ')');
            details.CleanName = string.IsNullOrWhiteSpace(working) ? rawName : working;
            return details;
        }

        public override string ToString() => $"{Name} ({GroupTitle})";

        // Advanced Multi-Source & Multi-Alternative Helpers
        public List<string> GetNamesList()
        {
            if (string.IsNullOrWhiteSpace(Name)) return new List<string>();
            return Name.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(n => n.Trim())
                       .Where(n => !string.IsNullOrEmpty(n))
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .ToList();
        }

        public List<string> GetUrlList()
        {
            if (string.IsNullOrWhiteSpace(Url)) return new List<string>();
            return Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(u => u.Trim())
                      .Where(u => !string.IsNullOrEmpty(u))
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .ToList();
        }

        public List<string> GetLogoList()
        {
            if (string.IsNullOrWhiteSpace(LogoUrl)) return new List<string>();
            return LogoUrl.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(l => l.Trim())
                          .Where(l => !string.IsNullOrEmpty(l))
                          .Distinct(StringComparer.OrdinalIgnoreCase)
                          .ToList();
        }

        public List<string> GetEpgIdList()
        {
            if (string.IsNullOrWhiteSpace(EpgId)) return new List<string>();
            return EpgId.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(e => e.Trim())
                        .Where(e => !string.IsNullOrEmpty(e))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
        }

        public void AddAlternativeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            var list = GetNamesList();
            if (!list.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                list.Add(name.Trim());
                Name = string.Join(", ", list);
            }
        }

        public void AddAlternativeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            var list = GetUrlList();
            if (!list.Contains(url.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                list.Add(url.Trim());
                Url = string.Join(",", list);
            }
        }

        public void AddAlternativeLogo(string logoUrl)
        {
            if (string.IsNullOrWhiteSpace(logoUrl)) return;
            var list = GetLogoList();
            if (!list.Contains(logoUrl.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                list.Add(logoUrl.Trim());
                LogoUrl = string.Join(",", list);
            }
        }

        public void AddAlternativeEpgId(string epgId)
        {
            if (string.IsNullOrWhiteSpace(epgId)) return;
            var list = GetEpgIdList();
            if (!list.Contains(epgId.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                list.Add(epgId.Trim());
                EpgId = string.Join(",", list);
            }
        }

        public void MergeWith(Channel other)
        {
            if (other == null) return;

            // 1. Merge Names
            foreach (var n in other.GetNamesList()) AddAlternativeName(n);

            // 2. Merge URLs
            foreach (var u in other.GetUrlList()) AddAlternativeUrl(u);

            // 3. Merge Logos
            foreach (var l in other.GetLogoList()) AddAlternativeLogo(l);

            // 4. Merge EPG IDs
            foreach (var e in other.GetEpgIdList()) AddAlternativeEpgId(e);

            // 5. Merge Metadata if missing
            if (string.IsNullOrWhiteSpace(ImdbId) && !string.IsNullOrWhiteSpace(other.ImdbId)) ImdbId = other.ImdbId;
            if (string.IsNullOrWhiteSpace(Overview) && !string.IsNullOrWhiteSpace(other.Overview)) Overview = other.Overview;
            if (string.IsNullOrWhiteSpace(BackdropUrl) && !string.IsNullOrWhiteSpace(other.BackdropUrl)) BackdropUrl = other.BackdropUrl;
            if (string.IsNullOrWhiteSpace(Cast) && !string.IsNullOrWhiteSpace(other.Cast)) Cast = other.Cast;
            if ((string.IsNullOrWhiteSpace(Category) || Category == "TV") && !string.IsNullOrWhiteSpace(other.Category) && other.Category != "TV") Category = other.Category;
            if ((string.IsNullOrWhiteSpace(Language) || Language == "und") && !string.IsNullOrWhiteSpace(other.Language) && other.Language != "und") Language = other.Language;

            // 6. Merge Lock States
            if (other.IsEpgLocked) IsEpgLocked = true;
            if (other.IsLocked) IsLocked = true;
            if (other.IsFavorite) IsFavorite = true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
