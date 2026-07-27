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
        public string Source { get; set; } = ""; // E.g., "IPTVCat Scraper", "FreeTuxTV", "Otomatik M3U", "IPTV-Org", "AceStream P2P"
        public string Category { get; set; } = "Genel";
        public string PeersOrDetails { get; set; } = "";
        public string LogoUrl { get; set; } = "";
        public string GroupTitle { get; set; } = "";
    }

    public class GlobalSearchEngine
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private readonly DatabaseEngine _db = new DatabaseEngine();
        private readonly AceEngine _ace = new AceEngine();
        private readonly M3uEngine _m3uEngine = new M3uEngine();

        static GlobalSearchEngine()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        public async Task<List<SearchResultItem>> SearchGlobalAsync(string query, string sourceFilter = "Tüm Kaynaklar")
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<SearchResultItem>();

            string q = query.Trim().ToLowerInvariant();
            var results = new List<SearchResultItem>();
            var tasks = new List<Task<List<SearchResultItem>>>();

            bool runAll = string.IsNullOrEmpty(sourceFilter) || sourceFilter.Contains("Tüm") || sourceFilter.Contains("Hepsi");
            bool runIptvCat = runAll || sourceFilter.Contains("IPTVCat") || sourceFilter.Contains("cat");
            bool runFreeTux = runAll || sourceFilter.Contains("FreeTux") || sourceFilter.Contains("Tux");
            bool runAuto = runAll || sourceFilter.Contains("Otomatik") || sourceFilter.Contains("Auto");
            bool runIptvOrg = runAll || sourceFilter.Contains("IPTV-Org") || sourceFilter.Contains("Açık Kaynak");
            bool runAce = runAll || sourceFilter.Contains("AceStream") || sourceFilter.Contains("P2P");

            // Task 1: IPTVCat Web Scraper
            if (runIptvCat)
            {
                tasks.Add(Task.Run(async () => await SearchIptvCatAsync(query)));
            }

            // Task 2: FreeTuxTV Live Database
            if (runFreeTux)
            {
                tasks.Add(Task.Run(async () => await SearchFreeTuxTvAsync(q)));
            }

            // Task 3: Search Auto-Update M3U Feeds
            if (runAuto)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var m3uItems = new List<SearchResultItem>();
                    try
                    {
                        string localPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "auto_update.json");
                        if (!System.IO.File.Exists(localPath)) localPath = "auto_update.json";
                        if (System.IO.File.Exists(localPath))
                        {
                            string json = await System.IO.File.ReadAllTextAsync(localPath);
                            var cfg = JsonConvert.DeserializeObject<AutoUpdateConfig>(json);
                            if (cfg != null)
                            {
                                var allUrls = cfg.Tv.Concat(cfg.Film).Concat(cfg.Dizi).Take(6).ToList();
                                foreach (var url in allUrls)
                                {
                                    try
                                    {
                                        var parsed = await _m3uEngine.ParseM3uAsync(url, "M3U");
                                        foreach (var ch in parsed)
                                        {
                                            if (ch.Name.ToLowerInvariant().Contains(q))
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
                                                if (m3uItems.Count >= 30) break;
                                            }
                                        }
                                    }
                                    catch { }
                                    if (m3uItems.Count >= 30) break;
                                }
                            }
                        }
                    }
                    catch { }
                    return m3uItems;
                }));
            }

            // Task 4: Search IPTV-Org Global Database
            if (runIptvOrg)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var iptvItems = new List<SearchResultItem>();
                    try
                    {
                        string iptvOrgUrl = "https://iptv-org.github.io/iptv/index.language.m3u";
                        var parsed = await _m3uEngine.ParseM3uAsync(iptvOrgUrl, "TV");
                        foreach (var ch in parsed)
                        {
                            if (ch.Name.ToLowerInvariant().Contains(q))
                            {
                                iptvItems.Add(new SearchResultItem
                                {
                                    Name = ch.Name,
                                    Url = ch.Url,
                                    Source = "IPTV-Org Küresel Liste",
                                    Category = "TV",
                                    GroupTitle = ch.GroupTitle ?? "Dünya Kanalları",
                                    LogoUrl = ch.LogoUrl ?? "",
                                    PeersOrDetails = "Açık Kaynak Kanal"
                                });
                                if (iptvItems.Count >= 30) break;
                            }
                        }
                    }
                    catch { }
                    return iptvItems;
                }));
            }

            // Task 5: AceStream Local API & Engine Search
            if (runAce)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var aceItems = new List<SearchResultItem>();
                    try
                    {
                        var aceResults = await _ace.SearchAsync(query);
                        foreach (var res in aceResults)
                        {
                            aceItems.Add(new SearchResultItem
                            {
                                Name = res.Name,
                                Url = res.Url,
                                Source = "AceStream P2P Ağ",
                                Category = "P2P Stream",
                                GroupTitle = "AceStream Media",
                                PeersOrDetails = $"Peers: {res.Peers}"
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

            return results.GroupBy(x => x.Url).Select(g => g.First()).ToList();
        }

        private async Task<List<SearchResultItem>> SearchIptvCatAsync(string query)
        {
            var list = new List<SearchResultItem>();
            string encoded = Uri.EscapeDataString(query);

            var urlsToFetch = new[]
            {
                (Url: $"https://iptvcat.com/s/{encoded}", State: "Working"),
                (Url: $"https://iptvcat.com/s/{encoded}?state=submitted", State: "Submitted")
            };

            foreach (var target in urlsToFetch)
            {
                try
                {
                    var html = await _httpClient.GetStringAsync(target.Url);

                    var matches = Regex.Matches(html, @"<tr[^>]*class=""channel""[^>]*>.*?<td[^>]*class=""name""[^>]*>(.*?)</td>.*?href=""(http[^""]+)""", RegexOptions.Singleline | RegexOptions.IgnoreCase);

                    if (matches.Count == 0)
                    {
                        matches = Regex.Matches(html, @"href=""(https?://[^""]+\.(?:m3u8|ts|mp4)[^""]*)""[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                        foreach (Match m in matches)
                        {
                            string streamUrl = m.Groups[1].Value;
                            string title = Regex.Replace(m.Groups[2].Value, "<.*?>", "").Trim();
                            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(streamUrl))
                            {
                                if (!list.Any(x => x.Url == streamUrl))
                                {
                                    list.Add(new SearchResultItem
                                    {
                                        Name = title,
                                        Url = streamUrl,
                                        Source = "IPTVCat Arama Motoru",
                                        Category = "Canlı TV",
                                        GroupTitle = $"IPTVCat ({target.State})",
                                        PeersOrDetails = $"Durum: {target.State}"
                                    });
                                }
                            }
                            if (list.Count >= 40) break;
                        }
                    }
                    else
                    {
                        foreach (Match m in matches)
                        {
                            string title = Regex.Replace(m.Groups[1].Value, "<.*?>", "").Trim();
                            string streamUrl = m.Groups[2].Value;
                            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(streamUrl))
                            {
                                if (!list.Any(x => x.Url == streamUrl))
                                {
                                    list.Add(new SearchResultItem
                                    {
                                        Name = title,
                                        Url = streamUrl,
                                        Source = "IPTVCat Arama Motoru",
                                        Category = "Canlı TV",
                                        GroupTitle = $"IPTVCat ({target.State})",
                                        PeersOrDetails = $"Durum: {target.State}"
                                    });
                                }
                            }
                            if (list.Count >= 40) break;
                        }
                    }
                }
                catch { }
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
                        if (list.Count >= 25) break;
                    }
                }
            }
            catch { }

            return list;
        }
    }
}
