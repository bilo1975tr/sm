using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace StreamMesh.Core.Utils
{
    /// <summary>
    /// Helper for canonicalizing stream URLs and producing stable channel identities.
    /// </summary>
    public static class StreamIdentityHelper
    {
        public static string CleanStreamUrlForIdentity(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            try
            {
                // AceStream hash
                if (url.Length == 40 && Regex.IsMatch(url, "^[a-fA-F0-9]{40}$"))
                    return url.ToLowerInvariant();

                if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = new Uri(url);
                    // Host + Path (Port :80 ve :443 standart olduğundan yoksay, query param'ları token vb. temizle)
                    string host = uri.Host.ToLowerInvariant();
                    string path = uri.AbsolutePath.TrimEnd('/');
                    return $"{host}{path}";
                }
            }
            catch { }
            return url.Trim().ToLowerInvariant();
        }

        public static string GenerateChannelIdFromUrl(string url)
        {
            string clean = CleanStreamUrlForIdentity(url);
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(clean));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
