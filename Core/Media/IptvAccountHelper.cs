using System;
using System.Collections.Generic;
using System.Linq;
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
    }

    public static class IptvAccountHelper
    {
        // Standart Xtream Codes ve IPTV URL kalıpları:
        // http(s)://host:port/live/username/password/123.ts
        // http(s)://host:port/movie/username/password/123.mp4
        // http(s)://host:port/series/username/password/123.mkv
        // http(s)://host:port/username/password/123
        // http(s)://host:port/get.php?username=...&password=...
        // http(s)://host:port/player_api.php?username=...&password=...
        private static readonly Regex XtreamPathRegex = new Regex(
            @"https?://(?<host>[^/]+)/(?:(?:live|movie|series|play|stream|v|hls)/)?(?<user>[a-zA-Z0-9_\-\.]+)/(?<pass>[a-zA-Z0-9_\-\.]+)/(?<id>[a-zA-Z0-9_\-\.]+)",
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
            "blogspot.com", "wordpress.com", "rawgit.com", "gitee.com"
        };

        private static readonly HashSet<string> NonIptvTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hls_vod", "vod", "videos", "video", "segments", "stream", "live", "static", "media", "assets", "hls",
            "master", "main", "refs", "heads", "blob", "raw", "playlist", "channels", "iptv", "m3u", "m3u8", "tv"
        };

        public static IptvAccountInfo? ParseAccountFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            try
            {
                // CDN veya resmi VOD servislerini hemen ele
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
                    string streamId = match.Groups["id"].Value;

                    // Geçersiz anahtarları (örn: 'get.php', 'live', 'ts', 'api', 'hls_vod') filtrele
                    if (user.Contains(".") || user.Length < 2 || pass.Length < 2 ||
                        NonIptvTokens.Contains(user) || NonIptvTokens.Contains(pass) ||
                        user.StartsWith("S", StringComparison.OrdinalIgnoreCase) && char.IsDigit(user.Length > 1 ? user[1] : 'x')) // S11, S1 gibi sunucu adları
                    {
                        // Geçersiz
                    }
                    else
                    {
                        return new IptvAccountInfo
                        {
                            HostWithPort = host,
                            Username = user,
                            Password = pass
                        };
                    }
                }

                var qMatch = XtreamQueryRegex.Match(url);
                if (qMatch.Success)
                {
                    string host = qMatch.Groups["host"].Value;
                    string user = Uri.UnescapeDataString(qMatch.Groups["user"].Value);
                    string pass = Uri.UnescapeDataString(qMatch.Groups["pass"].Value);

                    if (!NonIptvTokens.Contains(user) && !NonIptvTokens.Contains(pass))
                    {
                        return new IptvAccountInfo
                        {
                            HostWithPort = host,
                            Username = user,
                            Password = pass
                        };
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
            var allChannels = sourceChannels ?? await db.GetAllChannelsAsync();

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
            var allChannels = await db.GetAllChannelsAsync();

            int totalChannels = 0;
            int totalUrls = 0;
            int exclusiveChannels = 0;

            foreach (var ch in allChannels)
            {
                var urls = ch.GetUrlList();
                var matchingUrls = urls.Where(u => UrlBelongsToAccount(u, account)).ToList();
                if (matchingUrls.Count > 0)
                {
                    totalChannels++;
                    totalUrls += matchingUrls.Count;
                    if (urls.Count == matchingUrls.Count)
                    {
                        exclusiveChannels++;
                    }
                }
            }

            return (totalChannels, totalUrls, exclusiveChannels);
        }

        /// <summary>
        /// Xtream / IPTV hesabından sağlayıcının sunduğu tüm güncel kanalları içeren tam M3U Plus URL'sini üretir.
        /// </summary>
        public static string GenerateFullM3uUrl(IptvAccountInfo account)
        {
            if (account == null) return string.Empty;
            string scheme = account.HostWithPort.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || 
                            account.HostWithPort.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "" : "http://";
            string host = account.HostWithPort.TrimEnd('/');
            return $"{scheme}{host}/get.php?username={Uri.EscapeDataString(account.Username)}&password={Uri.EscapeDataString(account.Password)}&type=m3u_plus&output=ts";
        }

        /// <summary>
        /// Tespit edilen IPTV hesabının sağlayıcısına doğrudan bağlanır, tam M3U paketini indirir,
        /// ayrıca Xtream Player API üzerinden eksik VOD (film) ve Dizi index listelerini de çeker.
        /// </summary>
        public static async Task<(bool success, int importedCount, string message)> FetchAndImportFullAccountAsync(
            IptvAccountInfo account, 
            Action<string, double>? progress = null)
        {
            if (account == null) return (false, 0, "Geçersiz hesap bilgisi.");

            string m3uUrl = GenerateFullM3uUrl(account);
            LogService.LogInfo($"[IptvAccountHelper] IPTV sağlayıcısından tam liste isteniyor: {account.DisplaySummary} -> {m3uUrl}");
            progress?.Invoke($"Sağlayıcıya bağlanılıyor ({account.HostWithPort})...", 10);

            var allChannels = new List<Channel>();

            try
            {
                // 1. TAM M3U PLUS LİSTESİNİ ÇEK
                try
                {
                    var m3uEngine = new M3uEngine();
                    var m3uChannels = await m3uEngine.ParseM3uAsync(m3uUrl, "TV", false, (msg, pct) =>
                    {
                        progress?.Invoke(msg, pct * 0.7);
                    });

                    if (m3uChannels != null && m3uChannels.Count > 0)
                    {
                        allChannels.AddRange(m3uChannels);
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogWarning($"[IptvAccountHelper] M3U Plus indirme uyarısı: {ex.Message}");
                }

                // 2. XTREAM PLAYER API ÜZERİNDEN VOD (FİLM) VE DİZİ İNDEXİNİ KONTROL ET VE ÇEK
                progress?.Invoke("Xtream API: Film ve Dizi arşivi sorgulanıyor...", 75);
                try
                {
                    string scheme = account.HostWithPort.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || 
                                    account.HostWithPort.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "" : "http://";
                    string host = account.HostWithPort.TrimEnd('/');
                    string apiUrl = $"{scheme}{host}/player_api.php?username={Uri.EscapeDataString(account.Username)}&password={Uri.EscapeDataString(account.Password)}";

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
                db.AddM3uSource(m3uUrl);

                // 4. Bu hesabı resmi olarak IptvAccounts tablosuna kaydet (Ayarlar -> IPTV Hesapları'nda görünsün)
                try
                {
                    string scheme = account.HostWithPort.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || 
                                    account.HostWithPort.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "" : "http://";
                    string serverUrl = $"{scheme}{account.HostWithPort.TrimEnd('/')}";

                    var iptvAcc = new IptvAccount
                    {
                        Name = $"{account.Username} ({account.HostWithPort})",
                        ServerUrl = serverUrl,
                        Username = account.Username,
                        Password = account.Password,
                        Status = "Aktif",
                        ExpiryDate = DateTime.Now.AddYears(1)
                    };
                    db.SaveIptvAccount(iptvAcc);
                    LogService.LogInfo($"[IptvAccountHelper] Hesap IptvAccounts tablosuna başarıyla kaydedildi: {iptvAcc.Name}");
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
