using System;

namespace StreamMesh.Models
{
    public class EpgChannelSearchResult
    {
        public string EpgId { get; set; } = "";
        public string ChannelName { get; set; } = "";
        public string CurrentProgram { get; set; } = "";
        public string SourceName { get; set; } = "";
    }
}
