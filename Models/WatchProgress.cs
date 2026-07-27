using System;

namespace StreamMesh.Models
{
    public class WatchProgress
    {
        public string ChannelId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public long Seconds { get; set; }
        public long Duration { get; set; }
        public DateTime LastWatched { get; set; } = DateTime.Now;

        // Progress percentage for UI representation (e.g. progress bar)
        public double Percentage => Duration > 0 ? Math.Min(100.0, (double)Seconds / Duration * 100.0) : 0.0;
    }
}
