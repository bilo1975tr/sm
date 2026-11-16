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
using StreamMesh.Core.Network;
using StreamMesh.Core.Utils;
using StreamMesh.Models;

namespace StreamMesh.Core.Media
{
    public class HlsProxyEngine : IDisposable
    {
        private static readonly Lazy<HlsProxyEngine> _instance = new(() => new HlsProxyEngine());
        public static HlsProxyEngine Instance => _instance.Value;

        private const int MaxMemoryCachedSegments = 320;
        private const int MaxTrackedHistorySegments = 15000;
        private const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

        private static readonly Regex ExtInfRegex = new(@"#EXTINF:\s*([0-9.]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AceHashRegex = new(@"^[a-fA-F0-9]{40}$", RegexOptions.Compiled);
        private static readonly Regex SessionQueryRegex = new(@"[?&]session=([^&]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private HttpListener? _httpListener;
        private CancellationTokenSource? _cts;
        private readonly HttpClient _httpClient;
        private readonly CookieContainer _cookieContainer = new();
        private readonly LruSegmentCache _segmentCache = new(MaxMemoryCachedSegments);
        private readonly ConcurrentDictionary<string, HlsSessionInfo> _sessions = new();
        private readonly ConcurrentDictionary<string, Task<byte[]?>> _inFlightSegments = new();
        private readonly AceEngine _ace = new();
        private int _port = 48931;
        private bool _isRunning = false;

        public int LocalPort => _port;
        public bool IsRunning => _isRunning;

        private HlsProxyEngine()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                CookieContainer = _cookieContainer,
                UseCookies = true,
#if DEBUG
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
#endif
            };
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", DefaultUserAgent);
            _httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
        }

        private static string EncodeSessionId(string url) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(url)).Replace("=", "").Replace("/", "_").Replace("+", "-");

        private static string? TryDecodeSessionId(string sessionId)
        {
            try
            {
                string padded = sessionId.Replace("_", "/").Replace("-", "+");
                switch (padded.Length % 4)
                {
                    case 2: padded += "=="; break;
                    case 3: padded += "="; break;
                }
                return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            }
            catch { return null; }
        }

