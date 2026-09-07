using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using StreamMesh.Core.Network;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    /// <summary>
    /// Represents an active AceStream consumer client connected to a shared AceStream session.
    /// Each subscriber owns a dedicated bounded channel and writer loop to ensure non-blocking broadcast.
    /// </summary>
    public class AceClientSubscriber
    {
        public string ClientId { get; set; } = Guid.NewGuid().ToString("N");
        public Stream OutputStream { get; set; }
        public TaskCompletionSource<bool> CompletionSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

        public Channel<byte[]> Channel { get; }
        public CancellationTokenSource WriterCts { get; } = new();
        public int IsCleanedUp;

        public AceClientSubscriber(Stream outputStream, int channelCapacity = 32)
        {
            OutputStream = outputStream;
            Channel = System.Threading.Channels.Channel.CreateBounded<byte[]>(new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
        }
    }

    /// <summary>
    /// Represents an in-memory MPEG-TS segment for AceStream DVR / Timeshift.
    /// </summary>
    public class AceDvrSegment
    {
        public int Index { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public double DurationSeconds { get; set; }
        public double StartTimeSeconds { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Bounded, thread-safe in-memory ring buffer segmenting AceStream MPEG-TS for HLS DVR playback.
    /// Protects against memory leaks by capping maximum stored history to a fixed number of segments.
    /// </summary>
    public class AceDvrBuffer
    {
        private const int MaxSegments = 180; // ~7-10 minutes DVR window (bounded memory footprint)
        private const int TargetSegmentBytes = 1024 * 1024; // 1MB target (~2-3s @ standard bitrate)
        private readonly List<byte> _accumulator = new(TargetSegmentBytes + 65536);
        private readonly ConcurrentDictionary<int, AceDvrSegment> _segmentDict = new();
        private readonly List<AceDvrSegment> _segmentList = new();
        private readonly object _syncLock = new();
        private int _nextIndex = 0;
        private double _totalDurationSec = 0;
        private DateTime _lastSegTime = DateTime.UtcNow;

        public void AppendChunk(byte[] chunk, int count)
        {
            if (chunk == null || count <= 0) return;

            lock (_syncLock)
            {
                for (int i = 0; i < count; i++)
                {
                    _accumulator.Add(chunk[i]);
                }

                var now = DateTime.UtcNow;
                double elapsed = (now - _lastSegTime).TotalSeconds;

                // Create a segment if target size reached or at least 2.5s elapsed with enough TS data
                // For the initial segment, emit quickly once initial data arrives (>= 64KB or elapsed >= 0.5s)
                bool isInitial = _segmentList.Count == 0;
                bool shouldCreate = isInitial
                    ? (_accumulator.Count >= 64 * 1024 || (elapsed >= 0.5 && _accumulator.Count >= 188 * 50))
                    : ((_accumulator.Count >= TargetSegmentBytes && elapsed >= 1.5) || elapsed >= 3.0);

                if (shouldCreate)
                {
                    if (_accumulator.Count >= 188)
                    {
                        // Align to 188-byte MPEG-TS packet boundary
                        int validBytes = (_accumulator.Count / 188) * 188;
                        byte[] segData = new byte[validBytes];
                        _accumulator.CopyTo(0, segData, 0, validBytes);

                        int leftover = _accumulator.Count - validBytes;
                        List<byte> remaining = new(leftover);
                        for (int r = validBytes; r < _accumulator.Count; r++)
                        {
                            remaining.Add(_accumulator[r]);
                        }
                        _accumulator.Clear();
                        _accumulator.AddRange(remaining);

                        double duration = Math.Max(0.5, Math.Min(6.0, elapsed > 0.1 ? elapsed : (isInitial ? 1.0 : 2.5)));
                        var seg = new AceDvrSegment
                        {
                            Index = _nextIndex++,
                            Data = segData,
                            DurationSeconds = duration,
                            StartTimeSeconds = _totalDurationSec,
                            CreatedAtUtc = now
                        };
                        _totalDurationSec += duration;
                        _lastSegTime = now;

                        _segmentDict[seg.Index] = seg;
                        _segmentList.Add(seg);

                        // Enforce bounded buffer limit (FIFO)
                        while (_segmentList.Count > MaxSegments)
                        {
                            var oldest = _segmentList[0];
                            _segmentList.RemoveAt(0);
                            _segmentDict.TryRemove(oldest.Index, out _);
                        }
                    }
                }
            }
        }

        public List<AceDvrSegment> GetSegmentsSnapshot()
        {
            lock (_syncLock)
            {
                return _segmentList.ToList();
            }
        }

        public byte[]? GetSegmentData(int index)
        {
            if (_segmentDict.TryGetValue(index, out var seg))
            {
                return seg.Data;
            }
            return null;
        }

        public double TotalDurationSeconds
        {
            get
            {
                lock (_syncLock)
                {
                    return _totalDurationSec;
                }
            }
        }

        public int SegmentCount
        {
            get
            {
                lock (_syncLock)
                {
                    return _segmentList.Count;
                }
            }
        }

        public void Clear()
        {
            lock (_syncLock)
            {
                _accumulator.Clear();
                _segmentDict.Clear();
                _segmentList.Clear();
                _totalDurationSec = 0;
                _nextIndex = 0;
            }
        }
    }

    /// <summary>
    /// Represents a shared AceStream broadcast session multiplexing a single AceEngine MPEG-TS stream to multiple connected clients.
    /// </summary>
    public class AceSharedSession
    {
        public string Hash { get; set; } = string.Empty;
        public string UpstreamUrl { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public long TotalBytesStreamed { get; set; } = 0;
        public bool IsActive => !Cts.IsCancellationRequested;
        public DateTime LastHlsAccessUtc { get; set; } = DateTime.UtcNow;

        public bool HasActiveHlsConsumers => (DateTime.UtcNow - LastHlsAccessUtc).TotalMinutes < 30;

        public readonly AceDvrBuffer DvrBuffer = new();
        public readonly CancellationTokenSource Cts = new();
        public readonly List<AceClientSubscriber> Subscribers = new();
        public readonly object SyncLock = new();

        public int SubscriberCount
        {
            get
            {
                lock (SyncLock)
                {
                    return Subscribers.Count;
                }
            }
        }
    }

    /// <summary>
    /// AceStream HTTP Bridge Service providing shared sessions, HTTP MPEG-TS proxying,
    /// and multi-client multiplexing over standard HTTP connections.
    /// </summary>
    public class AceStreamService
    {
        private static readonly Lazy<AceStreamService> _instance = new(() => new AceStreamService());
        public static AceStreamService Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, AceSharedSession> _activeSessions = new(StringComparer.OrdinalIgnoreCase);
        private readonly AceEngine _engine = new();

        private static readonly HttpClient _httpClient = new(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.None,
            AllowAutoRedirect = true,
            MaxConnectionsPerServer = 50
        }) { Timeout = TimeSpan.FromHours(24) };

        public AceStreamService()
        {
        }

        /// <summary>
        /// Gets the direct AceEngine HTTP stream URL for a given content ID or hash.
        /// </summary>
        public string GetHttpUrl(string contentId)
        {
            string hash = _engine.ExtractHash(contentId);
            if (string.IsNullOrEmpty(hash)) hash = contentId;
            return $"http://127.0.0.1:6878/ace/getstream?id={hash}";
        }

        /// <summary>
        /// Starts the local ace_engine.exe process if installed and not already running.
        /// </summary>
        public async Task<bool> EnsureEngineRunningAsync()
        {
            if (await _engine.IsEngineRunningAsync()) return true;
            LogService.LogInfo("AceStreamService: Starting AceEngine...");
            await _engine.StartEngineAsync();
            return await _engine.IsEngineRunningAsync();
        }

        /// <summary>
        /// Gets a list of currently active shared AceStream sessions for telemetry and diagnostics.
        /// </summary>
        public List<object> GetActiveSessionsSnapshot()
        {
            return _activeSessions.Values.Select(s => new
            {
                s.Hash,
                s.UpstreamUrl,
                s.StartedAt,
                Subscribers = s.SubscriberCount,
                BytesStreamed = s.TotalBytesStreamed,
                s.IsActive
            }).Cast<object>().ToList();
        }

        /// <summary>
        /// Streams an AceStream channel to an HTTP client OutputStream, sharing the underlying
        /// AceEngine session with any other concurrent viewers of the same stream.
        /// </summary>
        public async Task StreamAceChannelAsync(string contentIdOrUrl, Stream clientOutputStream, CancellationToken clientDisconnectToken)
        {
            string hash = _engine.ExtractHash(contentIdOrUrl);
            if (string.IsNullOrEmpty(hash))
            {
                throw new ArgumentException($"Invalid AceStream content ID or URL: {contentIdOrUrl}");
            }

            // Ensure AceStream Engine is running
            bool engineReady = await EnsureEngineRunningAsync();
            if (!engineReady)
            {
                LogService.LogWarning($"AceStreamService: AceEngine is not running or not installed. Attempting direct fallback connection for hash {hash}");
            }

            AceSharedSession session;
            bool isNewSession = false;

            lock (_activeSessions)
            {
                if (!_activeSessions.TryGetValue(hash, out session!) || !session.IsActive)
                {
                    session = new AceSharedSession
                    {
                        Hash = hash,
                        UpstreamUrl = $"http://127.0.0.1:6878/ace/getstream?id={hash}"
                    };
                    _activeSessions[hash] = session;
                    isNewSession = true;
                }
            }

            var subscriber = new AceClientSubscriber(clientOutputStream);
            lock (session.SyncLock)
            {
                session.Subscribers.Add(subscriber);
            }

            // Start dedicated non-blocking writer loop for this subscriber
            _ = Task.Run(() => RunSubscriberWriterLoopAsync(subscriber, session));

            LogService.LogInfo($"AceStreamBridge: Client connected to shared session [{hash}]. Total active subscribers: {session.SubscriberCount}");

            if (isNewSession)
            {
                _ = Task.Run(() => RunUpstreamBroadcastLoopAsync(session));
            }

            // Wait until client disconnects or session ends
            using var reg = clientDisconnectToken.Register(() =>
            {
                CleanupSubscriber(subscriber, session, result: true);
            });

            await subscriber.CompletionSource.Task;
        }

        private void CleanupSubscriber(AceClientSubscriber subscriber, AceSharedSession session, bool result)
        {
            if (Interlocked.Exchange(ref subscriber.IsCleanedUp, 1) != 0)
            {
                return;
            }

            try
            {
                subscriber.Channel.Writer.TryComplete();
            }
            catch { }

            try
            {
                if (!subscriber.WriterCts.IsCancellationRequested)
                {
                    subscriber.WriterCts.Cancel();
                }
            }
            catch { }

            lock (session.SyncLock)
            {
                session.Subscribers.Remove(subscriber);
            }

            subscriber.CompletionSource.TrySetResult(result);

            LogService.LogInfo($"AceStreamBridge: Subscriber [{subscriber.ClientId}] cleaned up from session [{session.Hash}]. Remaining subscribers: {session.SubscriberCount}");
            CheckAndScheduleSessionCleanup(session);
        }

        private async Task RunSubscriberWriterLoopAsync(AceClientSubscriber subscriber, AceSharedSession session)
        {
            var reader = subscriber.Channel.Reader;
            var ct = subscriber.WriterCts.Token;

            try
            {
                while (await reader.WaitToReadAsync(ct))
                {
                    while (reader.TryRead(out var chunk))
                    {
                        await subscriber.OutputStream.WriteAsync(chunk, 0, chunk.Length, ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation (client disconnect, channel completed, or session ended)
            }
            catch (Exception ex)
            {
                // Expected when client disconnects or socket drops (IOException, HttpListenerException, ObjectDisposedException)
                LogService.LogInfo($"AceStreamBridge: Subscriber [{subscriber.ClientId}] writer loop ended: {ex.Message}");
            }
            finally
            {
                CleanupSubscriber(subscriber, session, result: false);
            }
        }

        /// <summary>
        /// Gets the active shared session for the specified hash, updating HLS consumer activity.
        /// </summary>
        public AceSharedSession? GetSharedSession(string hash)
        {
            string cleanedHash = _engine.ExtractHash(hash);
            if (string.IsNullOrEmpty(cleanedHash)) cleanedHash = hash;
            if (_activeSessions.TryGetValue(cleanedHash, out var session) && session.IsActive)
            {
                session.LastHlsAccessUtc = DateTime.UtcNow;
                return session;
            }
            return null;
        }

        /// <summary>
        /// Retrieves a specific DVR segment from memory for HLS Timeshift playback.
        /// </summary>
        public byte[]? GetDvrSegment(string hash, int segIndex)
        {
            var session = GetSharedSession(hash);
            if (session != null)
            {
                session.LastHlsAccessUtc = DateTime.UtcNow;
                return session.DvrBuffer.GetSegmentData(segIndex);
            }
            return null;
        }

        /// <summary>
        /// Ensures an upstream AceStream broadcast session is active, enabling DVR buffering even before direct subscribers connect.
        /// </summary>
        public async Task<AceSharedSession> EnsureSessionStartedAsync(string contentIdOrUrl, CancellationToken cancellationToken = default)
        {
            string hash = _engine.ExtractHash(contentIdOrUrl);
            if (string.IsNullOrEmpty(hash)) hash = contentIdOrUrl;

            await EnsureEngineRunningAsync();

            AceSharedSession session;
            bool isNewSession = false;

            lock (_activeSessions)
            {
                if (!_activeSessions.TryGetValue(hash, out session!) || !session.IsActive)
                {
                    session = new AceSharedSession
                    {
                        Hash = hash,
                        UpstreamUrl = $"http://127.0.0.1:6878/ace/getstream?id={hash}",
                        StartedAt = DateTime.UtcNow,
                        LastHlsAccessUtc = DateTime.UtcNow
                    };
                    _activeSessions[hash] = session;
                    isNewSession = true;
                }
                else
                {
                    session.LastHlsAccessUtc = DateTime.UtcNow;
                }
            }

            if (isNewSession)
            {
                _ = Task.Run(() => RunUpstreamBroadcastLoopAsync(session));
            }

            return session;
        }

        private void CheckAndScheduleSessionCleanup(AceSharedSession session)
        {
            if (session.SubscriberCount == 0 && !session.HasActiveHlsConsumers)
            {
                // Grace period before closing upstream AceEngine session
                _ = Task.Run(async () =>
                {
                    await Task.Delay(5000);
                    if (session.SubscriberCount == 0 && !session.HasActiveHlsConsumers)
                    {
                        LogService.LogInfo($"AceStreamBridge: No active subscribers or HLS consumers remaining for [{session.Hash}]. Closing upstream session.");
                        session.Cts.Cancel();
                        _activeSessions.TryRemove(session.Hash, out _);
                    }
                });
            }
        }

        private async Task RunUpstreamBroadcastLoopAsync(AceSharedSession session)
        {
            var hash = session.Hash;
            var upstreamUrls = new List<string>
            {
                $"http://127.0.0.1:6878/ace/getstream?id={hash}",
                $"http://127.0.0.1:6878/ace/getstream?infohash={hash}"
            };

            int attempt = 0;
            byte[] readBuffer = new byte[64 * 1024]; // 64KB MPEG-TS read buffer

            while (!session.Cts.IsCancellationRequested && (session.SubscriberCount > 0 || session.HasActiveHlsConsumers))
            {
                string targetUrl = upstreamUrls[attempt % upstreamUrls.Count];
                attempt++;

                try
                {
                    LogService.LogInfo($"AceStreamBridge: Connecting to AceEngine upstream -> {targetUrl}");
                    using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
                    request.Headers.Add("User-Agent", "StreamMesh/2.0 AceStreamBridge");
                    request.Headers.Add("Accept", "*/*");

                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, session.Cts.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        LogService.LogWarning($"AceStreamBridge: AceEngine upstream returned HTTP {response.StatusCode} for {targetUrl}");
                        await Task.Delay(200, session.Cts.Token);
                        continue;
                    }

                    using var upstreamStream = await response.Content.ReadAsStreamAsync(session.Cts.Token);
                    LogService.LogInfo($"AceStreamBridge: MPEG-TS broadcast started for [{hash}]. Multiplexing to {session.SubscriberCount} clients (HLS active: {session.HasActiveHlsConsumers}).");

                    while (!session.Cts.IsCancellationRequested && (session.SubscriberCount > 0 || session.HasActiveHlsConsumers))
                    {
                        int bytesRead = await upstreamStream.ReadAsync(readBuffer, 0, readBuffer.Length, session.Cts.Token);
                        if (bytesRead <= 0)
                        {
                            LogService.LogWarning($"AceStreamBridge: Upstream stream ended for [{hash}]");
                            break;
                        }

                        session.TotalBytesStreamed += bytesRead;

                        // Allocate isolated chunk for queued dispatch to guarantee safe buffer ownership
                        byte[] chunk = new byte[bytesRead];
                        Buffer.BlockCopy(readBuffer, 0, chunk, 0, bytesRead);

                        // Non-blocking DVR buffer append for HLS Timeshift
                        session.DvrBuffer.AppendChunk(chunk, bytesRead);

                        // Snapshot active subscribers
                        List<AceClientSubscriber> activeSubscribers;
                        lock (session.SyncLock)
                        {
                            activeSubscribers = session.Subscribers.ToList();
                        }

                        List<AceClientSubscriber>? stalledSubscribers = null;

                        foreach (var sub in activeSubscribers)
                        {
                            // Non-blocking dispatch to subscriber's bounded queue
                            if (!sub.Channel.Writer.TryWrite(chunk))
                            {
                                stalledSubscribers ??= new List<AceClientSubscriber>();
                                stalledSubscribers.Add(sub);
                            }
                        }

                        if (stalledSubscribers != null && stalledSubscribers.Count > 0)
                        {
                            foreach (var stalled in stalledSubscribers)
                            {
                                LogService.LogWarning($"AceStreamBridge: Subscriber [{stalled.ClientId}] stalled (buffer queue full). Disconnecting slow subscriber.");
                                CleanupSubscriber(stalled, session, result: false);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogService.LogError($"AceStreamBridge: Upstream loop exception for [{hash}]: {ex.Message}", ex);
                    await Task.Delay(2000, session.Cts.Token);
                }
            }

            // Cleanup session
            session.Cts.Cancel();
            _activeSessions.TryRemove(hash, out _);

            List<AceClientSubscriber> remainingSubscribers;
            lock (session.SyncLock)
            {
                remainingSubscribers = session.Subscribers.ToList();
                session.Subscribers.Clear();
            }

            foreach (var sub in remainingSubscribers)
            {
                CleanupSubscriber(sub, session, result: true);
            }

            LogService.LogInfo($"AceStreamBridge: Shared session ended for [{hash}]. Total bytes: {session.TotalBytesStreamed}");
        }

        public void StopSession(string hash)
        {
            string cleaned = _engine.ExtractHash(hash);
            if (string.IsNullOrEmpty(cleaned)) cleaned = hash;
            if (_activeSessions.TryRemove(cleaned, out var session))
            {
                try { session.Cts.Cancel(); } catch { }
                LogService.LogInfo($"AceStreamBridge: Explicitly stopped session for [{cleaned}].");
            }
        }

        public void StopAllSessions()
        {
            foreach (var kvp in _activeSessions)
            {
                try { kvp.Value.Cts.Cancel(); } catch { }
            }
            _activeSessions.Clear();
            LogService.LogInfo("AceStreamBridge: Explicitly stopped all active sessions.");
        }
    }
}
