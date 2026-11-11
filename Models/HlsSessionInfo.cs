using System;
using System.Collections.Generic;
using System.Threading;

namespace StreamMesh.Models
{
    /// <summary>
    /// Represents active HLS proxy session metadata, segments, headers, and time-shift state.
    /// </summary>
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
        public int SeekAnchorIndex { get; set; } = -1;
        public long SeekAnchorTicks { get; set; } = 0;
        public double LastAnchorStartSec { get; set; } = -1;
    }
}
