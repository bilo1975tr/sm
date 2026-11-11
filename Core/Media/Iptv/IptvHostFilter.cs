using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using StreamMesh.Models;

namespace StreamMesh.Core.Media.Iptv
{
    /// <summary>
    /// Filters out non-IPTV hosts (CDNs, public repositories, web portals) and tracks failed accounts.
    /// </summary>
    public static class IptvHostFilter
    {
        // Known official CDNs and repositories that must never be treated as IPTV providers
        public static readonly string[] NonIptvHosts = new[]
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

        public static readonly HashSet<string> NonIptvTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "hls_vod", "vod", "videos", "video", "segments", "stream", "live", "static", "media", "assets", "hls",
            "master", "main", "refs", "heads", "blob", "raw", "playlist", "channels", "m3u", "m3u8",
            "bant1", "tracks", "smil", "atv", "showtv", "haberturktv", "nowtv", "teve2", "tv8", "aspor", "a2tv",
            "streams", "v1", "105", "livestream", "tools", "fox", "css", "js", "images", "img",
            "movie", "movies", "episode", "episodes", "film", "filmler", "series", "dizi", "diziler",
            "trailer", "watch", "player", "play", "share", "embed", "content", "download", "token", "auth"
        };

        private static readonly ConcurrentDictionary<string, byte> _failedAccountsCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, byte> _ignoredHostsCache = new(StringComparer.OrdinalIgnoreCase);

        public static bool IsIgnoredHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host)) return true;
            string h = IptvUrlParser.ExtractHostAndPort(host).ToLowerInvariant();
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

        public static bool IsFailedAccount(IptvAccountInfo? account)
        {
            if (account == null) return true;
            return _failedAccountsCache.ContainsKey(account.SignatureKey) || IsIgnoredHost(account.HostWithPort);
        }

        public static void MarkAccountFailed(IptvAccountInfo? account, bool markHostFailed = false)
        {
            if (account == null) return;
            _failedAccountsCache.TryAdd(account.SignatureKey, 1);
            if (markHostFailed && !string.IsNullOrWhiteSpace(account.HostWithPort))
            {
                _ignoredHostsCache.TryAdd(IptvUrlParser.ExtractHostAndPort(account.HostWithPort).ToLowerInvariant(), 1);
            }
        }
    }
}
