using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace StreamMesh.Core.Media.Ace
{
    /// <summary>
    /// Extracts AceStream content IDs, infohashes, and PIDs from arbitrary URLs or tokens.
    /// </summary>
    public static class AceIdExtractor
    {
        public static bool IsAceStreamUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            string trimmed = url.Trim();
            if (trimmed.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.Contains(":6878/ace/") || trimmed.Contains(":6878/content_id/")) return true;
            if (trimmed.Contains("channel_id=")) return true;
            if (trimmed.Length == 40 && Regex.IsMatch(trimmed, "^[a-fA-F0-9]{40}$")) return true;
            return false;
        }

        public static string ExtractPid(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            var match = Regex.Match(url, @"[?&]pid=([^&]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        public static string ExtractAceId(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            string trimmed = url.Trim();

            // 1. Any 40-hex hash in id=, content_id=, infohash=
            var match40 = Regex.Match(trimmed, @"[?&](?:id|content_id|infohash)=([a-fA-F0-9]{40})", RegexOptions.IgnoreCase);
            if (match40.Success) return match40.Groups[1].Value.ToLowerInvariant();

            // 2. Channel ID in query
            if (trimmed.Contains(":6878") || trimmed.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase))
            {
                var matchChannel = Regex.Match(trimmed, @"[?&](?:channel_id|channel)=(\d+)", RegexOptions.IgnoreCase);
                if (matchChannel.Success) return matchChannel.Groups[1].Value;
            }

            // 3. acestream:// scheme
            if (trimmed.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase))
            {
                string afterScheme = trimmed.Substring("acestream://".Length).Trim();
                if (afterScheme.StartsWith("?channel_id=", StringComparison.OrdinalIgnoreCase))
                {
                    return afterScheme.Substring("?channel_id=".Length);
                }
                var m = Regex.Match(afterScheme, @"^([a-fA-F0-9]{40})");
                if (m.Success) return m.Groups[1].Value.ToLowerInvariant();
                if (afterScheme.Length == 40 && Regex.IsMatch(afterScheme, "^[a-fA-F0-9]{40}$"))
                    return afterScheme.ToLowerInvariant();
            }

            // 4. Standalone 40-character hex hash
            if (trimmed.Length == 40 && Regex.IsMatch(trimmed, @"^[a-fA-F0-9]{40}$"))
            {
                return trimmed.ToLowerInvariant();
            }

            return "";
        }

        public static string Find40CharContentId(JToken? token)
        {
            if (token == null) return "";
            string[] keys = { "content_id", "contentId", "infohash", "info_hash", "id", "torrent_hash", "hash", "url" };
            foreach (var k in keys)
            {
                string val = token[k]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(val))
                {
                    var match = Regex.Match(val, @"\b([a-fA-F0-9]{40})\b");
                    if (match.Success) return match.Groups[1].Value.ToLowerInvariant();
                }
            }

            if (token is JObject obj)
            {
                foreach (var prop in obj.Properties())
                {
                    string v = prop.Value?.ToString() ?? "";
                    if (v.Length == 40 && Regex.IsMatch(v, "^[a-fA-F0-9]{40}$"))
                    {
                        return v.ToLowerInvariant();
                    }
                }
            }
            return "";
        }
    }
}
