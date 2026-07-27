using System;

namespace StreamMesh.Models
{
    public class MetadataResult
    {
        public string ImdbId { get; set; }
        public string Title { get; set; }
        public string PosterUrl { get; set; }
        public string BackdropUrl { get; set; }
        public string Overview { get; set; }
        public string Cast { get; set; }
        public string Director { get; set; }
        public string TrailerUrl { get; set; }
        public string ReleaseDate { get; set; }
        public double VoteAverage { get; set; }
        public string MediaType { get; set; } // movie, tv
    }
}
