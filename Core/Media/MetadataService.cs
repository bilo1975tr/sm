using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StreamMesh.Models;
using StreamMesh.Core.Database;

namespace StreamMesh.Core.Media
{
    public static class MetadataService
    {
        private static readonly HttpClient _http = new HttpClient();
        private const string TmdbApiKey = "3fd2be6f0c70a2a598f084dd23308883";

        public static async Task<MetadataResult?> FetchMetadataAsync(string title)
        {
            try
            {
                string url = $"https://api.themoviedb.org/3/search/multi?api_key={TmdbApiKey}&query={Uri.EscapeDataString(title)}&language=tr-TR";
                var response = await _http.GetStringAsync(url);
                var json = JObject.Parse(response);
                var results = json["results"] as JArray;

                if (results != null && results.Count > 0)
                {
                    var first = results[0];
                    return new MetadataResult
                    {
                        Title = first["title"]?.ToString() ?? first["name"]?.ToString() ?? title,
                        Overview = first["overview"]?.ToString() ?? "",
                        PosterUrl = $"https://image.tmdb.org/t/p/w500{first["poster_path"]}",
                        BackdropUrl = $"https://image.tmdb.org/t/p/w1280{first["backdrop_path"]}",
                        ReleaseDate = first["release_date"]?.ToString() ?? first["first_air_date"]?.ToString() ?? ""
                    };
                }
            }
            catch { }
            return null;
        }
    }
}
