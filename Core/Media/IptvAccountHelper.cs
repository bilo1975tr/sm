using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StreamMesh.Core.Database;
using StreamMesh.Core.Network;
using StreamMesh.Core.Utils;
using StreamMesh.Models;

namespace StreamMesh.Core.Media
{
    public class IptvAccountInfo
    {
        public string HostWithPort { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string SignatureKey => $"{HostWithPort}/{Username}/{Password}".ToLowerInvariant();
        public string DisplaySummary => $"{Username} @ {HostWithPort}";

        public static IptvAccountInfo FromAccount(IptvAccount acc)
        {
            if (acc == null) return new IptvAccountInfo();
            return new IptvAccountInfo
            {
                HostWithPort = IptvAccountHelper.ExtractHostAndPort(acc.ServerUrl),
                Username = acc.Username ?? "",
                Password = acc.Password ?? ""
            };
        }
    }

    public static class IptvAccountHelper
    {
        public static string ExtractHostAndPort(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            string s = url.Trim();
            if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                s = "http://" + s;
            }
            if (Uri.TryCreate(s, UriKind.Absolute, out var uri))
            {
                return uri.Authority;
            }
            return url.Replace("http://", "").Replace("https://", "").TrimEnd('/');
        }
        // Standart Xtream Codes ve IPTV URL kalıpları:
        // http(s)://host:port/live/username/password/123.ts
        // http(s)://host:port/movie/username/password/123.mp4
        // http(s)://host:port/series/username/password/123.mkv
        // http(s)://host:port/get.php?username=...&password=...
        // http(s)://host:port/player_api.php?username=...&password=...
        private static readonly Regex XtreamPathRegex = new Regex(
            @"https?://(?<host>[^/]+)/(?:(?<type>live|movie|series|play|stream|hls|ch|channel|tv)/)?(?<user>[a-zA-Z0-9_\-\.]+)/(?<pass>[a-zA-Z0-9_\-\.]+)/(?<id>[^/\s\?#]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex XtreamQueryRegex = new Regex(
            @"https?://(?<host>[^/]+)/.*?[?&](?:username|u)=(?<user>[^&#\s]+)&(?:password|p)=(?<pass>[^&#\s]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Bilinen resmi CDN, açık playlist depoları ve web siteleri (Asla IPTV hesabı olarak algılanmamalıdır)
        private static readonly string[] NonIptvHosts = new[]
        {
            "duhnet.tv", "kanaldvod", "puhutv", "dogusdigital", "akamaized.net",
            "cloudfront.net", "fastly.net", "youtube.com", "googlevideo.com",
            "dailymotion.com", "vimeo.com", "githubusercontent.com", "github.com",
            "gitlab.com", "bitbucket.org", "pastebin.com", "onureroz.com",
            "blogspot.com", "wordpress.com", "rawgit.com", "gitee.com",
            "turknet", "ercdn", "mediatriple", "artiyerelmedya", "streamlock",
            "socialsmart", "bozztv", "qaotic", "mdn.ors", "huberwebmedia",
            "mncdn", "twimg", "beltelecom", "balkanweb", "fulltvizle", "tvradiozap",
            "stream.taksimbilisim", "prepublish", "mediapackage", "sobreatsesuyp.com",
            "diziyou.one", "mediafire.com", "radioking.com", "streamingwebtv24.it"
        };

        private static readonly HashSet<string> NonIptvTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hls_vod", "vod", "videos", "video", "segments", "stream", "live", "static", "media", "assets", "hls",
            "master", "main", "refs", "heads", "blob", "raw", "playlist", "channels", "m3u", "m3u8",
            "bant1", "tracks", "smil", "atv", "showtv", "haberturktv", "nowtv", "teve2", "tv8", "aspor", "a2tv",
            "streams", "v1", "105", "livestream", "tools", "fox", "css", "js", "images", "img",
            "movie", "movies", "episode", "episodes", "film", "filmler", "series", "dizi", "diziler",
            "trailer", "watch", "player", "play", "share", "embed", "content", "download", "token", "auth"
        };

