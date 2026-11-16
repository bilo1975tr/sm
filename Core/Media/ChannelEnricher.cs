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
        private static readonly Regex SignificantTagRegex = new(@"\d{4}|\[(de|tr|en|fr|es|it|ru|uk|us)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NonLangBracketRegex = new(@"\[(?!(de|tr|en|fr|es|it|ru|uk|us)\]).*?\]|\((?!(de|tr|en|fr|es|it|ru|uk|us)\)).*?\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AnyBracketRegex = new(@"\[.*?\]|\(.*?\)", RegexOptions.Compiled);
        private static readonly Regex ResolutionTagRegex = new(@"\b(fhd|hd|sd|4k|raw|1080p|720p|hevc|h265|h264)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ResolutionUhdRegex = new(@"\b(fhd|hd|sd|4k|raw|1080p|720p|hevc|h265|h264|uhd)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MultiSpaceRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex GermanYearTagRegex = new(@"\d{4}\s*\[de\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NonAlphaNumRegex = new(@"[^a-z0-9]", RegexOptions.Compiled);
        private static readonly Regex LangTagRegex = new(@"\[(de|tr|en|fr|es|it|ru|uk|us|nl|pl|ar|az)\]|\((de|tr|en|fr|es|it|ru|uk|us|nl|pl|ar|az)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex OtherLangTagRegex = new(@"\[(de|en|fr|es|it|ru|uk|us)\]|\((de|en|fr|es|it|ru|uk|us)\)", RegexOptions.Compiled);

        public static string GetDisplayBaseName(string? rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return "";
            string cleaned = AnyBracketRegex.Replace(rawName, " ").Trim();
            cleaned = ResolutionUhdRegex.Replace(cleaned, " ").Trim();
            cleaned = Regex.Replace(cleaned, @"(?i)\b(uhd|fhd|hd|sd|raw|hevc|h\.?264|h\.?265)\b", " ");
            cleaned = MultiSpaceRegex.Replace(cleaned, " ").Trim(' ', '-', '_', ':', '|', '.', '\'', '"', '`', '’', '‘', '“', '”');
            return string.IsNullOrWhiteSpace(cleaned) ? rawName.Trim() : cleaned;
        }

        public static string DetectCountryCode(string? rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return "";
            string s = rawName.ToLowerInvariant();
            if (s.Contains("[de]") || s.Contains("(de)") || s.StartsWith("de•") || s.StartsWith("de:") || s.StartsWith("de -") || s.EndsWith(" de")) return "de";
            if (s.Contains("[tr]") || s.Contains("(tr)") || s.StartsWith("tr•") || s.StartsWith("tr:") || s.StartsWith("tr -") || s.EndsWith(" tr")) return "tr";
            if (s.Contains("[uk]") || s.Contains("(uk)") || s.Contains("[gb]") || s.Contains("(gb)") || s.StartsWith("uk•") || s.StartsWith("uk:")) return "gb";
            if (s.Contains("[us]") || s.Contains("(us)") || s.StartsWith("us•") || s.StartsWith("us:")) return "us";
            if (s.Contains("[fr]") || s.Contains("(fr)") || s.StartsWith("fr•") || s.StartsWith("fr:") || s.EndsWith(" fr")) return "fr";
            if (s.Contains("[es]") || s.Contains("(es)") || s.StartsWith("es•") || s.StartsWith("es:") || s.EndsWith(" es")) return "es";
            if (s.Contains("[it]") || s.Contains("(it)") || s.StartsWith("it•") || s.StartsWith("it:") || s.EndsWith(" it")) return "it";
            if (s.Contains("[nl]") || s.Contains("(nl)") || s.StartsWith("nl•") || s.StartsWith("nl:") || s.EndsWith(" nl")) return "nl";
            if (s.Contains("[ru]") || s.Contains("(ru)") || s.StartsWith("ru•") || s.StartsWith("ru:") || s.EndsWith(" ru")) return "ru";
            if (s.Contains("[az]") || s.Contains("(az)") || s.StartsWith("az•") || s.StartsWith("az:") || s.EndsWith(" az")) return "az";
            if (s.Contains("[ar]") || s.Contains("(ar)") || s.StartsWith("ar•") || s.StartsWith("ar:")) return "ar";
            return "";
        }

        public static string GetCleanName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return "";

            string cleaned = SignificantTagRegex.IsMatch(rawName)
                ? NonLangBracketRegex.Replace(rawName, "").Trim()
                : AnyBracketRegex.Replace(rawName, "").Trim();

            cleaned = ResolutionTagRegex.Replace(cleaned, "").Trim();
            cleaned = MultiSpaceRegex.Replace(cleaned, " ").Trim(' ', '-', '_', ':', '|', '.', '\'', '"', '`', '’', '‘', '“', '”');
            return string.IsNullOrWhiteSpace(cleaned) ? rawName : cleaned;
        }

        public static string ToNormalizedKey(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            if (GermanYearTagRegex.IsMatch(name))
            {
                return name.ToLowerInvariant().Replace(" ", "");
            }
            string clean = GetCleanName(name);
            return NonAlphaNumRegex.Replace(clean.ToLowerInvariant(), "");
        }

        public static string NormalizeForMatch(string? name)
        {
            return ToNormalizedKey(name ?? "");
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

            string strippedLang = LangTagRegex.Replace(channelName, "").Trim();
            if (!string.IsNullOrWhiteSpace(strippedLang) && !list.Contains(strippedLang, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(strippedLang);
            }

            string clean = GetCleanName(channelName);
            if (!string.IsNullOrWhiteSpace(clean) && !list.Contains(clean, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(clean);
            }

            string fullyStripped = ResolutionUhdRegex.Replace(strippedLang, "").Trim();
            fullyStripped = MultiSpaceRegex.Replace(fullyStripped, " ").Trim(' ', '-', '_', ':', '|', '.', '\'', '"');
            if (!string.IsNullOrWhiteSpace(fullyStripped) && !list.Contains(fullyStripped, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(fullyStripped);
            }

            var langMatch = LangTagRegex.Match(channelName);
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
                return GermanYearTagRegex.IsMatch(nameLower) || nameLower.Contains("[de]") || nameLower.Contains("(de)") || nameLower.Contains("deutsch") || nameLower.Contains("german");
            }
            if (languageFilter.Contains("Türkçe") || languageFilter.Contains("TR") || languageFilter.Equals("tr", StringComparison.OrdinalIgnoreCase))
            {
                bool hasOtherLangTag = OtherLangTagRegex.IsMatch(nameLower);
                bool isTurkish = nameLower.Contains("[tr]") || nameLower.Contains("(tr)") || nameLower.Contains("türk") || nameLower.Contains("turk") || nameLower.Contains("tr ") || nameLower.EndsWith(" tr");
                return isTurkish || !hasOtherLangTag;
            }
            if (languageFilter.Contains("İngilizce") || languageFilter.Contains("EN") || languageFilter.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                return nameLower.Contains("[en]") || nameLower.Contains("(en)") || nameLower.Contains("english") || nameLower.Contains("ingiltere") || nameLower.Contains("usa") || nameLower.Contains("uk");
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
            if (term.StartsWith("[") && term.EndsWith("]"))
            {
                string innerTag = term.Trim('[', ']').Trim();
                if (string.IsNullOrWhiteSpace(innerTag)) return false;

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
                if (name.Contains($"[{term}]") || name.Contains($"({term})") || 
                    name.Contains($"|{term}|") || name.Contains($"| {term} |") ||
                    name.StartsWith($"{term}:") || name.StartsWith($"{term} -") ||
                    group.Contains($"[{term}]") || group.Contains($"({term})") ||
                    cat.Contains($"[{term}]") || cat.Contains($"({term})"))
                {
                    return true;
                }

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
        private readonly DatabaseEngine _db = DatabaseEngine.Instance;

        public static void InvalidateLogoCache()
        {
            lock (_lock)
            {
                _globalLogoIndex = null;
            }
        }

        private static string ToSlug(string text)
        {
            string s = text.ToLowerInvariant().Replace("&", "and").Replace("+", "plus").Replace(".", "-");
            s = Regex.Replace(s, @"\s+", "-");
            s = Regex.Replace(s, @"[^a-z0-9-]", "");
            s = Regex.Replace(s, @"-{2,}", "-");
            return s.Trim('-');
        }

        public static string? GetLogoFromIndex(string rawName, string? languageHint = null)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return null;

            if (_globalLogoIndex == null || _globalLogoIndex.Count == 0)
            {
                var db = DatabaseEngine.Instance;
                var cached = db.GetAllLogoIndex();
                lock (_lock) { _globalLogoIndex = cached; }
            }

            if (_globalLogoIndex == null || _globalLogoIndex.Count == 0) return null;

            string baseName = ChannelUtils.GetDisplayBaseName(rawName);
            string countrySuffix = ChannelUtils.DetectCountryCode(rawName);
            if (string.IsNullOrEmpty(countrySuffix) && !string.IsNullOrWhiteSpace(languageHint))
            {
                string lh = languageHint.Trim().ToLowerInvariant();
                if (lh is "de" or "tr" or "fr" or "es" or "it" or "nl" or "ru" or "az" or "ar")
                    countrySuffix = lh;
            }

            string dashSlug = ToSlug(baseName);
            string compactSlug = Regex.Replace(baseName.ToLowerInvariant().Replace("&", "and").Replace("+", "plus"), @"[^a-z0-9]", "");
            string compactNoPlus = Regex.Replace(baseName.ToLowerInvariant().Replace("+", ""), @"[^a-z0-9]", "");

            var keysToTry = new List<string>();
            if (!string.IsNullOrEmpty(countrySuffix))
            {
                if (!string.IsNullOrEmpty(dashSlug)) keysToTry.Add($"{dashSlug}-{countrySuffix}");
                if (!string.IsNullOrEmpty(compactSlug)) keysToTry.Add($"{compactSlug}-{countrySuffix}");
                if (!string.IsNullOrEmpty(compactNoPlus)) keysToTry.Add($"{compactNoPlus}-{countrySuffix}");
                if (countrySuffix == "gb")
                {
                    if (!string.IsNullOrEmpty(dashSlug)) keysToTry.Add($"{dashSlug}-uk");
                    if (!string.IsNullOrEmpty(compactSlug)) keysToTry.Add($"{compactSlug}-uk");
                }
            }

            if (!string.IsNullOrEmpty(dashSlug)) keysToTry.Add(dashSlug);
            if (!string.IsNullOrEmpty(compactSlug)) keysToTry.Add(compactSlug);
            if (!string.IsNullOrEmpty(compactNoPlus)) keysToTry.Add(compactNoPlus);

            // Exact normalized key fallback without country tag pollution
            string cleanKey = Regex.Replace(baseName.ToLowerInvariant(), @"[^a-z0-9]", "");
            if (!string.IsNullOrEmpty(cleanKey)) keysToTry.Add(cleanKey);

            foreach (var k in keysToTry.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(k)) continue;
                if (_globalLogoIndex.TryGetValue(k, out string? logo) && !string.IsNullOrWhiteSpace(logo))
                {
                    return logo;
                }
            }
            return null;
        }

        public async Task EnrichChannelsAsync(List<Channel> channels)
        {
            if (channels == null || channels.Count == 0) return;
            var updatedChannels = new List<Channel>();

            // Build fast in-memory EPG icon lookup (prefer PNG/JPG/WebP over SVG for WPF compatibility)
            var epgIconById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var epgIconByNormName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var epgLogos = await _db.GetEpgLogoLookupAsync().ConfigureAwait(false);
                foreach (var (epgId, name, logoUrl) in epgLogos)
                {
                    if (logoUrl.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrWhiteSpace(epgId) && !epgIconById.ContainsKey(epgId))
                        epgIconById[epgId] = logoUrl;

                    foreach (var n in name.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string norm = Regex.Replace(ChannelUtils.GetDisplayBaseName(n).ToLowerInvariant(), @"[^a-z0-9]", "");
                        if (!string.IsNullOrEmpty(norm) && !epgIconByNormName.ContainsKey(norm))
                            epgIconByNormName[norm] = logoUrl;
                    }
                }
            }
            catch { }

            foreach (var ch in channels)
            {
                if (!string.IsNullOrWhiteSpace(ch.LogoUrl)) continue;
                if (!MetadataEngine.IsLikelyLiveTv(ch)) continue;

                string? foundLogo = null;

                // 1. EPG Channel <icon> by EpgId
                foreach (var epgId in ch.GetEpgIdList())
                {
                    if (epgIconById.TryGetValue(epgId, out var epgLogo))
                    {
                        foundLogo = epgLogo;
                        break;
                    }
                }

                // 2. EPG Channel <icon> by normalized channel name
                if (string.IsNullOrEmpty(foundLogo))
                {
                    string chNorm = Regex.Replace(ChannelUtils.GetDisplayBaseName(ch.PrimaryName ?? ch.Name).ToLowerInvariant(), @"[^a-z0-9]", "");
                    if (!string.IsNullOrEmpty(chNorm) && epgIconByNormName.TryGetValue(chNorm, out var epgNameLogo))
                    {
                        foundLogo = epgNameLogo;
                    }
                }

                // 3. iptv-org + tv-logos + local LogoIndex
                if (string.IsNullOrEmpty(foundLogo))
                {
                    foundLogo = GetLogoFromIndex(ch.PrimaryName ?? ch.Name, ch.Language);
                }

                if (!string.IsNullOrEmpty(foundLogo))
                {
                    ch.LogoUrl = foundLogo;
                    updatedChannels.Add(ch);
                }
            }

            if (updatedChannels.Count > 0)
            {
                await _db.SaveChannelsBatchAsync(updatedChannels, clearFirst: false, notifyUpdated: false).ConfigureAwait(false);
            }
        }
    }
}
