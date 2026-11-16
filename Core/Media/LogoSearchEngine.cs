using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using StreamMesh.Core.Database;
using StreamMesh.Core.Network;
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
                using var resp = await MediaHttpClient.Client.SendAsync(req, cts.Token);
                if (resp.IsSuccessStatusCode) return true;

                // Fallback GET (headers only) if HEAD is forbidden or unsupported
                using var reqGet = new HttpRequestMessage(HttpMethod.Get, url);
                reqGet.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) StreamMesh/1.0");
                using var cts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(2500));
                using var respGet = await MediaHttpClient.Client.SendAsync(reqGet, HttpCompletionOption.ResponseHeadersRead, cts2.Token);
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
            string clean = ChannelUtils.GetDisplayBaseName(rawQuery);
            string countryCode = ChannelUtils.DetectCountryCode(rawQuery);
            string normKey = Regex.Replace(clean.ToLowerInvariant(), @"[^a-z0-9]", "");

            // 0. Search local /logos directory and subdirectories directly
            try
            {
                var baseDirs = new[]
                {
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logos"),
                    System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "logos")
                };

                var validExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".png", ".jpg", ".jpeg", ".ico", ".webp"
                };

                foreach (var dir in baseDirs.Distinct())
                {
                    if (!System.IO.Directory.Exists(dir)) continue;
                    var files = System.IO.Directory.GetFiles(dir, "*.*", System.IO.SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        string ext = System.IO.Path.GetExtension(file);
                        if (!validExtensions.Contains(ext)) continue;

                        string fn = System.IO.Path.GetFileNameWithoutExtension(file);
                        if (fn.Contains(clean, StringComparison.OrdinalIgnoreCase) || 
                            fn.Contains(rawQuery, StringComparison.OrdinalIgnoreCase) ||
                            Regex.Replace(fn.ToLowerInvariant(), @"[^a-z0-9]", "").Contains(normKey, StringComparison.OrdinalIgnoreCase))
                        {
                            string rel = "logos/" + System.IO.Path.GetRelativePath(dir, file).Replace("\\", "/");
                            if (!results.Any(r => r.Url.Equals(rel, StringComparison.OrdinalIgnoreCase)))
                            {
                                results.Add(new LogoSearchResult
                                {
                                    Name = $"{fn} (Yerel Dosya)",
                                    Url = rel,
                                    Source = "Yerel Logo Klasörü"
                                });
                            }
                        }
                    }
                }
            }
            catch { }

            // 1. Check local indexed database (iptv-org + tv-logos + local)
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
                var db = DatabaseEngine.Instance;
                var logoDict = db.GetAllLogoIndex();
                foreach (var kvp in logoDict)
                {
                    string k = kvp.Key.ToLowerInvariant();
                    if ((!string.IsNullOrEmpty(normKey) && k.Contains(normKey)) || (!string.IsNullOrEmpty(clean) && k.Contains(clean.ToLowerInvariant().Replace(" ", "-"))))
                    {
                        if (!results.Any(r => r.Url.Equals(kvp.Value, StringComparison.OrdinalIgnoreCase)))
                        {
                            results.Add(new LogoSearchResult
                            {
                                Name = $"{kvp.Key} (İndeks Veritabanı)",
                                Url = kvp.Value,
                                Source = "İndekslenmiş Logo"
                            });
                            if (results.Count >= 12) break;
                        }
                    }
                }
            }
            catch { }

            // 1b. Check EPG channel <icon> entries
            try
            {
                var epgLogos = await DatabaseEngine.Instance.GetEpgLogoLookupAsync().ConfigureAwait(false);
                foreach (var (epgId, name, logoUrl) in epgLogos)
                {
                    if (logoUrl.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;
                    string epgNorm = Regex.Replace(ChannelUtils.GetDisplayBaseName(name).ToLowerInvariant(), @"[^a-z0-9]", "");
                    if ((!string.IsNullOrEmpty(normKey) && epgNorm == normKey) ||
                        epgId.Contains(clean, StringComparison.OrdinalIgnoreCase) ||
                        name.Contains(clean, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!results.Any(r => r.Url.Equals(logoUrl, StringComparison.OrdinalIgnoreCase)))
                        {
                            results.Add(new LogoSearchResult
                            {
                                Name = $"{name} (EPG)",
                                Url = logoUrl,
                                Source = "EPG Rehberi İkonu"
                            });
                        }
                    }
                }
            }
            catch { }

            // 2. Search local TV/Radio Channels database for matching channel with valid TV LogoUrl
            try
            {
                var db = DatabaseEngine.Instance;
                var allChannels = await db.GetAllChannelsAsync().ConfigureAwait(false);
                var matchedInDb = allChannels.Where(c =>
                    !string.IsNullOrWhiteSpace(c.LogoUrl) &&
                    MetadataEngine.IsLikelyLiveTv(c) &&
                    (c.Name.Contains(clean, StringComparison.OrdinalIgnoreCase) || 
                     c.Name.Contains(rawQuery, StringComparison.OrdinalIgnoreCase) ||
                     ChannelUtils.ToNormalizedKey(c.Name).Contains(normKey, StringComparison.OrdinalIgnoreCase))).ToList();

                foreach (var ch in matchedInDb.Take(5))
                {
                    if (!results.Any(r => r.Url.Equals(ch.PrimaryLogoUrl, StringComparison.OrdinalIgnoreCase)))
                    {
                        results.Add(new LogoSearchResult
                        {
                            Name = $"{ch.Name} (Veritabanı)",
                            Url = ch.PrimaryLogoUrl,
                            Source = "Mevcut Kanal Logosu"
                        });
                    }
                }
            }
            catch { }

            // Candidate remote URLs to check
            var candidateResults = new List<LogoSearchResult>();

            // 3. TV-Logo & Personal GitHub Candidates
            if (sourceFilter == "ALL" || sourceFilter == "TV_LOGOS")
            {
                string[] repos = { "bilo1975tr/tv-logos/main", "bilo1975tr/tv-logos/master", "tv-logo/tv-logos/main" };
                var countryList = new List<string>();
                if (countryCode == "de") countryList.Add("germany");
                else if (countryCode == "tr") countryList.Add("turkey");
                else if (countryCode == "fr") countryList.Add("france");
                else if (countryCode == "es") countryList.Add("spain");
                else if (countryCode == "it") countryList.Add("italy");
                else if (countryCode == "gb") countryList.Add("united-kingdom");
                else if (countryCode == "us") countryList.Add("united-states");
                else countryList.AddRange(new[] { "turkey", "germany", "united-kingdom", "united-states", "france", "spain" });

                foreach (var repoPath in repos)
                {
                    foreach (var country in countryList)
                    {
                        string suffix = country == "germany" ? "de" : country == "turkey" ? "tr" : country == "france" ? "fr" : country == "spain" ? "es" : country == "italy" ? "it" : country == "united-kingdom" ? "uk" : "";
                        
                        var slugs = new List<string>();
                        string dashName = clean.ToLowerInvariant().Replace(" ", "-").Replace("&", "and").Replace("+", "plus").Replace(".", "-");
                        dashName = Regex.Replace(dashName, @"[^a-z0-9-]", "").Trim('-');
                        if (!string.IsNullOrEmpty(dashName)) slugs.Add(dashName);

                        string noDashName = Regex.Replace(clean.ToLowerInvariant(), @"[^a-z0-9]", "");
                        if (!string.IsNullOrEmpty(noDashName) && !slugs.Contains(noDashName)) slugs.Add(noDashName);

                        if (clean.Equals("NOW TV", StringComparison.OrdinalIgnoreCase) || clean.Equals("NOW", StringComparison.OrdinalIgnoreCase))
                        {
                            slugs.AddRange(new[] { "now", "now-tv", "now-tr", "now-tv-tr", "fox-tr", "fox-tv-tr" });
                        }

                        foreach (var baseSlug in slugs)
                        {
                            if (string.IsNullOrWhiteSpace(baseSlug)) continue;
                            string s = baseSlug;
                            if (!string.IsNullOrEmpty(suffix) && !s.EndsWith("-" + suffix))
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

            // Validate candidates in parallel to eliminate broken 404 links
            if (candidateResults.Count > 0)
            {
                var validCandidates = new ConcurrentBag<LogoSearchResult>();
                var tasks = candidateResults.Select(async candidate =>
                {
                    if (await IsUrlAccessibleAsync(candidate.Url).ConfigureAwait(false))
                    {
                        validCandidates.Add(candidate);
                    }
                });

                await Task.WhenAll(tasks).ConfigureAwait(false);
                results.AddRange(validCandidates);
            }

            return results;
        }
    }
}

