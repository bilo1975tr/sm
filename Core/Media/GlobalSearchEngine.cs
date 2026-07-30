using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StreamMesh.Models;
using StreamMesh.Core.Database;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class SearchResultItem
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string Source { get; set; } = "";
        public string Category { get; set; } = "Genel";
        public string PeersOrDetails { get; set; } = "";
        public string LogoUrl { get; set; } = "";
        public string GroupTitle { get; set; } = "";
    }

    public class GlobalSearchEngine
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private readonly DatabaseEngine _db = new DatabaseEngine();
        private readonly AceEngine _ace = new AceEngine();
        private readonly M3uEngine _m3uEngine = new M3uEngine();

        static GlobalSearchEngine()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        public async Task StartAceEngineAsync()
        {
            await _ace.StartEngineAsync();
        }

        public async Task<List<SearchResultItem>> SearchGlobalAsync(string query, string sourceFilter = "Tüm Kaynaklar", string categoryFilter = "Tüm Kategoriler", string languageFilter = "Tüm Diller")
        {
            string cleanQuery = (query ?? "").Trim();
            string searchKeyword = cleanQuery.ToLowerInvariant();

            // V1.8.7: Eğer sorgu köşeli parantez içeriyorsa (örn: [de]), sorguyu olduğu gibi koru.
            bool hasBrackets = cleanQuery.Contains("[") || cleanQuery.Contains("]");

            if (string.IsNullOrWhiteSpace(cleanQuery) && !hasBrackets)
            {
                if (!string.IsNullOrWhiteSpace(languageFilter) && !languageFilter.Contains("Tüm"))
                {
                    if (languageFilter.Contains("Almanca")) searchKeyword = "[de]";
                    else if (languageFilter.Contains("Türkçe")) searchKeyword = "[tr]";
                    else if (languageFilter.Contains("İngilizce")) searchKeyword = "[en]";
                    else searchKeyword = languageFilter.ToLowerInvariant();
                }
                else if (!string.IsNullOrWhiteSpace(categoryFilter) && !categoryFilter.Contains("Tüm"))
                {
                    searchKeyword = categoryFilter.ToLowerInvariant();
                }
            }
            else if (hasBrackets)
            {
                searchKeyword = cleanQuery.ToLowerInvariant();
            }

            var results = new List<SearchResultItem>();
            var tasks = new List<Task<List<SearchResultItem>>>();

            bool runAll = string.IsNullOrEmpty(sourceFilter) || sourceFilter.Contains("Tüm") || sourceFilter.Contains("Hepsi");
            bool runIptvCat = runAll || sourceFilter.Contains("IPTVCat") || sourceFilter.Contains("cat");
            bool runFreeTux = runAll || sourceFilter.Contains("FreeTux") || sourceFilter.Contains("Tux");
            bool runAuto = runAll || sourceFilter.Contains("Otomatik") || sourceFilter.Contains("Auto");
            bool runAce = runAll || sourceFilter.Contains("AceStream") || sourceFilter.Contains("P2P");

            // Task 1: IPTVCat Web Scraper (Combined Active + Submitted)
            if (runIptvCat && !string.IsNullOrWhiteSpace(searchKeyword))
            {
                tasks.Add(Task.Run(async () => await SearchIptvCatAsync(searchKeyword)));
            }

            // Task 2: FreeTuxTV Live Database
            if (runFreeTux && !string.IsNullOrWhiteSpace(searchKeyword))
            {
                tasks.Add(Task.Run(async () => await SearchFreeTuxTvAsync(searchKeyword)));
            }

            // Task 3: Search Auto-Update M3U Feeds (Deep Search)
            if (runAuto && !string.IsNullOrWhiteSpace(searchKeyword))
            {
                tasks.Add(Task.Run(async () =>
                {
                    var m3uItems = new List<SearchResultItem>();
                    try
                    {
                        string localPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "auto_update.json");
                        if (System.IO.File.Exists(localPath))
                        {
                            string json = await System.IO.File.ReadAllTextAsync(localPath);
                            var cfg = JsonConvert.DeserializeObject<AutoUpdateConfig>(json);
                            if (cfg != null)
                            {
                                var allUrls = cfg.Tv.Concat(cfg.Film).Concat(cfg.Dizi).Take(20).ToList();
                                foreach (var url in allUrls)
                                {
                                    try
                                    {
                                        var parsed = await _m3uEngine.ParseM3uAsync(url, "M3U");
                                        foreach (var ch in parsed)
                                        {
                                            if (string.IsNullOrWhiteSpace(searchKeyword) || ch.Name.ToLowerInvariant().Contains(searchKeyword) || (ch.Category != null && ch.Category.ToLowerInvariant().Contains(searchKeyword)))
                                            {
                                                m3uItems.Add(new SearchResultItem
                                                {
                                                    Name = ch.Name,
                                                    Url = ch.Url,
                                                    Source = "Otomatik IPTV Listeleri",
                                                    Category = ch.Category ?? "TV",
                                                    GroupTitle = ch.GroupTitle ?? "M3U Stream",
                                                    LogoUrl = ch.LogoUrl ?? "",
                                                    PeersOrDetails = "M3U Canlı Akış"
                                                });
                                                if (m3uItems.Count >= 250) break;
                                            }
                                        }
                                    }
                                    catch { }
                                    if (m3uItems.Count >= 250) break;
                                }
                            }
                        }
                    }
                    catch { }
                    return m3uItems;
                }));
            }

            // Task 4: AceStream Local API & Engine Search
            if (runAce)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var aceItems = new List<SearchResultItem>();
                    try
                    {
                        var aceResults = await _ace.SearchAsync(cleanQuery, categoryFilter, languageFilter);
                        foreach (var res in aceResults)
                        {
                            aceItems.Add(new SearchResultItem
                            {
                                Name = res.Name,
                                Url = res.Url,
                                Source = string.IsNullOrEmpty(res.SourceName) ? "AceStream P2P Ağ" : res.SourceName,
                                Category = string.IsNullOrEmpty(res.Category) ? "P2P Stream" : res.Category,
                                GroupTitle = "AceStream Media",
                                LogoUrl = res.LogoUrl,
                                PeersOrDetails = res.Peers
                            });
                        }
                    }
                    catch { }
                    return aceItems;
                }));
            }

            await Task.WhenAll(tasks);

            foreach (var t in tasks)
            {
                results.AddRange(await t);
            }

            // Filter gathered results strictly by query and language
            results = results.Where(x =>
                ChannelUtils.MatchesQueryFilter(x.Name, x.Category, x.GroupTitle, x.Url, cleanQuery) &&
                ChannelUtils.MatchesLanguageFilter(x.Name, languageFilter)
            ).ToList();

            return results.GroupBy(x => x.Url).Select(g => g.First()).OrderByDescending(x => x.Source.Contains("AceStream")).ToList();
        }

        private async Task<List<SearchResultItem>> SearchIptvCatAsync(string query)
        {
            var list = new List<SearchResultItem>();
            string encoded = Uri.EscapeDataString(query);

            var urlsToFetch = new[]
            {
                (Url: $"https://iptvcat.com/s/{encoded}?state=working", State: "Çalışan"),
                (Url: $"https://iptvcat.com/s/{encoded}?state=submitted", State: "Eklenen")
            };

            foreach (var target in urlsToFetch)
            {
                try
                {
                    var html = await _httpClient.GetStringAsync(target.Url);
                    var matches = Regex.Matches(html, @"(?:href=""([^""]+)""|data-url=""([^""]+)"")[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

                    foreach (Match m in matches)
                    {
                        string link = !string.IsNullOrEmpty(m.Groups[1].Value) ? m.Groups[1].Value : m.Groups[2].Value;
                        string title = System.Net.WebUtility.HtmlDecode(Regex.Replace(m.Groups[3].Value, "<.*?>", "").Trim());

                        if (string.IsNullOrWhiteSpace(link) || string.IsNullOrWhiteSpace(title)) continue;

                        if (link.Contains(".m3u8") || link.Contains(".ts") || link.Contains("acestream://") || link.Contains("get_stream") || link.Contains("live"))
                        {
                            if (!link.StartsWith("http") && !link.StartsWith("acestream://"))
                            {
                                if (link.StartsWith("/")) link = $"https://iptvcat.com{link}";
                                else link = $"https://iptvcat.com/{link}";
                            }

                            if (!list.Any(x => x.Url == link))
                            {
                                list.Add(new SearchResultItem
                                {
                                    Name = title,
                                    Url = link,
                                    Source = "IPTVCat Arama Motoru",
                                    Category = "Canlı TV",
                                    GroupTitle = $"IPTVCat ({target.State})",
                                    PeersOrDetails = $"Durum: {target.State}"
                                });
                            }
                        }
                        if (list.Count >= 300) break;
                    }
                }
                catch { }
                if (list.Count >= 300) break;
            }

            return list;
        }

        private async Task<List<SearchResultItem>> SearchFreeTuxTvAsync(string queryLower)
        {
            var list = new List<SearchResultItem>();
            try
            {
                string url = "https://raw.githubusercontent.com/FreeTuxTV/FreeTuxTV/master/m3u/free_tv.m3u";
                var parsed = await _m3uEngine.ParseM3uAsync(url, "FreeTuxTV");
                foreach (var ch in parsed)
                {
                    if (ch.Name.ToLowerInvariant().Contains(queryLower))
                    {
                        list.Add(new SearchResultItem
                        {
                            Name = ch.Name,
                            Url = ch.Url,
                            Source = "FreeTuxTV Veritabanı",
                            Category = "Canlı TV",
                            GroupTitle = ch.GroupTitle ?? "FreeTuxTV",
                            LogoUrl = ch.LogoUrl ?? "",
                            PeersOrDetails = "Açık Liste"
                        });
                        if (list.Count >= 200) break;
                    }
                }
            }
            catch { }

            return list;
        }
    }
}
