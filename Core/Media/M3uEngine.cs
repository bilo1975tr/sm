using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using StreamMesh.Models;

namespace StreamMesh.Core.Media
{
    public class M3uEngine
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        static M3uEngine()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) StreamMesh/1.0");
        }

        public async Task<List<Channel>> ParseM3uAsync(string urlOrPath, string categoryHint = "TV", bool forceCategory = false, Action<string, double>? progressCallback = null)
        {
            var channels = new List<Channel>();
            string content = "";

            try
            {
                if (urlOrPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    progressCallback?.Invoke($"Bağlanılıyor: {GetShortUrl(urlOrPath)}", 0);

                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                    using var response = await _httpClient.GetAsync(urlOrPath, HttpCompletionOption.ResponseHeadersRead, cts.Token);
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

                        // Group Title
                        var groupMatch = System.Text.RegularExpressions.Regex.Match(line, @"group-title=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (groupMatch.Success)
                        {
                            current.GroupTitle = groupMatch.Groups[1].Value;
                            if (!forceCategory) current.Category = groupMatch.Groups[1].Value;
                        }

                        int nameIdx = line.LastIndexOf(',');
                        if (nameIdx != -1)
                        {
                            current.Name = line.Substring(nameIdx + 1).Trim();
                            if (string.IsNullOrEmpty(current.Name)) current.Name = "İsimsiz Kanal";
                        }
                    }
                    else if (!line.StartsWith("#"))
                    {
                        string url = line;
                        if (!IsValidStreamUrl(url))
                        {
                            current = null;
                            continue;
                        }

                        if (current == null)
                        {
                            // Single line format without #EXTINF
                            string baseName = Path.GetFileNameWithoutExtension(url);
                            if (string.IsNullOrWhiteSpace(baseName)) baseName = "Yayın";
                            current = new Channel { Category = categoryHint, PlaylistUrl = urlOrPath, Name = baseName };
                        }

                        if (!string.IsNullOrEmpty(url))
                        {
                            current.Url = url;

                            using (var sha1 = System.Security.Cryptography.SHA1.Create())
                            {
                                byte[] hash = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(url));
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
