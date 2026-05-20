using System;

namespace StreamMesh.Models
{
    public class EpgProgram
    {
        public int Id { get; set; }
        public string ChannelName { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string SourceUrl { get; set; }
    }
}
