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
        private string _language = "Bilinmiyor";
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
            set
            {
                string normalized = NormalizeLanguage(value);
                if (_language != normalized)
                {
                    _language = normalized;
                    OnPropertyChanged();
                }
            }
        }

        public static string NormalizeLanguage(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return "Bilinmiyor";
            
            string lower = lang.ToLower(new System.Globalization.CultureInfo("tr-TR")).Trim();

            // Parantez içi temizliği (örn. "Almanca (Almanya)" -> "Almanca" veya "tr (Turkey)" -> "tr")
            int parenIndex = lower.IndexOf('(');
            if (parenIndex > 0)
            {
                lower = lower.Substring(0, parenIndex).Trim();
            }

            if (lower.Contains("türkçe") || lower.Contains("turkce") || lower == "tr" || lower == "tur" || lower.Contains("turkish")) return "Türkçe";
            if (lower.Contains("ingilizce") || lower.Contains("english") || lower == "en" || lower == "eng" || lower == "usa" || lower == "uk") return "İngilizce";
            if (lower.Contains("almanca") || lower.Contains("deutsch") || lower.Contains("german") || lower == "de" || lower == "ger") return "Almanca";
            if (lower.Contains("fransızca") || lower.Contains("french") || lower.Contains("français") || lower == "fr" || lower == "fra" || lower.Contains("fransizca")) return "Fransızca";
            if (lower.Contains("ispanyolca") || lower.Contains("spanish") || lower.Contains("español") || lower == "es" || lower == "esp") return "İspanyolca";
            if (lower.Contains("rusça") || lower.Contains("russian") || lower.Contains("русский") || lower == "ru" || lower == "rus" || lower.Contains("rusca")) return "Rusça";
            if (lower.Contains("italyanca") || lower.Contains("italian") || lower.Contains("italiano") || lower == "it" || lower == "ita") return "İtalyanca";
            if (lower.Contains("arapça") || lower.Contains("arabic") || lower == "ar" || lower == "ara" || lower.Contains("arapca")) return "Arapça";
            if (lower.Contains("kürtçe") || lower.Contains("kurtçe") || lower.Contains("kurdish") || lower == "ku" || lower == "kur" || lower.Contains("kurtce")) return "Kürtçe";
            if (lower.Contains("azerice") || lower.Contains("azerbaijani") || lower.Contains("azeri") || lower == "az" || lower == "aze") return "Azerice";
            if (lower == "bilinmiyor" || lower == "unknown" || lower == "none" || lower == "hiçbiri") return "Bilinmiyor";

            // Eğer özel bir dille eşleşmediyse, ilk harfini büyük yapıp döndürelim (örn: Portekizce, Yunanca vb)
            string cleanVal = lang;
            int pIdx = cleanVal.IndexOf('(');
            if (pIdx > 0) cleanVal = cleanVal.Substring(0, pIdx).Trim();
            
            if (cleanVal.Length > 0)
            {
                return char.ToUpper(cleanVal[0], new System.Globalization.CultureInfo("tr-TR")) + 
                       (cleanVal.Length > 1 ? cleanVal.Substring(1).ToLower(new System.Globalization.CultureInfo("tr-TR")) : "");
            }

            return "Bilinmiyor";
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

        private static readonly Dictionary<string, string> StandardizedGenres = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "komedi", "Komedi" },
            { "suç", "Suç" },
            { "aksiyon", "Aksiyon" },
            { "macera", "Macera" },
            { "drama", "Dram" },
            { "dram", "Dram" },
            { "gerilim", "Gerilim" },
            { "bilim kurgu", "Bilim Kurgu" },
            { "bilim-kurgu", "Bilim Kurgu" },
            { "bilimkurgu", "Bilim Kurgu" },
            { "fantastik", "Fantastik" },
            { "korku", "Korku" },
            { "gizem", "Gizem" },
            { "romantik", "Romantik" },
            { "animasyon", "Animasyon" },
            { "belgesel", "Belgesel" },
            { "aile", "Aile" },
            { "savaş", "Savaş" },
            { "tarih", "Tarih" },
            { "western", "Western" },
            { "müzikal", "Müzikal" },
            { "biyografi", "Biyografi" },
            { "yerli", "Yerli" },
            { "yabancı", "Yabancı" },
            { "türkçe", "Türkçe" },
            { "polisiye", "Polisiye" }
        };

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
            var imdbRegex = new System.Text.RegularExpressions.Regex(@"\((?:[★\*]\s*|imdb\s*[:\-\s]?\s*)?(\d+(?:\.\d+)?)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var imdbMatch = imdbRegex.Match(workingName);
            if (imdbMatch.Success)
            {
                details.ImdbRating = imdbMatch.Groups[1].Value;
                workingName = workingName.Replace(imdbMatch.Value, "");
            }

            // 2. Yıl Bulma (1900-2029 arası)
            var yearRegex = new System.Text.RegularExpressions.Regex(@"\(\s*(19\d{2}|20\d{2})\s*\)");
            var yearMatch = yearRegex.Match(workingName);
            if (yearMatch.Success)
            {
                details.MovieYear = yearMatch.Groups[1].Value;
                workingName = workingName.Replace(yearMatch.Value, "");
            }
            else
            {
                var yearRegexNoParen = new System.Text.RegularExpressions.Regex(@"\b(19\d{2}|20\d{2})\b");
                var yearMatchNoParen = yearRegexNoParen.Match(workingName);
                if (yearMatchNoParen.Success)
                {
                    details.MovieYear = yearMatchNoParen.Groups[1].Value;
                    workingName = workingName.Replace(yearMatchNoParen.Value, "");
                }
            }

            // 3. Film Türlerini Bulma
            var parenRegex = new System.Text.RegularExpressions.Regex(@"\(([^)]+)\)");
            var cleanGenresList = new List<string>();

            foreach (System.Text.RegularExpressions.Match match in parenRegex.Matches(workingName))
            {
                string chunk = match.Groups[1].Value;
                string cleanedChunk = chunk.ToLowerInvariant();
                bool hasGenreInChunk = false;

                // Bilim kurgu kontrolü
                if (cleanedChunk.Contains("bilim kurgu") || cleanedChunk.Contains("bilim-kurgu") || cleanedChunk.Contains("bilimkurgu"))
                {
                    if (!cleanGenresList.Contains("Bilim Kurgu"))
                    {
                        cleanGenresList.Add("Bilim Kurgu");
                    }
                    hasGenreInChunk = true;
                }

                // Diğer türlerin kontrolü
                foreach (var kvp in StandardizedGenres)
                {
                    if (kvp.Key == "bilim kurgu" || kvp.Key == "bilim-kurgu" || kvp.Key == "bilimkurgu")
                        continue;

                    var wordPattern = @"\b" + System.Text.RegularExpressions.Regex.Escape(kvp.Key) + @"\b";
                    if (System.Text.RegularExpressions.Regex.IsMatch(cleanedChunk, wordPattern))
                    {
                        if (!cleanGenresList.Contains(kvp.Value))
                        {
                            cleanGenresList.Add(kvp.Value);
                        }
                        hasGenreInChunk = true;
                    }
                }

                // Eğer bu parantezin içinde geçerli bir film türü tespit ettiysek, o parantezi isimden temizleyelim
                if (hasGenreInChunk)
                {
                    workingName = workingName.Replace(match.Value, "");
                }
            }

            if (cleanGenresList.Count > 0)
            {
                details.MovieGenre = string.Join(" / ", cleanGenresList);
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
