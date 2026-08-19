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
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");
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
            bool runAceEngine = sourceFilter.Contains("Engine") || sourceFilter.Contains("Yerel");
            bool runAceSearchWeb = sourceFilter.Contains("search-ace") || sourceFilter.Contains("Search Web");
            bool runAceNetWeb = sourceFilter.Contains("ace-stream.net") || sourceFilter.Contains("Net Web");
            bool runIptvCat = runAll || sourceFilter.Contains("IPTVCat") || sourceFilter.Contains("cat");
            bool runFreeTux = runAll || sourceFilter.Contains("FreeTux") || sourceFilter.Contains("Tux");

            // 1. IPTVCat Web Scraper (Working Streams Only)
            if (runIptvCat && !string.IsNullOrWhiteSpace(searchKeyword))
            {
                tasks.Add(Task.Run(async () => await SearchIptvCatAsync(searchKeyword)));
            }

            // 2. FreeTuxTV Web Database Scraper (Working Streams Only)
            if (runFreeTux && !string.IsNullOrWhiteSpace(searchKeyword))
            {
                tasks.Add(Task.Run(async () => await SearchFreeTuxTvAsync(searchKeyword)));
            }

            // 3. AceStream Sources
            if (runAll)
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
                                Source = string.IsNullOrEmpty(res.SourceName) ? "AceStream Engine API" : res.SourceName,
                                Category = string.IsNullOrEmpty(res.Category) ? "P2P Stream" : res.Category,
                                GroupTitle = "AceStream Media",
                                LogoUrl = res.LogoUrl,
                                PeersOrDetails = res.Peers
                            });
                        }
                        LogService.LogInfo($"GlobalSearch: AceStream combined search returned {aceItems.Count} items.");
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError("GlobalSearch: AceStream combined search failed", ex);
                    }
                    return aceItems;
                }));
            }
            else
            {
                if (runAceEngine)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        var aceItems = new List<SearchResultItem>();
                        try
                        {
                            var aceResults = await _ace.SearchEngineApiOnlyAsync(cleanQuery, categoryFilter, languageFilter);
                            foreach (var res in aceResults)
                            {
                                aceItems.Add(new SearchResultItem
                                {
                                    Name = res.Name,
                                    Url = res.Url,
                                    Source = "AceStream Engine API",
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

                if (runAceSearchWeb)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        var aceItems = new List<SearchResultItem>();
                        try
                        {
                            var aceResults = await _ace.SearchSearchAceStreamWebAsync(cleanQuery, categoryFilter, languageFilter);
                            foreach (var res in aceResults)
                            {
                                aceItems.Add(new SearchResultItem
                                {
                                    Name = res.Name,
                                    Url = res.Url,
                                    Source = "search-ace.stream Web",
                                    Category = string.IsNullOrEmpty(res.Category) ? "P2P Stream" : res.Category,
                                    GroupTitle = "AceStream Media",
                                    LogoUrl = res.LogoUrl,
                                    PeersOrDetails = "Web Index"
                                });
                            }
                        }
                        catch { }
                        return aceItems;
                    }));
                }

                if (runAceNetWeb)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        var aceItems = new List<SearchResultItem>();
                        try
                        {
                            var aceResults = await _ace.SearchAceStreamNetWebAsync(cleanQuery, categoryFilter, languageFilter);
                            foreach (var res in aceResults)
                            {
                                aceItems.Add(new SearchResultItem
                                {
                                    Name = res.Name,
                                    Url = res.Url,
                                    Source = "ace-stream.net Web",
                                    Category = string.IsNullOrEmpty(res.Category) ? "P2P Stream" : res.Category,
                                    GroupTitle = "AceStream Media",
                                    LogoUrl = res.LogoUrl,
                                    PeersOrDetails = "Web Index"
                                });
                            }
                        }
                        catch { }
                        return aceItems;
                    }));
                }
            }

            await Task.WhenAll(tasks);

            foreach (var t in tasks)
            {
                results.AddRange(await t);
            }

            // Filter gathered results strictly by query, language and remove fake/generic placeholder titles
            results = results.Where(x =>
                !string.IsNullOrWhiteSpace(x.Name) &&
                x.Name.Length >= 2 &&
                !Regex.IsMatch(x.Name, @"AceStream (Content|İçeriği|Media)", RegexOptions.IgnoreCase) &&
                !x.Name.Equals("AceStream", StringComparison.OrdinalIgnoreCase) &&
                ChannelUtils.MatchesQueryFilter(x.Name, x.Category, x.GroupTitle, x.Url, cleanQuery) &&
                ChannelUtils.MatchesLanguageFilter(x.Name, languageFilter)
            ).ToList();

            return results.GroupBy(x => x.Url).Select(g => g.First()).OrderByDescending(x => x.Source.Contains("AceStream")).ToList();
        }

        private async Task<List<SearchResultItem>> SearchIptvCatAsync(string query)
        {
            var list = new List<SearchResultItem>();
            string encoded = Uri.EscapeDataString(query);

            string targetUrl = $"https://iptvcat.com/s/{encoded}?state=working";

            try
            {
                var html = await _httpClient.GetStringAsync(targetUrl);
                var rowMatches = Regex.Matches(html, @"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

                foreach (Match row in rowMatches)
                {
                    string rowHtml = row.Groups[1].Value;

                    string title = "";
                    var titleMatch = Regex.Match(rowHtml, @"class=[""']channel_name[""'][^>]*title=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                    if (!titleMatch.Success)
                    {
                        titleMatch = Regex.Match(rowHtml, @"class=[""']channel_name[""'][^>]*>(.*?)</span>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    }
                    if (titleMatch.Success)
                    {
                        title = System.Net.WebUtility.HtmlDecode(Regex.Replace(titleMatch.Groups[1].Value, "<.*?>", "").Trim());
                    }

                    string link = "";
                    var urlMatch = Regex.Match(rowHtml, @"data-clipboard-text=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                    if (!urlMatch.Success)
                    {
                        urlMatch = Regex.Match(rowHtml, @"href=[""'](https?://[^""']+)[""']", RegexOptions.IgnoreCase);
                    }
                    if (!urlMatch.Success)
                    {
                        urlMatch = Regex.Match(rowHtml, @"href=[""'](/[^""']+)[""']", RegexOptions.IgnoreCase);
                    }

                    if (urlMatch.Success)
                    {
                        link = urlMatch.Groups[1].Value.Trim();
                        if (link.StartsWith("/")) link = $"https://iptvcat.com{link}";
                    }

                    if (!string.IsNullOrWhiteSpace(link) && !string.IsNullOrWhiteSpace(title) && title.Length >= 2)
                    {
                        bool isStream = link.Contains(".m3u8") || link.Contains(".ts") || link.Contains("acestream://") ||
                                        link.Contains("get_stream") || link.Contains("live") || link.Contains("my_list") ||
                                        link.Contains("playlist") || link.Contains("stream") || link.Contains("chunk");

                        if (isStream && !list.Any(x => x.Url == link) && ChannelUtils.MatchesQueryFilter(title, "Canlı TV", "Çalışan", link, query))
                        {
                            list.Add(new SearchResultItem
                            {
                                Name = title,
                                Url = link,
                                Source = "IPTVCat Arama Motoru",
                                Category = "Canlı TV",
                                GroupTitle = "IPTVCat",
                                PeersOrDetails = "Durum: Çalışan"
                            });
                        }
                    }
                    if (list.Count >= 300) break;
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[GlobalSearch] IPTVcat arama hatası ('{query}'): {ex.Message}");
            }

            return list;
        }

        private async Task<List<SearchResultItem>> SearchFreeTuxTvAsync(string queryLower)
        {
            var list = new List<SearchResultItem>();
            if (string.IsNullOrWhiteSpace(queryLower)) return list;

            try
            {
                string encoded = Uri.EscapeDataString(queryLower);
                string searchUrl = $"https://database.freetuxtv.net/WebStream/index?WebStreamSearchForm%5BName%5D={encoded}&WebStreamSearchForm%5BEditPending%5D=0&yt0=Search";
                string html = await _httpClient.GetStringAsync(searchUrl);

                // Parse FreeTuxTV web table rows (Active/Live streams)
                var rowMatches = Regex.Matches(html, @"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                foreach (Match row in rowMatches)
                {
                    string rowHtml = row.Groups[1].Value;
                    if (!rowHtml.Contains("http") && !rowHtml.Contains("m3u8") && !rowHtml.Contains("=>") && !rowHtml.Contains("=&gt;")) continue;

                    // Exclude Dead and Invalid streams
                    if (rowHtml.Contains("Dead") || rowHtml.Contains("Invalid"))
                    {
                        continue;
                    }

                    string name = "";
                    string streamUrl = "";

                    // Match stream link
                    var linkMatch = Regex.Match(rowHtml, @"(?:=>|=&gt;)\s*<a[^>]*href=[""']?([^""'>]+)[""']?[^>]*>(.*?)</a>", RegexOptions.IgnoreCase);
                    if (!linkMatch.Success)
                    {
                        linkMatch = Regex.Match(rowHtml, @"(?:=>|=&gt;)\s*(https?://[^\s<]+)", RegexOptions.IgnoreCase);
                    }

                    if (linkMatch.Success)
                    {
                        streamUrl = linkMatch.Groups[1].Value.Trim();
                    }

                    // Extract stream name before <br> or =>
                    var nameMatch = Regex.Match(rowHtml, @"<td[^>]*>(.*?)(?:<br\s*/?>|=>|=&gt;)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    if (nameMatch.Success)
                    {
                        name = Regex.Replace(nameMatch.Groups[1].Value, "<.*?>", "").Trim();
                        name = System.Net.WebUtility.HtmlDecode(name);
                    }

                    if (!string.IsNullOrWhiteSpace(streamUrl) && !string.IsNullOrWhiteSpace(name) && name.Length >= 2)
                    {
                        if (!list.Any(x => x.Url == streamUrl) && ChannelUtils.MatchesQueryFilter(name, "Canlı TV", "Çalışan", streamUrl, queryLower))
                        {
                            list.Add(new SearchResultItem
                            {
                                Name = name,
                                Url = streamUrl,
                                Source = "FreeTuxTV Veritabanı",
                                Category = "Canlı TV",
                                GroupTitle = "FreeTuxTV",
                                PeersOrDetails = "Durum: Çalışan"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[GlobalSearch] FreeTuxTV web arama uyarısı ('{queryLower}'): {ex.Message}");
            }

            // Fallback & supplement with FreeTuxTV M3U repository
            try
            {
                string m3uUrl = "https://raw.githubusercontent.com/FreeTuxTV/FreeTuxTV/master/m3u/free_tv.m3u";
                var parsed = await _m3uEngine.ParseM3uAsync(m3uUrl, "FreeTuxTV");
                foreach (var ch in parsed)
                {
                    if (ChannelUtils.MatchesQueryFilter(ch.Name, ch.Category ?? "", ch.GroupTitle ?? "", ch.Url ?? "", queryLower))
                    {
                        if (!list.Any(x => x.Url == ch.Url))
                        {
                            list.Add(new SearchResultItem
                            {
                                Name = ch.Name,
                                Url = ch.Url ?? "",
                                Source = "FreeTuxTV Veritabanı",
                                Category = string.IsNullOrWhiteSpace(ch.Category) ? "Canlı TV" : ch.Category,
                                GroupTitle = ch.GroupTitle ?? "FreeTuxTV",
                                LogoUrl = ch.LogoUrl ?? "",
                                PeersOrDetails = "Açık Liste"
                            });
                        }
                        if (list.Count >= 200) break;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[GlobalSearch] FreeTuxTV M3U yedek liste uyarısı: {ex.Message}");
            }

            return list;
        }
    }
}
