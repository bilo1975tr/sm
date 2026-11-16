using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class AceStreamSession
    {
        public string Hash { get; set; } = "";
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastHlsAccessUtc { get; set; } = DateTime.UtcNow;
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
    }
}
