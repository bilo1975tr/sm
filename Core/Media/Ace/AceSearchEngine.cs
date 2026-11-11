using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StreamMesh.Core.Utils;
using StreamMesh.Models;

namespace StreamMesh.Core.Media.Ace
{
    /// <summary>
    /// Searches local AceEngine P2P network and public AceStream directories.
    /// </summary>
    public static class AceSearchEngine
    {
        private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

        public static async Task<List<SearchResultItem>> SearchAceStreamP2pAsync(string query)
        {
            var results = new List<SearchResultItem>();
            string clean = (query ?? "").Trim();
            if (string.IsNullOrWhiteSpace(clean)) return results;

            if (AceIdExtractor.IsAceStreamUrl(clean))
            {
                string id = AceIdExtractor.ExtractAceId(clean);
                if (id.Length == 40 && Regex.IsMatch(id, "^[a-fA-F0-9]{40}$"))
                {
                    int peers = await AceStreamTokenResolver.GetStreamPeersAsync(clean);
                    results.Add(new SearchResultItem
                    {
                        Name = $"AceStream P2P Yayını ({id.Substring(0, Math.Min(8, id.Length))}...)",
                        Url = $"acestream://{id}",
                        Source = "AceStream P2P",
                        Category = "Canlı TV",
                        GroupTitle = "AceStream P2P",
                        PeersOrDetails = peers > 0 ? $"{peers} Peer / Eş" : "Yerel AceEngine P2P"
                    });
                    return results;
                }
            }

            string cleanTag = clean.Trim('[', ']', '(', ')', '{', '}').Trim();
            var searchTerms = new List<string> { clean };
            if (!string.IsNullOrEmpty(cleanTag) && !cleanTag.Equals(clean, StringComparison.OrdinalIgnoreCase))
            {
                searchTerms.Add(cleanTag);
            }

            try
            {
                if (await AceProcessManager.IsEngineHttpAliveAsync().ConfigureAwait(false) || AceProcessManager.IsAnyAceProcessRunning())
                {
                    string? token = await AceStreamTokenResolver.GetApiAccessTokenAsync().ConfigureAwait(false);

                    foreach (var sTerm in searchTerms)
                    {
                        string encoded = Uri.EscapeDataString(sTerm);

                        for (int page = 0; page < 4; page++)
                        {
                            using var cts = new CancellationTokenSource(3000);
                            int beforeCount = results.Count;

                            var engineUrls = new List<string>();
                            if (!string.IsNullOrWhiteSpace(token))
                            {
                                engineUrls.Add($"{AceProcessManager.AceStreamBaseUrl}/server/api?method=search&token={token}&query={encoded}&api_version=4&page_size=200&page={page}");
                                engineUrls.Add($"{AceProcessManager.AceStreamBaseUrl}/server/api?method=search&token={token}&q={encoded}&api_version=4&page_size=200&page={page}");
                            }
                            engineUrls.Add($"{AceProcessManager.AceStreamBaseUrl}/server/api?method=search&query={encoded}&api_version=4&page_size=200&page={page}");
                            engineUrls.Add($"{AceProcessManager.AceStreamBaseUrl}/search?query={encoded}&page_size=200&page={page}");

                            foreach (var url in engineUrls)
                            {
                                try
                                {
                                    var json = await HttpClient.GetStringAsync(url, cts.Token).ConfigureAwait(false);
                                    if (!string.IsNullOrWhiteSpace(json) && json.StartsWith("{"))
                                    {
                                        var data = Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(json);
                                        var resultsToken = data?["result"]?["results"] ?? data?["results"] ?? data?["result"];
                                        
                                        if (resultsToken != null)
                                        {
                                            if (resultsToken.Type == JTokenType.Array)
                                            {
                                                foreach (var chItem in resultsToken)
                                                {
                                                    ParseLocalChannelItem(chItem, results);
                                                }
                                            }
                                            else if (resultsToken.Type == JTokenType.Object)
                                            {
                                                ParseLocalChannelItem(resultsToken, results);
                                            }
                                        }
                                    }
                                    if (results.Count > beforeCount) break;
                                }
                                catch { }
                            }

                            if (results.Count == beforeCount || results.Count >= 400)
                            {
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"[AceSearchEngine] P2P Search Error: {ex.Message}");
            }

            try
            {
                var webItems = await SearchAceStreamWebAsync(clean);
                foreach (var itm in webItems)
                {
                    results.Add(itm);
                }

                if (searchTerms.Count > 1 && results.Count < 50)
                {
                    var webItemsTag = await SearchAceStreamWebAsync(cleanTag);
                    foreach (var itm in webItemsTag)
                    {
                        results.Add(itm);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[AceSearchEngine] AceStream P2P Web Complement error: {ex.Message}");
            }

            return results.GroupBy(r => r.Url).Select(g => g.First()).ToList();
        }

        private static void ParseLocalChannelItem(JToken? chItem, List<SearchResultItem> resList)
        {
            if (chItem == null) return;
            string chName = (string?)chItem["name"] ?? (string?)chItem["title"] ?? "AceStream P2P Yayın";
            string chIcon = (string?)chItem["icon"] ?? (string?)chItem["logo"] ?? "";
            string chCategory = (string?)chItem["category"] ?? "Canlı TV";
            string epgTitle = (string?)chItem["epg"]?["name"] ?? "";

            var itemsToken = chItem["items"] ?? chItem["streams"];
            if (itemsToken != null && itemsToken.Type == JTokenType.Array && itemsToken.Any())
            {
                foreach (var stream in itemsToken)
                {
                    string infohash = AceIdExtractor.Find40CharContentId(stream);
                    if (string.IsNullOrEmpty(infohash))
                    {
                        infohash = AceIdExtractor.Find40CharContentId(chItem);
                    }

                    if (!string.IsNullOrWhiteSpace(infohash) && infohash.Length == 40 && Regex.IsMatch(infohash, "^[a-fA-F0-9]{40}$"))
                    {
                        int peers = (int?)stream["availability"] ?? (int?)stream["peers"] ?? (int?)chItem["peers"] ?? 0;
                        string details = peers > 0 ? $"{peers} Peer / Eş" : (!string.IsNullOrWhiteSpace(epgTitle) ? $"Yayın: {epgTitle}" : "127.0.0.1 AceEngine");
                        resList.Add(new SearchResultItem
                        {
                            Name = chName,
                            Url = $"acestream://{infohash}",
                            Source = "AceStream P2P",
                            Category = chCategory,
                            GroupTitle = "AceStream P2P (Yerel)",
                            LogoUrl = chIcon,
                            PeersOrDetails = details
                        });
                    }
                }
            }
            else
            {
                string infohash = AceIdExtractor.Find40CharContentId(chItem);
                if (!string.IsNullOrWhiteSpace(infohash) && infohash.Length == 40 && Regex.IsMatch(infohash, "^[a-fA-F0-9]{40}$"))
                {
                    int peers = (int?)chItem["availability"] ?? (int?)chItem["peers"] ?? 0;
                    string details = peers > 0 ? $"{peers} Peer / Eş" : (!string.IsNullOrWhiteSpace(epgTitle) ? $"Yayın: {epgTitle}" : "127.0.0.1 AceEngine");
                    resList.Add(new SearchResultItem
                    {
                        Name = chName,
                        Url = $"acestream://{infohash}",
                        Source = "AceStream P2P",
                        Category = chCategory,
                        GroupTitle = "AceStream P2P (Yerel)",
                        LogoUrl = chIcon,
                        PeersOrDetails = details
                    });
                }
            }
        }

        public static async Task<List<SearchResultItem>> SearchAceStreamWebAsync(string query, string domain = "all")
        {
            var results = new List<SearchResultItem>();
            string clean = (query ?? "").Trim();
            if (string.IsNullOrWhiteSpace(clean)) return results;

            string encoded = Uri.EscapeDataString(clean);
            var targets = new List<(string url, string sourceName)>();

            string d = domain.ToLowerInvariant();
            if (d.Contains("me") && !d.Contains("tüm") && !d.Contains("all"))
            {
                targets.Add(($"https://api.acestream.me/search?q={encoded}", "AceStream (me)"));
            }
            else if (d.Contains("org"))
            {
                targets.Add(($"https://api.acestream.me/search?q={encoded}", "AceStream (org)"));
                targets.Add(($"https://search.acestream.net/search?q={encoded}", "AceStream (org)"));
                targets.Add(($"https://api.acestream.org/search?q={encoded}", "AceStream (org)"));
            }
            else if (d.Contains("net"))
            {
                targets.Add(($"https://search.acestream.net/search?q={encoded}", "AceStream (net)"));
                targets.Add(($"https://api.acestream.me/search?q={encoded}", "AceStream (net)"));
            }
            else
            {
                targets.Add(($"https://api.acestream.me/search?q={encoded}", "AceStream (me)"));
                targets.Add(($"https://api.acestream.me/search?q={encoded}", "AceStream (org)"));
                targets.Add(($"https://search.acestream.net/search?q={encoded}", "AceStream (net)"));
            }

            foreach (var (endpoint, srcName) in targets)
            {
                for (int page = 1; page <= 4; page++)
                {
                    int beforeCount = results.Count;
                    string pageUrl = endpoint.Contains("?") ? $"{endpoint}&page={page}&page_size=100" : $"{endpoint}?page={page}&page_size=100";

                    try
                    {
                        using var cts = new CancellationTokenSource(3500);
                        using var req = new HttpRequestMessage(HttpMethod.Get, pageUrl);
                        req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 StreamMesh/2.1");
                        var resp = await HttpClient.SendAsync(req, cts.Token).ConfigureAwait(false);
                        if (resp.IsSuccessStatusCode)
                        {
                            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(body) && (body.StartsWith("{") || body.StartsWith("[")))
                            {
                                dynamic? data = Newtonsoft.Json.JsonConvert.DeserializeObject(body);
                                var itemsToken = data?.results ?? data;
                                if (itemsToken != null)
                                {
                                    foreach (var item in itemsToken)
                                    {
                                        string name = (string?)item.name ?? (string?)item.title ?? "";
                                        string icon = (string?)item.icon ?? "";
                                        string category = (string?)item.category ?? "Canlı TV";
                                        string epgTitle = (string?)item.epg?.name ?? "";

                                        string cid = AceIdExtractor.Find40CharContentId(item);

                                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(cid) && cid.Length == 40 && Regex.IsMatch(cid, "^[a-fA-F0-9]{40}$"))
                                        {
                                            string playUrl = $"acestream://{cid}";
                                            string details = !string.IsNullOrWhiteSpace(epgTitle)
                                                ? $"Yayın: {epgTitle}"
                                                : $"{srcName} İndeksi";

                                            results.Add(new SearchResultItem
                                             {
                                                Name = name,
                                                Url = playUrl,
                                                Source = srcName,
                                                Category = category,
                                                GroupTitle = srcName,
                                                LogoUrl = icon,
                                                PeersOrDetails = details
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.LogWarning($"[AceSearchEngine] AceStream Web endpoint {endpoint} (sayfa {page}) hatası: {ex.Message}");
                        break;
                    }

                    if (results.Count == beforeCount || results.Count >= 400)
                    {
                        break;
                    }
                }
            }

            return results.GroupBy(r => r.Url).Select(g => g.First()).ToList();
        }

        public static async Task<List<SearchResultItem>> SearchAceStreamsAsync(string query)
        {
            var p2pTask = SearchAceStreamP2pAsync(query);
            var webTask = SearchAceStreamWebAsync(query);

            await Task.WhenAll(p2pTask, webTask).ConfigureAwait(false);

            var results = new List<SearchResultItem>();
            results.AddRange(await p2pTask);
            results.AddRange(await webTask);

            return results.GroupBy(r => r.Url).Select(g => g.First()).ToList();
        }
    }
}
