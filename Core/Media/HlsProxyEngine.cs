using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class HlsSegment
    {
        public int Index { get; set; }
        public string Url { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public double StartTimeSeconds { get; set; }
        public double EndTimeSeconds => StartTimeSeconds + DurationSeconds;
        public long SequenceNumber { get; set; }
        public DateTime? ProgramDateTime { get; set; }
    }

    public class HlsSessionInfo
    {
        public string SessionId { get; set; } = string.Empty;
        public string OriginalUrl { get; set; } = string.Empty;
        public string MediaPlaylistUrl { get; set; } = string.Empty;
        public double TotalDurationSeconds { get; set; }
        public bool HasDvrWindow { get; set; }
        public List<HlsSegment> Segments { get; } = new();
        public readonly object SyncLock = new();
        public DateTime StartWallClockTime { get; set; } = DateTime.Now;
        public DateTime LastRefreshedUtc { get; set; } = DateTime.UtcNow;
        public long MediaSequence { get; set; } = 0;
        public double TargetDuration { get; set; } = 6.0;
        public bool IsLive { get; set; } = true;
        public CancellationTokenSource? PollerCts { get; set; }
        public string? CustomUserAgent { get; set; }
        public string? CustomReferer { get; set; }
        public string? CustomCookie { get; set; }
        public string? CustomOrigin { get; set; }
        public Dictionary<string, string> CustomHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class HlsProxyEngine : IDisposable
    {
        private static readonly Lazy<HlsProxyEngine> _instance = new(() => new HlsProxyEngine());
        public static HlsProxyEngine Instance => _instance.Value;

        private HttpListener? _httpListener;
        private CancellationTokenSource? _cts;
        private readonly HttpClient _httpClient;
        private readonly CookieContainer _cookieContainer = new CookieContainer();
        private readonly ConcurrentDictionary<string, byte[]> _segmentCache = new();
        private readonly ConcurrentDictionary<string, HlsSessionInfo> _sessions = new();
        private int _port = 48931;
        private bool _isRunning = false;

        public int LocalPort => _port;
        public bool IsRunning => _isRunning;

        public HlsSessionInfo? GetSession(string originalUrl)
        {
            string sessionId = Convert.ToBase64String(Encoding.UTF8.GetBytes(originalUrl)).Replace("=", "").Replace("/", "_").Replace("+", "-");
            _sessions.TryGetValue(sessionId, out var session);
            return session;
        }

        private HlsProxyEngine()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                CookieContainer = _cookieContainer,
                UseCookies = true,
#if DEBUG
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
#endif
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
        }

        public void Start()
        {
            if (_isRunning) return;

            _cts = new CancellationTokenSource();
            
            for (int p = 48931; p < 48950; p++)
            {
                try
                {
                    _httpListener = new HttpListener();
                    _httpListener.Prefixes.Add($"http://127.0.0.1:{p}/");
                    _httpListener.Prefixes.Add($"http://localhost:{p}/");
                    _httpListener.Start();
                    _port = p;
                    _isRunning = true;
                    LogService.LogInfo($"HlsProxy: Yerel Timeshift sunucusu 127.0.0.1:{_port} portunda başlatıldı.");
                    break;
                }
                catch
                {
                    _httpListener?.Close();
                    _httpListener = null;
                }
            }

            if (_isRunning && _httpListener != null)
            {
                Task.Run(() => ListenLoop(_cts.Token));
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            try
            {
                _httpListener?.Stop();
                _httpListener?.Close();
            }
            catch { }
            _isRunning = false;
            ClearChannelCache();
        }

        public void ClearChannelCache()
        {
            foreach (var kv in _sessions)
            {
                try
                {
                    kv.Value.PollerCts?.Cancel();
                    kv.Value.PollerCts?.Dispose();
                }
                catch { }
            }
            _sessions.Clear();
            _segmentCache.Clear();
        }

        private static string GetShortUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            try
            {
                var uri = new Uri(url);
                string fn = Path.GetFileName(uri.AbsolutePath);
                if (!string.IsNullOrEmpty(fn)) return fn;
                return url.Length > 45 ? "..." + url.Substring(url.Length - 45) : url;
            }
            catch
            {
                return url.Length > 45 ? "..." + url.Substring(url.Length - 45) : url;
            }
        }

        public static (string CleanUrl, Dictionary<string, string> Headers) ExtractHeadersFromUrl(string rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl)) return (rawUrl ?? "", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int pipeIdx = rawUrl.IndexOf('|');
            if (pipeIdx == -1) return (rawUrl.Trim(), headers);

            string cleanUrl = rawUrl.Substring(0, pipeIdx).Trim();
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
                        headers["User-Agent"] = val;
                    else if (key.Equals("http-referrer", StringComparison.OrdinalIgnoreCase) || key.Equals("http-referer", StringComparison.OrdinalIgnoreCase) || key.Equals("referer", StringComparison.OrdinalIgnoreCase) || key.Equals("referrer", StringComparison.OrdinalIgnoreCase))
                        headers["Referer"] = val;
                    else if (key.Equals("http-cookie", StringComparison.OrdinalIgnoreCase) || key.Equals("cookie", StringComparison.OrdinalIgnoreCase))
                        headers["Cookie"] = val;
                    else if (key.Equals("http-origin", StringComparison.OrdinalIgnoreCase) || key.Equals("origin", StringComparison.OrdinalIgnoreCase))
                        headers["Origin"] = val;
                    else
                        headers[key] = val;
                }
            }

            return (cleanUrl, headers);
        }

        private void ApplyRequestHeaders(HttpRequestMessage request, HlsSessionInfo? session, string? fallbackReferer = null)
        {
            // 1. User-Agent
            string userAgent = !string.IsNullOrWhiteSpace(session?.CustomUserAgent)
                ? session.CustomUserAgent
                : "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

            request.Headers.Remove("User-Agent");
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);

            // 2. Referer: Only send if present in session/M3U metadata. Do NOT fabricate fake Referers!
            string? refererToUse = !string.IsNullOrWhiteSpace(session?.CustomReferer)
                ? session.CustomReferer
                : fallbackReferer;

            if (!string.IsNullOrWhiteSpace(refererToUse) && Uri.TryCreate(refererToUse, UriKind.Absolute, out var refUri))
            {
                request.Headers.Referrer = refUri;
            }

            // 3. Origin
            if (!string.IsNullOrWhiteSpace(session?.CustomOrigin))
            {
                request.Headers.TryAddWithoutValidation("Origin", session.CustomOrigin);
            }

            // 4. Cookie
            if (!string.IsNullOrWhiteSpace(session?.CustomCookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", session.CustomCookie);
            }

            // 5. Other custom headers
            if (session?.CustomHeaders != null)
            {
                foreach (var kv in session.CustomHeaders)
                {
                    if (kv.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase) ||
                        kv.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase) ||
                        kv.Key.Equals("Referrer", StringComparison.OrdinalIgnoreCase) ||
                        kv.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase) ||
                        kv.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }
        }

        public async Task<HlsSessionInfo?> InspectAndPrepareHlsAsync(
            string streamUrl,
            string? customUserAgent = null,
            string? customReferer = null,
            string? customCookie = null,
            string? customOrigin = null,
            Dictionary<string, string>? customHeaders = null)
        {
            if (string.IsNullOrWhiteSpace(streamUrl)) return null;

            try
            {
                // Parse pipe headers from the URL if present
                var (cleanUrl, pipeHeaders) = ExtractHeadersFromUrl(streamUrl);
                string targetUrl = cleanUrl;

                if (string.IsNullOrWhiteSpace(customUserAgent) && pipeHeaders.TryGetValue("User-Agent", out var pipeUa))
                    customUserAgent = pipeUa;
                if (string.IsNullOrWhiteSpace(customReferer) && pipeHeaders.TryGetValue("Referer", out var pipeRef))
                    customReferer = pipeRef;
                if (string.IsNullOrWhiteSpace(customCookie) && pipeHeaders.TryGetValue("Cookie", out var pipeCk))
                    customCookie = pipeCk;
                if (string.IsNullOrWhiteSpace(customOrigin) && pipeHeaders.TryGetValue("Origin", out var pipeOg))
                    customOrigin = pipeOg;

                var mergedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (customHeaders != null)
                {
                    foreach (var kv in customHeaders) mergedHeaders[kv.Key] = kv.Value;
                }
                foreach (var kv in pipeHeaders)
                {
                    mergedHeaders[kv.Key] = kv.Value;
                }

                string uaLog = !string.IsNullOrWhiteSpace(customUserAgent) ? customUserAgent : "Default (Chrome)";
                string refLog = !string.IsNullOrWhiteSpace(customReferer) ? customReferer : "None";
                LogService.LogInfo($"[HLS PROXY] Inspecting upstream stream: {targetUrl} (UA: {uaLog}, Referer: {refLog})");

                string sessionId = Convert.ToBase64String(Encoding.UTF8.GetBytes(streamUrl)).Replace("=", "").Replace("/", "_").Replace("+", "-");
                
                if (_sessions.TryGetValue(sessionId, out var existingSession))
                {
                    existingSession.PollerCts?.Cancel();
                }

                var session = new HlsSessionInfo
                {
                    SessionId = sessionId,
                    OriginalUrl = streamUrl,
                    MediaPlaylistUrl = targetUrl,
                    LastRefreshedUtc = DateTime.UtcNow,
                    CustomUserAgent = customUserAgent,
                    CustomReferer = customReferer,
                    CustomCookie = customCookie,
                    CustomOrigin = customOrigin,
                    CustomHeaders = mergedHeaders
                };

                // Direct MPEG-TS stream detection (e.g. Stalker/Xtream live.php?extension=ts).
                // These servers stream continuous progressive MPEG-TS via a single HTTP connection.
                // Do NOT convert to extension=m3u8 or force HLS segmentation.
                if (targetUrl.Contains("extension=ts", StringComparison.OrdinalIgnoreCase) ||
                    (targetUrl.Contains("live.php", StringComparison.OrdinalIgnoreCase) && !targetUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)))
                {
                    LogService.LogInfo($"[HLS PROXY] Direct MPEG-TS stream detected ({targetUrl}), bypassing HLS proxy to play natively via Flyleaf/FFmpeg.");
                    return null;
                }

                string manifestContent = "";
                bool isNativeHls = false;

                try
                {
                    using var ctsCheck = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
                    ApplyRequestHeaders(request, session);
                    var response = await _httpClient.SendAsync(request, ctsCheck.Token);
                    manifestContent = await response.Content.ReadAsStringAsync(ctsCheck.Token);

                    if (manifestContent.Contains("#EXTM3U"))
                    {
                        isNativeHls = true;

                        // Capture any Set-Cookie headers from manifest response into session if present
                        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
                        {
                            var cookieStr = string.Join("; ", setCookies.Select(c => c.Split(';')[0].Trim()));
                            if (!string.IsNullOrWhiteSpace(cookieStr))
                            {
                                session.CustomCookie = string.IsNullOrWhiteSpace(session.CustomCookie)
                                    ? cookieStr
                                    : $"{session.CustomCookie}; {cookieStr}";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError($"[HLS PROXY] Manifest fetch error ({targetUrl}): {ex.Message}", ex);
                }

                if (isNativeHls)
                {
                    string mediaPlaylistUrl = targetUrl;
                    if (manifestContent.Contains("#EXT-X-STREAM-INF"))
                    {
                        string targetVariant = ResolveMasterVariant(manifestContent, targetUrl);
                        if (!string.IsNullOrEmpty(targetVariant) && targetVariant != targetUrl)
                        {
                            LogService.LogInfo($"[HLS PROXY] Master playlist resolved to variant/media playlist: {targetVariant}");
                            mediaPlaylistUrl = targetVariant;
                            var request = new HttpRequestMessage(HttpMethod.Get, targetVariant);
                            ApplyRequestHeaders(request, session, customReferer);

                            using var ctsVariant = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            var response = await _httpClient.SendAsync(request, ctsVariant.Token);
                            manifestContent = await response.Content.ReadAsStringAsync(ctsVariant.Token);

                            if (response.Headers.TryGetValues("Set-Cookie", out var variantSetCookies))
                            {
                                var cookieStr = string.Join("; ", variantSetCookies.Select(c => c.Split(';')[0].Trim()));
                                if (!string.IsNullOrWhiteSpace(cookieStr))
                                {
                                    session.CustomCookie = string.IsNullOrWhiteSpace(session.CustomCookie)
                                        ? cookieStr
                                        : $"{session.CustomCookie}; {cookieStr}";
                                }
                            }
                        }
                    }

                    session.MediaPlaylistUrl = mediaPlaylistUrl;
                    ParseMediaPlaylist(session, manifestContent, mediaPlaylistUrl);

                    if (session.Segments.Count > 0)
                    {
                        session.StartWallClockTime = DateTime.Now.AddSeconds(-session.TotalDurationSeconds);
                        _sessions[sessionId] = session;

                        LogService.LogInfo($"[HLS PROXY] Initial parse: {session.Segments.Count} segments, target_duration={session.TargetDuration:0.0}s, is_live={session.IsLive}, media_seq={session.MediaSequence}");

                        if (session.IsLive)
                        {
                            StartLivePoller(session);
                        }
                        return session;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                LogService.LogError($"[HLS PROXY] Stream parsing error ({streamUrl}): {ex.Message}", ex);
                return null;
            }
        }

        public string GetProxyPlaybackUrl(string originalM3u8Url, double startOffsetSeconds = -1)
        {
            if (!_isRunning) Start();
            string sessionId = Convert.ToBase64String(Encoding.UTF8.GetBytes(originalM3u8Url)).Replace("=", "").Replace("/", "_").Replace("+", "-");
            return $"http://127.0.0.1:{_port}/playlist.m3u8?session={sessionId}&start={(int)startOffsetSeconds}";
        }

        private const int MaxMemoryCachedSegments = 320;
        private const int MaxTrackedHistorySegments = 15000;

        private void StartLivePoller(HlsSessionInfo session)
        {
            session.PollerCts?.Cancel();
            session.PollerCts = new CancellationTokenSource();
            var ct = session.PollerCts.Token;

            Task.Run(async () =>
            {
                string ua = !string.IsNullOrWhiteSpace(session.CustomUserAgent) ? session.CustomUserAgent : "Default (Chrome)";
                string refLog = !string.IsNullOrWhiteSpace(session.CustomReferer) ? session.CustomReferer : "None";
                LogService.LogInfo($"[HLS PROXY] Live poller started for media playlist: {session.MediaPlaylistUrl} (UA: {ua}, Referer: {refLog})");
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        double pollDelay = Math.Max(1.5, session.TargetDuration / 2.0);
                        await Task.Delay(TimeSpan.FromSeconds(pollDelay), ct);

                        // Upstream URL preserved exactly with original query/token parameters, without _t
                        string pollUrl = session.MediaPlaylistUrl;

                        var request = new HttpRequestMessage(HttpMethod.Get, pollUrl);
                        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
                        request.Headers.Pragma.Add(new System.Net.Http.Headers.NameValueHeaderValue("no-cache"));

                        ApplyRequestHeaders(request, session, session.CustomReferer);

                        var swPoll = System.Diagnostics.Stopwatch.StartNew();
                        var response = await _httpClient.SendAsync(request, ct);
                        swPoll.Stop();
                        response.EnsureSuccessStatusCode();

                        string updatedManifest = await response.Content.ReadAsStringAsync(ct);
                        int beforeCount;
                        lock (session.SyncLock)
                        {
                            beforeCount = session.Segments.Count;
                        }

                        ParseMediaPlaylist(session, updatedManifest, session.MediaPlaylistUrl);

                        int addedCount;
                        long firstSeq = 0, lastSeq = 0, mediaSeq = session.MediaSequence;
                        List<string> latestSegmentUrls;
                        lock (session.SyncLock)
                        {
                            addedCount = session.Segments.Count - beforeCount;
                            firstSeq = session.Segments.Count > 0 ? session.Segments[0].SequenceNumber : 0;
                            lastSeq = session.Segments.Count > 0 ? session.Segments.Last().SequenceNumber : 0;
                            latestSegmentUrls = session.Segments.TakeLast(6).Select(s => s.Url).ToList();
                        }

                        LogService.LogInfo($"[HLS PROXY] Poller tick: upstream MEDIA-SEQUENCE={mediaSeq}, new_segments_added={addedCount}, total_tracked={session.Segments.Count}, range=[{firstSeq}..{lastSeq}] (Fetch: {swPoll.ElapsedMilliseconds}ms)");

                        foreach (var segUrl in latestSegmentUrls)
                        {
                            if (!string.IsNullOrEmpty(segUrl) && !_segmentCache.ContainsKey(segUrl))
                            {
                                _ = FetchOrGetSegmentAsync(segUrl, session.CustomReferer, session);
                            }
                        }
                    }
                    catch (TaskCanceledException) { break; }
                    catch (Exception ex)
                    {
                        LogService.LogWarning($"[HLS PROXY] Poller error ({session.SessionId}): {ex.Message}");
                    }
                }
            }, ct);
        }

        private void StartTsLiveStreamChunker(HlsSessionInfo session, string streamUrl)
        {
            session.PollerCts?.Cancel();
            session.PollerCts = new CancellationTokenSource();
            var ct = session.PollerCts.Token;

            Task.Run(async () =>
            {
                LogService.LogInfo($"HlsProxy: Direct TS chunker started -> {streamUrl}");
                int segIndex = 0;
                double currentTimeSec = 0;

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, streamUrl);
                        ApplyRequestHeaders(req, session, session.CustomReferer);
                        using var response = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

                        if (!response.IsSuccessStatusCode)
                        {
                            await Task.Delay(2000, ct);
                            continue;
                        }

                        using var stream = await response.Content.ReadAsStreamAsync(ct);
                        const int targetChunkSize = 1200 * 188;
                        byte[] buffer = new byte[64 * 1024];

                        using var ms = new MemoryStream();
                        DateTime segmentStartTime = DateTime.UtcNow;

                        while (!ct.IsCancellationRequested)
                        {
                            int read = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                            if (read <= 0) break;

                            ms.Write(buffer, 0, read);

                            var elapsed = (DateTime.UtcNow - segmentStartTime).TotalSeconds;
                            if (ms.Length >= 500 * 188 && (elapsed >= 2.5 || ms.Length >= targetChunkSize))
                            {
                                byte[] chunkBytes = ms.ToArray();
                                ms.SetLength(0);
                                segmentStartTime = DateTime.UtcNow;

                                double dur = Math.Max(1.0, elapsed);
                                string segKey = $"rawts://{session.SessionId}/seg_{segIndex}.ts";
                                _segmentCache[segKey] = chunkBytes;

                                lock (session.SyncLock)
                                {
                                    session.Segments.Add(new HlsSegment
                                    {
                                        Index = segIndex,
                                        Url = segKey,
                                        DurationSeconds = dur,
                                        StartTimeSeconds = currentTimeSec,
                                        SequenceNumber = segIndex,
                                        ProgramDateTime = DateTime.Now.AddSeconds(-dur)
                                    });

                                    currentTimeSec += dur;
                                    session.TotalDurationSeconds = currentTimeSec;
                                    session.LastRefreshedUtc = DateTime.UtcNow;
                                    session.HasDvrWindow = session.TotalDurationSeconds >= 15;

                                    if (session.Segments.Count > MaxMemoryCachedSegments)
                                    {
                                        var oldest = session.Segments[0];
                                        session.Segments.RemoveAt(0);
                                        _segmentCache.TryRemove(oldest.Url, out _);
                                    }
                                }
                                segIndex++;
                            }
                        }
                    }
                    catch (TaskCanceledException) { break; }
                    catch (Exception ex)
                    {
                        LogService.LogWarning($"HlsProxy: TS reader warning: {ex.Message}");
                        await Task.Delay(1500, ct);
                    }
                }
            }, ct);
        }

        private string ResolveMasterVariant(string masterContent, string baseUrl)
        {
            using var reader = new StringReader(masterContent);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("#EXT-X-STREAM-INF"))
                {
                    string? uriLine = reader.ReadLine()?.Trim();
                    if (!string.IsNullOrEmpty(uriLine) && !uriLine.StartsWith("#"))
                    {
                        return MakeAbsoluteUrl(baseUrl, uriLine);
                    }
                }
            }
            return baseUrl;
        }

        private void ParseMediaPlaylist(HlsSessionInfo session, string content, string baseUrl)
        {
            using var reader = new StringReader(content);
            string? line;
            double pendingDuration = 0;
            long mediaSequence = 0;
            bool isEndList = false;
            double targetDuration = 6.0;
            DateTime? currentProgramTime = null;

            var newSegments = new List<HlsSegment>();

            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("#EXT-X-TARGETDURATION:"))
                {
                    if (double.TryParse(line.Substring(22).Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double td))
                        targetDuration = td;
                }
                else if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE:"))
                {
                    if (long.TryParse(line.Substring(22).Trim(), out long seq))
                        mediaSequence = seq;
                }
                else if (line.StartsWith("#EXT-X-PROGRAM-DATE-TIME:"))
                {
                    if (DateTime.TryParse(line.Substring(25).Trim(), null, DateTimeStyles.RoundtripKind, out DateTime dt))
                        currentProgramTime = dt.ToLocalTime();
                }
                else if (line.StartsWith("#EXT-X-ENDLIST"))
                {
                    isEndList = true;
                }
                else if (line.StartsWith("#EXTINF:"))
                {
                    var match = Regex.Match(line, @"#EXTINF:\s*([0-9.]+)", RegexOptions.IgnoreCase);
                    if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double dur))
                        pendingDuration = dur;
                    else
                        pendingDuration = targetDuration > 0 ? targetDuration : 6.0;
                }
                else if (!line.StartsWith("#"))
                {
                    string absUrl = MakeAbsoluteUrl(baseUrl, line);
                    newSegments.Add(new HlsSegment
                    {
                        Url = absUrl,
                        DurationSeconds = pendingDuration > 0 ? pendingDuration : targetDuration,
                        SequenceNumber = mediaSequence++,
                        ProgramDateTime = currentProgramTime
                    });

                    if (currentProgramTime.HasValue)
                        currentProgramTime = currentProgramTime.Value.AddSeconds(pendingDuration > 0 ? pendingDuration : targetDuration);

                    pendingDuration = 0;
                }
            }

            lock (session.SyncLock)
            {
                session.IsLive = !isEndList;
                session.TargetDuration = targetDuration;
                session.LastRefreshedUtc = DateTime.UtcNow;

                if (session.Segments.Count == 0)
                {
                    double curTime = 0;
                    int idx = 0;
                    foreach (var s in newSegments)
                    {
                        s.Index = idx++;
                        s.StartTimeSeconds = curTime;
                        curTime += s.DurationSeconds;
                        session.Segments.Add(s);
                    }
                    session.TotalDurationSeconds = curTime;

                    if (session.Segments.Count > 0 && !session.Segments[0].ProgramDateTime.HasValue)
                        session.StartWallClockTime = DateTime.Now.AddSeconds(-session.TotalDurationSeconds);
                    else if (session.Segments.Count > 0 && session.Segments[0].ProgramDateTime.HasValue)
                        session.StartWallClockTime = session.Segments[0].ProgramDateTime!.Value;
                }
                else
                {
                    var existingUrls = new HashSet<string>(session.Segments.Select(s => s.Url));
                    double curTime = session.Segments.Last().EndTimeSeconds;
                    int idx = session.Segments.Count;
                    int addedCount = 0;

                    foreach (var s in newSegments)
                    {
                        if (!existingUrls.Contains(s.Url))
                        {
                            s.Index = idx++;
                            s.StartTimeSeconds = curTime;
                            curTime += s.DurationSeconds;
                            session.Segments.Add(s);
                            addedCount++;
                        }
                    }
                    session.TotalDurationSeconds = curTime;

                    if (session.Segments.Count > MaxTrackedHistorySegments)
                    {
                        session.Segments.RemoveRange(0, 500);
                        if (session.Segments.Count > 0 && session.Segments[0].ProgramDateTime.HasValue)
                            session.StartWallClockTime = session.Segments[0].ProgramDateTime!.Value;
                    }
                }

                session.HasDvrWindow = session.TotalDurationSeconds >= 30;
            }
        }

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _httpListener != null && _httpListener.IsListening)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context), ct);
                }
                catch
                {
                    if (ct.IsCancellationRequested) break;
                }
            }
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url?.AbsolutePath ?? "";
                var query = context.Request.QueryString;

                if (path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
                {
                    string sessionId = query["session"] ?? "";
                    int startSec = -1;
                    if (int.TryParse(query["start"], out int parsedStart))
                        startSec = parsedStart;

                    if (!_sessions.TryGetValue(sessionId, out var session))
                    {
                        try
                        {
                            string padded = sessionId.Replace("_", "/").Replace("-", "+");
                            switch (padded.Length % 4)
                            {
                                case 2: padded += "=="; break;
                                case 3: padded += "="; break;
                            }
                            byte[] rawBytes = Convert.FromBase64String(padded);
                            string origUrl = Encoding.UTF8.GetString(rawBytes);
                            session = await InspectAndPrepareHlsAsync(origUrl);
                        }
                        catch { }
                    }

                    if (session != null)
                    {
                        byte[] m3u8Bytes = GenerateManifest(session, startSec, out long firstSeq, out long lastSeq, out int servedCount);
                        context.Response.ContentType = "application/vnd.apple.mpegurl";
                        context.Response.StatusCode = 200;
                        context.Response.ContentLength64 = m3u8Bytes.Length;
                        await context.Response.OutputStream.WriteAsync(m3u8Bytes);
                        context.Response.Close();

                        LogService.LogInfo($"[HLS MANIFEST] Served playlist -> media_seq={firstSeq}, count={servedCount}, range=[{firstSeq}..{lastSeq}], total_tracked={session.Segments.Count}, size={m3u8Bytes.Length}B");
                        return;
                    }
                    else
                    {
                        LogService.LogWarning($"[HLS MANIFEST] Failed to serve M3U8, session {sessionId} not found.");
                    }
                }
                else if (path.StartsWith("/seg/"))
                {
                    string sessionId = query["session"] ?? "";
                    string encodedUrl = query["url"] ?? "";
                    if (!string.IsNullOrEmpty(encodedUrl))
                    {
                        // HttpListener automatically decodes query parameters.
                        // If we UrlEncoded it in the manifest, 'encodedUrl' here is the original Base64.
                        string segUrl = Encoding.UTF8.GetString(Convert.FromBase64String(encodedUrl));
                        HlsSessionInfo? session = null;
                        if (!string.IsNullOrEmpty(sessionId) && _sessions.TryGetValue(sessionId, out var foundSession))
                            session = foundSession;

                        byte[]? data = await FetchOrGetSegmentAsync(segUrl, session?.CustomReferer, session);
                        if (data != null)
                        {
                            context.Response.ContentType = "video/MP2T";
                            context.Response.StatusCode = 200;
                            context.Response.ContentLength64 = data.Length;
                            await context.Response.OutputStream.WriteAsync(data);
                            context.Response.Close();

                            LogService.LogInfo($"[HLS SEGMENT SERVED] {GetShortUrl(segUrl)} -> Player 200 OK ({data.Length} bytes)");
                            return;
                        }
                        else
                        {
                            LogService.LogWarning($"[HLS SEGMENT SERVED] Failed (null data) -> 404 for {GetShortUrl(segUrl)}");
                        }
                    }
                }

                context.Response.StatusCode = 404;
                context.Response.Close();
            }
            catch (Exception ex)
            {
                if (ex is HttpListenerException hle && (hle.ErrorCode == 995 || hle.NativeErrorCode == 995))
                {
                    System.Diagnostics.Trace.WriteLine($"[HLS PROXY] Client disconnected (Socket 995): {hle.Message}");
                }
                else if (ex is OperationCanceledException || ex is TaskCanceledException)
                {
                    System.Diagnostics.Trace.WriteLine("[HLS PROXY] Request canceled by client");
                }
                else
                {
                    LogService.LogError("[HLS PROXY] HandleRequest error", ex);
                }
                try { context.Response.Close(); } catch { }
            }
        }

        private byte[] GenerateManifest(HlsSessionInfo session, int startSec, out long firstSeq, out long lastSeq, out int servedCount)
        {
            var sb = new StringBuilder();
            sb.Append("#EXTM3U\r\n");
            sb.Append("#EXT-X-VERSION:3\r\n");
            sb.Append($"#EXT-X-TARGETDURATION:{(int)Math.Ceiling(session.TargetDuration > 0 ? session.TargetDuration : 6.0)}\r\n");

            List<HlsSegment> segmentsToServe;

            lock (session.SyncLock)
            {
                bool isTeleportMode = startSec >= 0;

                if (!session.IsLive)
                {
                    sb.Append("#EXT-X-PLAYLIST-TYPE:VOD\r\n");
                    segmentsToServe = session.Segments.ToList();
                }
                else if (isTeleportMode)
                {
                    sb.Append("#EXT-X-PLAYLIST-TYPE:EVENT\r\n");
                    segmentsToServe = session.Segments
                        .Where(s => s.EndTimeSeconds >= startSec)
                        .Take(1000)
                        .ToList();

                    if (segmentsToServe.Count == 0)
                        segmentsToServe = session.Segments.TakeLast(14).ToList();

                    sb.Append("#EXT-X-START:TIME-OFFSET=0,PRECISE=YES\r\n");
                }
                else
                {
                    // Standard live sliding window
                    segmentsToServe = session.Segments.TakeLast(14).ToList();
                }

                firstSeq = segmentsToServe.Count > 0 ? segmentsToServe[0].SequenceNumber : 0;
                lastSeq = segmentsToServe.Count > 0 ? segmentsToServe.Last().SequenceNumber : 0;
                servedCount = segmentsToServe.Count;

                sb.Append($"#EXT-X-MEDIA-SEQUENCE:{firstSeq}\r\n");

                foreach (var seg in segmentsToServe)
                {
                    if (seg.ProgramDateTime.HasValue)
                        sb.Append($"#EXT-X-PROGRAM-DATE-TIME:{seg.ProgramDateTime.Value:yyyy-MM-ddTHH:mm:ss.fffzzz}\r\n");

                    // Double-check: UrlEncode the Base64 so special chars like '+' don't become spaces
                    string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(seg.Url));
                    string encoded = WebUtility.UrlEncode(b64);
                    string encodedSession = WebUtility.UrlEncode(session.SessionId);

                    sb.Append($"#EXTINF:{seg.DurationSeconds.ToString("0.000", CultureInfo.InvariantCulture)},\r\n");
                    sb.Append($"http://127.0.0.1:{_port}/seg/{seg.Index}.ts?session={encodedSession}&url={encoded}\r\n");
                }

                if (!session.IsLive)
                    sb.Append("#EXT-X-ENDLIST\r\n");
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private async Task<byte[]?> FetchOrGetSegmentAsync(string url, string? referer = null, HlsSessionInfo? session = null)
        {
            if (_segmentCache.TryGetValue(url, out var cached))
            {
                LogService.LogInfo($"[HLS SEGMENT] CACHE_HIT {GetShortUrl(url)} (bytes={cached.Length})");
                return cached;
            }

            if (url.StartsWith("rawts://", StringComparison.OrdinalIgnoreCase))
                return null;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                ApplyRequestHeaders(request, session, referer ?? session?.CustomReferer);

                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                sw.Stop();
                int httpStatus = (int)response.StatusCode;
                response.EnsureSuccessStatusCode();

                byte[] bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
                LogService.LogInfo($"[HLS SEGMENT] DOWNLOAD_OK http={httpStatus} duration={sw.ElapsedMilliseconds}ms bytes={bytes.Length} {GetShortUrl(url)}");

                if (_segmentCache.Count > MaxMemoryCachedSegments)
                {
                    var oldest = _segmentCache.Keys.Take(50).ToList();
                    foreach (var k in oldest) _segmentCache.TryRemove(k, out _);
                }
                _segmentCache[url] = bytes;
                return bytes;
            }
            catch (Exception ex)
            {
                sw.Stop();
                LogService.LogError($"[HLS SEGMENT] DOWNLOAD_FAIL duration={sw.ElapsedMilliseconds}ms {GetShortUrl(url)}: {ex.Message}", ex);
                return null;
            }
        }

        private string MakeAbsoluteUrl(string baseUrl, string relativeUrl)
        {
            if (string.IsNullOrWhiteSpace(relativeUrl)) return baseUrl;

            // 1. If relativeUrl is already absolute (e.g. https://cdn.example.com/seg1.ts), preserve it untouched
            if (Uri.TryCreate(relativeUrl, UriKind.Absolute, out var abs))
                return abs.ToString();

            // 2. Resolve relative path against base URL
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUriObj) && Uri.TryCreate(baseUriObj, relativeUrl, out var combined))
            {
                // If the relative URL did not specify its own query parameters, and base URL had query parameters (auth/tokens), inherit them
                if (string.IsNullOrEmpty(combined.Query) && !string.IsNullOrEmpty(baseUriObj.Query))
                {
                    var builder = new UriBuilder(combined)
                    {
                        Query = baseUriObj.Query.TrimStart('?')
                    };
                    return builder.Uri.ToString();
                }

                return combined.ToString();
            }

            return relativeUrl;
        }

        public void Dispose()
        {
            Stop();
            _httpClient.Dispose();
        }
    }
}