        // Başarısız olan ya da IPTV olmayan hesap/host adresleri için hızlı önbellek (Ignore / Kara Liste)
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _failedAccountsCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _ignoredHostsCache = new(StringComparer.OrdinalIgnoreCase);

        public static bool IsIgnoredHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return true;
            string h = ExtractHostAndPort(host).ToLowerInvariant();
            if (_ignoredHostsCache.ContainsKey(h)) return true;
            foreach (var nh in NonIptvHosts)
            {
                if (h.Contains(nh, StringComparison.OrdinalIgnoreCase))
                {
                    _ignoredHostsCache.TryAdd(h, 1);
                    return true;
                }
            }
            return false;
        }

        public static bool IsFailedAccount(IptvAccountInfo account)
        {
            if (account == null) return true;
            return _failedAccountsCache.ContainsKey(account.SignatureKey) || IsIgnoredHost(account.HostWithPort);
        }

        public static void MarkAccountFailed(IptvAccountInfo account, bool markHostFailed = false)
        {
            if (account == null) return;
            _failedAccountsCache.TryAdd(account.SignatureKey, 1);
            if (markHostFailed && !string.IsNullOrWhiteSpace(account.HostWithPort))
            {
                _ignoredHostsCache.TryAdd(ExtractHostAndPort(account.HostWithPort).ToLowerInvariant(), 1);
            }
        }

