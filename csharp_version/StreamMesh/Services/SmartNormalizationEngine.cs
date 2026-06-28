using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StreamMesh.Models;
using StreamMesh.Services.P2P;

namespace StreamMesh.Services
{
    public class SmartNormalizationEngine
    {
        private static readonly SmartNormalizationEngine _instance = new SmartNormalizationEngine();
        public static SmartNormalizationEngine Instance => _instance;

        private readonly DatabaseService _databaseService = new DatabaseService();
        private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private readonly ConcurrentDictionary<string, string> _memoryCache = new ConcurrentDictionary<string, string>();

        private static readonly Dictionary<string, string> GroupMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Çocuk
            { "kids", "Çocuk" },
            { "children", "Çocuk" },
            { "cartoons", "Çocuk" },
            { "cartoon", "Çocuk" },
            { "animation", "Çocuk" },
            { "kinder", "Çocuk" },
            { "animationen", "Çocuk" },
            { "dessins animés", "Çocuk" },
            { "animación", "Çocuk" },
            { "bambini", "Çocuk" },
            { "детские", "Çocuk" },
            { "мультфильмы", "Çocuk" },

            // Sinema / Film
            { "movies", "Sinema" },
            { "cinema", "Sinema" },
            { "films", "Sinema" },
            { "movie", "Sinema" },
            { "film", "Sinema" },
            { "sinema", "Sinema" },
            { "kinos", "Sinema" },
            { "películas", "Sinema" },
            { "pelicula", "Sinema" },
            { "filme", "Sinema" },
            { "cine", "Sinema" },
            { "action", "Sinema" },
            { "aksiyon", "Sinema" },
            { "comedy", "Sinema" },
            { "komedi", "Sinema" },
            { "drama", "Sinema" },
            { "thriller", "Sinema" },
            { "gerilim", "Sinema" },
            { "sci-fi", "Sinema" },
            { "bilim kurgu", "Sinema" },
            { "romance", "Sinema" },
            { "romantik", "Sinema" },
            { "horror", "Sinema" },
            { "korku", "Sinema" },
            { "adventures", "Sinema" },
            { "macera", "Sinema" },
            { "fantasy", "Sinema" },
            { "fantastik", "Sinema" },
            { "vod", "Sinema" },
            { "фильмы", "Sinema" },
            { "кино", "Sinema" },

            // Spor
            { "sports", "Spor" },
            { "sport", "Spor" },
            { "football", "Spor" },
            { "soccer", "Spor" },
            { "basketball", "Spor" },
            { "tennis", "Spor" },
            { "formula 1", "Spor" },
            { "f1", "Spor" },
            { "bein sports", "Spor" },
            { "tivibu spor", "Spor" },
            { "ssport", "Spor" },
            { "deportes", "Spor" },
            { "deporte", "Spor" },
            { "esportes", "Spor" },
            { "esporte", "Spor" },
            { "sportive", "Spor" },
            { "спортивные", "Spor" },
            { "спорт", "Spor" },

            // Haber
            { "news", "Haber" },
            { "information", "Haber" },
            { "breaking news", "Haber" },
            { "nachrichten", "Haber" },
            { "infos", "Haber" },
            { "noticias", "Haber" },
            { "notizia", "Haber" },
            { "notizie", "Haber" },
            { "новости", "Haber" },
            { "haberler", "Haber" },

            // Belgesel
            { "documentary", "Belgesel" },
            { "documentaries", "Belgesel" },
            { "nature", "Belgesel" },
            { "history", "Belgesel" },
            { "dokumentar", "Belgesel" },
            { "dokumentationen", "Belgesel" },
            { "documental", "Belgesel" },
            { "documentales", "Belgesel" },
            { "documentários", "Belgesel" },
            { "documentario", "Belgesel" },
            { "documentari", "Belgesel" },
            { "документальные", "Belgesel" },
            { "документалка", "Belgesel" },
            { "belgeseller", "Belgesel" },

            // Müzik
            { "music", "Müzik" },
            { "radio music", "Müzik" },
            { "musique", "Müzik" },
            { "musik", "Müzik" },
            { "música", "Müzik" },
            { "musica", "Müzik" },
            { "музыка", "Müzik" },
            { "клипы", "Müzik" },

            // Dini
            { "religion", "Dini" },
            { "religious", "Dini" },
            { "faith", "Dini" },
            { "glaube", "Dini" },
            { "religiöse", "Dini" },
            { "religioso", "Dini" },
            { "religión", "Dini" },
            { "религия", "Dini" },
            { "islam", "Dini" },
            { "islamic", "Dini" },
            { "quran", "Dini" },
            { "kuran", "Dini" },

