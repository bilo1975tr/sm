using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StreamMesh.Models;

namespace StreamMesh.Services
{
    public class MetadataResult
    {
        public string Title { get; set; }
        public string PosterUrl { get; set; }
        public string BackdropUrl { get; set; }
        public string Overview { get; set; }
        public string ImdbId { get; set; }
        public string Cast { get; set; }
        public double VoteAverage { get; set; }
        public string ReleaseDate { get; set; }
    }

    public static class MetadataService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private const string TmdbApiKey = "3fd2be6f0c70a2a598f084dd23308883"; // Free public TMDB API key fallback
        private const string OmdbApiKey = "trilogy"; // Public OMDB fallback

        static MetadataService()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("StreamMesh/1.8.3 (Windows Desktop)");
        }

        public static string CleanTitleForSearch(string rawTitle)
        {
            if (string.IsNullOrWhiteSpace(rawTitle)) return string.Empty;

            string clean = rawTitle;

            // Remove brackets/parentheses content if tags like [4K], (2022), [Dual]
            clean = Regex.Replace(clean, @"\[.*?\]|\(.*?\)", " ");

            // Remove common quality & format keywords
            string pattern = @"\b(1080p|720p|4k|2160p|bluray|web-dl|webrip|hdrip|dual|tr|eng|multi|x264|x265|hevc|subbed|dubbed|aac|dts)\b";
            clean = Regex.Replace(clean, pattern, "", RegexOptions.IgnoreCase);

            // Replace dots, underscores, dashes with space
            clean = clean.Replace(".", " ").Replace("_", " ").Replace("-", " ");

            // Collapse multiple spaces
            clean = Regex.Replace(clean, @"\s+", " ").Trim();

            return clean;
        }

        public static async Task<MetadataResult> FetchMetadataAsync(string title, string category)
        {
            string query = CleanTitleForSearch(title);
            if (string.IsNullOrWhiteSpace(query)) return null;

            try
            {
                // Attempt TMDB Search
                bool isSeries = category != null && (category.Equals("Series", StringComparison.OrdinalIgnoreCase) || category.Equals("Dizi", StringComparison.OrdinalIgnoreCase));
                string type = isSeries ? "tv" : "movie";

                string url = $"https://api.themoviedb.org/3/search/{type}?api_key={TmdbApiKey}&query={Uri.EscapeDataString(query)}&language=tr-TR";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode && isSeries)
                {
                    // Fallback to movie search if series search failed
                    url = $"https://api.themoviedb.org/3/search/movie?api_key={TmdbApiKey}&query={Uri.EscapeDataString(query)}&language=tr-TR";
                    response = await _httpClient.GetAsync(url);
                }

                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var jObj = JObject.Parse(json);
                    var results = jObj["results"] as JArray;

                    if (results != null && results.Count > 0)
                    {
                        var first = results[0];
                        int id = first["id"]?.Value<int>() ?? 0;
                        string posterPath = first["poster_path"]?.ToString();
                        string backdropPath = first["backdrop_path"]?.ToString();
                        string overview = first["overview"]?.ToString();
                        double voteAvg = first["vote_average"]?.Value<double>() ?? 0.0;
                        string releaseDate = isSeries ? first["first_air_date"]?.ToString() : first["release_date"]?.ToString();

                        string posterUrl = !string.IsNullOrEmpty(posterPath) ? $"https://image.tmdb.org/t/p/w500{posterPath}" : null;
                        string backdropUrl = !string.IsNullOrEmpty(backdropPath) ? $"https://image.tmdb.org/t/p/w1280{backdropPath}" : null;

                        // Fetch detailed credits/cast & IMDb ID
                        string cast = "";
                        string imdbId = "";

                        try
                        {
                            string detailUrl = $"https://api.themoviedb.org/3/{type}/{id}?api_key={TmdbApiKey}&append_to_response=credits,external_ids&language=tr-TR";
                            var detailRes = await _httpClient.GetAsync(detailUrl);
                            if (detailRes.IsSuccessStatusCode)
                            {
                                string detailJson = await detailRes.Content.ReadAsStringAsync();
                                var detailObj = JObject.Parse(detailJson);

                                imdbId = detailObj["external_ids"]?["imdb_id"]?.ToString() ?? detailObj["imdb_id"]?.ToString() ?? "";

                                var castArray = detailObj["credits"]?["cast"] as JArray;
                                if (castArray != null)
                                {
                                    var castNames = new List<string>();
                                    for (int i = 0; i < Math.Min(5, castArray.Count); i++)
                                    {
                                        string name = castArray[i]["name"]?.ToString();
                                        if (!string.IsNullOrEmpty(name)) castNames.Add(name);
                                    }
                                    cast = string.Join(", ", castNames);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.LogError($"Error fetching TMDB detail credits for {query}", ex);
                        }

                        return new MetadataResult
                        {
                            Title = first["title"]?.ToString() ?? first["name"]?.ToString() ?? query,
                            PosterUrl = posterUrl,
                            BackdropUrl = backdropUrl,
                            Overview = overview,
                            ImdbId = imdbId,
                            Cast = cast,
                            VoteAverage = voteAvg,
                            ReleaseDate = releaseDate
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"MetadataService TMDB error for query '{query}'", ex);
            }

            // Fallback OMDB Search
            try
            {
                string omdbUrl = $"https://www.omdbapi.com/?t={Uri.EscapeDataString(query)}&apikey={OmdbApiKey}";
                var omdbRes = await _httpClient.GetAsync(omdbUrl);
                if (omdbRes.IsSuccessStatusCode)
                {
                    string omdbJson = await omdbRes.Content.ReadAsStringAsync();
                    var jObj = JObject.Parse(omdbJson);
                    if (jObj["Response"]?.ToString() == "True")
                    {
                        string poster = jObj["Poster"]?.ToString();
                        if (poster == "N/A") poster = null;

                        return new MetadataResult
                        {
                            Title = jObj["Title"]?.ToString() ?? query,
                            PosterUrl = poster,
                            Overview = jObj["Plot"]?.ToString(),
                            ImdbId = jObj["imdbID"]?.ToString(),
                            Cast = jObj["Actors"]?.ToString(),
                            ReleaseDate = jObj["Year"]?.ToString()
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"MetadataService OMDB fallback error for '{query}'", ex);
            }

            return null;
        }

        public static async Task EnrichChannelMetadataAsync(Channel channel, DatabaseService dbService = null)
        {
            if (channel == null) return;

            // Only fetch for Movies or Series
            bool isMedia = channel.Category == "Movies" || channel.Category == "Series" || channel.Category == "Film" || channel.Category == "Dizi";
            if (!isMedia) return;

            // Skip if overview & backdrop already loaded
            if (!string.IsNullOrEmpty(channel.Overview) && !string.IsNullOrEmpty(channel.BackdropUrl)) return;

            var meta = await FetchMetadataAsync(channel.Name, channel.Category);
            if (meta != null)
            {
                if (!string.IsNullOrEmpty(meta.PosterUrl) && (string.IsNullOrEmpty(channel.LogoUrl) || channel.LogoUrl.StartsWith("http")))
                {
                    channel.LogoUrl = meta.PosterUrl;
                }
                if (!string.IsNullOrEmpty(meta.BackdropUrl)) channel.BackdropUrl = meta.BackdropUrl;
                if (!string.IsNullOrEmpty(meta.Overview)) channel.Overview = meta.Overview;
                if (!string.IsNullOrEmpty(meta.ImdbId)) channel.ImdbId = meta.ImdbId;
                if (!string.IsNullOrEmpty(meta.Cast)) channel.Cast = meta.Cast;

                if (dbService != null)
                {
                    dbService.SaveChannel(channel);
                }
            }
        }
    }
}
