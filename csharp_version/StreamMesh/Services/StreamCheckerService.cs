using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
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

        public List<string> BrokenChannelIds { get; set; } = new List<string>();

        // Genişletilmiş İstatistikler (Geriye dönük uyumlu)
        public int LibVlcUsedCount { get; set; }
        public int HttpVerifiedCount { get; set; }
        public int DnsErrorCount { get; set; }
        public int TimeoutCount { get; set; }
        public int HttpErrorCount { get; set; }
        public double AverageCheckTimeMs { get; set; }
        public List<(string Name, long DurationMs)> SlowestChannelsList { get; set; } = new List<(string Name, long DurationMs)>();
        public List<string> SlowestChannels => SlowestChannelsList.Select(x => $"{x.Name} ({x.DurationMs} ms)").ToList();
    }

    public class StreamCheckerService
    {
        private readonly HttpClient _httpClient;
        private static readonly ConcurrentDictionary<string, (IPAddress[] IPs, DateTime Expiry)> _dnsCache = new ConcurrentDictionary<string, (IPAddress[], DateTime)>();
        private static readonly TimeSpan DnsCacheTtl = TimeSpan.FromMinutes(10);
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _hostSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>();
        private readonly object _statsLock = new object();

        public StreamCheckerService()
        {
            _httpClient = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true // SSL hatalarını es geç
            });
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "VLC/3.0.16 LibVLC/3.0.16");
        }

        // DNS Önbellek Çözümlemesi
        private async Task<IPAddress[]> ResolveDnsWithCacheAsync(string host, CancellationToken cancellationToken)
        {
            if (IPAddress.TryParse(host, out var ip))
            {
                return new[] { ip };
            }

            if (_dnsCache.TryGetValue(host, out var cached) && cached.Expiry > DateTime.UtcNow)
            {
                return cached.IPs;
            }

            try
            {
                using (var dnsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    dnsCts.CancelAfter(TimeSpan.FromSeconds(2)); // İlk DNS denemesi 2 saniye
                    var addresses = await Dns.GetHostAddressesAsync(host).WaitAsync(dnsCts.Token);
                    if (addresses != null && addresses.Length > 0)
                    {
                        _dnsCache[host] = (addresses, DateTime.UtcNow.Add(DnsCacheTtl));
                        return addresses;
                    }
                }
            }
            catch
            {
                // DNS Çözümleme başarısız
            }
            return null;
        }

        // Host bazlı bağlantı limiti kontrolü (Maksimum 3 paralel bağlantı)
        private async Task<IDisposable> AcquireHostSemaphoreAsync(string host)
        {
            var semaphore = _hostSemaphores.GetOrAdd(host, _ => new SemaphoreSlim(3, 3));
            await semaphore.WaitAsync();
            return new SemaphoreReleaser(semaphore);
        }

        private class SemaphoreReleaser : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;
            private int _disposed;

            public SemaphoreReleaser(SemaphoreSlim semaphore)
            {
                _semaphore = semaphore;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _semaphore.Release();
                }
            }
        }

        // Geriye Dönük Uyumlu: CheckStreamAsync
        public async Task<bool> CheckStreamAsync(string url)
        {
            return await CheckStreamAsync(url, CancellationToken.None);
        }

        // Yeni İptal Belirteçli Sürüm
        public async Task<bool> CheckStreamAsync(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            try
            {
                var uri = new Uri(url);
                var host = uri.Host;

                // 1. DNS Önbelleği Kontrolü
                var ips = await ResolveDnsWithCacheAsync(host, cancellationToken);
                if (ips == null || ips.Length == 0)
                {
                    return false;
                }

                // 2. Host bazlı kilit al (Aynı hosta en fazla 3 bağlantı)
                using (await AcquireHostSemaphoreAsync(host))
                {
                    HttpResponseMessage response = null;
                    bool isSuccess = false;

                    // 3. HTTP HEAD ve GET katmanlı sorgulama boru hattı (Timeout Stratejisi: 2s ve 5s)
                    try
                    {
                        // İlk deneme: HEAD isteği (2 saniye timeout)
                        response = await SendWithTimeoutAsync(() => new HttpRequestMessage(HttpMethod.Head, url), TimeSpan.FromSeconds(2), cancellationToken);
                        if (!response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.MethodNotAllowed)
                        {
                            response?.Dispose();
                            // HEAD başarısızsa GET ile 2 saniye dene
                            response = await SendWithTimeoutAsync(() => new HttpRequestMessage(HttpMethod.Get, url), TimeSpan.FromSeconds(2), cancellationToken);
                        }
                    }
                    catch
                    {
                        // İlk deneme başarısız olursa ikinci deneme: GET isteği (5 saniye timeout)
                        try
                        {
                            response = await SendWithTimeoutAsync(() => new HttpRequestMessage(HttpMethod.Get, url), TimeSpan.FromSeconds(5), cancellationToken);
                        }
                        catch
                        {
                            return false;
                        }
                    }

                    if (response != null && response.IsSuccessStatusCode)
                    {
                        var actualUrl = response.RequestMessage.RequestUri.ToString();
                        string contentType = response.Content.Headers.ContentType?.MediaType?.ToLower() ?? "";

                        // 4. Content-Type doğrulaması
                        if (contentType.Contains("text/html") || 
                            contentType.Contains("application/json") || 
                            contentType.Contains("application/xhtml+xml"))
                        {
                            response.Dispose();
                            return false;
                        }

                        bool isM3u8 = actualUrl.Contains(".m3u8") || 
                                      contentType.Contains("mpegurl") || 
                                      contentType.Contains("vnd.apple.mpegurl") ||
                                      contentType.Contains("x-mpegurl");

                        // 5. M3U8 analizi (Derinlik en fazla 1)
                        if (isM3u8)
                        {
                            if (response.RequestMessage.Method == HttpMethod.Head)
                            {
                                response.Dispose();
                                response = await SendWithTimeoutAsync(() => new HttpRequestMessage(HttpMethod.Get, actualUrl), TimeSpan.FromSeconds(5), cancellationToken);
                            }
                            string content = await response.Content.ReadAsStringAsync(cancellationToken);
                            response.Dispose();
                            return await VerifyM3u8ContentAsync(content, actualUrl, 0, cancellationToken);
                        }

                        // 6. Diğer akışlar için Magic Byte / Sahte içerik kontrolü
                        if (response.RequestMessage.Method == HttpMethod.Head)
                        {
                            response.Dispose();
                            response = await SendWithTimeoutAsync(() => new HttpRequestMessage(HttpMethod.Get, actualUrl), TimeSpan.FromSeconds(5), cancellationToken);
                        }

                        using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                        {
                            var buffer = new byte[8192];
                            using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                            {
                                readCts.CancelAfter(TimeSpan.FromSeconds(3));
                                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, readCts.Token);
                                response.Dispose();
                                if (bytesRead > 0)
                                {
                                    if (IsHtmlOrJson(buffer, bytesRead))
                                    {
                                        return false;
                                    }
                                    return true; // Akış veri akışı sağlıyor
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
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

        private async Task<bool> VerifyM3u8ContentAsync(string content, string baseUrl, int depth, CancellationToken cancellationToken)
        {
            if (depth > 1) return false; // En fazla 1 derinlikte rekürsiyon

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

                    var host = variantUri.Host;
                    var ips = await ResolveDnsWithCacheAsync(host, cancellationToken);
                    if (ips == null || ips.Length == 0) return false;

                    using (await AcquireHostSemaphoreAsync(host))
                    {
                        using (var response = await SendWithTimeoutAsync(() => new HttpRequestMessage(HttpMethod.Get, variantUri.ToString()), TimeSpan.FromSeconds(5), cancellationToken))
                        {
                            if (!response.IsSuccessStatusCode) return false;
                            string variantContent = await response.Content.ReadAsStringAsync(cancellationToken);
                            string actualVariantUrl = response.RequestMessage.RequestUri.ToString();
                            return await VerifyM3u8ContentAsync(variantContent, actualVariantUrl, depth + 1, cancellationToken);
                        }
                    }
                }

                if (firstTsUrl != null)
                {
                    Uri tsUri;
                    if (!Uri.TryCreate(firstTsUrl, UriKind.Absolute, out tsUri))
                    {
                        tsUri = new Uri(baseUri, firstTsUrl);
                    }

                    var host = tsUri.Host;
                    var ips = await ResolveDnsWithCacheAsync(host, cancellationToken);
                    if (ips == null || ips.Length == 0) return false;

                    using (await AcquireHostSemaphoreAsync(host))
                    {
                        using (var response = await SendWithTimeoutAsync(() => new HttpRequestMessage(HttpMethod.Get, tsUri.ToString()), TimeSpan.FromSeconds(5), cancellationToken))
                        {
                            if (!response.IsSuccessStatusCode) return false;

                            string contentType = response.Content.Headers.ContentType?.MediaType?.ToLower() ?? "";
                            if (contentType.Contains("text/html") || contentType.Contains("application/json"))
                            {
                                return false;
                            }

                            using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                            {
                                var buffer = new byte[8192];
                                using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                                {
                                    readCts.CancelAfter(TimeSpan.FromSeconds(3));
                                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, readCts.Token);
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
            }
            catch
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
                    return response.IsSuccessStatusCode;
                }
            }
            catch { return false; }
        }

        // Akıllı HTTP ve LibVLC hibrit doğrulama motoru
        public async Task<(bool working, string category, string resolution, bool usedVlc)> VerifyStreamSmartAsync(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url)) return (false, null, null, false);

            if (url.Contains("youtube.com") || url.Contains("youtu.be"))
            {
                bool isYtWorking = await CheckYouTubeAsync(url);
                return (isYtWorking, "TV", null, false);
            }
            if (url.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase))
            {
                return (true, "TV", null, false);
            }

            bool httpWorking = false;
            string inferredCategory = "TV";
            string contentType = "";

            try
            {
                var uri = new Uri(url);
                var host = uri.Host;

                // DNS Çözümlemesi
                var ips = await ResolveDnsWithCacheAsync(host, cancellationToken);
                if (ips != null && ips.Length > 0)
                {
                    using (await AcquireHostSemaphoreAsync(host))
                    {
                        HttpResponseMessage response = null;
                        try
                        {
                            response = await SendWithTimeoutAsync(() => new HttpRequestMessage(HttpMethod.Head, url), TimeSpan.FromSeconds(2), cancellationToken);
                            if (!response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.MethodNotAllowed)
                            {
                                response?.Dispose();
                                response = await SendWithTimeoutAsync(() => new HttpRequestMessage(HttpMethod.Get, url), TimeSpan.FromSeconds(2), cancellationToken);
                            }
                        }
                        catch
                        {
                            try
                            {
                                response = await SendWithTimeoutAsync(() => new HttpRequestMessage(HttpMethod.Get, url), TimeSpan.FromSeconds(5), cancellationToken);
                            }
                            catch
                            {
                                // İki deneme de başarısız
                            }
                        }

                        if (response != null && response.IsSuccessStatusCode)
                        {
                            var actualUrl = response.RequestMessage.RequestUri.ToString();
                            contentType = response.Content.Headers.ContentType?.MediaType?.ToLower() ?? "";

                            if (!contentType.Contains("text/html") && 
                                !contentType.Contains("application/json") && 
                                !contentType.Contains("application/xhtml+xml"))
                            {
                                bool isM3u8 = actualUrl.Contains(".m3u8") || 
                                              contentType.Contains("mpegurl") || 
                                              contentType.Contains("vnd.apple.mpegurl") ||
                                              contentType.Contains("x-mpegurl");

                                if (isM3u8)
                                {
                                    if (response.RequestMessage.Method == HttpMethod.Head)
                                    {
                                        response.Dispose();
                                        response = await SendWithTimeoutAsync(() => new HttpRequestMessage(HttpMethod.Get, actualUrl), TimeSpan.FromSeconds(5), cancellationToken);
                                    }
                                    string content = await response.Content.ReadAsStringAsync(cancellationToken);
                                    httpWorking = await VerifyM3u8ContentAsync(content, actualUrl, 0, cancellationToken);
                                }
                                else
                                {
                                    if (response.RequestMessage.Method == HttpMethod.Head)
                                    {
                                        response.Dispose();
                                        response = await SendWithTimeoutAsync(() => new HttpRequestMessage(HttpMethod.Get, actualUrl), TimeSpan.FromSeconds(5), cancellationToken);
                                    }
                                    using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                                    {
                                        var buffer = new byte[8192];
                                        using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                                        {
                                            readCts.CancelAfter(TimeSpan.FromSeconds(3));
                                            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, readCts.Token);
                                            if (bytesRead > 0)
                                            {
                                                httpWorking = !IsHtmlOrJson(buffer, bytesRead);
                                            }
                                        }
                                    }
                                }
                            }
                            response?.Dispose();
                        }
                    }
                }
            }
            catch
            {
                httpWorking = false;
            }

            // İçerik türünden kategori çıkarımı
            if (!string.IsNullOrEmpty(contentType))
            {
                if (contentType.StartsWith("audio/") || contentType.Contains("aac") || (contentType.Contains("mpeg") && !contentType.Contains("mpegurl")))
                {
                    inferredCategory = "Radyo";
                }
            }

            // HTTP kontrolünden başarıyla geçen ve güvenilir MIME türüne sahip akışlar için LibVLC başlatılmaz. (Zorunlu Kural 2)
            if (httpWorking && !string.IsNullOrEmpty(contentType))
            {
                bool isExpectedMime = contentType.Contains("mpegurl") || 
                                      contentType.Contains("video/") || 
                                      contentType.Contains("audio/") || 
                                      contentType.Contains("octet-stream") ||
                                      contentType.Contains("mp2t") ||
                                      contentType.Contains("mp4");

                if (isExpectedMime)
                {
                    return (true, inferredCategory, null, false);
                }
            }

            // Sadece HTTP doğrulamasından geçmeyen veya detaylı analiz gereken durumlarda LibVLC başlatılır. (Zorunlu Kural 2)
            try
            {
                using (var libVlc = new LibVLC(new string[] { "--quiet", "--no-video-title-show" }))
                {
                    using (var media = new Media(libVlc, new Uri(url)))
                    {
                        using (var mediaPlayer = new MediaPlayer(media))
                        {
                            mediaPlayer.Volume = 0;
                            mediaPlayer.Play();

                            bool hasVideo = false;
                            bool hasAudio = false;
                            string resolution = null;
                            int elapsedMs = 0;

                            while (elapsedMs < 3000)
                            {
                                await Task.Delay(150, cancellationToken);
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
                                return (false, null, null, true);
                            }

                            string category = "TV";
                            if (hasAudio && !hasVideo)
                            {
                                category = "Radyo";
                            }

                            return (true, category, resolution, true);
                        }
                    }
                }
            }
            catch
            {
                return (httpWorking, inferredCategory, null, false);
            }
        }

        // Geriye Dönük Uyumlu: AnalyzeStreamWithVlcAsync
        public async Task<(bool working, string category, string resolution)> AnalyzeStreamWithVlcAsync(string url)
        {
            var res = await VerifyStreamSmartAsync(url, CancellationToken.None);
            return (res.working, res.category, res.resolution);
        }

        private async Task<HttpResponseMessage> SendWithTimeoutAsync(Func<HttpRequestMessage> requestFactory, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                linkedCts.CancelAfter(timeout);
                try
                {
                    var request = requestFactory();
                    return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("HTTP isteği zaman aşımına uğradı.");
                }
            }
        }

        // Akıllı Önbellek Süre Kontrolü (Zorunlu Kural 4)
        private bool IsCacheValid(long verifiedAt, string category)
        {
            var age = TimeSpan.FromSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - verifiedAt);
            if (category == "Radyo")
            {
                return age.TotalHours < 24; // Radyo: 24 saat
            }
            else if (category == "Sinema" || category == "Film" || category == "Dizi" || category == "VOD")
            {
                return age.TotalDays < 7; // VOD: 7 gün
            }
            else
            {
                return age.TotalHours < 12; // Canlı TV: 12 saat
            }
        }

        // Paralel Doğrulama ve Raporlama Motoru (Zorunlu Kural 5)
        public async Task<StreamCheckStats> CheckChannelsAsync(List<Channel> channels, bool unverifiedOnly, CancellationToken cancellationToken, Action<StreamCheckStats> progressCallback)
        {
            var stats = new StreamCheckStats { Total = channels.Count, Processed = 0 };
            var db = new DatabaseService();
            var newlyVerified = new ConcurrentBag<Channel>();

            var totalSw = Stopwatch.StartNew();

            // Paralel işlemler için ayarlar (32 thread havuzu)
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 32,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(channels, parallelOptions, async (channel, ct) =>
            {
                if (ct.IsCancellationRequested) return;

                if (channel.IsLocked)
                {
                    lock (_statsLock) { stats.Processed++; }
                    progressCallback?.Invoke(stats);
                    return;
                }

                // Akıllı Önbellek Kontrolü (Sadece onaysız modda veya normal akışta)
                // unverifiedOnly = false ise 'Tüm Kanalları Kontrol Et' (Zorla Yeniden Doğrula) olarak davranır ve önbellek atlanır.
                if (unverifiedOnly)
                {
                    var cached = db.GetVerificationCache(channel.Id);
                    if (cached != null && IsCacheValid(cached.Value.VerifiedAt, cached.Value.Category))
                    {
                        lock (_statsLock)
                        {
                            channel.IsVerified = cached.Value.IsWorking;
                            if (cached.Value.IsWorking)
                            {
                                channel.Category = cached.Value.Category;
                                if (channel.SourceType == "ACESTREAM" || channel.Url.Contains("acestream")) stats.AceStreamWorking++;
                                else if (channel.Url.Contains("youtube.com") || channel.Url.Contains("youtu.be")) stats.YouTubeWorking++;
                                else stats.M3u8Working++;
                            }
                            else
                            {
                                stats.BrokenChannelIds.Add(channel.Id);
                            }

                            if (channel.SourceType == "ACESTREAM" || channel.Url.Contains("acestream")) stats.AceStreamTotal++;
                            else if (channel.Url.Contains("youtube.com") || channel.Url.Contains("youtu.be")) stats.YouTubeTotal++;
                            else stats.M3u8Total++;

                            stats.Processed++;
                        }
                        progressCallback?.Invoke(stats);
                        return;
                    }
                }

                bool wasVerifiedLocally = channel.IsVerified;

                // Kanal tipini belirle
                bool isAce = channel.SourceType == "ACESTREAM" || channel.Url.Contains("acestream://", StringComparison.OrdinalIgnoreCase);
                bool isYt = channel.SourceType == "YOUTUBE" || channel.Url.Contains("youtube.com") || channel.Url.Contains("youtu.be");
                bool isM3u = !isAce && !isYt;

                lock (_statsLock)
                {
                    if (isAce) stats.AceStreamTotal++;
                    else if (isYt) stats.YouTubeTotal++;
                    else if (isM3u) stats.M3u8Total++;
                }

                var urls = channel.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                bool anyWorking = false;
                string detectedCategory = null;
                string detectedResolution = null;
                bool vlcUsed = false;

                var channelSw = Stopwatch.StartNew();

                foreach (var url in urls)
                {
                    if (ct.IsCancellationRequested) break;

                    if (isAce)
                    {
                        anyWorking = true;
                        detectedCategory = "TV";
                        break;
                    }
                    else if (isYt)
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
                        var (working, cat, res, vlcRun) = await VerifyStreamSmartAsync(url, ct);
                        if (vlcRun) vlcUsed = true;

                        if (working)
                        {
                            anyWorking = true;
                            detectedCategory = cat;
                            detectedResolution = res;
                            break;
                        }
                    }
                }

                channelSw.Stop();
                long durationMs = channelSw.ElapsedMilliseconds;

                if (anyWorking)
                {
                    lock (_statsLock)
                    {
                        if (isAce) stats.AceStreamWorking++;
                        else if (isYt) stats.YouTubeWorking++;
                        else if (isM3u) stats.M3u8Working++;

                        if (vlcUsed) stats.LibVlcUsedCount++;
                        else stats.HttpVerifiedCount++;

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

                        channel.IsVerified = true;
                        
                        // Önbelleğe kaydet
                        db.SaveVerificationCache(channel.Id, channel.Category, detectedResolution, true);
                        db.SaveChannel(channel);

                        if (!wasVerifiedLocally || !unverifiedOnly)
                        {
                            newlyVerified.Add(channel);
                        }

                        // En yavaş kanalları listele
                        stats.SlowestChannelsList.Add((channel.Name, durationMs));
                        stats.SlowestChannelsList = stats.SlowestChannelsList.OrderByDescending(x => x.DurationMs).Take(10).ToList();
                    }
                }
                else
                {
                    lock (_statsLock)
                    {
                        channel.IsVerified = false;
                        db.SaveVerificationCache(channel.Id, channel.Category, null, false);
                        db.SaveChannel(channel);
                        stats.BrokenChannelIds.Add(channel.Id);

                        if (!string.IsNullOrEmpty(channel.Url))
                        {
                            foreach (var failedUrl in channel.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                db.AddDeadLink(failedUrl);
                            }
                        }
                    }
                }

                lock (_statsLock)
                {
                    stats.Processed++;
                }
                progressCallback?.Invoke(stats);
            });

            totalSw.Stop();

            lock (_statsLock)
            {
                stats.AverageCheckTimeMs = stats.Processed > 0 ? (double)totalSw.ElapsedMilliseconds / stats.Processed : 0;
            }

            // Arka planda Firebase senkronizasyonu toplu (Batch) olarak gönderilir (Zorunlu Kural 10)
            if (newlyVerified.Count > 0)
            {
                var listToSend = newlyVerified.ToList();
                _ = GitHubSyncService.PushNewChannelsToFirebasePoolAsync(listToSend);
            }

            return stats;
        }
    }
}
