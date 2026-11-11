using System;
using System.Text.RegularExpressions;
using StreamMesh.Models;

namespace StreamMesh.Core.Media.Iptv
{
    /// <summary>
    /// Parses Xtream Codes and IPTV URLs for credentials and host information.
    /// </summary>
    public static class IptvUrlParser
    {
        public static readonly Regex XtreamPathRegex = new(
            @"https?://(?<host>[^/]+)/(?:(?<type>live|movie|series|play|stream|hls|ch|channel|tv)/)?(?<user>[a-zA-Z0-9_\-\.]+)/(?<pass>[a-zA-Z0-9_\-\.]+)/(?<id>[^/\s\?#]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static readonly Regex XtreamQueryRegex = new(
            @"https?://(?<host>[^/]+)/.*?[?&](?:username|u)=(?<user>[^&#\s]+)&(?:password|p)=(?<pass>[^&#\s]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
                if (uri.IsDefaultPort)
                {
                    return uri.Host;
                }
                return uri.Authority;
            }
            string rawHost = url.Replace("http://", "").Replace("https://", "").TrimEnd('/');
            if (rawHost.EndsWith(":80")) rawHost = rawHost.Substring(0, rawHost.Length - 3);
            else if (rawHost.EndsWith(":443")) rawHost = rawHost.Substring(0, rawHost.Length - 4);
            return rawHost;
        }

        public static IptvAccountInfo? ParseAccountFromUrl(string? rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl)) return null;

            // 1. Path Regex check
            var match = XtreamPathRegex.Match(rawUrl);
            if (match.Success)
            {
                string host = match.Groups["host"].Value;
                string user = match.Groups["user"].Value;
                string pass = match.Groups["pass"].Value;

                if (!IptvHostFilter.NonIptvTokens.Contains(user) &&
                    !IptvHostFilter.NonIptvTokens.Contains(pass) &&
                    !IptvHostFilter.IsIgnoredHost(host))
                {
                    return new IptvAccountInfo
                    {
                        HostWithPort = ExtractHostAndPort(host),
                        Username = user,
                        Password = pass
                    };
                }
            }

            // 2. Query Regex check
            var qMatch = XtreamQueryRegex.Match(rawUrl);
            if (qMatch.Success)
            {
                string host = qMatch.Groups["host"].Value;
                string user = qMatch.Groups["user"].Value;
                string pass = qMatch.Groups["pass"].Value;

                if (!IptvHostFilter.NonIptvTokens.Contains(user) &&
                    !IptvHostFilter.NonIptvTokens.Contains(pass) &&
                    !IptvHostFilter.IsIgnoredHost(host))
                {
                    return new IptvAccountInfo
                    {
                        HostWithPort = ExtractHostAndPort(host),
                        Username = user,
                        Password = pass
                    };
                }
            }

            return null;
        }
    }
}
