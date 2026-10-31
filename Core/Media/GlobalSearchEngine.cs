using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using StreamMesh.Models;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class SearchResultItem : INotifyPropertyChanged
    {
        private string _name = "";
        private string _url = "";
        private string _source = "";
        private string _category = "Canlı TV";
        private string _peersOrDetails = "";
        private string _logoUrl = "";
        private string _groupTitle = "";
        private bool _isAdded = false;
        private string _addButtonText = "➕ Kütüphaneye Ekle";

        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        public string Url { get => _url; set { _url = value; OnPropertyChanged(); } }
        public string Source { get => _source; set { _source = value; OnPropertyChanged(); } }
        public string Category { get => _category; set { _category = value; OnPropertyChanged(); } }
        public string PeersOrDetails { get => _peersOrDetails; set { _peersOrDetails = value; OnPropertyChanged(); } }
        public string LogoUrl { get => _logoUrl; set { _logoUrl = value; OnPropertyChanged(); } }
        public string GroupTitle { get => _groupTitle; set { _groupTitle = value; OnPropertyChanged(); } }
        public bool IsAdded { get => _isAdded; set { _isAdded = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanAdd)); } }
        public bool CanAdd => !_isAdded;
        public string AddButtonText { get => _addButtonText; set { _addButtonText = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }

    /// <summary>
    /// StreamMesh Global Harici Arama Motoru.
    /// Yerel veritabanı veya kütüphane TARANMAZ. Yalnızca harici internet kaynakları,
    /// açık IPTV havuzları ve AceStream motoru/web API'si taranır.
    /// </summary>
    public class GlobalSearchEngine
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private readonly M3uEngine _m3uEngine = new M3uEngine();

        // In-memory thread-safe caches for public external catalogs to make repeated searches instant
        private static List<Channel>? _cachedFreeTuxChannels;
        private static DateTime _freeTuxCacheTime = DateTime.MinValue;

        private static List<Channel>? _cachedIptvOrgChannels;
        private static DateTime _iptvOrgCacheTime = DateTime.MinValue;

        private static List<Channel>? _cachedFreeTvChannels;
        private static DateTime _freeTvCacheTime = DateTime.MinValue;

        private static readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(2);

        static GlobalSearchEngine()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml,application/json,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");
        }

        public async Task<List<SearchResultItem>> SearchAsync(string query)
        {
            return await SearchGlobalAsync(query);
        }

        public async Task<bool> StartAceEngineAsync()
        {
            return await AceEngine.Instance.StartEngineIfNotRunningAsync();
        }

        public async Task<bool> StartEngineAsync()
        {
            return await AceEngine.Instance.StartEngineIfNotRunningAsync();
        }

        /// <summary>
        /// Harici internet kaynaklarında yayın arar.
        /// Desteklenen filtreler: Tümü, AceStream P2P, AceStream (me), AceStream (org), AceStream (net), IPTVCat, FreeTuxTV, Free-TV
        /// </summary>
        public async Task<List<SearchResultItem>> SearchGlobalAsync(
            string query,
            string sourceFilter = "Tümü",
            string categoryFilter = "Tüm Kategoriler",
            string languageFilter = "Tüm Diller")
        {
            string cleanQuery = (query ?? "").Trim();
            if (string.IsNullOrWhiteSpace(cleanQuery))
            {
                return new List<SearchResultItem>();
            }

            string searchKeyword = cleanQuery.ToLowerInvariant();
            var results = new ConcurrentBag<SearchResultItem>();

            string filter = (sourceFilter ?? "").Trim();
            bool runAll = string.IsNullOrEmpty(filter) || filter.Equals("Tümü", StringComparison.OrdinalIgnoreCase) || filter.Contains("Tüm");

            bool runAceP2p = runAll || filter.Contains("P2P");
            bool runAceMe = runAll || filter.Contains("(me)");
            bool runAceOrg = runAll || filter.Contains("(org)");
            bool runAceNet = runAll || filter.Contains("(net)");
            bool runIptvCat = runAll || filter.Contains("IPTVCat");
            bool runFreeTux = runAll || filter.Contains("FreeTux");
            bool runFreeTv = runAll || filter.Contains("Free-TV");

            var tasks = new List<Task>();

            // 1. AceStream Yerel P2P Motor Arama Servisi (127.0.0.1:6878)
            if (runAceP2p)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var items = await AceEngine.Instance.SearchAceStreamP2pAsync(cleanQuery);
                        foreach (var itm in items) results.Add(itm);
                    }
                    catch (Exception ex)
                    {
                        LogService.LogWarning($"[GlobalSearch] AceStream P2P Search hatası: {ex.Message}");
                    }
                }));
            }

            // 2. AceStream (me) API
            if (runAceMe)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var items = await AceEngine.Instance.SearchAceStreamWebAsync(cleanQuery, "me");
                        foreach (var itm in items) results.Add(itm);
                    }
                    catch (Exception ex)
                    {
                        LogService.LogWarning($"[GlobalSearch] AceStream (me) Search hatası: {ex.Message}");
                    }
                }));
            }

            // 3. AceStream (org) API
            if (runAceOrg)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var items = await AceEngine.Instance.SearchAceStreamWebAsync(cleanQuery, "org");
                        foreach (var itm in items) results.Add(itm);
                    }
                    catch (Exception ex)
                    {
                        LogService.LogWarning($"[GlobalSearch] AceStream (org) Search hatası: {ex.Message}");
                    }
                }));
            }

            // 4. AceStream (net) API
            if (runAceNet)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var items = await AceEngine.Instance.SearchAceStreamWebAsync(cleanQuery, "net");
                        foreach (var itm in items) results.Add(itm);
                    }
                    catch (Exception ex)
                    {
                        LogService.LogWarning($"[GlobalSearch] AceStream (net) Search hatası: {ex.Message}");
                    }
                }));
            }

            // 5. IPTVCat Web Arama Servisi
            if (runIptvCat)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var items = await SearchIptvCatAsync(searchKeyword);
                        foreach (var itm in items) results.Add(itm);
                    }
                    catch (Exception ex)
                    {
                        LogService.LogWarning($"[GlobalSearch] IPTVCat Search hatası: {ex.Message}");
                    }
                }));
            }

            // 6. FreeTuxTV Web Veritabanı ve Havuz Arama Servisi
            if (runFreeTux)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var items = await SearchFreeTuxTvWebAsync(searchKeyword);
                        foreach (var itm in items) results.Add(itm);
                    }
                    catch (Exception ex)
                    {
                        LogService.LogWarning($"[GlobalSearch] FreeTuxTV Web Search hatası: {ex.Message}");
                    }
                }));

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var items = await SearchFreeTuxTvM3uAsync(searchKeyword);
                        foreach (var itm in items) results.Add(itm);
                    }
                    catch (Exception ex)
                    {
                        LogService.LogWarning($"[GlobalSearch] FreeTuxTV M3U Pool hatası: {ex.Message}");
                    }
                }));
            }

            // 7. Free-TV Açık IPTV Veritabanı
            if (runFreeTv)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var items = await SearchFreeTvAsync(searchKeyword);
                        foreach (var itm in items) results.Add(itm);
                    }
                    catch (Exception ex)
                    {
                        LogService.LogWarning($"[GlobalSearch] Free-TV Search hatası: {ex.Message}");
                    }
                }));
            }

            // Tüm paralel harici aramaları bekle
            await Task.WhenAll(tasks);

            // Sonuçları temizle, doğrula ve ayıkla
            var finalResults = results
                .Where(x => !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.Url) && IsValidStreamUrl(x.Url))
                .Where(x => ChannelUtils.MatchesQueryFilter(x.Name, x.Category, x.GroupTitle, x.Url, cleanQuery, sourceType: x.Source))
                .ToList();

            // Dil filtresi uygulanmışsa
            if (!string.IsNullOrWhiteSpace(languageFilter) && !languageFilter.Contains("Tüm"))
            {
                finalResults = finalResults.Where(x => ChannelUtils.MatchesLanguageFilter(x.Name, languageFilter)).ToList();
            }

            // Kategori filtresi uygulanmışsa
            if (!string.IsNullOrWhiteSpace(categoryFilter) && !categoryFilter.Contains("Tüm"))
            {
                string catLower = categoryFilter.ToLowerInvariant();
                finalResults = finalResults.Where(x => 
                    x.Category.ToLowerInvariant().Contains(catLower) || 
                    x.GroupTitle.ToLowerInvariant().Contains(catLower) ||
                    x.Name.ToLowerInvariant().Contains(catLower)
                ).ToList();
            }

            // URL tekilleştirme (aynı akış adreslerini birleştir)
            return finalResults
                .GroupBy(x => NormalizeStreamUrl(x.Url))
                .Select(g => g.First())
                .OrderByDescending(x => x.Source.Contains("AceStream") ? 2 : 1)
                .ThenBy(x => x.Name)
                .ToList();
        }

        private static bool IsValidStreamUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            string u = url.Trim().ToLowerInvariant();
            if (u.StartsWith("acestream://") || u.Contains(":6878/ace/") || u.Contains(":6878/content_id/"))
            {
                // Sadece ve sadece 40 haneli geçerli hex Content ID barındıran AceStream linkleri geçerlidir!
                return Regex.IsMatch(u, @"\b[a-f0-9]{40}\b");
            }
            if (u.Contains(".m3u8") || u.Contains(".ts") || u.Contains(".mpd") || u.Contains(".mp4")) return true;
            if (u.Contains("/live/") || u.Contains("get_stream") || u.Contains("playlist") || u.Contains("/stream/") || u.Contains("chunk")) return true;
            if (u.StartsWith("http://") || u.StartsWith("https://")) return true;
            return false;
        }

        private static string NormalizeStreamUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            string trimmed = url.Trim();
            int queryIndex = trimmed.IndexOf('?');
            if (queryIndex > 0 && !trimmed.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring(0, queryIndex).ToLowerInvariant();
            }
            return trimmed.ToLowerInvariant();
        }

        private async Task<List<SearchResultItem>> SearchIptvCatAsync(string query)
        {
            var list = new List<SearchResultItem>();
            string encoded = Uri.EscapeDataString(query);
            string targetUrl = $"https://iptvcat.com/s/{encoded}?state=working";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, targetUrl);
                var resp = await _httpClient.SendAsync(req, cts.Token);
                if (!resp.IsSuccessStatusCode) return list;

                var html = await resp.Content.ReadAsStringAsync();
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
                        if (IsValidStreamUrl(link) && !list.Any(x => x.Url == link) && ChannelUtils.MatchesQueryFilter(title, "Canlı TV", "Çalışan", link, query))
                        {
                            list.Add(new SearchResultItem
                            {
                                Name = title,
                                Url = link,
                                Source = "IPTVCat Arama Motoru",
                                Category = "Canlı TV",
                                GroupTitle = "IPTVCat",
                                PeersOrDetails = "Durum: Çalışan Akış"
                            });
                        }
                    }
                    if (list.Count >= 500) break;
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[GlobalSearch] IPTVCat sorgu hatası: {ex.Message}");
            }

            return list;
        }

        private async Task<List<SearchResultItem>> SearchFreeTuxTvWebAsync(string queryLower)
        {
            var list = new List<SearchResultItem>();
            if (string.IsNullOrWhiteSpace(queryLower)) return list;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            try
            {
                string encoded = Uri.EscapeDataString(queryLower);
                string searchUrl = $"https://database.freetuxtv.net/WebStream/index?WebStreamSearchForm%5BName%5D={encoded}&WebStreamSearchForm%5BEditPending%5D=0&yt0=Search";
                using var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                var resp = await _httpClient.SendAsync(req, cts.Token);
                if (!resp.IsSuccessStatusCode) return list;

                string html = await resp.Content.ReadAsStringAsync();
                var rowMatches = Regex.Matches(html, @"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

                foreach (Match row in rowMatches)
                {
                    string rowHtml = row.Groups[1].Value;
                    if (!rowHtml.Contains("http") && !rowHtml.Contains("m3u8") && !rowHtml.Contains("=>") && !rowHtml.Contains("=&gt;")) continue;
                    if (rowHtml.Contains("Dead") || rowHtml.Contains("Invalid")) continue;

                    string name = "";
                    string streamUrl = "";

                    var linkMatch = Regex.Match(rowHtml, @"(?:=>|=&gt;)\s*<a[^>]*href=[""']?([^""'>]+)[""']?[^>]*>(.*?)</a>", RegexOptions.IgnoreCase);
                    if (!linkMatch.Success)
                    {
                        linkMatch = Regex.Match(rowHtml, @"(?:=>|=&gt;)\s*(https?://[^\s<]+)", RegexOptions.IgnoreCase);
                    }
                    if (linkMatch.Success)
                    {
                        streamUrl = linkMatch.Groups[1].Value.Trim();
                    }

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
                                PeersOrDetails = "Web Kaynağı"
                            });
                        }
                    }
                    if (list.Count >= 500) break;
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[GlobalSearch] FreeTuxTV Web arama uyarısı: {ex.Message}");
            }

            return list;
        }

        private async Task<List<SearchResultItem>> SearchFreeTuxTvM3uAsync(string queryLower)
        {
            var list = new List<SearchResultItem>();
            try
            {
                if (_cachedFreeTuxChannels == null || (DateTime.Now - _freeTuxCacheTime) > CacheTtl)
                {
                    await _cacheLock.WaitAsync();
                    try
                    {
                        if (_cachedFreeTuxChannels == null || (DateTime.Now - _freeTuxCacheTime) > CacheTtl)
                        {
                            string m3uUrl = "https://raw.githubusercontent.com/FreeTuxTV/FreeTuxTV/master/m3u/free_tv.m3u";
                            _cachedFreeTuxChannels = await _m3uEngine.ParseM3uAsync(m3uUrl, "FreeTuxTV");
                            _freeTuxCacheTime = DateTime.Now;
                        }
                    }
                    finally
                    {
                        _cacheLock.Release();
                    }
                }

                if (_cachedFreeTuxChannels != null)
                {
                    foreach (var ch in _cachedFreeTuxChannels)
                    {
                        if (ChannelUtils.MatchesQueryFilter(ch.Name, ch.Category ?? "", ch.GroupTitle ?? "", ch.Url ?? "", queryLower))
                        {
                            if (!list.Any(x => x.Url == ch.Url))
                            {
                                list.Add(new SearchResultItem
                                {
                                    Name = ch.Name,
                                    Url = ch.Url ?? "",
                                    Source = "FreeTuxTV Havuzu",
                                    Category = string.IsNullOrWhiteSpace(ch.Category) ? "Canlı TV" : ch.Category,
                                    GroupTitle = ch.GroupTitle ?? "FreeTuxTV",
                                    LogoUrl = ch.LogoUrl ?? "",
                                    PeersOrDetails = "Açık Liste"
                                });
                            }
                            if (list.Count >= 500) break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[GlobalSearch] FreeTuxTV M3U havuz uyarısı: {ex.Message}");
            }

            return list;
        }

        private async Task<List<SearchResultItem>> SearchIptvOrgAsync(string queryLower, string categoryFilter, string languageFilter)
        {
            var list = new List<SearchResultItem>();
            try
            {
                if (_cachedIptvOrgChannels == null || (DateTime.Now - _iptvOrgCacheTime) > CacheTtl)
                {
                    await _cacheLock.WaitAsync();
                    try
                    {
                        if (_cachedIptvOrgChannels == null || (DateTime.Now - _iptvOrgCacheTime) > CacheTtl)
                        {
                            string url = "https://iptv-org.github.io/iptv/index.m3u";
                            _cachedIptvOrgChannels = await _m3uEngine.ParseM3uAsync(url, "IPTV-Org");
                            _iptvOrgCacheTime = DateTime.Now;
                        }
                    }
                    finally
                    {
                        _cacheLock.Release();
                    }
                }

                if (_cachedIptvOrgChannels != null)
                {
                    foreach (var ch in _cachedIptvOrgChannels)
                    {
                        if (ChannelUtils.MatchesQueryFilter(ch.Name, ch.Category ?? "", ch.GroupTitle ?? "", ch.Url ?? "", queryLower))
                        {
                            if (!list.Any(x => x.Url == ch.Url))
                            {
                                list.Add(new SearchResultItem
                                {
                                    Name = ch.Name,
                                    Url = ch.Url ?? "",
                                    Source = "IPTV-Org Global",
                                    Category = string.IsNullOrWhiteSpace(ch.Category) ? "Global TV" : ch.Category,
                                    GroupTitle = ch.GroupTitle ?? "IPTV-Org",
                                    LogoUrl = ch.LogoUrl ?? "",
                                    PeersOrDetails = "Küresel Açık Yayın"
                                });
                            }
                            if (list.Count >= 500) break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[GlobalSearch] IPTV-Org M3U arama hatası: {ex.Message}");
            }

            return list;
        }

        private async Task<List<SearchResultItem>> SearchFreeTvAsync(string queryLower)
        {
            var list = new List<SearchResultItem>();
            try
            {
                if (_cachedFreeTvChannels == null || (DateTime.Now - _freeTvCacheTime) > CacheTtl)
                {
                    await _cacheLock.WaitAsync();
                    try
                    {
                        if (_cachedFreeTvChannels == null || (DateTime.Now - _freeTvCacheTime) > CacheTtl)
                        {
                            string url = "https://raw.githubusercontent.com/Free-TV/IPTV/master/playlist.m3u8";
                            _cachedFreeTvChannels = await _m3uEngine.ParseM3uAsync(url, "Free-TV");
                            _freeTvCacheTime = DateTime.Now;
                        }
                    }
                    finally
                    {
                        _cacheLock.Release();
                    }
                }

                if (_cachedFreeTvChannels != null)
                {
                    foreach (var ch in _cachedFreeTvChannels)
                    {
                        if (ChannelUtils.MatchesQueryFilter(ch.Name, ch.Category ?? "", ch.GroupTitle ?? "", ch.Url ?? "", queryLower))
                        {
                            if (!list.Any(x => x.Url == ch.Url))
                            {
                                list.Add(new SearchResultItem
                                {
                                    Name = ch.Name,
                                    Url = ch.Url ?? "",
                                    Source = "Free-TV Kataloğu",
                                    Category = string.IsNullOrWhiteSpace(ch.Category) ? "Canlı TV" : ch.Category,
                                    GroupTitle = ch.GroupTitle ?? "Free-TV",
                                    LogoUrl = ch.LogoUrl ?? "",
                                    PeersOrDetails = "Açık Yayın"
                                });
                            }
                            if (list.Count >= 500) break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[GlobalSearch] Free-TV arama hatası: {ex.Message}");
            }

            return list;
        }
    }
}
