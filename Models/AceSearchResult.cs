using System.Text.Json.Serialization;

namespace StreamMesh.Models
{
    public class AceSearchResult
    {
        [JsonPropertyName("content_id")]
        public string ContentId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("pid")]
        public int Pid { get; set; }

        [JsonPropertyName("translated_name")]
        public string TranslatedName { get; set; }
        
        [JsonIgnore]
        public string SourceName { get; set; } = "AceStream";
    }
}
