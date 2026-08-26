using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using StreamMesh.Models;
using StreamMesh.Core.Network;

namespace StreamMesh.Core.Media
{
    public class M3uEngine
    {
        public async Task<List<Channel>> ParseM3uAsync(string urlOrPath, string categoryHint = "TV", bool forceCategory = false, Action<string, double>? progressCallback = null)
        {
            var channels = new List<Channel>();
            string content = "";

            try
            {
                if (urlOrPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    progressCallback?.Invoke($"Bağlanılıyor: {GetShortUrl(urlOrPath)}", 0);

                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var response = await MediaHttpClient.GetAsync(urlOrPath, HttpCompletionOption.ResponseHeadersRead, 10, cts.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        progressCallback?.Invoke($"Hata ({response.StatusCode}): {GetShortUrl(urlOrPath)}", 0);
                        return channels;
                    }

                    long totalBytes = response.Content.Headers.ContentLength ?? -1;
                    using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                    using var ms = new MemoryStream();

                    byte[] buffer = new byte[81920]; // 80KB buffer
                    long totalRead = 0;
                    int bytesRead = 0;
                    var startTime = DateTime.Now;

                    while (true)
                    {
                        using var readCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                        bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, readCts.Token);
                        if (bytesRead <= 0) break;

                        await ms.WriteAsync(buffer, 0, bytesRead, cts.Token);
                        totalRead += bytesRead;

                        double elapsed = (DateTime.Now - startTime).TotalSeconds;
                        double speedMBs = elapsed > 0 ? (totalRead / 1024.0 / 1024.0) / elapsed : 0;
                        double totalMB = totalBytes > 0 ? totalBytes / 1024.0 / 1024.0 : 0;
                        double currentMB = totalRead / 1024.0 / 1024.0;

                        if (totalBytes > 0)
                        {
                            double percent = Math.Min(100.0, (double)totalRead / totalBytes * 100.0);
                            progressCallback?.Invoke($"İndiriliyor: {currentMB:F2} MB / {totalMB:F2} MB (%{percent:F0}) - {speedMBs:F2} MB/s", percent);
                        }
                        else
                        {
                            progressCallback?.Invoke($"İndiriliyor: {currentMB:F2} MB - {speedMBs:F2} MB/s", 50);
                        }
                    }

                    byte[] rawBytes = ms.ToArray();
                    content = DecodeM3uContent(rawBytes);
                }
                else if (File.Exists(urlOrPath))
                {
                    progressCallback?.Invoke("Yerel dosya okunuyor...", 50);
                    byte[] rawBytes = await File.ReadAllBytesAsync(urlOrPath);
                    content = DecodeM3uContent(rawBytes);
                }

                if (string.IsNullOrEmpty(content)) return channels;

                // 1. Check if the content is HTML, JS or script response instead of M3U
                string trimmedStart = content.TrimStart();
                if (trimmedStart.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
                    trimmedStart.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
                    trimmedStart.StartsWith("<script", StringComparison.OrdinalIgnoreCase) ||
                    trimmedStart.StartsWith("(function", StringComparison.OrdinalIgnoreCase) ||
                    trimmedStart.StartsWith("var ", StringComparison.OrdinalIgnoreCase) ||
                    trimmedStart.StartsWith("function", StringComparison.OrdinalIgnoreCase))
                {
                    progressCallback?.Invoke("Hata: Geçersiz M3U içeriği (Web sayfası veya script algılandı)", 0);
                    return channels;
                }

                progressCallback?.Invoke("Ayrıştırılıyor...", 80);

                var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                Channel? current = null;
                var db = new StreamMesh.Core.Database.DatabaseEngine();

                foreach (var rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    if (line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
                    {
                        // Check for url-tvg, x-tvg-url or tvg-url header attributes
                        var match = System.Text.RegularExpressions.Regex.Match(line, @"(?:url-tvg|x-tvg-url|tvg-url)=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            string epgUrlsRaw = match.Groups[1].Value;
                            var epgUrls = epgUrlsRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var epgUrl in epgUrls)
                            {
                                string trimmedEpg = epgUrl.Trim();
                                if (!string.IsNullOrEmpty(trimmedEpg))
                                {
                                    db.AddEpgSource(trimmedEpg);
                                }
                            }
                        }
                        continue;
                    }

                    if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
                    {
                        current = new Channel { Category = categoryHint, PlaylistUrl = urlOrPath };
                        if (forceCategory) current.Notes = "FORCE_CAT";

                        // Logo
                        var logoMatch = System.Text.RegularExpressions.Regex.Match(line, @"tvg-logo=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (logoMatch.Success) current.LogoUrl = logoMatch.Groups[1].Value;

                        // EPG ID
                        var epgMatch = System.Text.RegularExpressions.Regex.Match(line, @"tvg-id=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (epgMatch.Success) current.EpgId = epgMatch.Groups[1].Value;

                        // tvg-name
                        var tvgNameMatch = System.Text.RegularExpressions.Regex.Match(line, @"tvg-name=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (tvgNameMatch.Success)
                        {
                            string tvgName = tvgNameMatch.Groups[1].Value.Trim();
                            if (!string.IsNullOrEmpty(tvgName)) current.AddAlternativeName(tvgName);
                        }

                        // Group Title
                        var groupMatch = System.Text.RegularExpressions.Regex.Match(line, @"group-title=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (groupMatch.Success)
                        {
                            current.GroupTitle = groupMatch.Groups[1].Value;
                            if (!forceCategory) current.Category = groupMatch.Groups[1].Value;
                        }

                        // HTTP Headers in EXTINF attributes
                        var uaMatch = System.Text.RegularExpressions.Regex.Match(line, @"(?:http-user-agent|user-agent)=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (uaMatch.Success) current.HttpUserAgent = uaMatch.Groups[1].Value.Trim();

                        var refMatch = System.Text.RegularExpressions.Regex.Match(line, @"(?:http-referrer|http-referer|referer|referrer)=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (refMatch.Success) current.HttpReferer = refMatch.Groups[1].Value.Trim();

                        var cookieMatch = System.Text.RegularExpressions.Regex.Match(line, @"(?:http-cookie|cookie)=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (cookieMatch.Success) current.HttpCookie = cookieMatch.Groups[1].Value.Trim();

                        var originMatch = System.Text.RegularExpressions.Regex.Match(line, @"(?:http-origin|origin)=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (originMatch.Success) current.HttpOrigin = originMatch.Groups[1].Value.Trim();

                        int nameIdx = line.LastIndexOf(',');
                        if (nameIdx != -1)
                        {
                            current.Name = line.Substring(nameIdx + 1).Trim();
                            if (string.IsNullOrEmpty(current.Name)) current.Name = "İsimsiz Kanal";
                        }
                    }
                    else if (line.StartsWith("#EXTVLCOPT:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (current == null) current = new Channel { Category = categoryHint, PlaylistUrl = urlOrPath };
                        string opt = line.Substring(11).Trim();
                        int eq = opt.IndexOf('=');
                        if (eq > 0)
                        {
                            string optKey = opt.Substring(0, eq).Trim().ToLowerInvariant();
                            string optVal = opt.Substring(eq + 1).Trim('"', '\'', ' ');

                            if (optKey == "http-user-agent" || optKey == "user-agent")
                                current.HttpUserAgent = optVal;
                            else if (optKey == "http-referrer" || optKey == "http-referer" || optKey == "referer" || optKey == "referrer")
                                current.HttpReferer = optVal;
                            else if (optKey == "http-cookie" || optKey == "cookie")
                                current.HttpCookie = optVal;
                            else if (optKey == "http-origin" || optKey == "origin")
                                current.HttpOrigin = optVal;
                            else
                                current.CustomHeaders[optKey] = optVal;
                        }
                    }
                    else if (line.StartsWith("#EXTHTTP:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (current == null) current = new Channel { Category = categoryHint, PlaylistUrl = urlOrPath };
                        string jsonPart = line.Substring(9).Trim();
                        try
                        {
                            var headers = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(jsonPart);
                            if (headers != null)
                            {
                                foreach (var kv in headers)
                                {
                                    if (kv.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase) || kv.Key.Equals("http-user-agent", StringComparison.OrdinalIgnoreCase))
                                        current.HttpUserAgent = kv.Value;
                                    else if (kv.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase) || kv.Key.Equals("http-referrer", StringComparison.OrdinalIgnoreCase) || kv.Key.Equals("http-referer", StringComparison.OrdinalIgnoreCase))
                                        current.HttpReferer = kv.Value;
                                    else if (kv.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) || kv.Key.Equals("http-cookie", StringComparison.OrdinalIgnoreCase))
                                        current.HttpCookie = kv.Value;
                                    else if (kv.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase) || kv.Key.Equals("http-origin", StringComparison.OrdinalIgnoreCase))
                                        current.HttpOrigin = kv.Value;
                                    else
                                        current.CustomHeaders[kv.Key] = kv.Value;
                                }
                            }
                        }
                        catch { }
                    }
                    else if (!line.StartsWith("#"))
                    {
                        string rawUrl = line;
                        if (!IsValidStreamUrl(rawUrl))
                        {
                            current = null;
                            continue;
                        }

                        if (current == null)
                        {
                            // Single line format without #EXTINF
                            string baseName = Path.GetFileNameWithoutExtension(rawUrl);
                            if (string.IsNullOrWhiteSpace(baseName)) baseName = "Yayın";
                            current = new Channel { Category = categoryHint, PlaylistUrl = urlOrPath, Name = baseName };
                        }

                        // Check for pipe syntax in URL (e.g. url|User-Agent=...&Referer=...)
                        if (rawUrl.Contains('|'))
                        {
                            int pipeIdx = rawUrl.IndexOf('|');
                            string headerPart = rawUrl.Substring(pipeIdx + 1).Trim();
                            var parts = headerPart.Split(new[] { '&', '|' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var part in parts)
                            {
                                int eqIdx = part.IndexOf('=');
                                if (eqIdx > 0)
                                {
                                    string key = part.Substring(0, eqIdx).Trim();
                                    string val = part.Substring(eqIdx + 1).Trim('"', '\'', ' ');

                                    if (key.Equals("http-user-agent", StringComparison.OrdinalIgnoreCase) || key.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (string.IsNullOrWhiteSpace(current.HttpUserAgent)) current.HttpUserAgent = val;
                                    }
                                    else if (key.Equals("http-referrer", StringComparison.OrdinalIgnoreCase) || key.Equals("http-referer", StringComparison.OrdinalIgnoreCase) || key.Equals("referer", StringComparison.OrdinalIgnoreCase) || key.Equals("referrer", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (string.IsNullOrWhiteSpace(current.HttpReferer)) current.HttpReferer = val;
                                    }
                                    else if (key.Equals("http-cookie", StringComparison.OrdinalIgnoreCase) || key.Equals("cookie", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (string.IsNullOrWhiteSpace(current.HttpCookie)) current.HttpCookie = val;
                                    }
                                    else if (key.Equals("http-origin", StringComparison.OrdinalIgnoreCase) || key.Equals("origin", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (string.IsNullOrWhiteSpace(current.HttpOrigin)) current.HttpOrigin = val;
                                    }
                                    else
                                    {
                                        if (!current.CustomHeaders.ContainsKey(key)) current.CustomHeaders[key] = val;
                                    }
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(rawUrl))
                        {
                            current.Url = rawUrl;

                            using (var sha1 = System.Security.Cryptography.SHA1.Create())
                            {
                                byte[] hash = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawUrl));
                                current.Id = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                            }

                            SmartNormalizationEngine.Instance.NormalizeChannel(current);
                            channels.Add(current);
                        }
                        current = null;
                    }
                }

                progressCallback?.Invoke($"Ayrıştırma tamamlandı: {channels.Count} kanal bulundu.", 100);
            }
            catch (Exception ex)
            {
                progressCallback?.Invoke($"Ayrıştırma hatası: {ex.Message}", 0);
            }

            return ChannelAggregator.Instance.AggregateChannels(channels);
        }

        private string DecodeM3uContent(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;

            try
            {
                // Try UTF8 first
                string utf8Str = System.Text.Encoding.UTF8.GetString(bytes);

                // If no replacement characters, check if valid UTF8
                if (!utf8Str.Contains('\uFFFD'))
                {
                    return utf8Str;
                }

                // If UTF8 produced replacement characters (broken encoding), fallback to Windows-1254 (Turkish) / ISO-8859-9
                try
                {
                    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                    var win1254 = System.Text.Encoding.GetEncoding("windows-1254");
                    return win1254.GetString(bytes);
                }
                catch
                {
                    return utf8Str;
                }
            }
            catch
            {
                return System.Text.Encoding.Default.GetString(bytes);
            }
        }

        private bool IsValidStreamUrl(string url)
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
                (url.Length == 40 && System.Text.RegularExpressions.Regex.IsMatch(url, "^[a-fA-F0-9]{40}$"))) // Raw AceStream hash
            {
                return true;
            }

            // Local file paths
            if (File.Exists(url)) return true;

            return false;
        }

        private string GetShortUrl(string url)
        {
            try { return new Uri(url).Host + new Uri(url).AbsolutePath; }
            catch { return url; }
        }
    }
}
