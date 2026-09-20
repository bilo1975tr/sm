using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StreamMesh.Core.Database;
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
            @"https?://(?<host>[^/]+)/(?:(?:live|movie|series|play|stream|v|hls)/)?(?<user>[a-zA-Z0-9_\-\.]+)/(?<pass>[a-zA-Z0-9_\-\.]+)/",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex XtreamQueryRegex = new Regex(
            @"https?://(?<host>[^/]+)/.*?[?&](?:username|u)=(?<user>[^&#\s]+)&(?:password|p)=(?<pass>[^&#\s]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static IptvAccountInfo? ParseAccountFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            try
            {
                var match = XtreamPathRegex.Match(url);
                if (match.Success)
                {
                    string host = match.Groups["host"].Value;
                    string user = match.Groups["user"].Value;
                    string pass = match.Groups["pass"].Value;

                    // Geçersiz anahtarları (örn: 'get.php', 'live', 'ts', 'api') filtrele
                    if (user.Contains(".") || user.Length < 2 || pass.Length < 2)
                    {
                        // İkinci formatı dene
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
                    return new IptvAccountInfo
                    {
                        HostWithPort = qMatch.Groups["host"].Value,
                        Username = Uri.UnescapeDataString(qMatch.Groups["user"].Value),
                        Password = Uri.UnescapeDataString(qMatch.Groups["pass"].Value)
                    };
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
    }
}
