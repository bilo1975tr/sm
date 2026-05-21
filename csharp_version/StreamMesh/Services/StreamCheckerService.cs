using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using StreamMesh.Models;

namespace StreamMesh.Services
{
    public class StreamCheckStats
    {
        public int Total { get; set; }
        public int Processed { get; set; }
        
        public int AceStreamTotal { get; set; }
        public int AceStreamWorking { get; set; }
        
        public int YouTubeTotal { get; set; }
        public int YouTubeWorking { get; set; }
        
        public int M3u8Total { get; set; }
        public int M3u8Working { get; set; }
    }

    public class StreamCheckerService
    {
        private readonly HttpClient _httpClient;

        public StreamCheckerService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "VLC/3.0.16 LibVLC/3.0.16");
        }

        // Returns true if the stream is accessible and looks like a valid media stream or playlist.
        public async Task<bool> CheckStreamAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return false;
                    }

                    var actualUrl = response.RequestMessage.RequestUri.ToString();
                    string contentType = response.Content.Headers.ContentType?.MediaType?.ToLower() ?? "";
                    bool isM3u8 = actualUrl.Contains(".m3u8") || contentType.Contains("mpegurl") || contentType.Contains("vnd.apple.mpegurl");

                    if (isM3u8)
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        return await VerifyM3u8ContentAsync(content, actualUrl, 0);
                    }

                    // Otherwise, just read some stream bytes (for mp4, ts, mkv, flv, etc.)
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    {
                        var buffer = new byte[8192];
                        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                        {
                            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                            if (bytesRead > 0)
                            {
                                return true; // Has data flow
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Silent mostly, log only critical
                Debug.WriteLine($"Stream check failed for {url}: {ex.Message}");
            }
            return false;
        }

        private async Task<bool> VerifyM3u8ContentAsync(string content, string baseUrl, int depth)
        {
            if (depth > 2) return false; // Prevent infinite recursion

            try
            {
                var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                string firstTsUrl = null;
                string firstVariantUrl = null;

                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;
                    if (trimmed.StartsWith("#")) continue;

                    if (trimmed.Contains(".m3u8"))
                    {
                        if (firstVariantUrl == null) firstVariantUrl = trimmed;
                    }
                    else
                    {
                        if (firstTsUrl == null) firstTsUrl = trimmed;
                    }
                }

                if (firstTsUrl == null && firstVariantUrl == null) return false;

                Uri baseUri = new Uri(baseUrl);

                if (firstVariantUrl != null && firstTsUrl == null)
                {
                    Uri variantUri;
                    if (!Uri.TryCreate(firstVariantUrl, UriKind.Absolute, out variantUri))
                    {
                        variantUri = new Uri(baseUri, firstVariantUrl);
                    }

                    var request = new HttpRequestMessage(HttpMethod.Get, variantUri.ToString());
                    using (var variantResponse = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                    {
                        if (!variantResponse.IsSuccessStatusCode) return false;
                        string variantContent = await variantResponse.Content.ReadAsStringAsync();
                        string actualVariantUrl = variantResponse.RequestMessage.RequestUri.ToString();
                        return await VerifyM3u8ContentAsync(variantContent, actualVariantUrl, depth + 1);
                    }
                }

                if (firstTsUrl != null)
                {
                    Uri tsUri;
                    if (!Uri.TryCreate(firstTsUrl, UriKind.Absolute, out tsUri))
                    {
                        tsUri = new Uri(baseUri, firstTsUrl);
                    }

                    var request = new HttpRequestMessage(HttpMethod.Get, tsUri.ToString());
                    using (var tsResponse = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                    {
                        if (!tsResponse.IsSuccessStatusCode) return false;

                        // Check if the TS block actually provides data flow
                        using (var stream = await tsResponse.Content.ReadAsStreamAsync())
                        {
                            var buffer = new byte[8192];
                            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                            {
                                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                                return bytesRead > 0;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        private async Task<bool> CheckYouTubeAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    // Basic check: HTTP 200 OK
                    return response.IsSuccessStatusCode;
                }
            }
            catch { return false; }
        }

        public async Task<StreamCheckStats> CheckChannelsAsync(List<Channel> channels, bool unverifiedOnly, CancellationToken cancellationToken, Action<StreamCheckStats> progressCallback)
        {
            var stats = new StreamCheckStats { Total = channels.Count, Processed = 0 };

            var db = new DatabaseService();
            var newlyVerified = new List<Channel>();

            foreach (var channel in channels)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (unverifiedOnly && channel.IsVerified) 
                {
                    stats.Processed++;
                    continue;
                }

                bool wasVerifiedLocally = channel.IsVerified;

                // Determine channel type
                bool isAce = channel.SourceType == "ACESTREAM" || channel.Url.Contains("acestream://", StringComparison.OrdinalIgnoreCase);
                bool isYt = channel.SourceType == "YOUTUBE" || channel.Url.Contains("youtube.com") || channel.Url.Contains("youtu.be");
                bool isM3u = !isAce && !isYt;

                if (isAce) stats.AceStreamTotal++;
                else if (isYt) stats.YouTubeTotal++;
                else if (isM3u) stats.M3u8Total++;

                // Handle merged urls if any (comma separated)
                var urls = channel.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                bool anyWorking = false;
                
                foreach (var url in urls)
                {
                    if (channel.SourceType == "ACESTREAM" || url.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase))
                    {
                        anyWorking = true;
                        break;
                    }
                    else if (channel.SourceType == "YOUTUBE" || url.Contains("youtube.com") || url.Contains("youtu.be"))
                    {
                        if (await CheckYouTubeAsync(url))
                        {
                            anyWorking = true;
                            break;
                        }
                    }
                    else
                    {
                        if (await CheckStreamAsync(url))
                        {
                            anyWorking = true;
                            break;
                        }
                    }
                }

                if (anyWorking)
                {
                    if (isAce) stats.AceStreamWorking++;
                    else if (isYt) stats.YouTubeWorking++;
                    else if (isM3u) stats.M3u8Working++;

                    channel.IsVerified = true;
                    db.SaveChannel(channel); // Update verification status
                    
                    // Kullanıcı manuel olarak tüm listeyi test ediyorsa, daha önce onaylı olsa da havuza göndererek günceller,
                    // Eğer sadece 'onaysız' listesi deneniyorsa zaten (!wasVerifiedLocally) bloğuna girer.
                    if (!wasVerifiedLocally || !unverifiedOnly)
                    {
                        newlyVerified.Add(channel);
                    }
                }
                else
                {
                    channel.IsVerified = false;
                    // Mark as broken for post-processing index or delete
                    channel.Url = "BROKEN_STREAM_" + channel.Url; 
                    db.SaveChannel(channel); 
                }

                stats.Processed++;
                progressCallback?.Invoke(stats);
            }

            if (newlyVerified.Count > 0)
            {
                // Arka planda Firebase bekleme havuzuna gönder
                _ = GitHubSyncService.PushNewChannelsToFirebasePoolAsync(newlyVerified);
            }

            return stats;
        }
    }
}
