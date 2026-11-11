using System;

namespace StreamMesh.Models
{
    public class MetadataResult
    {
        public string ImdbId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string PosterUrl { get; set; } = string.Empty;
        public string BackdropUrl { get; set; } = string.Empty;
        public string Overview { get; set; } = string.Empty;
        public string Cast { get; set; } = string.Empty;
        public string Director { get; set; } = string.Empty;
        public string TrailerUrl { get; set; } = string.Empty;
        public string ReleaseDate { get; set; } = string.Empty;
        public double VoteAverage { get; set; }
        public string MediaType { get; set; } = string.Empty; // movie, tv
    }
}
