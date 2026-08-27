using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using StreamMesh.Core.Network;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    /// <summary>
    /// Represents an active AceStream consumer client connected to a shared AceStream session.
    /// </summary>
    public class AceClientSubscriber
    {
        public string ClientId { get; set; } = Guid.NewGuid().ToString("N");
        public Stream OutputStream { get; set; }
        public TaskCompletionSource<bool> CompletionSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

        public AceClientSubscriber(Stream outputStream)
        {
            OutputStream = outputStream;
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

            LogService.LogInfo($"AceStreamBridge: Client connected to shared session [{hash}]. Total active subscribers: {session.SubscriberCount}");

            if (isNewSession)
            {
                _ = Task.Run(() => RunUpstreamBroadcastLoopAsync(session));
            }

            // Wait until client disconnects or session ends
            using var reg = clientDisconnectToken.Register(() =>
            {
                lock (session.SyncLock)
                {
                    session.Subscribers.Remove(subscriber);
                }
                subscriber.CompletionSource.TrySetResult(true);
                LogService.LogInfo($"AceStreamBridge: Client disconnected from session [{hash}]. Remaining subscribers: {session.SubscriberCount}");

                CheckAndScheduleSessionCleanup(session);
            });

            await subscriber.CompletionSource.Task;
        }

        private void CheckAndScheduleSessionCleanup(AceSharedSession session)
        {
            if (session.SubscriberCount == 0)
            {
                // Grace period before closing upstream AceEngine session
                _ = Task.Run(async () =>
                {
                    await Task.Delay(3000);
                    if (session.SubscriberCount == 0)
                    {
                        LogService.LogInfo($"AceStreamBridge: No active subscribers remaining for [{session.Hash}]. Closing upstream session.");
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
            byte[] buffer = new byte[64 * 1024]; // 64KB MPEG-TS buffer

            while (!session.Cts.IsCancellationRequested && session.SubscriberCount > 0)
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
                        await Task.Delay(1500, session.Cts.Token);
                        continue;
                    }

                    using var upstreamStream = await response.Content.ReadAsStreamAsync(session.Cts.Token);
                    LogService.LogInfo($"AceStreamBridge: MPEG-TS broadcast started for [{hash}]. Multiplexing to {session.SubscriberCount} clients.");

                    while (!session.Cts.IsCancellationRequested && session.SubscriberCount > 0)
                    {
                        int bytesRead = await upstreamStream.ReadAsync(buffer, 0, buffer.Length, session.Cts.Token);
                        if (bytesRead <= 0)
                        {
                            LogService.LogWarning($"AceStreamBridge: Upstream stream ended for [{hash}]");
                            break;
                        }

                        session.TotalBytesStreamed += bytesRead;

                        // Broadcast chunk to all active subscribers in parallel
                        List<AceClientSubscriber> activeSubscribers;
                        lock (session.SyncLock)
                        {
                            activeSubscribers = session.Subscribers.ToList();
                        }

                        var deadSubscribers = new List<AceClientSubscriber>();

                        foreach (var sub in activeSubscribers)
                        {
                            try
                            {
                                await sub.OutputStream.WriteAsync(buffer, 0, bytesRead, session.Cts.Token);
                            }
                            catch (Exception)
                            {
                                // Client connection dropped
                                deadSubscribers.Add(sub);
                            }
                        }

                        if (deadSubscribers.Count > 0)
                        {
                            lock (session.SyncLock)
                            {
                                foreach (var dead in deadSubscribers)
                                {
                                    session.Subscribers.Remove(dead);
                                    dead.CompletionSource.TrySetResult(false);
                                }
                            }
                            LogService.LogInfo($"AceStreamBridge: Removed {deadSubscribers.Count} disconnected subscribers from [{hash}]. Remaining: {session.SubscriberCount}");
                            CheckAndScheduleSessionCleanup(session);
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

            lock (session.SyncLock)
            {
                foreach (var sub in session.Subscribers)
                {
                    sub.CompletionSource.TrySetResult(true);
                }
                session.Subscribers.Clear();
            }

            LogService.LogInfo($"AceStreamBridge: Shared session ended for [{hash}]. Total bytes: {session.TotalBytesStreamed}");
        }
    }
}
