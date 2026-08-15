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
    }

    public class HlsProxyEngine : IDisposable
    {
        private static readonly Lazy<HlsProxyEngine> _instance = new(() => new HlsProxyEngine());
        public static HlsProxyEngine Instance => _instance.Value;

        private HttpListener? _httpListener;
        private CancellationTokenSource? _cts;
        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<string, byte[]> _segmentCache = new();
        private readonly ConcurrentDictionary<string, HlsSessionInfo> _sessions = new();
        private int _port = 48931;
        private bool _isRunning = false;

        public int LocalPort => _port;
        public bool IsRunning => _isRunning;

        private HlsProxyEngine()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
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

        public async Task<HlsSessionInfo?> InspectAndPrepareHlsAsync(string m3u8Url)
        {
            if (string.IsNullOrWhiteSpace(m3u8Url)) return null;

            try
            {
                string manifestContent = await _httpClient.GetStringAsync(m3u8Url);
                if (!manifestContent.Contains("#EXTM3U"))
                {
                    return null;
                }

                string mediaPlaylistUrl = m3u8Url;

                // If Master Playlist with multiple variants, resolve highest/first media stream
                if (manifestContent.Contains("#EXT-X-STREAM-INF"))
                {
                    string targetVariant = ResolveMasterVariant(manifestContent, m3u8Url);
                    if (!string.IsNullOrEmpty(targetVariant) && targetVariant != m3u8Url)
                    {
                        mediaPlaylistUrl = targetVariant;
                        manifestContent = await _httpClient.GetStringAsync(targetVariant);
                    }
                }

                string sessionId = Convert.ToBase64String(Encoding.UTF8.GetBytes(m3u8Url)).Replace("=", "").Replace("/", "_").Replace("+", "-");
                
                // Stop any existing session poller
                if (_sessions.TryGetValue(sessionId, out var existingSession))
                {
                    existingSession.PollerCts?.Cancel();
                }

                var session = new HlsSessionInfo
                {
                    SessionId = sessionId,
                    OriginalUrl = m3u8Url,
                    MediaPlaylistUrl = mediaPlaylistUrl,
                    LastRefreshedUtc = DateTime.UtcNow,
                    StartWallClockTime = DateTime.Now
                };

                ParseMediaPlaylist(session, manifestContent, mediaPlaylistUrl);

                _sessions[sessionId] = session;

                // Start continuous background live poller & TS prefetcher if live
                if (session.IsLive)
                {
                    StartLivePoller(session);
                }

                return session;
            }
            catch (Exception ex)
            {
                LogService.LogError($"HlsProxy: HLS çözümleme hatası ({m3u8Url})", ex);
                return null;
            }
        }

        public string GetProxyPlaybackUrl(string originalM3u8Url, double startOffsetSeconds = -1)
        {
            if (!_isRunning) Start();
            string sessionId = Convert.ToBase64String(Encoding.UTF8.GetBytes(originalM3u8Url)).Replace("=", "").Replace("/", "_").Replace("+", "-");
            return $"http://127.0.0.1:{_port}/playlist.m3u8?session={sessionId}&start={(int)startOffsetSeconds}";
        }

        private void StartLivePoller(HlsSessionInfo session)
        {
            session.PollerCts?.Cancel();
            session.PollerCts = new CancellationTokenSource();
            var ct = session.PollerCts.Token;

            Task.Run(async () =>
            {
                LogService.LogInfo($"HlsProxy: Canlı TS arka plan indirici/tamponlayıcı başlatıldı (Orijinal: {session.MediaPlaylistUrl})");
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        double pollDelay = Math.Max(1.5, session.TargetDuration / 2.0);
                        await Task.Delay(TimeSpan.FromSeconds(pollDelay), ct);

                        string updatedManifest = await _httpClient.GetStringAsync(session.MediaPlaylistUrl);
                        ParseMediaPlaylist(session, updatedManifest, session.MediaPlaylistUrl);

                        // Proactively pre-download and buffer the latest 3 incoming TS segments in background!
                        List<string> latestSegmentUrls;
                        lock (session.SyncLock)
                        {
                            latestSegmentUrls = session.Segments.TakeLast(4).Select(s => s.Url).ToList();
                        }

                        foreach (var segUrl in latestSegmentUrls)
                        {
                            if (!string.IsNullOrEmpty(segUrl) && !_segmentCache.ContainsKey(segUrl))
                            {
                                _ = FetchOrGetSegmentAsync(segUrl);
                            }
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        // Silent retry on network glitch
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

            var newSegments = new List<HlsSegment>();

            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("#EXT-X-TARGETDURATION:"))
                {
                    if (double.TryParse(line.Substring(22).Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double td))
                    {
                        targetDuration = td;
                    }
                }
                else if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE:"))
                {
                    if (long.TryParse(line.Substring(22).Trim(), out long seq))
                    {
                        mediaSequence = seq;
                    }
                }
                else if (line.StartsWith("#EXT-X-ENDLIST"))
                {
                    isEndList = true;
                }
                else if (line.StartsWith("#EXTINF:"))
                {
                    var match = Regex.Match(line, @"#EXTINF:\s*([0-9.]+)", RegexOptions.IgnoreCase);
                    if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double dur))
                    {
                        pendingDuration = dur;
                    }
                    else
                    {
                        pendingDuration = targetDuration > 0 ? targetDuration : 6.0;
                    }
                }
                else if (!line.StartsWith("#"))
                {
                    string absUrl = MakeAbsoluteUrl(baseUrl, line);
                    newSegments.Add(new HlsSegment
                    {
                        Url = absUrl,
                        DurationSeconds = pendingDuration > 0 ? pendingDuration : targetDuration,
                        SequenceNumber = mediaSequence++
                    });
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
                }
                else
                {
                    // Merge newly appeared segments into existing rolling session
                    var existingUrls = new HashSet<string>(session.Segments.Select(s => s.Url));
                    double curTime = session.Segments.Count > 0 ? session.Segments.Last().EndTimeSeconds : 0;
                    int idx = session.Segments.Count;

                    foreach (var s in newSegments)
                    {
                        if (!existingUrls.Contains(s.Url))
                        {
                            s.Index = idx++;
                            s.StartTimeSeconds = curTime;
                            curTime += s.DurationSeconds;
                            session.Segments.Add(s);
                        }
                    }
                    session.TotalDurationSeconds = curTime;

                    // Cap maximum retained segments to ~2500 (~4-5 hours of Timeshift buffer)
                    if (session.Segments.Count > 2500)
                    {
                        session.Segments.RemoveRange(0, 500);
                    }
                }

                session.HasDvrWindow = session.TotalDurationSeconds >= 45 || session.Segments.Count >= 8;
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
                    {
                        startSec = parsedStart;
                    }

                    if (_sessions.TryGetValue(sessionId, out var session))
                    {
                        byte[] m3u8Bytes = GenerateManifest(session, startSec);
                        context.Response.ContentType = "application/vnd.apple.mpegurl";
                        context.Response.StatusCode = 200;
                        context.Response.ContentLength64 = m3u8Bytes.Length;
                        await context.Response.OutputStream.WriteAsync(m3u8Bytes);
                        context.Response.OutputStream.Close();
                        return;
                    }
                }
                else if (path.StartsWith("/seg/"))
                {
                    string encodedUrl = query["url"] ?? "";
                    if (!string.IsNullOrEmpty(encodedUrl))
                    {
                        string segUrl = Encoding.UTF8.GetString(Convert.FromBase64String(encodedUrl));
                        byte[]? data = await FetchOrGetSegmentAsync(segUrl);
                        if (data != null)
                        {
                            context.Response.ContentType = "video/MP2T";
                            context.Response.StatusCode = 200;
                            context.Response.ContentLength64 = data.Length;
                            await context.Response.OutputStream.WriteAsync(data);
                            context.Response.OutputStream.Close();
                            return;
                        }
                    }
                }

                context.Response.StatusCode = 404;
                context.Response.Close();
            }
            catch
            {
                try { context.Response.Close(); } catch { }
            }
        }

        private byte[] GenerateManifest(HlsSessionInfo session, int startSec)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#EXTM3U");
            sb.AppendLine("#EXT-X-VERSION:3");
            sb.AppendLine($"#EXT-X-TARGETDURATION:{(int)Math.Ceiling(session.TargetDuration > 0 ? session.TargetDuration : 6.0)}");

            List<HlsSegment> segmentsToServe;

            lock (session.SyncLock)
            {
                if (startSec < 0)
                {
                    // Live Mode: Serve last 10 segments with matching media sequence
                    segmentsToServe = session.Segments.TakeLast(10).ToList();
                    long firstSeq = segmentsToServe.Count > 0 ? segmentsToServe[0].SequenceNumber : 0;
                    sb.AppendLine($"#EXT-X-MEDIA-SEQUENCE:{firstSeq}");
                }
                else
                {
                    // Timeshift / Rewound Mode: Serve from target second as an continuous EVENT / VOD playlist
                    sb.AppendLine("#EXT-X-PLAYLIST-TYPE:EVENT");
                    sb.AppendLine("#EXT-X-MEDIA-SEQUENCE:0");
                    segmentsToServe = session.Segments
                        .Where(s => s.EndTimeSeconds >= startSec)
                        .ToList();

                    if (segmentsToServe.Count == 0)
                    {
                        segmentsToServe = session.Segments.TakeLast(10).ToList();
                    }
                }

                foreach (var seg in segmentsToServe)
                {
                    string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(seg.Url));
                    sb.AppendLine($"#EXTINF:{seg.DurationSeconds.ToString("0.000", CultureInfo.InvariantCulture)},");
                    sb.AppendLine($"http://127.0.0.1:{_port}/seg/{seg.Index}?url={encoded}");
                }

                if (!session.IsLive)
                {
                    sb.AppendLine("#EXT-X-ENDLIST");
                }
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private async Task<byte[]?> FetchOrGetSegmentAsync(string url)
        {
            if (_segmentCache.TryGetValue(url, out var cached))
            {
                return cached;
            }

            try
            {
                byte[] bytes = await _httpClient.GetByteArrayAsync(url);
                
                // Ring buffer cache up to 500 segments (~45-60 min TS data)
                if (_segmentCache.Count > 500)
                {
                    var oldest = _segmentCache.Keys.Take(100).ToList();
                    foreach (var k in oldest) _segmentCache.TryRemove(k, out _);
                }
                _segmentCache[url] = bytes;
                return bytes;
            }
            catch
            {
                return null;
            }
        }

        private string MakeAbsoluteUrl(string baseUrl, string relativeUrl)
        {
            if (Uri.TryCreate(relativeUrl, UriKind.Absolute, out var abs))
            {
                return abs.ToString();
            }
            if (Uri.TryCreate(new Uri(baseUrl), relativeUrl, out var combined))
            {
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
