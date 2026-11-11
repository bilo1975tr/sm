using System;
using System.Text;

namespace StreamMesh.Core.Utils
{
    /// <summary>
    /// Handles intelligent decoding of raw M3U bytes with UTF-8 and Turkish ISO/Windows-1254 fallbacks.
    /// </summary>
    public static class M3uContentDecoder
    {
        public static string DecodeM3uContent(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;

            try
            {
                // Try UTF8 first
                string utf8Str = Encoding.UTF8.GetString(bytes);

                // If no replacement characters, check if valid UTF8
                if (!utf8Str.Contains('\uFFFD'))
                {
                    return utf8Str;
                }

                // If UTF8 produced replacement characters (broken encoding), fallback to Windows-1254 (Turkish) / ISO-8859-9
                try
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    var win1254 = Encoding.GetEncoding("windows-1254");
                    return win1254.GetString(bytes);
                }
                catch
                {
                    return utf8Str;
                }
            }
            catch
            {
                return Encoding.Default.GetString(bytes);
            }
        }
    }
}
