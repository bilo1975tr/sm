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
        private bool _isPremium = false;
        private string _notes = string.Empty;
        private DateTime _createdAt = DateTime.Now;

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

            // Bölge/Kültür veya Boşluk Ayrımı (örn. "tr-tr" -> "tr", "en_us" -> "en")
            string baseCode = lower;
            int separatorIndex = lower.IndexOfAny(new char[] { '-', '_', ' ' });
            if (separatorIndex > 0)
            {
                baseCode = lower.Substring(0, separatorIndex).Trim();
            }

            if (lower.Contains("türk") || lower.Contains("turk") || baseCode == "tr" || baseCode == "tur" || lower.Contains("turkish")) return "Türkçe";
            if (lower.Contains("ingilizce") || lower.Contains("english") || lower.Contains("ingiliz") || baseCode == "en" || baseCode == "eng" || baseCode == "usa" || baseCode == "uk") return "İngilizce";
            if (lower.Contains("almanca") || lower.Contains("deutsch") || lower.Contains("german") || baseCode == "de" || baseCode == "ger" || baseCode == "deu") return "Almanca";
            if (lower.Contains("fransızca") || lower.Contains("french") || lower.Contains("français") || baseCode == "fr" || baseCode == "fra" || lower.Contains("fransizca")) return "Fransızca";
            if (lower.Contains("ispanyolca") || lower.Contains("spanish") || lower.Contains("español") || baseCode == "es" || baseCode == "esp") return "İspanyolca";
            if (lower.Contains("rusça") || lower.Contains("russian") || lower.Contains("русский") || baseCode == "ru" || baseCode == "rus" || lower.Contains("rusca")) return "Rusça";
            if (lower.Contains("italyanca") || lower.Contains("italian") || lower.Contains("italiano") || baseCode == "it" || baseCode == "ita") return "İtalyanca";
            if (lower.Contains("arapça") || lower.Contains("arabic") || baseCode == "ar" || baseCode == "ara" || lower.Contains("arapca")) return "Arapça";
            if (lower.Contains("kürtçe") || lower.Contains("kurtçe") || lower.Contains("kurdish") || baseCode == "ku" || baseCode == "kur" || lower.Contains("kurtce")) return "Kürtçe";
            if (lower.Contains("azerice") || lower.Contains("azerbaijani") || lower.Contains("azeri") || baseCode == "az" || baseCode == "aze") return "Azerice";
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

        // Dizi Gruplama Özellikleri
        private bool _isSeriesGroup = false;
        public bool IsSeriesGroup
        {
            get => _isSeriesGroup;
            set { if (_isSeriesGroup != value) { _isSeriesGroup = value; OnPropertyChanged(); OnPropertyChanged(nameof(CleanName)); } }
        }

        private List<Channel> _seriesEpisodes = null;
        public List<Channel> SeriesEpisodes
        {
            get => _seriesEpisodes;
            set { _seriesEpisodes = value; OnPropertyChanged(); }
        }

        private string _seriesName = string.Empty;
        public string SeriesName
        {
            get => _seriesName;
            set { if (_seriesName != value) { _seriesName = value; OnPropertyChanged(); OnPropertyChanged(nameof(CleanName)); } }
        }

        private int _totalSeasonsCount = 1;
        public int TotalSeasonsCount
        {
            get => _totalSeasonsCount;
            set { if (_totalSeasonsCount != value) { _totalSeasonsCount = value; OnPropertyChanged(); } }
        }

        private int _totalEpisodesCount = 0;
        public int TotalEpisodesCount
        {
            get => _totalEpisodesCount;
            set { if (_totalEpisodesCount != value) { _totalEpisodesCount = value; OnPropertyChanged(); } }
        }

        public class SeriesDetails
        {
            public string SeriesName { get; set; } = string.Empty;
            public int Season { get; set; } = 1;
            public int Episode { get; set; } = 1;
            public string Year { get; set; } = string.Empty;
            public bool IsParsed { get; set; } = false;
        }

        public static SeriesDetails ParseSeriesDetails(string name, string url = null)
        {
            var details = new SeriesDetails { SeriesName = name };
            if (string.IsNullOrEmpty(name)) return details;

            string working = name;

            // 1. Yıl Çıkarımı (Örn: "(2023)" veya "2016")
            var yearRegex = new System.Text.RegularExpressions.Regex(@"\(\s*(19\d{2}|20\d{2})\s*\)");
            var yearMatch = yearRegex.Match(working);
            if (yearMatch.Success)
            {
                details.Year = yearMatch.Groups[1].Value;
                working = working.Replace(yearMatch.Value, "");
            }
            else
            {
                var yearRegexNoParen = new System.Text.RegularExpressions.Regex(@"\b(19\d{2}|20\d{2})\b");
                var yearMatchNoParen = yearRegexNoParen.Match(working);
                if (yearMatchNoParen.Success)
                {
                    details.Year = yearMatchNoParen.Groups[1].Value;
                    working = working.Replace(yearMatchNoParen.Value, "");
                }
            }

            // 2. Sezon ve Bölüm Çıkarımı
            bool parsedSe = false;

            // Pattern A: S01E02 veya s1e2 veya S1 E2 veya S01 E02
            var patA = new System.Text.RegularExpressions.Regex(@"[Ss](\d+)\s*[Ee](\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var patB = new System.Text.RegularExpressions.Regex(@"(\d+)\.?\s*[Ss]ezon\s*(\d+)\.?\s*[Bb]ölüm", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var patC = new System.Text.RegularExpressions.Regex(@"[Ss]ezon\s*(\d+)\s*[Bb]ölüm\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var patD = new System.Text.RegularExpressions.Regex(@"\b(\d+)x(\d+)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var patE = new System.Text.RegularExpressions.Regex(@"(\d+)\.?\s*[Bb]ölüm", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var patE2 = new System.Text.RegularExpressions.Regex(@"[Bb]ölüm\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var matchA = patA.Match(working);
            if (matchA.Success)
            {
                details.Season = int.Parse(matchA.Groups[1].Value);
                details.Episode = int.Parse(matchA.Groups[2].Value);
                working = working.Replace(matchA.Value, "");
                parsedSe = true;
            }

            // Pattern B: 1. Sezon 2. Bölüm veya 1.Sezon 2.Bölüm
            if (!parsedSe)
            {
                var matchB = patB.Match(working);
                if (matchB.Success)
                {
                    details.Season = int.Parse(matchB.Groups[1].Value);
                    details.Episode = int.Parse(matchB.Groups[2].Value);
                    working = working.Replace(matchB.Value, "");
                    parsedSe = true;
                }
            }

            // Pattern C: Sezon 1 Bölüm 2
            if (!parsedSe)
            {
                var matchC = patC.Match(working);
                if (matchC.Success)
                {
                    details.Season = int.Parse(matchC.Groups[1].Value);
                    details.Episode = int.Parse(matchC.Groups[2].Value);
                    working = working.Replace(matchC.Value, "");
                    parsedSe = true;
                }
            }

            // Pattern D: 1x02 veya 1x2
            if (!parsedSe)
            {
                var matchD = patD.Match(working);
                if (matchD.Success)
                {
                    details.Season = int.Parse(matchD.Groups[1].Value);
                    details.Episode = int.Parse(matchD.Groups[2].Value);
                    working = working.Replace(matchD.Value, "");
                    parsedSe = true;
                }
            }

            // Pattern E: Bölüm 2 veya 2. Bölüm (Sezon varsayılan 1)
            if (!parsedSe)
            {
                var matchE = patE.Match(working);
                if (matchE.Success)
                {
                    details.Season = 1;
                    details.Episode = int.Parse(matchE.Groups[1].Value);
                    working = working.Replace(matchE.Value, "");
                    parsedSe = true;
                }
                else
                {
                    var matchE2 = patE2.Match(working);
                    if (matchE2.Success)
                    {
                        details.Season = 1;
                        details.Episode = int.Parse(matchE2.Groups[1].Value);
                        working = working.Replace(matchE2.Value, "");
                        parsedSe = true;
                    }
                }
            }

            // Eğer isimden çözülemediyse ve bir URL adresi verilmişse, adresteki dosya adından çözmeye çalışalım
            if (!parsedSe && !string.IsNullOrEmpty(url))
            {
                try
                {
                    string urlDecoded = System.Uri.UnescapeDataString(url);
                    int lastSlash = urlDecoded.LastIndexOf('/');
                    if (lastSlash >= 0 && lastSlash < urlDecoded.Length - 1)
                    {
                        string segment = urlDecoded.Substring(lastSlash + 1);

                        // Pattern A (S01E02) taraması
                        var matchA_Url = patA.Match(segment);
                        if (matchA_Url.Success)
                        {
                            details.Season = int.Parse(matchA_Url.Groups[1].Value);
                            details.Episode = int.Parse(matchA_Url.Groups[2].Value);
                            parsedSe = true;
                        }

                        // Pattern B (1.Sezon 2.Bölüm) taraması
                        if (!parsedSe)
                        {
                            var matchB_Url = patB.Match(segment);
                            if (matchB_Url.Success)
                            {
                                details.Season = int.Parse(matchB_Url.Groups[1].Value);
                                details.Episode = int.Parse(matchB_Url.Groups[2].Value);
                                parsedSe = true;
                            }
                        }

                        // Pattern C (Sezon 1 Bölüm 2) taraması
                        if (!parsedSe)
                        {
                            var matchC_Url = patC.Match(segment);
                            if (matchC_Url.Success)
                            {
                                details.Season = int.Parse(matchC_Url.Groups[1].Value);
                                details.Episode = int.Parse(matchC_Url.Groups[2].Value);
                                parsedSe = true;
                            }
                        }

                        // Pattern D (1x02) taraması
                        if (!parsedSe)
                        {
                            var matchD_Url = patD.Match(segment);
                            if (matchD_Url.Success)
                            {
                                details.Season = int.Parse(matchD_Url.Groups[1].Value);
                                details.Episode = int.Parse(matchD_Url.Groups[2].Value);
                                parsedSe = true;
                            }
                        }

                        // Pattern E (Bölüm 2) taraması
                        if (!parsedSe)
                        {
                            var matchE_Url = patE.Match(segment);
                            if (matchE_Url.Success)
                            {
                                details.Season = 1;
                                details.Episode = int.Parse(matchE_Url.Groups[1].Value);
                                parsedSe = true;
                            }
                            else
                            {
                                var matchE2_Url = patE2.Match(segment);
                                if (matchE2_Url.Success)
                                {
                                    details.Season = 1;
                                    details.Episode = int.Parse(matchE2_Url.Groups[1].Value);
                                    parsedSe = true;
                                }
                            }
                        }
                    }
                }
                catch {}
            }

            // 3. İsim Temizleme
            string clean = working;

            if (parsedSe)
            {
                // Sezon/bölüm bilgisi çözüldüyse, kanal adındaki fazla bölüm/sezon veya gereksiz kısımları temizleyelim
                int idxDash = clean.IndexOf('-');
                if (idxDash > 0)
                {
                    clean = clean.Substring(0, idxDash);
                }
                else
                {
                    int idxColon = clean.IndexOf(':');
                    if (idxColon > 0) clean = clean.Substring(0, idxColon);
                }

                int idxBracket = clean.IndexOf('[');
                if (idxBracket > 0) clean = clean.Substring(0, idxBracket);

                int idxParen = clean.IndexOf('(');
                if (idxParen > 0) clean = clean.Substring(0, idxParen);
            }

            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+", " ");
            clean = clean.Trim(' ', ':', '-', '(', ')', '[', ']', ',');
            details.SeriesName = string.IsNullOrWhiteSpace(clean) ? name : clean;
            details.IsParsed = parsedSe;

            return details;
        }

        // Akıllı Film Detay Çözümlemesi (Read-Only Properties)
        public string CleanName
        {
            get
            {
                if (IsSeriesGroup) return SeriesName;
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
