using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StreamMesh.Models;
using StreamMesh.Core.Database;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public static class ChannelUtils
    {
        public static string GetCleanName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return "";

            // V1.8.7: [de], [tr], [en] gibi ülke etiketlerini ve 4 haneli kodları temizlikten muaf tut.
            bool hasSignificantTag = Regex.IsMatch(rawName, @"\d{4}|\[(de|tr|en|fr|es|it|ru|uk|us)\]", RegexOptions.IgnoreCase);

            string cleaned;
            if (hasSignificantTag)
            {
                cleaned = Regex.Replace(rawName, @"\[(?!(de|tr|en|fr|es|it|ru|uk|us)\])\.*?\]|\((?!(de|tr|en|fr|es|it|ru|uk|us)\))\.*?\)", "", RegexOptions.IgnoreCase).Trim();
            }
            else
            {
                cleaned = Regex.Replace(rawName, @"\[.*?\]|\(.*?\)", "").Trim();
            }

            cleaned = Regex.Replace(cleaned, @"\b(fhd|hd|sd|4k|raw|1080p|720p|hevc|h265|h264)\b", "", RegexOptions.IgnoreCase).Trim();
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', '-', '_', ':', '|', '.');
            return string.IsNullOrWhiteSpace(cleaned) ? rawName : cleaned;
        }

        public static string ToNormalizedKey(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            if (Regex.IsMatch(name, @"\d{4}\s*\[de\]", RegexOptions.IgnoreCase))
            {
                return name.ToLowerInvariant().Replace(" ", "");
            }
            string clean = GetCleanName(name);
            return Regex.Replace(clean.ToLowerInvariant(), @"[^a-z0-9]", "");
        }

        public static bool MatchesLanguageFilter(string channelName, string languageFilter)
        {
            if (string.IsNullOrWhiteSpace(languageFilter) || languageFilter.Contains("Tüm")) return true;
            if (string.IsNullOrWhiteSpace(channelName)) return false;

            string nameLower = channelName.ToLowerInvariant();
            if (languageFilter.Contains("Almanca") || languageFilter.Contains("DE") || languageFilter.Equals("de", StringComparison.OrdinalIgnoreCase))
            {
                bool isGerman = Regex.IsMatch(nameLower, @"\d{4}\s*\[de\]") || nameLower.Contains("[de]") || nameLower.Contains("(de)") || nameLower.Contains("deutsch") || nameLower.Contains("german");
                return isGerman;
            }
            else if (languageFilter.Contains("Türkçe") || languageFilter.Contains("TR") || languageFilter.Equals("tr", StringComparison.OrdinalIgnoreCase))
            {
                bool hasOtherLangTag = Regex.IsMatch(nameLower, @"\[(de|en|fr|es|it|ru|uk|us)\]|\((de|en|fr|es|it|ru|uk|us)\)");
                bool isTurkish = nameLower.Contains("[tr]") || nameLower.Contains("(tr)") || nameLower.Contains("türk") || nameLower.Contains("turk") || nameLower.Contains("tr ") || nameLower.EndsWith(" tr");
                return isTurkish || !hasOtherLangTag;
            }
            else if (languageFilter.Contains("İngilizce") || languageFilter.Contains("EN") || languageFilter.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                bool isEnglish = nameLower.Contains("[en]") || nameLower.Contains("(en)") || nameLower.Contains("english") || nameLower.Contains("ingiltere") || nameLower.Contains("usa") || nameLower.Contains("uk");
                return isEnglish;
            }
            return true;
        }

        public static int CalculateSearchScore(Channel ch, string query)
        {
            if (ch == null || string.IsNullOrWhiteSpace(query)) return 0;
            string q = query.Trim().ToLowerInvariant();
            string rawName = (ch.Name ?? "").ToLowerInvariant();
            string cleanName = (ch.CleanName ?? "").ToLowerInvariant();

            int score = 0;

            // 1. Exact match with clean name gets highest priority
            if (cleanName == q || rawName == q) score += 1000;
            else if (cleanName.StartsWith(q)) score += 500;
            else if (rawName.StartsWith(q)) score += 400;

            // 2. Tokenized search (Faster than Regex for simple word matching)
            var terms = q.Split(new[] { ' ', '[', ']', '(', ')', '-', '_', '.', ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length > 0)
            {
                bool allTermsMatch = true;
                foreach (var t in terms)
                {
                    if (cleanName.Contains(t))
                    {
                        // Word boundary bonus without Regex overhead
                        if (cleanName.StartsWith(t) || cleanName.Contains(" " + t)) score += 100;
                        else score += 30;
                    }
                    else if (rawName.Contains(t))
                    {
                        score += 10;
                    }
                    else
                    {
                        allTermsMatch = false;
                    }
                }
                if (allTermsMatch) score += 200;
            }

            return score;
        }

        public static bool MatchesQueryFilter(Channel ch, string query)
        {
            if (ch == null) return false;
            return MatchesQueryFilter(ch.Name, ch.Category, ch.GroupTitle, ch.Url, query, ch.Language, ch.SourceType);
        }

        public static bool MatchesQueryFilter(string channelName, string category, string groupTitle, string url, string query, string language = "", string sourceType = "")
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            string q = query.Trim().ToLowerInvariant();
            string name = (channelName ?? "").ToLowerInvariant();

            // Simple fast path
            if (name.Contains(q)) return true;

            var terms = q.Split(new[] { ' ', '[', ']', '(', ')', '-', '_', '.', ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0) return true;

            // Check if all terms are present in the name or metadata
            foreach (var t in terms)
            {
                bool match = name.Contains(t) ||
                             (category ?? "").ToLowerInvariant().Contains(t) ||
                             (groupTitle ?? "").ToLowerInvariant().Contains(t) ||
                             (url ?? "").ToLowerInvariant().Contains(t);

                if (!match) return false;
            }

            return true;
        }
    }

    public class ChannelEnricher
    {
        private static Dictionary<string, string>? _globalLogoIndex;
        private static readonly object _lock = new object();
        private readonly DatabaseEngine _db = new DatabaseEngine();

        public static string? GetLogoFromIndex(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return null;

            if (_globalLogoIndex == null || _globalLogoIndex.Count == 0)
            {
                var db = new DatabaseEngine();
                var cached = db.GetAllLogoIndex();
                lock (_lock) { _globalLogoIndex = cached; }
            }

            if (_globalLogoIndex == null || _globalLogoIndex.Count == 0) return null;

            string rawLower = rawName.ToLowerInvariant();
            string cleanName = ChannelUtils.GetCleanName(rawName);

            // Country suffix detection
            string countrySuffix = "";
            if (rawLower.Contains("[de]") || rawLower.Contains("(de)") || rawLower.EndsWith(" de") || rawLower.Contains(" de ")) countrySuffix = "de";
            else if (rawLower.Contains("[tr]") || rawLower.Contains("(tr)") || rawLower.EndsWith(" tr") || rawLower.Contains(" tr ")) countrySuffix = "tr";

            // Standard tv-logos key transformer
            string ToTvLogoKey(string name, string suffix)
            {
                string s = name.ToLowerInvariant();
                s = s.Replace("&", "and").Replace("+", "plus");
                s = Regex.Replace(s, @"\s+", "-");
                s = Regex.Replace(s, @"[^a-z0-9-]", "");
                string key = s.Trim('-');
                if (!string.IsNullOrEmpty(suffix) && !key.EndsWith("-" + suffix)) key += "-" + suffix;
                return key;
            }

            var keysToTry = new List<string>();
            keysToTry.Add(ToTvLogoKey(cleanName, countrySuffix));
            keysToTry.Add(Regex.Replace(cleanName.ToLowerInvariant().Replace(" ", "-"), @"[^a-z0-9-]", "").Trim('-'));

            foreach (var k in keysToTry.Distinct())
            {
                if (string.IsNullOrEmpty(k)) continue;
                if (_globalLogoIndex.TryGetValue(k, out string? logo))
                {
                    LogService.LogInfo($"[LogoMatch] Success: '{rawName}' -> Key: '{k}' -> URL: {logo}");
                    return logo;
                }
            }
            return null;
        }

        public async Task EnrichChannelsAsync(List<Channel> channels)
        {
            if (channels == null || channels.Count == 0) return;
            var updatedChannels = new List<Channel>();

            foreach (var ch in channels)
            {
                if (string.IsNullOrWhiteSpace(ch.LogoUrl))
                {
                    string? indexedLogo = GetLogoFromIndex(ch.Name);
                    if (!string.IsNullOrEmpty(indexedLogo))
                    {
                        ch.LogoUrl = indexedLogo;
                        updatedChannels.Add(ch);
                    }
                }
            }

            if (updatedChannels.Count > 0)
            {
                await _db.SaveChannelsBatchAsync(updatedChannels);
            }
        }
    }
}
