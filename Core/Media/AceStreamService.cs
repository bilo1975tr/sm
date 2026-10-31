using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class AceDvrSegment
    {
        public int Index { get; set; }
        public double DurationSeconds { get; set; } = 3.0;
        public double StartTimeSeconds { get; set; }
        public byte[]? Data { get; set; }
    }

    public class AceDvrBuffer
    {
        private readonly List<AceDvrSegment> _segments = new List<AceDvrSegment>();
        private readonly object _lock = new object();

        public int SegmentCount
        {
            get { lock (_lock) return _segments.Count; }
        }

        public double TotalDurationSeconds
        {
            get { lock (_lock) return _segments.Count * 3.0; }
        }

        public List<AceDvrSegment> GetSegmentsSnapshot()
        {
            lock (_lock) return new List<AceDvrSegment>(_segments);
        }

        public void AddSegment(AceDvrSegment seg)
        {
            lock (_lock)
            {
                _segments.Add(seg);
                if (_segments.Count > 100) _segments.RemoveAt(0);
            }
        }

        public byte[]? GetSegmentData(int index)
        {
            lock (_lock)
            {
                var s = _segments.FirstOrDefault(x => x.Index == index);
                return s?.Data;
            }
        }
    }

    public class AceStreamSession
    {
        public string Hash { get; set; } = "";
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastHlsAccessUtc { get; set; } = DateTime.UtcNow;
        public AceDvrBuffer DvrBuffer { get; } = new AceDvrBuffer();
    }

    public class AceStreamService
    {
        private static readonly Lazy<AceStreamService> _lazy = new Lazy<AceStreamService>(() => new AceStreamService());
        public static AceStreamService Instance => _lazy.Value;

        private readonly ConcurrentDictionary<string, AceStreamSession> _sessions = new ConcurrentDictionary<string, AceStreamSession>(StringComparer.OrdinalIgnoreCase);
        private readonly AceEngine _engine = AceEngine.Instance;

        private AceStreamService() { }

        public void RegisterSession(string contentId, string? streamUrl = null)
        {
            if (string.IsNullOrWhiteSpace(contentId)) return;
            var session = _sessions.GetOrAdd(contentId, id => new AceStreamSession { Hash = id });
            session.LastHlsAccessUtc = DateTime.UtcNow;
            LogService.LogInfo($"[ACESTREAM] Session registered: {contentId} (Total active: {_sessions.Count})");
        }

        public void UnregisterSession(string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId)) return;
            _sessions.TryRemove(contentId, out _);
            LogService.LogInfo($"[ACESTREAM] Session unregistered: {contentId}");
        }

        public void StopAllSessions()
        {
            _sessions.Clear();
            _ = _engine.StopAllStreamsAsync();
        }

        public List<string> GetActiveSessionsSnapshot()
        {
            return _sessions.Keys.ToList();
        }

        public AceStreamSession? GetSharedSession(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash)) return null;
            _sessions.TryGetValue(hash, out var session);
            return session;
        }

        public byte[]? GetDvrSegment(string hash, int segIndex)
        {
            var session = GetSharedSession(hash);
            return session?.DvrBuffer.GetSegmentData(segIndex);
        }

        public async Task<AceStreamSession> EnsureSessionStartedAsync(string hash, CancellationToken token = default)
        {
            var session = _sessions.GetOrAdd(hash, h => new AceStreamSession { Hash = h, StartedAt = DateTime.UtcNow });
            session.LastHlsAccessUtc = DateTime.UtcNow;
            await _engine.StartEngineIfNotRunningAsync().ConfigureAwait(false);
            return session;
        }

        public Task<bool> EnsureEngineAliveAsync() => _engine.StartEngineIfNotRunningAsync();

        public string ExtractHash(string url) => _engine.ExtractAceId(url);
    }
}
