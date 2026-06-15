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

        // Akıllı Film Detay Çözümlemesi (Read-Only Properties)
        public string CleanName
        {
            get
            {
                if (Category != "Film") return Name;
                return ParsedMovieDetails.CleanName;
            }
        }

        public string ImdbRating
        {
            get
            {
                if (Category != "Film") return string.Empty;
                return ParsedMovieDetails.ImdbRating;
            }
        }

        public string MovieYear
        {
            get
            {
                if (Category != "Film") return string.Empty;
                return ParsedMovieDetails.MovieYear;
            }
        }

        public string MovieGenre
        {
            get
            {
                if (Category != "Film") return string.Empty;
                return ParsedMovieDetails.MovieGenre;
            }
        }

        public bool HasImdb => !string.IsNullOrEmpty(ImdbRating);
        public bool HasMovieYear => !string.IsNullOrEmpty(MovieYear);
        public bool HasMovieGenre => !string.IsNullOrEmpty(MovieGenre);

        // Performans için önbellekleme (lazy cache)
        private string _lastNameForParsing = null;
        private MovieDetails _cachedDetails = null;

        private MovieDetails ParsedMovieDetails
        {
            get
            {
                if (_lastNameForParsing == Name && _cachedDetails != null)
                    return _cachedDetails;

                _lastNameForParsing = Name;
                _cachedDetails = ParseNameDetails(Name);
                return _cachedDetails;
            }
        }

        private class MovieDetails
        {
            public string CleanName { get; set; }
            public string ImdbRating { get; set; }
            public string MovieYear { get; set; }
            public string MovieGenre { get; set; }
        }

        private MovieDetails ParseNameDetails(string rawName)
        {
            var details = new MovieDetails
            {
                CleanName = rawName ?? string.Empty,
                ImdbRating = "",
                MovieYear = "",
                MovieGenre = ""
            };

            if (string.IsNullOrEmpty(rawName)) return details;

            string workingName = rawName;

            // 1. IMDb Puanı Bulma
            // Örnekler: (★ 8.2), (imdb: 8.2), (8.2) gibi ifadelere bakabiliriz.
            var imdbRegex = new System.Text.RegularExpressions.Regex(@"\((?:[★\*]\s*|imdb\s*[:\-\s]?\s*)?(\d+(?:\.\d+)?)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var imdbMatch = imdbRegex.Match(workingName);
            if (imdbMatch.Success)
            {
                details.ImdbRating = imdbMatch.Groups[1].Value;
                workingName = workingName.Replace(imdbMatch.Value, "");
            }

            // 2. Yıl Bulma (1900-2029 arası 4 basamaklı sayılar, parantez içinde)
            var yearRegex = new System.Text.RegularExpressions.Regex(@"\((19\d{2}|20\d{2})\)");
            var yearMatch = yearRegex.Match(workingName);
            if (yearMatch.Success)
            {
                details.MovieYear = yearMatch.Groups[1].Value;
                workingName = workingName.Replace(yearMatch.Value, "");
            }
            else
            {
                // Parantezsiz yıl da olabilir (en sonda boşluktan sonra)
                var yearRegexNoParen = new System.Text.RegularExpressions.Regex(@"\b(19\d{2}|20\d{2})\b");
                var yearMatchNoParen = yearRegexNoParen.Match(workingName);
                if (yearMatchNoParen.Success)
                {
                    details.MovieYear = yearMatchNoParen.Groups[1].Value;
                    workingName = workingName.Replace(yearMatchNoParen.Value, "");
                }
            }

            // 3. Film Türü Bulma (Parantez içi harf/karakter içeren kelimeler, örn: (Komedi-Suç) veya (Aksiyon/Macera))
            var genreKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "komedi", "suç", "aksiyon", "macera", "drama", "dram", "gerilim", "bilim kurgu", "bilim-kurgu", 
                "fantastik", "korku", "gizem", "romantik", "animasyon", "belgesel", "aile", "savaş", "tarih", 
                "western", "müzikal", "biyografi", "komedi-suç", "suç-komedi", "aksiyon-macera", "yerli", "yabancı", "türkçe"
            };

            var genreRegex = new System.Text.RegularExpressions.Regex(@"\(([A-Za-zÇŞĞÜÖİçşğüöı\s\-\/\+]+)\)");
            var genreMatches = genreRegex.Matches(workingName);
            foreach (System.Text.RegularExpressions.Match match in genreMatches)
            {
                string val = match.Groups[1].Value.Trim();
                bool isGenre = false;
                if (genreKeywords.Contains(val))
                {
                    isGenre = true;
                }
                else
                {
                    var parts = val.Split(new[] { '-', '/', ' ', '+' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var p in parts)
                    {
                        if (genreKeywords.Contains(p))
                        {
                            isGenre = true;
                            break;
                        }
                    }
                }

                if (isGenre)
                {
                    details.MovieGenre = val;
                    workingName = workingName.Replace(match.Value, "");
                    break;
                }
            }

            // 4. Temizlenmiş İsim
            string clean = workingName;
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+", " ");
            clean = clean.Trim(' ', ':', '-', '(', ')');
            details.CleanName = string.IsNullOrWhiteSpace(clean) ? rawName : clean;

            return details;
        }

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