            // Genel / Eğlence
            { "entertainment", "Genel" },
            { "general", "Genel" },
            { "variety", "Genel" },
            { "unterhaltung", "Genel" },
            { "allgemein", "Genel" },
            { "divertissement", "Genel" },
            { "général", "Genel" },
            { "entretenimiento", "Genel" },
            { "entretenimento", "Genel" },
            { "intrattenimento", "Genel" },
            { "развлекательные", "Genel" },
            { "общее", "Genel" },
            { "tv", "Genel" },
            { "live tv", "Genel" },
            { "canlı tv", "Genel" },
            { "ulusal", "Genel" },
            { "yerel", "Genel" }
        };

        private SmartNormalizationEngine()
        {
            _databaseService.EnsureNormalizationCacheTableExists();
        }

        public void NormalizeChannel(Channel channel)
        {
            if (channel == null) return;

            // 1. Language Detection & Normalization
            if (string.IsNullOrWhiteSpace(channel.Language) || channel.Language == "Bilinmiyor")
            {
                string detected = DetectLanguage(channel.Name, channel.GroupTitle, null, null, channel.PlaylistUrl);
                if (!string.IsNullOrEmpty(detected) && detected != "Bilinmiyor")
                {
                    channel.Language = detected;
                }
            }
            channel.Language = Channel.NormalizeLanguage(channel.Language);

            // 2. Group Normalization
            if (!string.IsNullOrWhiteSpace(channel.GroupTitle))
            {
                channel.GroupTitle = NormalizeGroup(channel.GroupTitle);
            }

            // 3. Category Determination
            string determinedCategory = DetermineCategory(channel.Category, channel.GroupTitle, channel.Name, channel.PlaylistUrl);
            if (!string.IsNullOrEmpty(determinedCategory))
            {
                channel.Category = determinedCategory;
            }

            // 4. Smart Logo Matching
            if (string.IsNullOrWhiteSpace(channel.LogoUrl) || channel.LogoUrl == "null")
            {
                string matchedLogo = FindBestLogo(channel.Name, channel.Language, out int score);
                if (score >= 80 && !string.IsNullOrEmpty(matchedLogo))
                {
                    channel.LogoUrl = matchedLogo;
                }
            }
        }

        public string NormalizeGroup(string groupTitle)
        {
            if (string.IsNullOrWhiteSpace(groupTitle)) return "Genel";
            string clean = groupTitle.Trim().ToLowerInvariant();

            if (GroupMapping.TryGetValue(clean, out string mapped))
            {
                return mapped;
            }

            foreach (var kvp in GroupMapping)
            {
                if (clean.Contains(kvp.Key))
                {
                    return kvp.Value;
                }
            }

            return groupTitle.Trim();
        }

        public string DetermineCategory(string currentCategory, string groupTitle, string name, string playlistUrl)
        {
            // Priority is original category if it is valid (Film, Dizi, Radyo, TV)
            string normCurrent = currentCategory?.Trim();
            if (normCurrent == "Film" || normCurrent == "Dizi" || normCurrent == "Radyo")
            {
                return normCurrent;
            }

            // Check playlist URL
            if (!string.IsNullOrEmpty(playlistUrl))
            {
                string lowerUrl = playlistUrl.ToLowerInvariant();
                if (lowerUrl.Contains("film")) return "Film";
                if (lowerUrl.Contains("dizi") || lowerUrl.Contains("series")) return "Dizi";
                if (lowerUrl.Contains("radyo") || lowerUrl.Contains("radio")) return "Radyo";
            }

            // Check group title
            if (!string.IsNullOrEmpty(groupTitle))
            {
                string lowerGroup = groupTitle.ToLowerInvariant();
                if (lowerGroup.Contains("sinema") || lowerGroup.Contains("film") || lowerGroup.Contains("movie") || lowerGroup.Contains("vod")) return "Film";
                if (lowerGroup.Contains("dizi") || lowerGroup.Contains("series") || lowerGroup.Contains("show")) return "Dizi";
                if (lowerGroup.Contains("radyo") || lowerGroup.Contains("radio")) return "Radyo";
            }

            // Check name
            if (!string.IsNullOrEmpty(name))
            {
                string lowerName = name.ToLowerInvariant();
                if (lowerName.Contains("radyo") || lowerName.Contains("radio")) return "Radyo";
            }

            return "TV";
        }

        public string DetectLanguage(string channelName, string groupTitle, string tvgGroup, string playlistMetadataLanguage, string playlistUrl)
        {
            string parsedLang = DetectLanguageFromM3uUrlOrMetadata(playlistUrl, playlistMetadataLanguage, groupTitle, tvgGroup, channelName);
            if (!string.IsNullOrEmpty(parsedLang) && parsedLang != "Bilinmiyor")
            {
                return parsedLang;
            }

            if (UserService.CurrentUser != null && !string.IsNullOrEmpty(UserService.CurrentUser.Country))
            {
                string countryLang = MapCountryToLanguage(UserService.CurrentUser.Country);
                if (!string.IsNullOrEmpty(countryLang))
                {
                    return countryLang;
                }
            }

            return "Bilinmiyor";
        }

