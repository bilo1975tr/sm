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
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', '-', '_', ':', '|', '.', '\'', '"', '`', '’', '‘', '“', '”');
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

        public static List<string> GetEpgSearchCandidateNames(string channelName, string? epgId = null)
        {
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(epgId))
            {
                list.AddRange(epgId.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
            }
            if (string.IsNullOrWhiteSpace(channelName)) return list;

            list.Add(channelName.Trim());

            // 1. Strip country tags like [DE], [TR], (DE), [EN] etc.
            string strippedLang = Regex.Replace(channelName, @"\[(de|tr|en|fr|es|it|ru|uk|us|nl|pl|ar|az)\]|\((de|tr|en|fr|es|it|ru|uk|us|nl|pl|ar|az)\)", "", RegexOptions.IgnoreCase).Trim();
            if (!string.IsNullOrWhiteSpace(strippedLang) && !list.Contains(strippedLang, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(strippedLang);
            }

            // 2. Clean name from GetCleanName
            string clean = GetCleanName(channelName);
            if (!string.IsNullOrWhiteSpace(clean) && !list.Contains(clean, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(clean);
            }

            // 3. Fully stripped of resolution tags (HD, FHD, 4K, UHD)
            string fullyStripped = Regex.Replace(strippedLang, @"\b(fhd|hd|sd|4k|raw|1080p|720p|hevc|h265|h264|uhd)\b", "", RegexOptions.IgnoreCase).Trim();
            fullyStripped = Regex.Replace(fullyStripped, @"\s+", " ").Trim(' ', '-', '_', ':', '|', '.', '\'', '"');
            if (!string.IsNullOrWhiteSpace(fullyStripped) && !list.Contains(fullyStripped, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(fullyStripped);
            }

            // 4. Country code variations for XMLTV (e.g. Syfy.de, Syfy DE, Syfy HD.de)
            var langMatch = Regex.Match(channelName, @"\[(de|tr|en|fr|es|it|ru|uk|us|nl|pl|ar|az)\]|\((de|tr|en|fr|es|it|ru|uk|us|nl|pl|ar|az)\)", RegexOptions.IgnoreCase);
            if (langMatch.Success && !string.IsNullOrWhiteSpace(fullyStripped))
            {
                string lang = (langMatch.Groups[1].Success ? langMatch.Groups[1].Value : langMatch.Groups[2].Value).ToLowerInvariant();
                list.Add($"{fullyStripped}.{lang}");
                list.Add($"{fullyStripped} {lang.ToUpperInvariant()}");
                if (!string.IsNullOrWhiteSpace(strippedLang) && !strippedLang.Equals(fullyStripped, StringComparison.OrdinalIgnoreCase))
                {
                    list.Add($"{strippedLang}.{lang}");
                }
            }

            return list.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
            string primaryName = (ch.PrimaryName ?? "").ToLowerInvariant();

            int score = 0;

            // 1. Direct exact or prefix match
            if (rawName == q || cleanName == q || primaryName == q) score += 1000;
            else if (rawName.StartsWith(q) || cleanName.StartsWith(q) || primaryName.StartsWith(q)) score += 500;
            else if (rawName.Contains(q) || cleanName.Contains(q) || primaryName.Contains(q)) score += 300;

            // 2. Tokenized search
            var terms = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length > 1)
            {
                bool allTermsMatch = true;
                foreach (var t in terms)
                {
                    if (rawName.Contains(t) || cleanName.Contains(t) || primaryName.Contains(t))
                    {
                        score += 50;
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
            if (string.IsNullOrWhiteSpace(query)) return true;
            return MatchesQueryFilter(ch.Name, ch.Category, ch.GroupTitle, ch.Url, query, ch.Language, ch.SourceType);
        }

        public static bool MatchesQueryFilter(string? channelName, string? category, string? groupTitle, string? url, string? query, string language = "", string sourceType = "")
        {
            if (string.IsNullOrWhiteSpace(query)) return true;

            string q = query.Trim().ToLowerInvariant();
            string name = (channelName ?? "").ToLowerInvariant();
            string cat = (category ?? "").ToLowerInvariant();
            string group = (groupTitle ?? "").ToLowerInvariant();
            string link = (url ?? "").ToLowerInvariant();
            string lang = (language ?? "").ToLowerInvariant();

            // Split into whitespace tokens
            var terms = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0) return true;

            foreach (var term in terms)
            {
                if (!MatchesSingleTerm(name, cat, group, link, lang, term))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MatchesSingleTerm(string name, string cat, string group, string link, string lang, string term)
        {
            if (string.IsNullOrWhiteSpace(term)) return true;

            // 1. Köşeli veya parantezli etiket aramaları: [de], (de), [tr], [4k], vb.
            // Kullanıcı köşeli parantez içinde arama yaptıysa (örn. [DE]), sadece ve sadece bu 4 karakteri ([DE]) içerenler kabul edilir!
            // "M. Liga de Campeones" gibi İspanyolca/Fransızca bağlaç/kelime içeren kanallar kesinlikle elenir.
            if (term.StartsWith("[") && term.EndsWith("]"))
            {
                string innerTag = term.Trim('[', ']').Trim();
                if (string.IsNullOrWhiteSpace(innerTag)) return false;

                // Tam köşeli etiket kontrolü: [de], [ de ], [de ]
                if (name.Contains(term) || name.Contains($"[{innerTag}]") || name.Contains($"[ {innerTag} ]") ||
                    group.Contains(term) || group.Contains($"[{innerTag}]") ||
                    cat.Contains(term) || cat.Contains($"[{innerTag}]"))
                {
                    return true;
                }

                return false;
            }

            if (term.StartsWith("(") && term.EndsWith(")"))
            {
                string innerTag = term.Trim('(', ')').Trim();
                if (string.IsNullOrWhiteSpace(innerTag)) return false;

                if (name.Contains(term) || name.Contains($"({innerTag})") || name.Contains($"( {innerTag} )") ||
                    group.Contains(term) || group.Contains($"({innerTag})") ||
                    cat.Contains(term) || cat.Contains($"({innerTag})"))
                {
                    return true;
                }

                return false;
            }

            // Çift tırnaklı tam ifade araması: "de"
            if (term.StartsWith("\"") && term.EndsWith("\"") && term.Length > 2)
            {
                string exact = term.Substring(1, term.Length - 2).Trim();
                return name.Contains(exact) || group.Contains(exact) || cat.Contains(exact);
            }

            // 2. Kısa kodlar (<= 3 karakter, örn. "de", "tr", "fr", "es", "it", "4k", "hd")
            if (term.Length <= 3)
            {
                // Etiketli kullanım varsa kabul et: [de], (de), |de|, "de: ", "de - "
                if (name.Contains($"[{term}]") || name.Contains($"({term})") || 
                    name.Contains($"|{term}|") || name.Contains($"| {term} |") ||
                    name.StartsWith($"{term}:") || name.StartsWith($"{term} -") ||
                    group.Contains($"[{term}]") || group.Contains($"({term})") ||
                    cat.Contains($"[{term}]") || cat.Contains($"({term})"))
                {
                    return true;
                }

                // "de" aramalarında İspanyolca/Fransızca "de" bağlacı geçen yabancı kanalları kesinlikle ele
                if (term == "de")
                {
                    if (name.Contains("liga") || name.Contains("campeones") || name.Contains("deportes") || 
                        name.Contains("copa") || name.Contains("tour") || name.Contains("france") || 
                        name.Contains("espana") || name.Contains("movistar"))
                    {
                        return false;
                    }

                    if (group.Contains("deutsch") || group.Contains("germany") || 
                        cat.Contains("deutsch") || cat.Contains("germany") || 
                        lang.Contains("de") || lang.Contains("ger"))
                    {
                        return true;
                    }
                }
                else if (term == "tr")
                {
                    if (group.Contains("turk") || cat.Contains("turk") || lang.Contains("tr") || lang.Contains("tur"))
                        return true;
                }
                else if (term == "fr")
                {
                    if (group.Contains("france") || cat.Contains("france") || lang.Contains("fr") || lang.Contains("fra"))
                        return true;
                }

                // 4k, hd, ufc, wwe, f1 gibi kısaltmalar için kelime sınırı kontrolü (bağlaç olan de, fr, es hariç)
                if (term != "de" && term != "fr" && term != "es" && term != "it" && term != "en" && term != "da")
                {
                    if (Regex.IsMatch(name, $@"\b{Regex.Escape(term)}\b", RegexOptions.IgnoreCase) ||
                        Regex.IsMatch(group, $@"\b{Regex.Escape(term)}\b", RegexOptions.IgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }

            // 3. Normal arama terimleri (> 3 karakter): alt dize eşleşmesi
            if (name.Contains(term) || cat.Contains(term) || group.Contains(term) || link.Contains(term))
            {
                return true;
            }

            return false;
        }
    }

    public class ChannelEnricher
    {
        private static Dictionary<string, string>? _globalLogoIndex;
        private static readonly object _lock = new object();
        private readonly DatabaseEngine _db = new DatabaseEngine();

        public static void InvalidateLogoCache()
        {
            lock (_lock)
            {
                _globalLogoIndex = null;
            }
        }

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
                await _db.SaveChannelsBatchAsync(updatedChannels, clearFirst: false, notifyUpdated: false);
            }
        }
    }
}
