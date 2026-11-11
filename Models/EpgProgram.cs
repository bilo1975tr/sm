using System;

namespace StreamMesh.Models
{
    public class EpgProgram
    {
        public int Id { get; set; }
        public string ChannelName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string SourceUrl { get; set; } = string.Empty;
    }
}
