using System;
using System.IO;
using System.Text.RegularExpressions;

namespace StreamMesh.Core.Utils
{
    /// <summary>
    /// Validates stream URLs, protocols, and schemes for live and on-demand streaming.
    /// </summary>
    public static class StreamUrlValidator
    {
        public static bool IsValidStreamUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            url = url.Trim();

            // Must be at least 5 characters
            if (url.Length < 5) return false;

            // Reject common code/html snippets
            if (url.Contains("<") || url.Contains(">") || url.Contains(";") || url.Contains("{") || url.Contains("}") || url.Contains("var ") || url.Contains("function"))
                return false;

            // Check if valid URL or supported scheme
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("mms://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("udp://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                (url.Length == 40 && Regex.IsMatch(url, "^[a-fA-F0-9]{40}$"))) // Raw AceStream hash
            {
                return true;
            }

            // Local file paths
            if (File.Exists(url)) return true;

            return false;
        }
    }
}
