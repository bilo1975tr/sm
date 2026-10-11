using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using StreamMesh.Core.Database;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class LogoSearchResult
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string Source { get; set; } = "";
    }

    public static class LogoSearchEngine
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        private static async Task<bool> IsUrlAccessibleAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            // Local files or data URLs are assumed valid
            if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return true;

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, url);
                req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) StreamMesh/1.0");
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(2000));
                using var resp = await _http.SendAsync(req, cts.Token);
                if (resp.IsSuccessStatusCode) return true;

                // Fallback GET (headers only) if HEAD is forbidden or unsupported
                using var reqGet = new HttpRequestMessage(HttpMethod.Get, url);
                reqGet.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) StreamMesh/1.0");
                using var cts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(2500));
                using var respGet = await _http.SendAsync(reqGet, HttpCompletionOption.ResponseHeadersRead, cts2.Token);
                return respGet.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<List<LogoSearchResult>> SearchLogosAsync(string query, string sourceFilter = "ALL")
        {
            var results = new List<LogoSearchResult>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            string rawQuery = query.Trim();
            string clean = ChannelUtils.GetCleanName(rawQuery);
            string normKey = ChannelUtils.ToNormalizedKey(clean);

            // 1. Check local indexed database (ChannelEnricher & LogoIndex table)
            string? indexedLogo = ChannelEnricher.GetLogoFromIndex(rawQuery);
            if (!string.IsNullOrEmpty(indexedLogo))
            {
                results.Add(new LogoSearchResult
                {
                    Name = $"{clean} (İndeks)",
                    Url = indexedLogo,
                    Source = "İndekslenmiş Logo Kütüphanesi"
                });
            }

            // Search all matching entries in LogoIndex table
            try
            {
                var db = new DatabaseEngine();
                var logoDict = db.GetAllLogoIndex();
                foreach (var kvp in logoDict)
                {
                    string k = kvp.Key.ToLowerInvariant();
                    if (k.Contains(normKey) || (!string.IsNullOrEmpty(clean) && k.Contains(clean.ToLowerInvariant())))
                    {
                        if (!results.Any(r => r.Url.Equals(kvp.Value, StringComparison.OrdinalIgnoreCase)))
                        {
                            results.Add(new LogoSearchResult
                            {
                                Name = $"{kvp.Key} (İndeks Veritabanı)",
                                Url = kvp.Value,
                                Source = "İndekslenmiş Logo"
                            });
                        }
                    }
                }
            }
            catch { }

            // 2. Search local Channels database for any channel matching query with a valid LogoUrl
            try
            {
                var db = new DatabaseEngine();
                var allChannels = await db.GetAllChannelsAsync();
                var matchedInDb = allChannels.Where(c => !string.IsNullOrWhiteSpace(c.LogoUrl) && 
                    (c.Name.Contains(clean, StringComparison.OrdinalIgnoreCase) || 
                     c.Name.Contains(rawQuery, StringComparison.OrdinalIgnoreCase) ||
                     ChannelUtils.ToNormalizedKey(c.Name).Contains(normKey, StringComparison.OrdinalIgnoreCase))).ToList();

                foreach (var ch in matchedInDb.Take(5))
                {
                    if (!results.Any(r => r.Url.Equals(ch.LogoUrl, StringComparison.OrdinalIgnoreCase)))
                    {
                        results.Add(new LogoSearchResult
                        {
                            Name = $"{ch.Name} (Veritabanı)",
                            Url = ch.LogoUrl!,
                            Source = "Mevcut Kanal Logosu"
                        });
                    }
                }
            }
            catch { }

            // Candidate remote URLs to check
            var candidateResults = new List<LogoSearchResult>();

            // 3. TV-Logo & Personal GitHub Candidates (tv-logo/tv-logos and bilo1975tr/tv-logos)
            if (sourceFilter == "ALL" || sourceFilter == "TV_LOGOS")
            {
                string[] repos = { "bilo1975tr/tv-logos/main", "bilo1975tr/tv-logos/master", "tv-logo/tv-logos/main" };
                string[] countries = { "turkey", "germany", "united-kingdom", "united-states", "france", "spain" };
                
                foreach (var repoPath in repos)
                {
                    foreach (var country in countries)
                    {
                        string suffix = country == "germany" ? "de" : country == "turkey" ? "tr" : country == "france" ? "fr" : country == "spain" ? "es" : "";
                        
                        var slugs = new List<string>();
                        string dashName = clean.ToLowerInvariant().Replace(" ", "-").Replace("&", "and").Replace("+", "plus");
                        dashName = Regex.Replace(dashName, @"[^a-z0-9-]", "").Trim('-');
                        slugs.Add(dashName);

                        string noDashName = Regex.Replace(clean.ToLowerInvariant(), @"[^a-z0-9]", "");
                        if (!slugs.Contains(noDashName)) slugs.Add(noDashName);

                        // Special alias mappings (e.g., NOW TV -> now, now-tv, fox-tr, etc.)
                        if (clean.Equals("NOW TV", StringComparison.OrdinalIgnoreCase) || clean.Equals("NOW", StringComparison.OrdinalIgnoreCase))
                        {
                            slugs.Add("now");
                            slugs.Add("now-tv");
                            slugs.Add("now-tr");
                            slugs.Add("now-tv-tr");
                            slugs.Add("fox-tr");
                            slugs.Add("fox-tv-tr");
                        }

                        foreach (var baseSlug in slugs)
                        {
                            if (string.IsNullOrWhiteSpace(baseSlug)) continue;
                            string s = baseSlug;
                            if (!string.IsNullOrEmpty(suffix) && !s.EndsWith("-" + suffix) && !s.Equals("now-tr") && !s.Equals("now-tv-tr"))
                                s += "-" + suffix;

                            string logoUrl = $"https://raw.githubusercontent.com/{repoPath}/countries/{country}/{s}.png";
                            if (!results.Any(r => r.Url.Equals(logoUrl, StringComparison.OrdinalIgnoreCase)) &&
                                !candidateResults.Any(r => r.Url.Equals(logoUrl, StringComparison.OrdinalIgnoreCase)))
                            {
                                candidateResults.Add(new LogoSearchResult
                                {
                                    Name = $"{clean} ({country.ToUpper()})",
                                    Url = logoUrl,
                                    Source = repoPath.StartsWith("bilo1975tr") ? $"Kendi GitHub Depon ({country})" : $"GitHub tv-logos ({country})"
                                });
                            }
                        }
                    }
                }
            }

            // 4. IPTV.org Logo CDN Candidates
            if (sourceFilter == "ALL" || sourceFilter == "IPTV_ORG")
            {
                string iptvSlug = clean.ToLowerInvariant().Replace(" ", "").Replace("&", "and").Replace("+", "plus");
                iptvSlug = Regex.Replace(iptvSlug, @"[^a-z0-9]", "");
                if (!string.IsNullOrEmpty(iptvSlug))
                {
                    string iptvUrl = $"https://iptv-org.github.io/iptv/logos/{iptvSlug}.png";
                    if (!results.Any(r => r.Url.Equals(iptvUrl, StringComparison.OrdinalIgnoreCase)) &&
                        !candidateResults.Any(r => r.Url.Equals(iptvUrl, StringComparison.OrdinalIgnoreCase)))
                    {
                        candidateResults.Add(new LogoSearchResult
                        {
                            Name = $"{clean} (IPTV.org)",
                            Url = iptvUrl,
                            Source = "IPTV.org CDN"
                        });
                    }
                }
            }

            // 5. Clearbit Logo candidate
            if (sourceFilter == "ALL" || sourceFilter == "CLEARBIT")
            {
                string cleanDomain = clean.ToLowerInvariant().Replace(" ", "").Replace("&", "");
                cleanDomain = Regex.Replace(cleanDomain, @"[^a-z0-9]", "");
                if (!string.IsNullOrEmpty(cleanDomain))
                {
                    string clearbitUrl = $"https://logo.clearbit.com/{cleanDomain}.com";
                    if (!results.Any(r => r.Url.Equals(clearbitUrl, StringComparison.OrdinalIgnoreCase)) &&
                        !candidateResults.Any(r => r.Url.Equals(clearbitUrl, StringComparison.OrdinalIgnoreCase)))
                    {
                        candidateResults.Add(new LogoSearchResult
                        {
                            Name = $"{cleanDomain}.com (Clearbit)",
                            Url = clearbitUrl,
                            Source = "Clearbit Logo API"
                        });
                    }
                }
            }

            // Validate candidates in parallel to eliminate broken 404 links
            if (candidateResults.Count > 0)
            {
                var validCandidates = new ConcurrentBag<LogoSearchResult>();
                var tasks = candidateResults.Select(async candidate =>
                {
                    if (await IsUrlAccessibleAsync(candidate.Url))
                    {
                        validCandidates.Add(candidate);
                    }
                });

                await Task.WhenAll(tasks);
                results.AddRange(validCandidates);
            }

            return results;
        }
    }
}