        private string ExtractAceHash(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var qMatch = SessionQueryRegex.Match(input);
            if (qMatch.Success)
            {
                string sess = Uri.UnescapeDataString(qMatch.Groups[1].Value);
                if (sess.StartsWith("ace_", StringComparison.OrdinalIgnoreCase))
                    return sess.Substring(4);
            }
            if (input.StartsWith("ace_", StringComparison.OrdinalIgnoreCase))
                return input.Substring(4);
            if (input.Length == 40 && AceHashRegex.IsMatch(input))
                return input;
            if (_ace.IsAceStreamUrl(input) || input.Contains(":6878/ace/") || input.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase))
                return _ace.ExtractHash(input);
            return string.Empty;
        }

        public HlsSessionInfo? GetSession(string originalUrl)
        {
            if (string.IsNullOrWhiteSpace(originalUrl)) return null;

            if (_sessions.TryGetValue(originalUrl, out var direct))
                return direct;

            var qMatch = SessionQueryRegex.Match(originalUrl);
            if (qMatch.Success)
            {
                string sessId = Uri.UnescapeDataString(qMatch.Groups[1].Value);
                if (_sessions.TryGetValue(sessId, out var byParam))
                    return byParam;
            }

            string aceHash = ExtractAceHash(originalUrl);
            if (!string.IsNullOrEmpty(aceHash))
            {
                string aceSessionId = $"ace_{aceHash}";
                if (_sessions.TryGetValue(aceSessionId, out var existingAce))
                    return existingAce;

                var aceSession = AceStreamService.Instance.GetSharedSession(aceHash);
                if (aceSession != null)
                {
                    return _sessions.GetOrAdd(aceSessionId, id => new HlsSessionInfo
                    {
                        SessionId = id,
                        OriginalUrl = originalUrl,
                        MediaPlaylistUrl = $"acestream://{aceHash}",
                        IsLive = true,
                        TargetDuration = 3.0,
                        StartWallClockTime = aceSession.StartedAt
                    });
                }
            }

            if (_sessions.TryGetValue(EncodeSessionId(originalUrl), out var encoded))
                return encoded;

            var (cleanUrl, _) = ExtractHeadersFromUrl(originalUrl);
            if (!string.IsNullOrEmpty(cleanUrl) && _sessions.TryGetValue(EncodeSessionId(cleanUrl), out var cleanEncoded))
                return cleanEncoded;

            return _sessions.Values.FirstOrDefault(s =>
                s.OriginalUrl.Equals(originalUrl, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(cleanUrl) && s.OriginalUrl.Equals(cleanUrl, StringComparison.OrdinalIgnoreCase)) ||
                s.MediaPlaylistUrl.Equals(originalUrl, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(cleanUrl) && s.MediaPlaylistUrl.Equals(cleanUrl, StringComparison.OrdinalIgnoreCase)));
        }

        public void SetSessionPaused(HlsSessionInfo? session, bool paused)
        {
            if (session == null) return;
            lock (session.SyncLock)
            {
                if (paused && !session.IsPaused)
                {
                    session.IsPaused = true;
                    session.PausedAtTicks = DateTime.UtcNow.Ticks;
                }
                else if (!paused && session.IsPaused)
                {
                    session.IsPaused = false;
                    if (session.PausedAtTicks > 0 && session.SeekAnchorTicks > 0)
                    {
                        session.SeekAnchorTicks += Math.Max(0, DateTime.UtcNow.Ticks - session.PausedAtTicks);
                    }
                    session.PausedAtTicks = 0;
                }
            }
        }

        public string GetAceStreamProxyUrl(string contentIdOrUrl, int startSec = -1)
        {
            Start();
            string hash = ExtractAceHash(contentIdOrUrl);
            if (string.IsNullOrEmpty(hash)) hash = contentIdOrUrl;
            string sessionId = $"ace_{hash}";
            return startSec >= 0
                ? $"http://127.0.0.1:{_port}/playlist.m3u8?session={sessionId}&start={startSec}&t={DateTime.UtcNow.Ticks}"
                : $"http://127.0.0.1:{_port}/playlist.m3u8?session={sessionId}&start=-1";
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
            _inFlightSegments.Clear();
        }

        private static string GetShortUrl(string url) => MediaHttpClient.GetShortUrl(url);

        public static (string CleanUrl, Dictionary<string, string> Headers) ExtractHeadersFromUrl(string rawUrl)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(rawUrl)) return ("", headers);

            int pipeIdx = rawUrl.IndexOf('|');
            if (pipeIdx == -1) return (rawUrl.Trim(), headers);

            string cleanUrl = rawUrl.Substring(0, pipeIdx).Trim();
            string headerPart = rawUrl.Substring(pipeIdx + 1).Trim();

            foreach (var part in headerPart.Split(new[] { '&', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eqIdx = part.IndexOf('=');
                if (eqIdx <= 0) continue;

                string key = part.Substring(0, eqIdx).Trim();
                string val = part.Substring(eqIdx + 1).Trim('"', '\'', ' ');

                if (key.Equals("http-user-agent", StringComparison.OrdinalIgnoreCase) || key.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
                    headers["User-Agent"] = val;
                else if (key.Equals("http-referrer", StringComparison.OrdinalIgnoreCase) || key.Equals("http-referer", StringComparison.OrdinalIgnoreCase) ||
                         key.Equals("referer", StringComparison.OrdinalIgnoreCase) || key.Equals("referrer", StringComparison.OrdinalIgnoreCase))
                    headers["Referer"] = val;
                else if (key.Equals("http-cookie", StringComparison.OrdinalIgnoreCase) || key.Equals("cookie", StringComparison.OrdinalIgnoreCase))
                    headers["Cookie"] = val;
                else if (key.Equals("http-origin", StringComparison.OrdinalIgnoreCase) || key.Equals("origin", StringComparison.OrdinalIgnoreCase))
                    headers["Origin"] = val;
                else
                    headers[key] = val;
            }

            return (cleanUrl, headers);
        }

        private void ApplyRequestHeaders(HttpRequestMessage request, HlsSessionInfo? session, string? fallbackReferer = null)
        {
            string ua = !string.IsNullOrWhiteSpace(session?.CustomUserAgent) ? session.CustomUserAgent : DefaultUserAgent;
            request.Headers.Remove("User-Agent");
            request.Headers.TryAddWithoutValidation("User-Agent", ua);

            string? referer = !string.IsNullOrWhiteSpace(session?.CustomReferer) ? session.CustomReferer : fallbackReferer;
            if (!string.IsNullOrWhiteSpace(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refUri))
                request.Headers.Referrer = refUri;

            if (!string.IsNullOrWhiteSpace(session?.CustomOrigin))
                request.Headers.TryAddWithoutValidation("Origin", session.CustomOrigin);

            if (!string.IsNullOrWhiteSpace(session?.CustomCookie))
                request.Headers.TryAddWithoutValidation("Cookie", session.CustomCookie);

            if (session?.CustomHeaders == null) return;
            foreach (var kv in session.CustomHeaders)
            {
                if (kv.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase) ||
                    kv.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase) ||
                    kv.Key.Equals("Referrer", StringComparison.OrdinalIgnoreCase) ||
                    kv.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase) ||
                    kv.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
                    continue;
                request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            }
        }

        private static void MergeResponseCookies(HttpResponseMessage response, HlsSessionInfo session)
        {
            if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies)) return;
            var cookieStr = string.Join("; ", setCookies.Select(c => c.Split(';')[0].Trim()).Where(c => !string.IsNullOrEmpty(c)));
            if (string.IsNullOrWhiteSpace(cookieStr)) return;

            session.CustomCookie = string.IsNullOrWhiteSpace(session.CustomCookie)
                ? cookieStr
                : $"{session.CustomCookie}; {cookieStr}";
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
                var (cleanUrl, pipeHeaders) = ExtractHeadersFromUrl(streamUrl);
                string targetUrl = cleanUrl;

                // Bypass non-HLS direct MPEG-TS streams
                if (targetUrl.Contains("extension=ts", StringComparison.OrdinalIgnoreCase) ||
                    targetUrl.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                    (targetUrl.Contains("live.php", StringComparison.OrdinalIgnoreCase) && !targetUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)))
                {
                    LogService.LogInfo($"[HLS PROXY] Direct MPEG-TS stream detected ({GetShortUrl(targetUrl)}), bypassing HLS proxy.");
                    return null;
                }

                if (string.IsNullOrWhiteSpace(customUserAgent) && pipeHeaders.TryGetValue("User-Agent", out var pipeUa)) customUserAgent = pipeUa;
                if (string.IsNullOrWhiteSpace(customReferer) && pipeHeaders.TryGetValue("Referer", out var pipeRef)) customReferer = pipeRef;
                if (string.IsNullOrWhiteSpace(customCookie) && pipeHeaders.TryGetValue("Cookie", out var pipeCk)) customCookie = pipeCk;
                if (string.IsNullOrWhiteSpace(customOrigin) && pipeHeaders.TryGetValue("Origin", out var pipeOg)) customOrigin = pipeOg;

                var mergedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (customHeaders != null)
                    foreach (var kv in customHeaders) mergedHeaders[kv.Key] = kv.Value;
                foreach (var kv in pipeHeaders) mergedHeaders[kv.Key] = kv.Value;

                string sessionId = EncodeSessionId(streamUrl);
                if (_sessions.TryGetValue(sessionId, out var existingSession))
                    existingSession.PollerCts?.Cancel();

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

                string manifestContent;
                using (var ctsCheck = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                using (var request = new HttpRequestMessage(HttpMethod.Get, targetUrl))
                {
                    ApplyRequestHeaders(request, session);
                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ctsCheck.Token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) return null;

                    string? mediaType = response.Content.Headers.ContentType?.MediaType;
                    if (!string.IsNullOrEmpty(mediaType) &&
                        (mediaType.Contains("video/", StringComparison.OrdinalIgnoreCase) ||
                         mediaType.Contains("audio/", StringComparison.OrdinalIgnoreCase) ||
                         mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)))
                    {
                        return null;
                    }

                    if (response.Content.Headers.ContentLength is long cl && cl > 4 * 1024 * 1024)
                        return null;

                    manifestContent = await ReadBoundedTextAsync(response.Content, 2 * 1024 * 1024, ctsCheck.Token).ConfigureAwait(false);
                    if (!manifestContent.Contains("#EXTM3U"))
                        return null;

                    MergeResponseCookies(response, session);
                }

                string mediaPlaylistUrl = targetUrl;
                if (manifestContent.Contains("#EXT-X-STREAM-INF"))
                {
                    string targetVariant = ResolveMasterVariant(manifestContent, targetUrl);
                    if (!string.IsNullOrEmpty(targetVariant) && targetVariant != targetUrl)
                    {
                        mediaPlaylistUrl = targetVariant;
                        using var ctsVariant = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        using var request = new HttpRequestMessage(HttpMethod.Get, targetVariant);
                        ApplyRequestHeaders(request, session, customReferer);
                        using var response = await _httpClient.SendAsync(request, ctsVariant.Token).ConfigureAwait(false);
                        manifestContent = await response.Content.ReadAsStringAsync(ctsVariant.Token).ConfigureAwait(false);
                        MergeResponseCookies(response, session);
                    }
                }

                session.MediaPlaylistUrl = mediaPlaylistUrl;
                ParseMediaPlaylist(session, manifestContent, mediaPlaylistUrl);

                if (session.Segments.Count > 0)
                {
                    session.StartWallClockTime = DateTime.Now.AddSeconds(-session.TotalDurationSeconds);
                    _sessions[sessionId] = session;

                    LogService.LogInfo($"[HLS PROXY] Session ready: {session.Segments.Count} segs, td={session.TargetDuration:0.0}s, live={session.IsLive}");
                    if (session.IsLive) StartLivePoller(session);
                    return session;
                }

                return null;
            }
            catch (Exception ex)
            {
                LogService.LogError($"[HLS PROXY] Inspect error ({GetShortUrl(streamUrl)}): {ex.Message}", ex);
                return null;
            }
        }

        public string GetProxyPlaybackUrl(string originalM3u8Url, double startOffsetSeconds = -1)
        {
            if (!_isRunning) Start();

            string aceHash = ExtractAceHash(originalM3u8Url);
            if (!string.IsNullOrEmpty(aceHash))
                return GetAceStreamProxyUrl(originalM3u8Url, (int)startOffsetSeconds);

            var qMatch = SessionQueryRegex.Match(originalM3u8Url);
            string sessionId = qMatch.Success
                ? Uri.UnescapeDataString(qMatch.Groups[1].Value)
                : EncodeSessionId(originalM3u8Url);

            return startOffsetSeconds >= 0
                ? $"http://127.0.0.1:{_port}/playlist.m3u8?session={sessionId}&start={(int)startOffsetSeconds}&t={DateTime.UtcNow.Ticks}"
                : $"http://127.0.0.1:{_port}/playlist.m3u8?session={sessionId}&start=-1";
        }

        private static async Task<string> ReadBoundedTextAsync(HttpContent content, int maxBytes, CancellationToken ct)
        {
            await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            byte[] buffer = new byte[Math.Min(8192, maxBytes)];
            using var ms = new MemoryStream();
            int totalRead = 0;

            while (totalRead < maxBytes)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maxBytes - totalRead)), ct).ConfigureAwait(false);
                if (read <= 0) break;

                if (totalRead == 0 && read >= 4 && (buffer[0] == 0x47 || (buffer[0] == 0x00 && buffer[1] == 0x00)))
                    return "";

                ms.Write(buffer, 0, read);
                totalRead += read;
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private void StartLivePoller(HlsSessionInfo session)
        {
            session.PollerCts?.Cancel();
            session.PollerCts = new CancellationTokenSource();
            var ct = session.PollerCts.Token;

            Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        double pollDelay = Math.Max(1.5, session.TargetDuration / 2.0);
                        await Task.Delay(TimeSpan.FromSeconds(pollDelay), ct).ConfigureAwait(false);

                        using var request = new HttpRequestMessage(HttpMethod.Get, session.MediaPlaylistUrl);
                        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
                        request.Headers.Pragma.Add(new System.Net.Http.Headers.NameValueHeaderValue("no-cache"));
                        ApplyRequestHeaders(request, session, session.CustomReferer);

                        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                        string updatedManifest = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                        ParseMediaPlaylist(session, updatedManifest, session.MediaPlaylistUrl);

                        List<string> latestUrls;
                        lock (session.SyncLock)
                        {
                            latestUrls = session.Segments.TakeLast(3).Select(s => s.Url).ToList();
                        }

                        // Warm latest 3 segments into RAM cache so live edge & short rewinds never block on network
                        foreach (var segUrl in latestUrls)
                        {
                            if (!string.IsNullOrEmpty(segUrl) && !_segmentCache.ContainsKey(segUrl) && !_inFlightSegments.ContainsKey(segUrl))
                                _ = FetchOrGetSegmentAsync(segUrl, session.CustomReferer, session);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.WriteLine($"[HLS PROXY] Poller error ({session.SessionId}): {ex.Message}");
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
                        return MakeAbsoluteUrl(baseUrl, uriLine);
                }
            }
            return baseUrl;
        }

        private void ParseMediaPlaylist(HlsSessionInfo session, string content, string baseUrl)
        {
            using var reader = new StringReader(content);
            string? line;
            double pendingDuration = 0;
            bool isEndList = false;
            bool pendingDiscontinuity = false;
            double targetDuration = 6.0;
            DateTime? currentProgramTime = null;
            var parsed = new List<HlsSegment>();

            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("#EXT-X-TARGETDURATION:"))
                {
                    if (double.TryParse(line.Substring(22).Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double td))
                        targetDuration = td;
                }
                else if (line.StartsWith("#EXT-X-PROGRAM-DATE-TIME:"))
                {
                    if (DateTime.TryParse(line.Substring(25).Trim(), null, DateTimeStyles.RoundtripKind, out DateTime dt))
                        currentProgramTime = dt.ToLocalTime();
                }
                else if (line.StartsWith("#EXT-X-DISCONTINUITY", StringComparison.OrdinalIgnoreCase))
                {
                    pendingDiscontinuity = true;
                }
                else if (line.StartsWith("#EXT-X-ENDLIST"))
                {
                    isEndList = true;
                }
                else if (line.StartsWith("#EXTINF:"))
                {
                    var match = ExtInfRegex.Match(line);
                    pendingDuration = match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double dur)
                        ? dur
                        : (targetDuration > 0 ? targetDuration : 6.0);
                }
                else if (!line.StartsWith("#"))
                {
                    double segDur = pendingDuration > 0 ? pendingDuration : targetDuration;
                    parsed.Add(new HlsSegment
                    {
                        Url = MakeAbsoluteUrl(baseUrl, line),
                        DurationSeconds = segDur,
                        ProgramDateTime = currentProgramTime,
                        HasDiscontinuity = pendingDiscontinuity
                    });

                    if (currentProgramTime.HasValue)
                        currentProgramTime = currentProgramTime.Value.AddSeconds(segDur);

                    pendingDuration = 0;
                    pendingDiscontinuity = false;
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
                    for (int i = 0; i < parsed.Count; i++)
                    {
                        var s = parsed[i];
                        s.Index = i;
                        s.SequenceNumber = i; // Strictly monotonic local sequence for FFmpeg stability
                        s.StartTimeSeconds = curTime;
                        curTime += s.DurationSeconds;
                        session.Segments.Add(s);
                    }
                    session.MediaSequence = parsed.Count;
                    session.TotalDurationSeconds = curTime;

                    if (session.Segments.Count > 0)
                    {
                        session.StartWallClockTime = session.Segments[0].ProgramDateTime ?? DateTime.Now.AddSeconds(-session.TotalDurationSeconds);
                    }
                }
                else
                {
                    var existingUrls = new HashSet<string>(session.Segments.Select(s => s.Url));
                    double curTime = session.Segments[^1].EndTimeSeconds;
                    int nextIdx = session.Segments[^1].Index + 1;
                    long nextSeq = session.Segments[^1].SequenceNumber + 1;

                    foreach (var s in parsed)
                    {
                        if (!existingUrls.Add(s.Url)) continue;
                        s.Index = nextIdx++;
                        s.SequenceNumber = nextSeq++; // Monotonic increment prevents demuxer stalls on upstream resets
                        s.StartTimeSeconds = curTime;
                        curTime += s.DurationSeconds;
                        session.Segments.Add(s);
                    }
                    session.MediaSequence = nextSeq;
                    session.TotalDurationSeconds = curTime;

                    if (session.Segments.Count > MaxTrackedHistorySegments)
                    {
                        int trimCount = session.Segments.Count - MaxTrackedHistorySegments + 500;
                        session.Segments.RemoveRange(0, trimCount);
                        for (int i = 0; i < session.Segments.Count; i++) session.Segments[i].Index = i;
                        if (session.SeekAnchorIndex >= 0)
                            session.SeekAnchorIndex = Math.Max(0, session.SeekAnchorIndex - trimCount);
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
                    var context = await _httpListener.GetContextAsync().ConfigureAwait(false);
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
            var res = context.Response;
            try
            {
                string path = context.Request.Url?.AbsolutePath ?? "";
                var query = context.Request.QueryString;

                if (path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
                {
                    string sessionId = query["session"] ?? "";
                    int startSec = int.TryParse(query["start"], out int parsedStart) ? parsedStart : -1;

                    if (!_sessions.TryGetValue(sessionId, out var session))
                    {
                        if (sessionId.StartsWith("ace_", StringComparison.OrdinalIgnoreCase))
                        {
                            session = GetSession(sessionId);
                        }
                        else
                        {
                            string? origUrl = TryDecodeSessionId(sessionId);
                            if (!string.IsNullOrEmpty(origUrl))
                                session = await InspectAndPrepareHlsAsync(origUrl).ConfigureAwait(false);
                        }
                    }

                    if (session != null)
                    {
                        byte[] m3u8Bytes = GenerateManifest(session, startSec);
                        res.ContentType = "application/vnd.apple.mpegurl";
                        res.Headers["Cache-Control"] = "no-store, no-cache";
                        res.StatusCode = 200;
                        res.ContentLength64 = m3u8Bytes.Length;
                        await res.OutputStream.WriteAsync(m3u8Bytes).ConfigureAwait(false);
                        return;
                    }
                }
                else if (path.StartsWith("/seg/", StringComparison.OrdinalIgnoreCase))
                {
                    string sessionId = query["session"] ?? "";
                    string encodedUrl = query["url"] ?? "";
                    if (!string.IsNullOrEmpty(encodedUrl))
                    {
                        string segUrl = Encoding.UTF8.GetString(Convert.FromBase64String(encodedUrl));
                        _sessions.TryGetValue(sessionId, out var session);

                        byte[]? data = await FetchOrGetSegmentAsync(segUrl, session?.CustomReferer, session).ConfigureAwait(false);
                        if (data != null)
                        {
                            res.ContentType = "video/MP2T";
                            res.Headers["Cache-Control"] = "public, max-age=300";
                            res.StatusCode = 200;
                            res.ContentLength64 = data.Length;
                            await res.OutputStream.WriteAsync(data).ConfigureAwait(false);
                            return;
                        }
                    }
                }

                res.StatusCode = 404;
            }
            catch (HttpListenerException) { }
            catch (IOException) { }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogService.LogError("[HLS PROXY] HandleRequest error", ex);
            }
            finally
            {
                try { res.Close(); } catch { }
            }
        }

        private byte[] GenerateManifest(HlsSessionInfo session, int startSec)
        {
            var sb = new StringBuilder(2048);
            sb.Append("#EXTM3U\r\n#EXT-X-VERSION:3\r\n");
            sb.Append($"#EXT-X-TARGETDURATION:{(int)Math.Ceiling(session.TargetDuration > 0 ? session.TargetDuration : 6.0)}\r\n");

            List<HlsSegment> segmentsToServe;

            lock (session.SyncLock)
            {
                bool isTeleportMode = startSec >= 0 && session.Segments.Count > 0;

                if (!session.IsLive)
                {
                    sb.Append("#EXT-X-PLAYLIST-TYPE:VOD\r\n");
                    segmentsToServe = session.Segments.ToList();
                }
                else if (isTeleportMode)
                {
                    int targetIdx = session.Segments.FindIndex(s => s.EndTimeSeconds >= startSec);
                    if (targetIdx < 0) targetIdx = Math.Max(0, session.Segments.Count - 1);

                    if (session.SeekAnchorIndex == -1 || Math.Abs(session.LastAnchorStartSec - startSec) > 1.0)
                    {
                        session.SeekAnchorIndex = targetIdx;
                        session.SeekAnchorTicks = session.IsPaused && session.PausedAtTicks > 0 ? session.PausedAtTicks : DateTime.UtcNow.Ticks;
                        session.LastAnchorStartSec = startSec;
                    }

                    long refTicks = session.IsPaused && session.PausedAtTicks > 0 ? session.PausedAtTicks : DateTime.UtcNow.Ticks;
                    double elapsed = Math.Max(0, (refTicks - session.SeekAnchorTicks) / (double)TimeSpan.TicksPerSecond);

                    int windowStart = Math.Clamp(session.SeekAnchorIndex, 0, Math.Max(0, session.Segments.Count - 1));
                    double accumSec = 0;
                    while (windowStart < session.Segments.Count - 1)
                    {
                        double segDur = Math.Max(0.5, session.Segments[windowStart].DurationSeconds);
                        if (accumSec + segDur > elapsed) break;
                        accumSec += segDur;
                        windowStart++;
                    }

                    if (!session.IsPaused && (windowStart >= session.Segments.Count - 3 || windowStart < session.SeekAnchorIndex))
                    {
                        session.SeekAnchorIndex = -1;
                        segmentsToServe = session.Segments.TakeLast(14).ToList();
                    }
                    else
                    {
                        segmentsToServe = session.Segments.Skip(windowStart).Take(16).ToList();
                    }
                }
                else
                {
                    segmentsToServe = session.Segments.TakeLast(14).ToList();
                }

                long firstSeq = segmentsToServe.Count > 0 ? segmentsToServe[0].SequenceNumber : 0;
                sb.Append($"#EXT-X-MEDIA-SEQUENCE:{firstSeq}\r\n");

                string encodedSession = WebUtility.UrlEncode(session.SessionId);
                for (int i = 0; i < segmentsToServe.Count; i++)
                {
                    var seg = segmentsToServe[i];
                    if (i > 0 && seg.HasDiscontinuity)
                        sb.Append("#EXT-X-DISCONTINUITY\r\n");

                    if (seg.ProgramDateTime.HasValue)
                        sb.Append($"#EXT-X-PROGRAM-DATE-TIME:{seg.ProgramDateTime.Value:yyyy-MM-ddTHH:mm:ss.fffzzz}\r\n");

                    string encoded = WebUtility.UrlEncode(Convert.ToBase64String(Encoding.UTF8.GetBytes(seg.Url)));
                    sb.Append($"#EXTINF:{seg.DurationSeconds.ToString("0.000", CultureInfo.InvariantCulture)},\r\n");
                    sb.Append($"http://127.0.0.1:{_port}/seg/{seg.Index}.ts?session={encodedSession}&url={encoded}\r\n");
                }

                if (!session.IsLive) sb.Append("#EXT-X-ENDLIST\r\n");
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private Task<byte[]?> FetchOrGetSegmentAsync(string url, string? referer = null, HlsSessionInfo? session = null)
        {
            if (_segmentCache.TryGetValue(url, out var cached) && cached != null)
                return Task.FromResult<byte[]?>(cached);

            return _inFlightSegments.GetOrAdd(url, _ => DownloadAndCacheSegmentAsync(url, referer, session));
        }

        private async Task<byte[]?> DownloadAndCacheSegmentAsync(string url, string? referer, HlsSessionInfo? session)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                ApplyRequestHeaders(request, session, referer ?? session?.CustomReferer);

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                byte[] bytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
                _segmentCache.Set(url, bytes);
                return bytes;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[HLS SEGMENT] Download fail ({GetShortUrl(url)}): {ex.Message}");
                return null;
            }
            finally
            {
                _inFlightSegments.TryRemove(url, out _);
            }
        }

        private static string MakeAbsoluteUrl(string baseUrl, string relativeUrl)
        {
            if (string.IsNullOrWhiteSpace(relativeUrl)) return baseUrl;
            if (Uri.TryCreate(relativeUrl, UriKind.Absolute, out var abs)) return abs.ToString();

            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUriObj) && Uri.TryCreate(baseUriObj, relativeUrl, out var combined))
            {
                if (string.IsNullOrEmpty(combined.Query) && !string.IsNullOrEmpty(baseUriObj.Query))
                {
                    var builder = new UriBuilder(combined) { Query = baseUriObj.Query.TrimStart('?') };
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
