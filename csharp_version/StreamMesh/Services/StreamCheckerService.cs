using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using StreamMesh.Models;
using LibVLCSharp.Shared;

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

                    // Web sayfalarını veya API JSON hatalarını en baştan eleyelim
                    if (contentType.Contains("text/html") || 
                        contentType.Contains("application/json") || 
                        contentType.Contains("application/xhtml+xml"))
                    {
                        Debug.WriteLine($"Rejected non-media contentType: {contentType} for {url}");
                        return false;
                    }

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
                                if (IsHtmlOrJson(buffer, bytesRead))
                                {
                                    Debug.WriteLine($"Rejected fake stream (HTML/JSON sniffed) for {url}");
                                    return false;
                                }
                                return true; // Has real data flow
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

        private static bool IsHtmlOrJson(byte[] buffer, int bytesRead)
        {
            if (bytesRead < 5) return false;
            try
            {
                string text = System.Text.Encoding.UTF8.GetString(buffer, 0, Math.Min(bytesRead, 128)).TrimStart();
                if (text.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                    text.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
                    text.StartsWith("<head", StringComparison.OrdinalIgnoreCase) ||
                    text.StartsWith("<script", StringComparison.OrdinalIgnoreCase) ||
                    text.StartsWith("{", StringComparison.OrdinalIgnoreCase) ||
                    text.StartsWith("[", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch { }
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

                        string contentType = tsResponse.Content.Headers.ContentType?.MediaType?.ToLower() ?? "";
                        if (contentType.Contains("text/html") || contentType.Contains("application/json"))
                        {
                            return false;
                        }

                        // Check if the TS block actually provides data flow
                        using (var stream = await tsResponse.Content.ReadAsStreamAsync())
                        {
                            var buffer = new byte[8192];
                            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                            {
                                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                                if (bytesRead > 0)
                                {
                                    if (IsHtmlOrJson(buffer, bytesRead))
                                    {
                                        return false;
                                    }
                                    return true;
                                }
                                return false;
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

        public async Task<(bool working, string category, string resolution)> AnalyzeStreamWithVlcAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return (false, null, null);

            // YouTube ve Acestream'i ayrı tutalım (onlar için HTTP check yeterli)
            if (url.Contains("youtube.com") || url.Contains("youtu.be"))
            {
                bool isYtWorking = await CheckYouTubeAsync(url);
                return (isYtWorking, "TV", null);
            }
            if (url.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase))
            {
                return (true, "TV", null);
            }

            try
            {
                // Init a temporary LibVLC instance safely
                using (var libVlc = new LibVLC(new string[] { "--quiet", "--no-video-title-show" }))
                {
                    using (var media = new Media(libVlc, new Uri(url)))
                    {
                        using (var mediaPlayer = new MediaPlayer(media))
                        {
                            mediaPlayer.Muted = true;
                            mediaPlayer.Play();

                            bool hasVideo = false;
                            bool hasAudio = false;
                            string resolution = null;
                            int elapsedMs = 0;

                            // En fazla 3 saniye (veya video/ses tespiti yapılana kadar) bekliyoruz
                            while (elapsedMs < 3000)
                            {
                                await Task.Delay(150);
                                elapsedMs += 150;

                                if (mediaPlayer.IsPlaying)
                                {
                                    var tracks = media.Tracks;
                                    if (tracks != null && tracks.Length > 0)
                                    {
                                        foreach (var track in tracks)
                                        {
                                            if (track.TrackType == TrackType.Video)
                                            {
                                                hasVideo = true;
                                                var videoTrack = track.Data.Video;
                                                if (videoTrack.Width > 0 && videoTrack.Height > 0)
                                                {
                                                    resolution = $"{videoTrack.Width}x{videoTrack.Height}";
                                                }
                                            }
                                            else if (track.TrackType == TrackType.Audio)
                                            {
                                                hasAudio = true;
                                            }
                                        }

                                        if (hasVideo || hasAudio)
                                        {
                                            break;
                                        }
                                    }
                                }
                            }

                            mediaPlayer.Stop();

                            bool isWorking = hasVideo || hasAudio || mediaPlayer.IsPlaying;
                            if (!isWorking)
                            {
                                return (false, null, null);
                            }

                            // Sadece ses var ve video track yoksa -> Radyo
                            string category = "TV";
                            if (hasAudio && !hasVideo)
                            {
                                category = "Radyo";
                            }

                            return (true, category, resolution);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LibVLC analysis failed for {url}: {ex.Message}");
                // Fallback to basic Http check
                bool httpWorking = await CheckStreamAsync(url);
                return (httpWorking, "TV", null);
            }
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
                string detectedCategory = null;
                string detectedResolution = null;
                
                foreach (var url in urls)
                {
                    if (channel.SourceType == "ACESTREAM" || url.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase))
                    {
                        anyWorking = true;
                        detectedCategory = "TV";
                        break;
                    }
                    else if (channel.SourceType == "YOUTUBE" || url.Contains("youtube.com") || url.Contains("youtu.be"))
                    {
                        if (await CheckYouTubeAsync(url))
                        {
                            anyWorking = true;
                            detectedCategory = "TV";
                            break;
                        }
                    }
                    else
                    {
                        var (working, cat, res) = await AnalyzeStreamWithVlcAsync(url);
                        if (working)
                        {
                            anyWorking = true;
                            detectedCategory = cat;
                            detectedResolution = res;
                            break;
                        }
                    }
                }

                if (anyWorking)
                {
                    if (isAce) stats.AceStreamWorking++;
                    else if (isYt) stats.YouTubeWorking++;
                    else if (isM3u) stats.M3u8Working++;

                    if (detectedCategory == "Radyo")
                    {
                        channel.Category = "Radyo";
                    }
                    else if (string.IsNullOrEmpty(channel.Category) || channel.Category == "TV")
                    {
                        if (!string.IsNullOrEmpty(detectedCategory))
                        {
                            channel.Category = detectedCategory;
                        }
                    }

                    if (!string.IsNullOrEmpty(detectedResolution))
                    {
                        LogService.Log($"[StreamChecker] {channel.Name} çözünürlüğü tespit edildi: {detectedResolution}");
                    }

                    channel.IsVerified = true;
                    db.SaveChannel(channel); // Update verification status and category
                    
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