        private string DetectLanguageFromM3uUrlOrMetadata(string playlistUrl, string playlistMetadataLanguage, string groupTitle, string tvgGroup, string channelName)
        {
            if (!string.IsNullOrEmpty(playlistMetadataLanguage))
            {
                string norm = Channel.NormalizeLanguage(playlistMetadataLanguage);
                if (norm != "Bilinmiyor") return norm;
            }

            if (!string.IsNullOrEmpty(playlistUrl))
            {
                string lowerUrl = playlistUrl.ToLowerInvariant();
                if (lowerUrl.Contains("/tur/") || lowerUrl.Contains("/tr/") || lowerUrl.Contains("turk") || lowerUrl.Contains("tr.m3u")) return "Türkçe";
                if (lowerUrl.Contains("/deu/") || lowerUrl.Contains("/de/") || lowerUrl.Contains("deutsch") || lowerUrl.Contains("de.m3u")) return "Almanca";
                if (lowerUrl.Contains("/eng/") || lowerUrl.Contains("/en/") || lowerUrl.Contains("english") || lowerUrl.Contains("en.m3u")) return "İngilizce";
                if (lowerUrl.Contains("/fra/") || lowerUrl.Contains("/fr/") || lowerUrl.Contains("french") || lowerUrl.Contains("fr.m3u")) return "Fransızca";
                if (lowerUrl.Contains("/esp/") || lowerUrl.Contains("/es/") || lowerUrl.Contains("spanish") || lowerUrl.Contains("es.m3u")) return "İspanyolca";
                if (lowerUrl.Contains("/rus/") || lowerUrl.Contains("/ru/") || lowerUrl.Contains("russian") || lowerUrl.Contains("ru.m3u")) return "Rusça";
                if (lowerUrl.Contains("/ita/") || lowerUrl.Contains("/it/") || lowerUrl.Contains("italian") || lowerUrl.Contains("it.m3u")) return "İtalyanca";
                if (lowerUrl.Contains("/ara/") || lowerUrl.Contains("/ar/") || lowerUrl.Contains("arabic") || lowerUrl.Contains("ar.m3u")) return "Arapça";
            }

            string combinedGroup = $"{groupTitle} {tvgGroup}".ToLower(new System.Globalization.CultureInfo("tr-TR"));
            if (combinedGroup.Contains("türk") || combinedGroup.Contains("turk") || combinedGroup.Contains("turkish") || combinedGroup.Contains(" tr")) return "Türkçe";
            if (combinedGroup.Contains("almanca") || combinedGroup.Contains("deutsch") || combinedGroup.Contains("german") || combinedGroup.Contains(" de")) return "Almanca";
            if (combinedGroup.Contains("ingilizce") || combinedGroup.Contains("english") || combinedGroup.Contains(" en")) return "İngilizce";
            if (combinedGroup.Contains("fransızca") || combinedGroup.Contains("french") || combinedGroup.Contains("français") || combinedGroup.Contains(" fr")) return "Fransızca";
            if (combinedGroup.Contains("ispanyolca") || combinedGroup.Contains("spanish") || combinedGroup.Contains("español") || combinedGroup.Contains(" es")) return "İspanyolca";
            if (combinedGroup.Contains("rusça") || combinedGroup.Contains("russian") || combinedGroup.Contains("русский") || combinedGroup.Contains(" ru")) return "Rusça";
            if (combinedGroup.Contains("italyanca") || combinedGroup.Contains("italian") || combinedGroup.Contains("italiano") || combinedGroup.Contains(" it")) return "İtalyanca";
            if (combinedGroup.Contains("arapça") || combinedGroup.Contains("arabic") || combinedGroup.Contains(" ar")) return "Arapça";

            if (!string.IsNullOrEmpty(channelName))
            {
                string cleanName = channelName.ToUpper(new System.Globalization.CultureInfo("tr-TR"));
                if (cleanName.StartsWith("TR:") || cleanName.StartsWith("[TR]") || cleanName.Contains("(TR)") || cleanName.Contains(" TÜRK ") || cleanName.Contains(" TURK ")) return "Türkçe";
                if (cleanName.StartsWith("DE:") || cleanName.StartsWith("[DE]") || cleanName.Contains("(DE)")) return "Almanca";
                if (cleanName.StartsWith("EN:") || cleanName.StartsWith("UK:") || cleanName.StartsWith("US:") || cleanName.StartsWith("[EN]") || cleanName.Contains("(EN)")) return "İngilizce";
                if (cleanName.StartsWith("FR:") || cleanName.StartsWith("[FR]") || cleanName.Contains("(FR)")) return "Fransızca";
                if (cleanName.StartsWith("ES:") || cleanName.StartsWith("[ES]") || cleanName.Contains("(ES)")) return "İspanyolca";
                if (cleanName.StartsWith("RU:") || cleanName.StartsWith("[RU]") || cleanName.Contains("(RU)")) return "Rusça";
                if (cleanName.StartsWith("IT:") || cleanName.StartsWith("[IT]") || cleanName.Contains("(IT)")) return "İtalyanca";
                if (cleanName.StartsWith("AR:") || cleanName.StartsWith("[AR]") || cleanName.Contains("(AR)")) return "Arapça";
            }

            return "Bilinmiyor";
        }

