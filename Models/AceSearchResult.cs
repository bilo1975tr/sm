using System.Text.Json.Serialization;

namespace StreamMesh.Models
{
    public class AceSearchResult
    {
        [JsonPropertyName("content_id")]
        public string ContentId { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("pid")]
        public int Pid { get; set; }

        [JsonPropertyName("peers")]
        public int Peers { get; set; }

        [JsonPropertyName("size")]
        public string Size { get; set; } = "";

        [JsonPropertyName("availability")]
        public double Availability { get; set; }

        [JsonPropertyName("translated_name")]
        public string TranslatedName { get; set; } = "";

        [JsonIgnore]
        public string SourceName { get; set; } = "AceStream";
    }
}
