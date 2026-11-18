using System;
using System.Collections.Generic;
using System.Linq;
using StreamMesh.Models;

namespace StreamMesh.Core.Media
{
    /// <summary>
    /// Resolves server identities and AceStream/IPTV single-connection conflicts
    /// for simultaneous 4-screen Multi-View playback.
    /// </summary>
    public static class MultiViewConflictHelper
    {
        public const string AceStreamServerKey = "acestream://p2p";

        public static bool IsAceStreamUrl(string? url, Channel? channel = null)
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                if (AceEngine.Instance.IsAceStreamUrl(url) ||
                    AceEngine.Instance.IsLocalAceHttpUrl(url) ||
                    url.Contains("session=ace_", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (channel != null &&
                string.Equals(channel.SourceType, "ACESTREAM", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase) || AceEngine.Instance.IsLocalAceHttpUrl(url)))
            {
                return true;
            }

            return false;
        }

        public static string ResolveUpstreamUrl(string rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl)) return "";
            string clean = rawUrl.Trim();
            int pipeIdx = clean.IndexOf('|');
            if (pipeIdx > 0) clean = clean.Substring(0, pipeIdx).Trim();

            if (clean.Contains("127.0.0.1:48931", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("localhost:48931", StringComparison.OrdinalIgnoreCase))
            {
                var session = HlsProxyEngine.Instance.GetSession(clean);
                if (session != null && !string.IsNullOrWhiteSpace(session.OriginalUrl))
                {
                    clean = session.OriginalUrl.Trim();
                    int origPipeIdx = clean.IndexOf('|');
                    if (origPipeIdx > 0) clean = clean.Substring(0, origPipeIdx).Trim();
                }
            }

            return clean;
        }

        public static string GetNormalizedServerKey(string? rawUrl, Channel? channel = null)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
                return IsAceStreamUrl(rawUrl, channel) ? AceStreamServerKey : "";

            string clean = ResolveUpstreamUrl(rawUrl);
            if (IsAceStreamUrl(clean, channel))
                return AceStreamServerKey;

            if (Uri.TryCreate(clean, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            {
                string host = uri.Host.ToLowerInvariant();
                if (host.StartsWith("www.", StringComparison.Ordinal))
                    host = host.Substring(4);

                if (host == "127.0.0.1" || host == "localhost" || host == "::1")
                {
                    if (uri.Port >= 6878 && uri.Port <= 6889) return AceStreamServerKey;
                    return "";
                }

                int port = uri.IsDefaultPort
                    ? (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80)
                    : uri.Port;

                string authority = (port == 80 || port == 443 || uri.Scheme.Equals("stalker", StringComparison.OrdinalIgnoreCase))
                    ? host
                    : $"{host}:{port}";

                return authority;
            }

            return "";
        }

        public static bool IsUrlAllowed(
            string url,
            Channel? channel,
            IReadOnlySet<string> blockedServerKeys,
            bool blockAceStream)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            if (IsAceStreamUrl(url, channel))
            {
                return !blockAceStream && !blockedServerKeys.Contains(AceStreamServerKey);
            }

            string key = GetNormalizedServerKey(url, channel);
            if (string.IsNullOrEmpty(key)) return true;
            return !blockedServerKeys.Contains(key);
        }

        public static List<string> FilterNonConflictingUrls(
            IEnumerable<string> candidateUrls,
            Channel? channel,
            IReadOnlySet<string> blockedServerKeys,
            bool blockAceStream)
        {
            var result = new List<string>();
            if (candidateUrls == null) return result;

            foreach (var u in candidateUrls)
            {
                if (string.IsNullOrWhiteSpace(u)) continue;
                if (IsUrlAllowed(u, channel, blockedServerKeys, blockAceStream))
                {
                    result.Add(u);
                }
            }
            return result;
        }

        public static string GetPreferredAvailableServerLabel(
            Channel channel,
            IReadOnlySet<string> blockedServerKeys,
            bool blockAceStream)
        {
            if (channel == null) return "";
            var urls = channel.GetOrderedUrlList();
            if (urls.Count == 0 && !string.IsNullOrWhiteSpace(channel.Url))
                urls.Add(channel.Url);

            foreach (var u in urls)
            {
                if (!IsUrlAllowed(u, channel, blockedServerKeys, blockAceStream))
                    continue;

                if (IsAceStreamUrl(u, channel)) return "AceStream P2P";
                string key = GetNormalizedServerKey(u, channel);
                if (!string.IsNullOrEmpty(key)) return key;
            }

            return "";
        }

        public static bool HasAnyAvailableUrl(
            Channel channel,
            IReadOnlySet<string> blockedServerKeys,
            bool blockAceStream)
        {
            if (channel == null) return false;
            var urls = channel.GetOrderedUrlList();
            if (urls.Count == 0 && !string.IsNullOrWhiteSpace(channel.Url))
                urls.Add(channel.Url);

            return urls.Any(u => IsUrlAllowed(u, channel, blockedServerKeys, blockAceStream));
        }
    }
}
