using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class AceResult
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string Peers { get; set; } = "0";
        public string SourceName { get; set; } = "AceStream";
        public string Category { get; set; } = "P2P Stream";
        public string LogoUrl { get; set; } = "";
    }

    public class AceEngine
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private const int ACESTREAM_PORT = 6878;
        private string? _cachedApiToken;

        static AceEngine()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");
        }

        public async Task<string?> GetApiAccessTokenAsync()
        {
            if (!string.IsNullOrEmpty(_cachedApiToken)) return _cachedApiToken;
            try
            {
                string url = $"http://127.0.0.1:{ACESTREAM_PORT}/server/api?method=get_api_access_token";
                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    dynamic? data = JsonConvert.DeserializeObject(json);
                    string? token = data?.result?.token;
                    if (!string.IsNullOrEmpty(token)) { _cachedApiToken = token; return _cachedApiToken; }
                }
            }
            catch { }
            return null;
        }

        public async Task<List<AceResult>> SearchEngineApiOnlyAsync(string query, string category = "", string language = "")
        {
            var list = new List<AceResult>();
            string cleanQuery = (query ?? "").Trim();
            if (string.IsNullOrWhiteSpace(cleanQuery)) return list;

            string possibleCid = ExtractHash(cleanQuery);
            if (!string.IsNullOrEmpty(possibleCid))
            {
                list.Add(new AceResult { Name = $"AceStream İçeriği ({possibleCid.Substring(0, 8)}...)", Url = $"acestream://{possibleCid}", Peers = "Doğrudan ID", SourceName = "AceStream Engine API", Category = "P2P Stream" });
                return list;
            }

            string apiCategory = MapCategoryToAceApi(category);
            bool hasBrackets = cleanQuery.Contains("[") || cleanQuery.Contains("]");
            string cleanStripped = hasBrackets ? cleanQuery : cleanQuery.Replace("[", " ").Replace("]", " ").Replace("(", " ").Replace(")", " ").Trim();

            try { await SearchServerSideApiAsync(list, cleanQuery, apiCategory, 0); } catch { }
            if (cleanStripped != cleanQuery && !string.IsNullOrWhiteSpace(cleanStripped) && !hasBrackets)
            {
                try { await SearchServerSideApiAsync(list, cleanStripped, apiCategory, 0); } catch { }
            }
            try { await SearchLocalEngineApiAsync(list, cleanQuery, apiCategory, 0); } catch { }

            if (list.Count < 30)
            {
                try { await SearchServerSideApiAsync(list, cleanQuery, apiCategory, 250); } catch { }
                try { await SearchServerSideAllSnapshotAsync(list, cleanQuery, apiCategory); } catch { }
            }

            return list.Where(x =>
                !string.IsNullOrWhiteSpace(x.Name) &&
                !x.Name.StartsWith("AceStream Content", StringComparison.OrdinalIgnoreCase) &&
                !x.Name.StartsWith("AceStream İçeriği", StringComparison.OrdinalIgnoreCase) &&
                ChannelUtils.MatchesQueryFilter(x.Name, x.Category, "", x.Url, cleanQuery) &&
                ChannelUtils.MatchesLanguageFilter(x.Name, language)
            ).ToList();
        }

        public async Task<List<AceResult>> SearchSearchAceStreamWebAsync(string query, string category = "", string language = "")
        {
            var list = new List<AceResult>();
            string cleanQuery = (query ?? "").Trim();
            if (string.IsNullOrWhiteSpace(cleanQuery)) return list;

            try
            {
                string url = $"https://search-ace.stream/?q={Uri.EscapeDataString(cleanQuery)}";
                var html = await _httpClient.GetStringAsync(url);
                var matches = Regex.Matches(html, @"(acestream://[a-f0-9]{40}|[a-f0-9]{40})[^>]*>(.*?)<");
                foreach (Match m in matches)
                {
                    string title = Regex.Replace(m.Groups[2].Value, "<.*?>", "").Trim();
                    if (string.IsNullOrWhiteSpace(title) || title.Length < 3) continue;

                    if (title.Equals("AceStream", StringComparison.OrdinalIgnoreCase) ||
                        title.Equals("Download", StringComparison.OrdinalIgnoreCase) ||
                        title.Equals("Play", StringComparison.OrdinalIgnoreCase) ||
                        title.Equals("Link", StringComparison.OrdinalIgnoreCase) ||
                        title.Contains("Ace Stream Search") ||
                        title.Contains("Index"))
                        continue;

                    if (!ChannelUtils.MatchesQueryFilter(title, "P2P Stream", "", "", cleanQuery)) continue;

                    string finalUrl = m.Groups[1].Value.StartsWith("acestream://") ? m.Groups[1].Value : $"acestream://{m.Groups[1].Value}";
                    if (!list.Any(x => x.Url.Equals(finalUrl, StringComparison.OrdinalIgnoreCase)))
                    {
                        list.Add(new AceResult { Name = title, Url = finalUrl, SourceName = "search-ace.stream Web", Category = "P2P Stream" });
                    }
                }
            }
            catch { }

            return list.Where(x => ChannelUtils.MatchesLanguageFilter(x.Name, language)).ToList();
        }

        public async Task<List<AceResult>> SearchAceStreamNetWebAsync(string query, string category = "", string language = "")
        {
            var list = new List<AceResult>();
            string cleanQuery = (query ?? "").Trim();
            if (string.IsNullOrWhiteSpace(cleanQuery)) return list;

            try
            {
                string url = $"https://ace-stream.net/search?q={Uri.EscapeDataString(cleanQuery)}";
                var html = await _httpClient.GetStringAsync(url);
                var matches = Regex.Matches(html, @"(acestream://[a-f0-9]{40}|[a-f0-9]{40})[^>]*>(.*?)<");
                foreach (Match m in matches)
                {
                    string title = Regex.Replace(m.Groups[2].Value, "<.*?>", "").Trim();
                    if (string.IsNullOrWhiteSpace(title) || title.Length < 3) continue;

                    if (title.Equals("AceStream", StringComparison.OrdinalIgnoreCase) ||
                        title.Equals("Download", StringComparison.OrdinalIgnoreCase) ||
                        title.Equals("Play", StringComparison.OrdinalIgnoreCase) ||
                        title.Equals("Link", StringComparison.OrdinalIgnoreCase) ||
                        title.Contains("Ace Stream Search") ||
                        title.Contains("Index"))
                        continue;

                    if (!ChannelUtils.MatchesQueryFilter(title, "P2P Stream", "", "", cleanQuery)) continue;

                    string finalUrl = m.Groups[1].Value.StartsWith("acestream://") ? m.Groups[1].Value : $"acestream://{m.Groups[1].Value}";
                    if (!list.Any(x => x.Url.Equals(finalUrl, StringComparison.OrdinalIgnoreCase)))
                    {
                        list.Add(new AceResult { Name = title, Url = finalUrl, SourceName = "ace-stream.net Web", Category = "P2P Stream" });
                    }
                }
            }
            catch { }

            return list.Where(x => ChannelUtils.MatchesLanguageFilter(x.Name, language)).ToList();
        }

        public async Task<List<AceResult>> SearchAsync(string query, string category = "", string language = "")
        {
            var list = new List<AceResult>();
            string cleanQuery = (query ?? "").Trim();

            string possibleCid = ExtractHash(cleanQuery);
            if (!string.IsNullOrEmpty(possibleCid))
            {
                list.Add(new AceResult { Name = $"AceStream İçeriği ({possibleCid.Substring(0, 8)}...)", Url = $"acestream://{possibleCid}", Peers = "Doğrudan ID", SourceName = "AceStream Engine API", Category = "P2P Stream" });
                return list;
            }

            // 1. Prioritize Engine API search
            try
            {
                var apiResults = await SearchEngineApiOnlyAsync(cleanQuery, category, language);
                list.AddRange(apiResults);
            }
            catch { }

            // Collect known URLs / Stream IDs from Engine API
            var knownUrls = new HashSet<string>(list.Select(x => x.Url.ToLowerInvariant()));

            // 2. Search search-ace.stream Web
            try
            {
                var webResults1 = await SearchSearchAceStreamWebAsync(cleanQuery, category, language);
                foreach (var res in webResults1)
                {
                    if (!knownUrls.Contains(res.Url.ToLowerInvariant()))
                    {
                        list.Add(res);
                        knownUrls.Add(res.Url.ToLowerInvariant());
                    }
                }
            }
            catch { }

            // 3. Search ace-stream.net Web
            try
            {
                var webResults2 = await SearchAceStreamNetWebAsync(cleanQuery, category, language);
                foreach (var res in webResults2)
                {
                    if (!knownUrls.Contains(res.Url.ToLowerInvariant()))
                    {
                        list.Add(res);
                        knownUrls.Add(res.Url.ToLowerInvariant());
                    }
                }
            }
            catch { }

            // Strict query and language filtering - filter out generic/fake placeholder items
            list = list.Where(x => 
                !string.IsNullOrWhiteSpace(x.Name) &&
                !x.Name.StartsWith("AceStream Content", StringComparison.OrdinalIgnoreCase) &&
                !x.Name.StartsWith("AceStream İçeriği", StringComparison.OrdinalIgnoreCase) &&
                ChannelUtils.MatchesQueryFilter(x.Name, x.Category, "", x.Url, cleanQuery) &&
                ChannelUtils.MatchesLanguageFilter(x.Name, language)
            ).ToList();

            return list.OrderByDescending(x => x.Peers.Contains("Yüksek") || x.Peers.Contains("Aktif")).ToList();
        }

        private string MapCategoryToAceApi(string cat)
        {
            if (string.IsNullOrWhiteSpace(cat) || cat.Contains("Tüm")) return "";
            string lower = cat.ToLowerInvariant();
            if (lower.Contains("film")) return "movies"; if (lower.Contains("dizi")) return "series";
            if (lower.Contains("tv")) return "tv"; if (lower.Contains("spor")) return "sport";
            return "";
        }

        private async Task SearchServerSideApiAsync(List<AceResult> list, string query, string category, int pageOffset)
        {
            string url = $"https://search.acestream.net/?method=search&api_version=1&api_key=test_api_key&page_size=250&page_offset={pageOffset}&group_by_channels=1";
            if (!string.IsNullOrEmpty(query)) url += $"&query={Uri.EscapeDataString(query)}";
            if (!string.IsNullOrEmpty(category)) url += $"&category={Uri.EscapeDataString(category)}";
            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode) ParseAceJsonResults(list, await response.Content.ReadAsStringAsync(), "AceStream Sunucu API");
        }

        private async Task SearchLocalEngineApiAsync(List<AceResult> list, string query, string category, int pageOffset)
        {
            if (!await IsEngineRunningAsync()) await StartEngineAsync();
            if (!await IsEngineRunningAsync()) return;
            string? token = await GetApiAccessTokenAsync();
            if (string.IsNullOrEmpty(token)) return;
            string url = $"http://127.0.0.1:{ACESTREAM_PORT}/server/api?method=search&token={token}&page_size=250&page_offset={pageOffset}";
            if (!string.IsNullOrEmpty(query)) url += $"&query={Uri.EscapeDataString(query)}";
            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode) ParseAceJsonResults(list, await response.Content.ReadAsStringAsync(), "AceStream Yerel Motor");
        }

        private async Task SearchServerSideAllSnapshotAsync(List<AceResult> list, string query, string category)
        {
            var response = await _httpClient.GetAsync("https://search.acestream.net/all?api_version=1&api_key=test_api_key");
            if (response.IsSuccessStatusCode) ParseAceJsonResults(list, await response.Content.ReadAsStringAsync(), "AceStream P2P Veritabanı");
        }

        private void ParseAceJsonResults(List<AceResult> list, string json, string sourceName)
        {
            try
            {
                dynamic? data = JsonConvert.DeserializeObject(json);
                var results = data?.result?.results ?? data?.result;
                if (results == null) return;
                foreach (var item in results)
                {
                    if (item.items != null) foreach (var sub in item.items) AddParsedAceItem(list, sub, (string)item.name, (string)item.icon, sourceName);
                    else AddParsedAceItem(list, item, "", "", sourceName);
                }
            }
            catch { }
        }

        private void AddParsedAceItem(List<AceResult> list, dynamic item, string fallbackName, string fallbackIcon, string sourceName)
        {
            string name = item.name ?? item.title ?? fallbackName;
            if (string.IsNullOrWhiteSpace(name) || name.Equals("AceStream", StringComparison.OrdinalIgnoreCase) || name.Equals("Download", StringComparison.OrdinalIgnoreCase)) return;
            string infohash = item.infohash ?? item.content_id ?? item.id ?? "";
            if (string.IsNullOrWhiteSpace(infohash)) return;
            string finalUrl = infohash.StartsWith("acestream://") ? infohash : $"acestream://{infohash}";
            if (list.Any(x => x.Url.Equals(finalUrl, StringComparison.OrdinalIgnoreCase))) return;
            list.Add(new AceResult { Name = name, Url = finalUrl, Peers = "Aktif P2P", SourceName = sourceName, Category = "P2P Stream", LogoUrl = item.icon ?? fallbackIcon ?? "" });
        }

        private async Task SearchWebIndexesAsync(List<AceResult> list, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return;
            string[] searchUrls = { $"https://search-ace.stream/?q={Uri.EscapeDataString(query)}", $"https://ace-stream.net/search?q={Uri.EscapeDataString(query)}" };
            foreach (var url in searchUrls)
            {
                try
                {
                    var html = await _httpClient.GetStringAsync(url);
                    var matches = Regex.Matches(html, @"(acestream://[a-f0-9]{40}|[a-f0-9]{40})[^>]*>(.*?)<");
                    foreach (Match m in matches)
                    {
                        string title = Regex.Replace(m.Groups[2].Value, "<.*?>", "").Trim();
                        if (string.IsNullOrWhiteSpace(title) || title.Length < 3) continue;

                        if (title.Equals("AceStream", StringComparison.OrdinalIgnoreCase) ||
                            title.Equals("Download", StringComparison.OrdinalIgnoreCase) ||
                            title.Equals("Play", StringComparison.OrdinalIgnoreCase) ||
                            title.Equals("Link", StringComparison.OrdinalIgnoreCase) ||
                            title.Contains("Ace Stream Search") ||
                            title.Contains("Index"))
                            continue;

                        if (!ChannelUtils.MatchesQueryFilter(title, "P2P Stream", "", "", query)) continue;

                        string finalUrl = m.Groups[1].Value.StartsWith("acestream://") ? m.Groups[1].Value : $"acestream://{m.Groups[1].Value}";
                        if (!list.Any(x => x.Url.Equals(finalUrl, StringComparison.OrdinalIgnoreCase)))
                        {
                            list.Add(new AceResult { Name = title, Url = finalUrl, SourceName = "P2P Web Index", Category = "P2P Stream" });
                        }
                    }
                } catch { }
            }
        }

        public async Task<bool> IsEngineRunningAsync()
        {
            try { var res = await _httpClient.GetAsync($"http://127.0.0.1:{ACESTREAM_PORT}/webui/api/service?method=get_version"); return res.IsSuccessStatusCode; }
            catch { return false; }
        }

        public static string GetEngineExecutablePath()
        {
            string[] paths = { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"ACEStream\engine\ace_engine.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"ACEStream\engine\ace_engine.exe"), @"C:\ACEStream\engine\ace_engine.exe" };
            return paths.FirstOrDefault(File.Exists) ?? "";
        }

        public bool IsInstalled() => !string.IsNullOrEmpty(GetEngineExecutablePath());

        public async Task<bool> DownloadAndExtractEngineAsync(Action<int>? progressCallback = null)
        {
            try
            {
                string dlUrl = "https://github.com/bilo1975tr/sm/releases/download/v1.0/AceStream.zip";
                string target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ACEStream");
                Directory.CreateDirectory(target);
                string temp = Path.Combine(Path.GetTempPath(), "AceStream_setup.zip");
                using var res = await _httpClient.GetAsync(dlUrl);
                using var fs = new FileStream(temp, FileMode.Create);
                await res.Content.CopyToAsync(fs);
                System.IO.Compression.ZipFile.ExtractToDirectory(temp, target, true);
                return IsInstalled();
            } catch { return false; }
        }

        public async Task StartEngineAsync()
        {
            if (await IsEngineRunningAsync()) return;
            string found = GetEngineExecutablePath();
            if (!string.IsNullOrEmpty(found))
            {
                Process.Start(new ProcessStartInfo { FileName = found, WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = true });
                for (int i = 0; i < 15; i++) { await Task.Delay(1000); if (await IsEngineRunningAsync()) return; }
            }
        }

        public List<string> GetHttpUrls(string cid)
        {
            var urls = new List<string>();
            if (string.IsNullOrWhiteSpace(cid)) return urls;

            string hash = ExtractHash(cid);
            if (string.IsNullOrEmpty(hash)) return urls;

            urls.Add($"http://127.0.0.1:{ACESTREAM_PORT}/ace/getstream?id={hash}");
            urls.Add($"http://127.0.0.1:{ACESTREAM_PORT}/ace/getstream?infohash={hash}");
            urls.Add($"http://127.0.0.1:{ACESTREAM_PORT}/ace/manifest.m3u8?id={hash}");
            return urls;
        }

        public string ExtractHash(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            // 1. Direct hex hash (40 chars)
            var match = Regex.Match(input, @"[a-fA-F0-9]{40}");
            if (match.Success) return match.Value.ToLowerInvariant();

            // 2. URL parameters (id=... or infohash=...)
            var idMatch = Regex.Match(input, @"[?&](?:id|infohash)=([a-fA-F0-9]{40})", RegexOptions.IgnoreCase);
            if (idMatch.Success) return idMatch.Groups[1].Value.ToLowerInvariant();

            return "";
        }

        public string GetHttpUrl(string cid)
        {
            var list = GetHttpUrls(cid);
            return list.Count > 0 ? list[0] : "";
        }
    }
}
