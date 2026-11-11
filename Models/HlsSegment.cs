using System;

namespace StreamMesh.Models
{
    /// <summary>
    /// Represents an individual HLS media segment within a playlist or DVR window.
    /// </summary>
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
}