        private string MapCountryToLanguage(string country)
        {
            if (string.IsNullOrEmpty(country)) return null;
            string lower = country.ToLower(new System.Globalization.CultureInfo("tr-TR"));
            if (lower.Contains("türk") || lower.Contains("turk")) return "Türkçe";
            if (lower.Contains("almanya") || lower.Contains("alman") || lower.Contains("deutsch")) return "Almanca";
            if (lower.Contains("ingiliz") || lower.Contains("birleşik krallık") || lower.Contains("amerika") || lower.Contains("english") || lower.Contains("united kingdom") || lower.Contains("united states")) return "İngilizce";
            if (lower.Contains("fransa") || lower.Contains("fransız")) return "Fransızca";
            if (lower.Contains("ispanya") || lower.Contains("ispanyol")) return "İspanyolca";
            if (lower.Contains("rusya") || lower.Contains("rus")) return "Rusça";
            if (lower.Contains("italya") || lower.Contains("italyan")) return "İtalyanca";
            if (lower.Contains("suudi") || lower.Contains("mısır") || lower.Contains("arap")) return "Arapça";
            return null;
        }

        public string FindBestLogo(string channelName, string language, out int score)
        {
            score = 0;
            if (string.IsNullOrWhiteSpace(channelName)) return null;

            string key = $"logo|{channelName}|{language}";
            string cached = _databaseService.GetCachedNormalization(key);
            if (cached != null)
            {
                if (cached.StartsWith("SCORE:"))
                {
                    var parts = cached.Split('|', 2);
                    if (parts.Length == 2 && int.TryParse(parts[0].Replace("SCORE:", ""), out score))
                    {
                        return parts[1];
                    }
                }
            }

            var logos = LogoSearchService.CachedLogos;
            if (logos == null || logos.Count == 0)
            {
                return null;
            }

            string cleanQuery = channelName.ToLowerInvariant().Trim();

            // 1. Exact match (Score 100)
            var exact = logos.FirstOrDefault(x => x.Name.Equals(cleanQuery, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                score = 100;
                _databaseService.SetCachedNormalization(key, $"SCORE:{score}|{exact.LogoUrl}");
                return exact.LogoUrl;
            }

            // 2. Alias match (Score 95)
            string normQuery = NormalizeChannelNameForLogoMatching(cleanQuery);
            var alias = logos.FirstOrDefault(x => NormalizeChannelNameForLogoMatching(x.Name) == normQuery);
            if (alias != null)
            {
                score = 95;
                _databaseService.SetCachedNormalization(key, $"SCORE:{score}|{alias.LogoUrl}");
                return alias.LogoUrl;
            }

            // 3. Similarity match (Score 80-90)
            double bestSim = 0;
            LogoSearchResult bestMatch = null;
            foreach (var logo in logos)
            {
                double sim = CalculateSimilarity(normQuery, NormalizeChannelNameForLogoMatching(logo.Name));
                if (sim > bestSim)
                {
                    bestSim = sim;
                    bestMatch = logo;
                }
            }

            if (bestMatch != null && bestSim >= 0.8)
            {
                score = (int)(bestSim * 100);
                _databaseService.SetCachedNormalization(key, $"SCORE:{score}|{bestMatch.LogoUrl}");
                return bestMatch.LogoUrl;
            }

            return null;
        }

        private string NormalizeChannelNameForLogoMatching(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            name = name.ToLowerInvariant();
            name = name.Replace(" hd", "").Replace(" sd", "").Replace(" fhd", "").Replace(" uhd", "").Replace(" hq", "");
            name = Regex.Replace(name, @"[^a-zA-Z0-9]", "");
            return name;
        }

        private double CalculateSimilarity(string source, string target)
        {
            if (source == target) return 1.0;
            if (target.StartsWith(source) || source.StartsWith(target)) return 0.9;
            if (target.Contains(source) || source.Contains(target)) return 0.8;
            return 0.0;
        }
    }
}