        /// <summary>
        /// Belirtilen sunucunun gerçekten bir Xtream / IPTV sunucusu olup olmadığını tek bir hızlı istek ile test eder.
        /// Sunucu HTML web sayfası dönüyorsa, 404 veriyorsa veya ulaşılamıyorsa host kara listeye alınır.
        /// Sunucu JSON ile Xtream yanıtı veriyorsa (auth:0 olsa bile) host geçerli bir IPTV sunucusudur.
        /// </summary>
        public static async Task<bool> FastCheckHostIsIptvAsync(IptvAccountInfo account, int timeoutMs = 1500)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.HostWithPort))
                return false;

            string host = ExtractHostAndPort(account.HostWithPort).ToLowerInvariant();
            if (IsIgnoredHost(host)) return false;

            string scheme = account.HostWithPort.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || 
                            account.HostWithPort.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "" : "http://";
            string hostTrimmed = account.HostWithPort.TrimEnd('/');

            // 1. player_api.php testi
            try
            {
                string apiUrl = $"{scheme}{hostTrimmed}/player_api.php?username={Uri.EscapeDataString(account.Username)}&password={Uri.EscapeDataString(account.Password)}";
                using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
                using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                req.Headers.TryAddWithoutValidation("User-Agent", "IPTVSmartersPro/1.0.0 (Linux;Android 11) StreamMesh/1.0");

                using var resp = await MediaHttpClient.Client.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        string trimmed = body.TrimStart();
                        // Xtream JSON dönerse (auth:1 veya auth:0 veya user_info) bu kesinlikle bir IPTV sunucusudur
                        if (trimmed.StartsWith("{") && (body.Contains("\"user_info\"") || body.Contains("\"auth\"")))
                        {
                            return true;
                        }
                        // HTML web sayfası dönerse kesinlikle IPTV sunucusu değildir
                        if (trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
                            body.Contains("<body", StringComparison.OrdinalIgnoreCase))
                        {
                            _ignoredHostsCache.TryAdd(host, 1);
                            return false;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _ignoredHostsCache.TryAdd(host, 1);
                return false;
            }
            catch (HttpRequestException)
            {
                _ignoredHostsCache.TryAdd(host, 1);
                return false;
            }
            catch { }

            // 2. get.php testi (M3U başlığı kontrolü)
            try
            {
                string m3uUrl = $"{scheme}{hostTrimmed}/get.php?username={Uri.EscapeDataString(account.Username)}&password={Uri.EscapeDataString(account.Password)}&type=m3u_plus";
                using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
                using var req = new HttpRequestMessage(HttpMethod.Get, m3uUrl);
                req.Headers.TryAddWithoutValidation("User-Agent", "IPTVSmartersPro/1.0.0 (Linux;Android 11) StreamMesh/1.0");

                using var resp = await MediaHttpClient.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    byte[] buf = new byte[512];
                    int read = await stream.ReadAsync(buf, 0, buf.Length, cts.Token).ConfigureAwait(false);
                    if (read > 0)
                    {
                        string snippet = System.Text.Encoding.UTF8.GetString(buf, 0, read);
                        if (snippet.Contains("#EXTM3U") || snippet.Contains("#EXTINF"))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }

            _ignoredHostsCache.TryAdd(host, 1);
            return false;
        }

        public static async Task<bool> FastVerifyAccountAsync(IptvAccountInfo account, int timeoutMs = 1500)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.Username) || string.IsNullOrWhiteSpace(account.Password))
                return false;

            if (IsFailedAccount(account)) return false;

            string scheme = account.HostWithPort.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || 
                            account.HostWithPort.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "" : "http://";
            string host = account.HostWithPort.TrimEnd('/');

            bool hostUnreachable = false;
            bool isNotIptv = false;

            // 1. player_api.php testi (en hızlı ve kesin Xtream IPTV kontrolü)
            try
            {
                string apiUrl = $"{scheme}{host}/player_api.php?username={Uri.EscapeDataString(account.Username)}&password={Uri.EscapeDataString(account.Password)}";
                using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
                using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                req.Headers.TryAddWithoutValidation("User-Agent", "IPTVSmartersPro/1.0.0 (Linux;Android 11) StreamMesh/1.0");

                using var resp = await MediaHttpClient.Client.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        string trimmed = body.TrimStart();
                        if (trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
                        {
                            isNotIptv = true;
                        }
                        else if (trimmed.StartsWith("{") && body.Contains("\"user_info\""))
                        {
                            // auth: 1 veya status: "Active"
                            if (body.Contains("\"auth\":1") || body.Contains("\"auth\": 1") || body.Contains("\"status\":\"Active\""))
                            {
                                return true;
                            }
                            else
                            {
                                // Xtream IPTV sunucusu aktif ancak bu hesap bilgileri hatalı veya süresi bitmiş (auth: 0)
                                // Host geçerli bir IPTV sunucusu olduğundan host'u kara listeye ALMIYORUZ!
                                MarkAccountFailed(account, markHostFailed: false);
                                return false;
                            }
                        }
                    }
                }
                else if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    isNotIptv = true;
                }
            }
            catch (OperationCanceledException)
            {
                hostUnreachable = true;
            }
            catch (HttpRequestException)
            {
                hostUnreachable = true;
            }
            catch { }

            // 2. get.php testi (IPTVSmartersPro User-Agent ile)
            if (!hostUnreachable && !isNotIptv)
            {
                try
                {
                    string m3uUrl = $"{scheme}{host}/get.php?username={Uri.EscapeDataString(account.Username)}&password={Uri.EscapeDataString(account.Password)}&type=m3u_plus";
                    using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
                    using var req = new HttpRequestMessage(HttpMethod.Get, m3uUrl);
                    req.Headers.TryAddWithoutValidation("User-Agent", "IPTVSmartersPro/1.0.0 (Linux;Android 11) StreamMesh/1.0");

                    using var resp = await MediaHttpClient.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                        byte[] buf = new byte[512];
                        int read = await stream.ReadAsync(buf, 0, buf.Length, cts.Token).ConfigureAwait(false);
                        if (read > 0)
                        {
                            string snippet = System.Text.Encoding.UTF8.GetString(buf, 0, read);
                            if (snippet.Contains("#EXTM3U") || snippet.Contains("#EXTINF"))
                            {
                                return true;
                            }
                        }
                    }
                    else if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        isNotIptv = true;
                    }
                }
                catch { }
            }

            // Başarısız veya ulaşılamaz ise kara listeye al
            MarkAccountFailed(account, markHostFailed: hostUnreachable || isNotIptv);
            return false;
        }

        public static IptvAccountInfo? ParseAccountFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            try
            {
                // Statik web dosyalarını ele
                string pathOnly = url.Split('?')[0];
                int dotIdx = pathOnly.LastIndexOf('.');
                if (dotIdx > 0 && dotIdx < pathOnly.Length - 1)
                {
                    string ext = pathOnly.Substring(dotIdx).ToLowerInvariant();
                    if (ext == ".jpg" || ext == ".png" || ext == ".jpeg" || ext == ".gif" || ext == ".svg" || ext == ".css" || ext == ".js" || ext == ".html")
                    {
                        return null;
                    }
                }

                // CDN veya bilinen web servislerini hemen ele
                foreach (var nonHost in NonIptvHosts)
                {
                    if (url.Contains(nonHost, StringComparison.OrdinalIgnoreCase)) return null;
                }

                var match = XtreamPathRegex.Match(url);
                if (match.Success)
                {
                    string host = match.Groups["host"].Value;
                    string user = match.Groups["user"].Value;
                    string pass = match.Groups["pass"].Value;

                    if (!user.Contains(".") && user.Length >= 2 && pass.Length >= 2 &&
                        !NonIptvTokens.Contains(user) && !NonIptvTokens.Contains(pass) &&
                        !(user.StartsWith("S", StringComparison.OrdinalIgnoreCase) && char.IsDigit(user.Length > 1 ? user[1] : 'x')))
                    {
                        var info = new IptvAccountInfo
                        {
                            HostWithPort = host,
                            Username = user,
                            Password = pass
                        };
                        if (!IsFailedAccount(info))
                        {
                            return info;
                        }
                    }
                }

                var qMatch = XtreamQueryRegex.Match(url);
                if (qMatch.Success)
                {
                    string host = qMatch.Groups["host"].Value;
                    string user = Uri.UnescapeDataString(qMatch.Groups["user"].Value);
                    string pass = Uri.UnescapeDataString(qMatch.Groups["pass"].Value);

                    if (user.Length >= 2 && pass.Length >= 2 &&
                        !NonIptvTokens.Contains(user) && !NonIptvTokens.Contains(pass))
                    {
                        var info = new IptvAccountInfo
                        {
                            HostWithPort = host,
                            Username = user,
                            Password = pass
                        };
                        if (!IsFailedAccount(info))
                        {
                            return info;
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        public static bool UrlBelongsToAccount(string url, IptvAccountInfo account)
        {
            if (string.IsNullOrWhiteSpace(url) || account == null) return false;

            if (!url.Contains(account.HostWithPort, StringComparison.OrdinalIgnoreCase)) return false;
            if (!url.Contains(account.Username, StringComparison.OrdinalIgnoreCase)) return false;

            if (!string.IsNullOrEmpty(account.Password) && url.Contains(account.Password, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Url'den parse ederek signature kontrolü
            var parsed = ParseAccountFromUrl(url);
            if (parsed != null && parsed.SignatureKey == account.SignatureKey)
            {
                return true;
            }

            return false;
        }

        public static bool ChannelBelongsToAccount(Channel? channel, IptvAccountInfo? account)
        {
            if (channel == null || account == null) return false;

            string pUrl = (channel.PlaylistUrl ?? "").Trim();
            if (!string.IsNullOrEmpty(pUrl) && pUrl.Contains(account.HostWithPort, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(account.Username) || pUrl.Contains(account.Username, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            foreach (var u in channel.GetUrlList())
            {
                if (UrlBelongsToAccount(u, account))
                {
                    return true;
                }
            }
            return false;
        }

        public class PurgeResult
        {
            public int ChannelsDeletedEntirely { get; set; }
            public int ChannelsModifiedSourcesRemoved { get; set; }
            public int TotalUrlsRemoved { get; set; }
            public int SourcesRemovedFromM3uSources { get; set; }
        }

        /// <summary>
        /// Belirtilen IPTV hesabına ait tüm URL'leri veritabanındaki tüm kanallardan topluca siler.
        /// Başka çalışan URL'leri olan kanallar güncellenir, tek kaynağı bu hesap olan kanallar veritabanından tamamen silinir.
        /// </summary>
        public static async Task<PurgeResult> PurgeAccountFromDatabaseAsync(IptvAccountInfo account, List<Channel>? sourceChannels = null)
        {
            var result = new PurgeResult();
            if (account == null) return result;

            var db = new DatabaseEngine();
            var allChannels = sourceChannels ?? await db.GetChannelsByAccountAsync(account.HostWithPort, account.Username);

            var channelsToDelete = new List<string>();
            var channelsToUpdate = new List<Channel>();

            foreach (var ch in allChannels)
            {
                var urls = ch.GetUrlList();
                if (urls.Count == 0) continue;

                var matchingUrls = urls.Where(u => UrlBelongsToAccount(u, account)).ToList();
                if (matchingUrls.Count == 0) continue;

                result.TotalUrlsRemoved += matchingUrls.Count;
                var remainingUrls = urls.Where(u => !UrlBelongsToAccount(u, account)).ToList();

                if (remainingUrls.Count == 0)
                {
                    // Kanalın başka hiçbir kaynağı kalmadı, kanalı tamamen sil
                    channelsToDelete.Add(ch.Id);
                    result.ChannelsDeletedEntirely++;
                }
                else
                {
                    // Kanalın diğer kaynakları var, sadece ölü hesap linklerini temizle
                    ch.Url = string.Join(",", remainingUrls);
                    // PreferredUrlIndex sınırını kontrol et
                    if (ch.PreferredUrlIndex >= remainingUrls.Count)
                    {
                        ch.PreferredUrlIndex = 0;
                    }
                    channelsToUpdate.Add(ch);
                    result.ChannelsModifiedSourcesRemoved++;
                }
            }

            // Silme işlemi
            if (channelsToDelete.Count > 0)
            {
                await db.DeleteChannelsAsync(channelsToDelete);
                LogService.LogInfo($"[IptvAccountHelper] Deleted {channelsToDelete.Count} channels that had only this account.");
            }

            // Güncelleme işlemi
            if (channelsToUpdate.Count > 0)
            {
                await db.SaveChannelsBatchAsync(channelsToUpdate, clearFirst: false, notifyUpdated: false);
                LogService.LogInfo($"[IptvAccountHelper] Updated {channelsToUpdate.Count} channels removing dead URLs.");
            }

            // Ayrıca M3U Kaynakları listesinde bu hesaba ait M3U URL'i varsa onu da kaldır
            try
            {
                var m3uSources = db.GetM3uSources();
                foreach (var src in m3uSources)
                {
                    if (UrlBelongsToAccount(src, account))
                    {
                        db.RemoveM3uSource(src);
                        result.SourcesRemovedFromM3uSources++;
                    }
                }
            }
            catch { }

            // IptvAccounts tablosunda bu hesap kayıtlıysa kaldır
            try
            {
                var iptvAccs = db.GetAllIptvAccounts();
                foreach (var acc in iptvAccs)
                {
                    if (acc.Username.Equals(account.Username, StringComparison.OrdinalIgnoreCase) &&
                        acc.ServerUrl.Contains(account.HostWithPort, StringComparison.OrdinalIgnoreCase))
                    {
                        db.RemoveIptvAccount(acc.Id);
                    }
                }
            }
            catch { }

            return result;
        }

        /// <summary>
        /// Veritabanında belirtilen hesaba ait kaç kanal/url bulunduğunu sayar.
        /// </summary>
        public static async Task<(int totalChannels, int totalUrls, int exclusiveChannels)> CountAccountOccurrencesAsync(IptvAccountInfo account)
        {
            if (account == null) return (0, 0, 0);

            var db = new DatabaseEngine();
            int total = await db.CountChannelsByAccountAsync(account.HostWithPort, account.Username).ConfigureAwait(false);
            return (total, total, total);
        }

        /// <summary>
        /// Xtream / IPTV hesabından sağlayıcının sunduğu tüm güncel kanalları içeren tam M3U Plus URL'sini üretir.
        /// </summary>
        public static string GenerateFullM3uUrl(IptvAccountInfo account, string? outputFormat = null)
        {
            if (account == null) return string.Empty;
            string scheme = account.HostWithPort.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || 
                            account.HostWithPort.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "" : "http://";
            string host = account.HostWithPort.TrimEnd('/');
            string baseQuery = $"{scheme}{host}/get.php?username={Uri.EscapeDataString(account.Username)}&password={Uri.EscapeDataString(account.Password)}&type=m3u_plus";
            if (!string.IsNullOrEmpty(outputFormat))
            {
                return $"{baseQuery}&output={outputFormat}";
            }
            return baseQuery;
        }

        /// <summary>
        /// Tespit edilen IPTV hesabının sağlayıcısına doğrudan bağlanır, tam M3U paketini indirir,
        /// ayrıca Xtream Player API üzerinden eksik VOD (film) ve Dizi index listelerini de çeker.
        /// </summary>
        public static async Task<(bool success, int importedCount, string message)> FetchAndImportFullAccountAsync(
            IptvAccountInfo account, 
            Action<string, double>? progress = null)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.Username) || string.IsNullOrWhiteSpace(account.Password))
                return (false, 0, "Geçersiz hesap bilgisi.");

            string scheme = account.HostWithPort.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || 
                            account.HostWithPort.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "" : "http://";
            string host = account.HostWithPort.TrimEnd('/');
            string apiUrl = $"{scheme}{host}/player_api.php?username={Uri.EscapeDataString(account.Username)}&password={Uri.EscapeDataString(account.Password)}";

            // Fast Pre-Check: Sunucu gerçekten aktif ve geçerli bir Xtream IPTV sunucusu mu?
            bool isVerified = await FastVerifyAccountAsync(account, 4000).ConfigureAwait(false);
            if (!isVerified)
            {
                LogService.LogInfo($"[IptvAccountHelper] Sağlayıcı doğrulanamadı veya aktif değil: {account.DisplaySummary}");
                return (false, 0, "Sağlayıcı aktif bir Xtream IPTV sunucusu değil veya yanıt vermiyor.");
            }

            progress?.Invoke($"Sağlayıcıya bağlanılıyor ({account.HostWithPort})...", 10);

            var allChannels = new List<Channel>();

            try
            {
                // 1. TAM M3U PLUS LİSTESİNİ ÇEK (Akıllı Çift Kademeli Fallback)
            // Bazı sunucular (örn: TomTV) 'output=ts' parametresiyle 0 bayt dönerken,
            // 'type=m3u_plus' (output parametresiz) veya 'output=m3u8' ile tam listeyi verir.
            string[] candidateUrls = new[]
            {
                GenerateFullM3uUrl(account, null),    // 1. Standart & en uyumlu (output parametresiz)
                GenerateFullM3uUrl(account, "ts"),    // 2. output=ts
                GenerateFullM3uUrl(account, "m3u8"),  // 3. output=m3u8
                $"{scheme}{host}/get.php?username={Uri.EscapeDataString(account.Username)}&password={Uri.EscapeDataString(account.Password)}&type=m3u" // 4. type=m3u
            };

            string successfulM3uUrl = candidateUrls[0];
            var m3uEngine = new M3uEngine();

            foreach (var tryUrl in candidateUrls)
            {
                try
                {
                    LogService.LogInfo($"[IptvAccountHelper] IPTV sağlayıcısından tam liste isteniyor: {account.DisplaySummary} -> {tryUrl}");
                    var m3uChannels = await m3uEngine.ParseM3uAsync(tryUrl, "TV", false, (msg, pct) =>
                    {
                        progress?.Invoke(msg, pct * 0.7);
                    });

                    if (m3uChannels != null && m3uChannels.Count > 0)
                    {
                        allChannels.AddRange(m3uChannels);
                        successfulM3uUrl = tryUrl;
                        LogService.LogInfo($"[IptvAccountHelper] Başarılı M3U formatı teyit edildi ({account.DisplaySummary}): {m3uChannels.Count} kanal içe aktarılıyor.");
                        break;
                    }
                    else
                    {
                        LogService.LogWarning($"[IptvAccountHelper] Format boş döndü ({tryUrl}), alternatif formata geçiliyor...");
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogWarning($"[IptvAccountHelper] Format deneme uyarısı ({tryUrl}): {ex.Message}");
                }
            }

            // 1.b Eğer M3U formatları boş döndüyse doğrudan player_api live_streams üzerinden çek
            if (allChannels.Count == 0)
            {
                try
                {
                    progress?.Invoke("Xtream API: Canlı yayın listesi sorgulanıyor...", 40);
                    string liveJson = await MediaHttpClient.GetStringAsync($"{apiUrl}&action=get_live_streams", 20).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(liveJson) && liveJson.StartsWith("[") && liveJson.Length > 10)
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(liveJson);
                        foreach (var elem in doc.RootElement.EnumerateArray())
                        {
                            string name = elem.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            string streamId = elem.TryGetProperty("stream_id", out var sid) ? sid.ToString() : "";
                            string icon = elem.TryGetProperty("stream_icon", out var si) ? si.GetString() ?? "" : "";
                            string grp = elem.TryGetProperty("category_name", out var cn) ? cn.GetString() ?? "Genel" : "Genel";

                            if (!string.IsNullOrEmpty(streamId) && !string.IsNullOrEmpty(name))
                            {
                                string liveUrl = $"{scheme}{host}/live/{account.Username}/{account.Password}/{streamId}.ts";
                                var ch = new Channel
                                {
                                    Name = name,
                                    Category = "TV",
                                    GroupTitle = grp,
                                    Url = liveUrl,
                                    LogoUrl = icon
                                };
                                allChannels.Add(ch);
                            }
                        }
                        LogService.LogInfo($"[IptvAccountHelper] Player API üzerinden {allChannels.Count} canlı kanal doğrudan çekildi.");
                    }
                }
                catch (Exception exLive)
                {
                    LogService.LogWarning($"[IptvAccountHelper] Player API canlı kanal çekme uyarısı: {exLive.Message}");
                }
            }

            // 2. XTREAM PLAYER API ÜZERİNDEN VOD (FİLM) VE DİZİ İNDEXİNİ KONTROL ET VE ÇEK
                progress?.Invoke("Xtream API: Film ve Dizi arşivi sorgulanıyor...", 75);
                try
                {
                    // VOD Filmleri Çek
                    var existingUrls = new HashSet<string>(allChannels.SelectMany(c => c.GetUrlList()), StringComparer.OrdinalIgnoreCase);

                    try
                    {
                        string vodJson = await MediaHttpClient.GetStringAsync($"{apiUrl}&action=get_vod_streams", 15).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(vodJson) && vodJson.StartsWith("[") && vodJson.Length > 10)
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(vodJson);
                            foreach (var elem in doc.RootElement.EnumerateArray())
                            {
                                string name = elem.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                                string streamId = elem.TryGetProperty("stream_id", out var sid) ? sid.ToString() : "";
                                string ext = elem.TryGetProperty("container_extension", out var ce) ? ce.GetString() ?? "mp4" : "mp4";
                                string icon = elem.TryGetProperty("stream_icon", out var si) ? si.GetString() ?? "" : "";

                                if (!string.IsNullOrEmpty(streamId) && !string.IsNullOrEmpty(name))
                                {
                                    string movieUrl = $"{scheme}{host}/movie/{account.Username}/{account.Password}/{streamId}.{ext}";
                                    if (!existingUrls.Contains(movieUrl))
                                    {
                                        var ch = new Channel
                                        {
                                            Name = name,
                                            Category = "Filmler",
                                            GroupTitle = "VOD Filmler",
                                            Url = movieUrl,
                                            LogoUrl = icon
                                        };
                                        allChannels.Add(ch);
                                        existingUrls.Add(movieUrl);
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
                catch { }

                if (allChannels.Count == 0)
                {
                    return (false, 0, "Sağlayıcıdan içerik alınamadı (Hesabın süresi dolmuş veya banlanmış olabilir).");
                }

                progress?.Invoke($"Eski parçalı kanallar temizleniyor...", 85);
                var db = new DatabaseEngine();

                // 1. Bu hesaba ait eski parçalı kanalları temizle
                await PurgeAccountFromDatabaseAsync(account);

                // 2. Sağlayıcının orijinal M3U'sundan gelen tüm kanalları kaydet
                progress?.Invoke($"{allChannels.Count} adet güncel kanal kütüphaneye kaydediliyor...", 90);
                await db.SaveChannelsBatchAsync(allChannels, clearFirst: false, notifyUpdated: true);

                // 3. Bu tam M3U adresini kalıcı M3U kaynaklarına ekle
                db.AddM3uSource(successfulM3uUrl);

                // 4. Bu hesabı resmi olarak IptvAccounts tablosuna kaydet (Ayarlar -> IPTV Hesapları'nda görünsün)
                try
                {
                    string serverUrl = $"{scheme}{host}";

                    var iptvAcc = new IptvAccount
                    {
                        Name = $"{account.Username} ({account.HostWithPort})",
                        ServerUrl = serverUrl,
                        Username = account.Username,
                        Password = account.Password,
                        Status = "Aktif",
                        ExpiryDate = DateTime.Now.AddYears(1),
                        HasServerIndex = true,
                        LocalChannelsCount = allChannels.Count
                    };
                    db.SaveIptvAccount(iptvAcc);

                    try
                    {
                        var xtream = new XtreamService();
                        await xtream.SyncAccountAsync(iptvAcc).ConfigureAwait(false);
                    }
                    catch { }

                    LogService.LogInfo($"[IptvAccountHelper] Hesap IptvAccounts tablosuna başarıyla kaydedildi ve senkronize edildi: {iptvAcc.Name}");
                }
                catch (Exception exAcc)
                {
                    LogService.LogWarning($"[IptvAccountHelper] IptvAccounts tablosuna kayıt hatası: {exAcc.Message}");
                }

                LogService.LogInfo($"[IptvAccountHelper] Sağlayıcıdan {allChannels.Count} kanal/film başarıyla içe aktarıldı ve kalıcı M3U kaynağı olarak eklendi.");
                progress?.Invoke("Tamamlandı!", 100);

                return (true, allChannels.Count, $"Başarılı! Sağlayıcıdan {allChannels.Count} adet güncel canlı TV kanalı ve film arşivi eksiksiz olarak kütüphanenize eklendi.");
            }
            catch (Exception ex)
            {
                LogService.LogError($"[IptvAccountHelper] Tam paket çekilirken hata oluştu: {account.DisplaySummary}", ex);
                return (false, 0, $"Sağlayıcıya bağlanırken hata: {ex.Message}");
            }
        }
    }
}
