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
        
        // DNS cache: now caches failed DNS (expiry with DateTime.UtcNow)
        private static readonly ConcurrentDictionary<string, (IPAddress[] IPs, DateTime Expiry)> _dnsCache = new ConcurrentDictionary<string, (IPAddress[], DateTime)>();
        private static readonly TimeSpan DnsCacheTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan DnsFailureTtl = TimeSpan.FromMinutes(2); // Failed DNS cache
        
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _hostSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>();
        
        // Host failure tracking for Circuit Breaker
        private static readonly ConcurrentDictionary<string, int> _hostConsecutiveFailures = new ConcurrentDictionary<string, int>();
        private static readonly ConcurrentDictionary<string, DateTime> _hostBlacklist = new ConcurrentDictionary<string, DateTime>();
        private const int CircuitBreakerThreshold = 5; // Failures before breaker trips
        private static readonly TimeSpan CircuitBreakerDuration = TimeSpan.FromMinutes(5); // How long host is blacklisted

        private readonly object _statsLock = new object();
        private long _lastProgressReportTicks = 0; // For progress reporting throttling

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
                // DNS Çözümleme başarısız -> Başarısız sonucu da cache'le
                _dnsCache[host] = (null, DateTime.UtcNow.Add(DnsFailureTtl));
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

        private bool IsHostCircuitBroken(string host)
        {
            if (_hostBlacklist.TryGetValue(host, out var expiry))
            {
                if (expiry > DateTime.UtcNow)
                {
                    return true;
                }
                else
                {
                    _hostBlacklist.TryRemove(host, out _);
                    _hostConsecutiveFailures.TryRemove(host, out _);
                }
            }
            return false;
        }

        private void RegisterHostSuccess(string host)
        {
            _hostConsecutiveFailures.TryRemove(host, out _);
        }

        private void RegisterHostFailure(string host)
        {
            int failures = _hostConsecutiveFailures.AddOrUpdate(host, 1, (_, val) => val + 1);
            if (failures >= CircuitBreakerThreshold)
            {
                _hostBlacklist[host] = DateTime.UtcNow.Add(CircuitBreakerDuration);
                Debug.WriteLine($"Host {host} is temporary blacklisted (Circuit Breaker Tripped).");
            }
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

            var res = await VerifyStreamSmartAsync(url, cancellationToken, detailed: false);
            return res.working;
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

        private bool VerifyM3u8ContentFast(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return false;
            if (!content.Contains("#EXTM3U")) return false;
            
            return content.Contains("#EXTINF") || 
                   content.Contains("#EXT-X-STREAM-INF") || 
                   content.Contains("#EXT-X-TARGETDURATION") || 
                   content.Contains(".ts") || 
                   content.Contains(".m3u8");
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

        // Akıllı HTTP ve LibVLC hibrit doğrulama motoru (Çift aşamalı)
        public async Task<(bool working, string category, string resolution, bool usedVlc)> VerifyStreamSmartAsync(string url, CancellationToken cancellationToken)
        {
            return await VerifyStreamSmartAsync(url, cancellationToken, detailed: false);
        }

        public async Task<(bool working, string category, string resolution, bool usedVlc)> VerifyStreamSmartAsync(string url, CancellationToken cancellationToken, bool detailed)
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

            // HIZLI TARAMA (Default) - LibVLC kesinlikle başlatılmaz
            if (!detailed)
            {
                try
                {
                    var uri = new Uri(url);
                    var host = uri.Host;

                    if (IsHostCircuitBroken(host))
                    {
                        return (false, null, null, false);
                    }

                    var ips = await ResolveDnsWithCacheAsync(host, cancellationToken);
                    if (ips == null || ips.Length == 0)
                    {
                        RegisterHostFailure(host);
                        return (false, null, null, false);
                    }

                    using (await AcquireHostSemaphoreAsync(host))
                    {
                        HttpResponseMessage response = null;
                        try
                        {
                            response = await SendWithTimeoutAsync(() => new HttpRequestMessage(HttpMethod.Get, url), TimeSpan.FromSeconds(3), cancellationToken);
                        }
                        catch
                        {
                            RegisterHostFailure(host);
                            return (false, null, null, false);
                        }

                        if (response != null && response.IsSuccessStatusCode)
                        {
                            RegisterHostSuccess(host);
                            var actualUrl = response.RequestMessage.RequestUri.ToString();
                            string fastContentType = response.Content.Headers.ContentType?.MediaType?.ToLower() ?? "";

                            if (fastContentType.Contains("text/html") || 
                                fastContentType.Contains("application/json") || 
                                fastContentType.Contains("application/xhtml+xml"))
                            {
                                response.Dispose();
                                return (false, null, null, false);
                            }

                            bool isM3u8 = actualUrl.Contains(".m3u8") || 
                                          fastContentType.Contains("mpegurl") || 
                                          fastContentType.Contains("vnd.apple.mpegurl") ||
                                          fastContentType.Contains("x-mpegurl");

                            if (isM3u8)
                            {
                                string content = await response.Content.ReadAsStringAsync(cancellationToken);
                                response.Dispose();
                                bool m3u8Valid = VerifyM3u8ContentFast(content);
                                return (m3u8Valid, "TV", null, false);
                            }
                            else
                            {
                                using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                                {
                                    var buffer = new byte[1024];
                                    using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                                    {
                                        readCts.CancelAfter(TimeSpan.FromSeconds(1));
                                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, readCts.Token);
                                        response.Dispose();
                                        if (bytesRead > 0)
                                        {
                                            bool htmlOrJson = IsHtmlOrJson(buffer, bytesRead);
                                            return (!htmlOrJson, "TV", null, false);
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            RegisterHostFailure(host);
                        }
                        response?.Dispose();
                    }
                }
                catch
                {
                    // Silent fail
                }
                return (false, null, null, false);
            }

            // DETAYLI TARAMA (LibVLC ile derin analiz)
            bool httpWorking = false;
            string inferredCategory = "TV";
            string contentType = "";
            string finalUrl = url;

            try
            {
                var uri = new Uri(url);
                var host = uri.Host;

                if (IsHostCircuitBroken(host)) return (false, null, null, false);

                var ips = await ResolveDnsWithCacheAsync(host, cancellationToken);
                if (ips != null && ips.Length > 0)
                {
                    using (await AcquireHostSemaphoreAsync(host))
                    {
                        HttpResponseMessage response = null;
                        try
                        {
                            response = await SendWithTimeoutAsync(() => new HttpRequestMessage(HttpMethod.Get, url), TimeSpan.FromSeconds(3), cancellationToken);
                        }
                        catch
                        {
                            RegisterHostFailure(host);
                        }

                        if (response != null && response.IsSuccessStatusCode)
                        {
                            RegisterHostSuccess(host);
                            finalUrl = response.RequestMessage.RequestUri.ToString();
                            contentType = response.Content.Headers.ContentType?.MediaType?.ToLower() ?? "";

                            if (!contentType.Contains("text/html") && 
                                !contentType.Contains("application/json") && 
                                !contentType.Contains("application/xhtml+xml"))
                            {
                                bool isM3u8 = finalUrl.Contains(".m3u8") || 
                                              contentType.Contains("mpegurl") || 
                                              contentType.Contains("vnd.apple.mpegurl") ||
                                              contentType.Contains("x-mpegurl");

                                if (isM3u8)
                                {
                                    string content = await response.Content.ReadAsStringAsync(cancellationToken);
                                    httpWorking = await VerifyM3u8ContentAsync(content, finalUrl, 0, cancellationToken);
                                }
                                else
                                {
                                    using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                                    {
                                        var buffer = new byte[8192];
                                        using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                                        {
                                            readCts.CancelAfter(TimeSpan.FromSeconds(2));
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
                        else
                        {
                            RegisterHostFailure(host);
                            response?.Dispose();
                        }
                    }
                }
            }
            catch
            {
                httpWorking = false;
            }

            if (!string.IsNullOrEmpty(contentType))
            {
                if (contentType.StartsWith("audio/") || contentType.Contains("aac") || (contentType.Contains("mpeg") && !contentType.Contains("mpegurl")))
                {
                    inferredCategory = "Radyo";
                }
            }

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

            try
            {
                using (var libVlc = new LibVLC(new string[] { "--quiet", "--no-video-title-show" }))
                {
                    using (var media = new Media(libVlc, new Uri(finalUrl)))
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
            var res = await VerifyStreamSmartAsync(url, CancellationToken.None, detailed: true);
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

        // Akıllı Önbellek Süre Kontrolü (Genişletilmiş)
        private bool IsCacheValid(long verifiedAt, string category, bool isWorking)
        {
            var age = TimeSpan.FromSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - verifiedAt);
            if (!isWorking)
            {
                return age.TotalMinutes < 30; // Başarısız kanallar için 30 dakika önbellek
            }
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

        private void ReportProgressThrottled(StreamCheckStats stats, Action<StreamCheckStats> progressCallback)
        {
            if (progressCallback == null) return;
            long currentTicks = DateTime.UtcNow.Ticks;
            long lastTicks = Volatile.Read(ref _lastProgressReportTicks);
            if (currentTicks - lastTicks > TimeSpan.FromMilliseconds(200).Ticks || stats.Processed == stats.Total)
            {
                Volatile.Write(ref _lastProgressReportTicks, currentTicks);
                progressCallback(stats);
            }
        }

        private void FlushResultQueue(DatabaseService db, ConcurrentQueue<DatabaseService.VerificationResultBatchItem> queue)
        {
            var listToSave = new List<DatabaseService.VerificationResultBatchItem>();
            while (listToSave.Count < 50 && queue.TryDequeue(out var item))
            {
                listToSave.Add(item);
            }
            if (listToSave.Count > 0)
            {
                db.SaveVerificationResultsBatch(listToSave);
            }
        }

        // Paralel Doğrulama ve Raporlama Motoru (Toplu Kayıt & Optimizasyonlu)
        public async Task<StreamCheckStats> CheckChannelsAsync(List<Channel> channels, bool unverifiedOnly, CancellationToken cancellationToken, Action<StreamCheckStats> progressCallback)
        {
            var stats = new StreamCheckStats { Total = channels.Count, Processed = 0 };
            var db = new DatabaseService();
            var newlyVerified = new ConcurrentBag<Channel>();
            var resultQueue = new ConcurrentQueue<DatabaseService.VerificationResultBatchItem>();

            var totalSw = Stopwatch.StartNew();

            // Paralel işlemler için ayarlar (Hızlı tarama için maksimum 128 paralel task)
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 128,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(channels, parallelOptions, async (channel, ct) =>
            {
                if (ct.IsCancellationRequested) return;

                if (channel.IsLocked)
                {
                    lock (_statsLock) { stats.Processed++; }
                    ReportProgressThrottled(stats, progressCallback);
                    return;
                }

                // Akıllı Önbellek Kontrolü
                if (unverifiedOnly)
                {
                    var cached = db.GetVerificationCache(channel.Id);
                    if (cached != null && IsCacheValid(cached.Value.VerifiedAt, cached.Value.Category, cached.Value.IsWorking))
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
                        ReportProgressThrottled(stats, progressCallback);
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
                        // Default: Hızlı doğrulama (detailed: false)
                        var (working, cat, res, vlcRun) = await VerifyStreamSmartAsync(url, ct, detailed: false);
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
                        stats.BrokenChannelIds.Add(channel.Id);
                    }
                }

                // SQLite toplu kayda ekle
                var batchItem = new DatabaseService.VerificationResultBatchItem
                {
                    Channel = channel,
                    Category = detectedCategory ?? channel.Category,
                    Resolution = detectedResolution,
                    IsWorking = anyWorking,
                    DeadUrls = anyWorking ? null : channel.Url?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(u => u.Trim()).ToList()
                };
                resultQueue.Enqueue(batchItem);

                // Queue boyutu 50'yi geçtiyse toplu yazdır
                if (resultQueue.Count >= 50)
                {
                    FlushResultQueue(db, resultQueue);
                }

                lock (_statsLock)
                {
                    stats.Processed++;
                }
                ReportProgressThrottled(stats, progressCallback);
            });

            // Geri kalan kuyruğu diske kaydet
            var finalBatch = new List<DatabaseService.VerificationResultBatchItem>();
            while (resultQueue.TryDequeue(out var item))
            {
                finalBatch.Add(item);
            }
            if (finalBatch.Count > 0)
            {
                db.SaveVerificationResultsBatch(finalBatch);
            }

            totalSw.Stop();

            lock (_statsLock)
            {
                stats.AverageCheckTimeMs = stats.Processed > 0 ? (double)totalSw.ElapsedMilliseconds / stats.Processed : 0;
            }

            // Arka planda Firebase senkronizasyonu toplu (Batch) olarak gönderilir
            if (newlyVerified.Count > 0)
            {
                var listToSend = newlyVerified.ToList();
                _ = GitHubSyncService.PushNewChannelsToFirebasePoolAsync(listToSend);
            }

            // En son durumun kesin yansıması için progressCallback'i son bir kez zorla tetikle
            progressCallback?.Invoke(stats);

            return stats;
        }
    }
}
