namespace StreamMesh.Models
{
    public class AceSearchResult
    {
        public string Title { get; set; } = string.Empty;
        public string ContentId { get; set; } = string.Empty;
        public string GroupTitle { get; set; } = "AceStream";
        public string Category { get; set; } = "Canlı TV";
        public string Details { get; set; } = string.Empty;
        public int Peers { get; set; } = 0;
        public string Quality { get; set; } = "HD";
        public string Url => $"acestream://{ContentId}";
        public string DirectHttpUrl => $"http://127.0.0.1:6878/ace/getstream?id={ContentId}";
    }
}
