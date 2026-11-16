using System;
using StreamMesh.Models;

namespace StreamMesh.Core.Media.Iptv
{
    /// <summary>
    /// Builds Xtream Codes Catch-up / Timeshift playback URLs for past EPG programs.
    /// </summary>
    public static class XtreamCatchupHelper
    {
        public static bool CanCatchup(Channel? channel, EpgProgram? program)
        {
            if (channel == null || program == null) return false;
            if (program.EndTime > DateTime.Now) return false;
            return TryExtractXtreamStreamParts(channel, out _, out _, out _, out _);
        }

        public static string? BuildCatchupUrl(Channel? channel, EpgProgram? program)
        {
            if (channel == null || program == null) return null;
            if (!TryExtractXtreamStreamParts(channel, out string scheme, out string host, out string user, out string pass, out string streamId))
            {
                return null;
            }

            int durationMinutes = Math.Clamp((int)Math.Ceiling((program.EndTime - program.StartTime).TotalMinutes), 5, 360);
            string startFormatted = program.StartTime.ToString("yyyy-MM-dd:HH-mm");
            return $"{scheme}://{host}/timeshift/{Uri.EscapeDataString(user)}/{Uri.EscapeDataString(pass)}/{durationMinutes}/{startFormatted}/{streamId}.ts";
        }

        private static bool TryExtractXtreamStreamParts(Channel channel, out string host, out string user, out string pass, out string streamId)
            => TryExtractXtreamStreamParts(channel, out _, out host, out user, out pass, out streamId);

        private static bool TryExtractXtreamStreamParts(Channel channel, out string scheme, out string host, out string user, out string pass, out string streamId)
        {
            scheme = "http";
            host = "";
            user = "";
            pass = "";
            streamId = "";

            foreach (var rawUrl in channel.GetOrderedUrlList())
            {
                if (string.IsNullOrWhiteSpace(rawUrl)) continue;
                var m = IptvUrlParser.XtreamPathRegex.Match(rawUrl);
                if (!m.Success) continue;

                string matchedHost = m.Groups["host"].Value;
                string matchedUser = m.Groups["user"].Value;
                string matchedPass = m.Groups["pass"].Value;
                string rawId = m.Groups["id"].Value;

                if (IptvHostFilter.NonIptvTokens.Contains(matchedUser) ||
                    IptvHostFilter.NonIptvTokens.Contains(matchedPass) ||
                    IptvHostFilter.IsIgnoredHost(matchedHost))
                {
                    continue;
                }

                string cleanId = rawId.Split('.')[0];
                if (!long.TryParse(cleanId, out _)) continue;

                scheme = rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
                host = matchedHost;
                user = matchedUser;
                pass = matchedPass;
                streamId = cleanId;
                return true;
            }

            return false;
        }
    }
}
