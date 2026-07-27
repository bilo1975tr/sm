using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StreamMesh.Models;
using StreamMesh.Core.Database;

namespace StreamMesh.Core.Media
{
    public class MetadataEngine
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private readonly DatabaseEngine _db = new DatabaseEngine();

        public async Task EnrichChannelAsync(Channel channel)
        {
            if (channel.Category != "Film" && channel.Category != "Dizi") return;
            if (!string.IsNullOrEmpty(channel.Overview)) return;

            // 1. Check local pool first
            var pooled = await _db.GetMetadataPoolForQueryAsync(channel.Name);
            if (pooled.Count > 0)
            {
                ApplyMetadata(channel, pooled[0]);
                return;
            }

            // 2. Check Daily Limit
            var stats = _db.GetDailyQueryStats();
            if (stats.count >= 1000) return;

            // 3. Fetch from API (TMDB example)
            string apiKey = _db.GetSetting("TmdbApiKey", "3fd2be6f0c70a2a598f084dd23308883");
            string url = $"https://api.themoviedb.org/3/search/multi?api_key={apiKey}&query={Uri.EscapeDataString(channel.Name)}&language=tr-TR";

            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var results = JObject.Parse(response)["results"] as JArray;

                if (results != null && results.Count > 0)
                {
                    _db.IncrementDailyQueryCount();
                    var metaResults = new List<MetadataResult>();

                    foreach (var item in results)
                    {
                        var res = new MetadataResult
                        {
                            Title = item["title"]?.ToString() ?? item["name"]?.ToString() ?? "",
                            Overview = item["overview"]?.ToString() ?? "",
                            PosterUrl = "https://image.tmdb.org/t/p/w500" + item["poster_path"]?.ToString(),
                            BackdropUrl = "https://image.tmdb.org/t/p/original" + item["backdrop_path"]?.ToString(),
                            ReleaseDate = item["release_date"]?.ToString() ?? item["first_air_date"]?.ToString() ?? "",
                            VoteAverage = item["vote_average"]?.Value<double>() ?? 0
                        };
                        metaResults.Add(res);
                    }

                    // Save all results to pool (Superman example)
                    await _db.SaveMetadataPoolResultsAsync(channel.Name, metaResults);

                    // Apply first result to current channel
                    ApplyMetadata(channel, metaResults[0]);
                    await _db.SaveChannelAsync(channel);
                }
            }
            catch { }
        }

        private void ApplyMetadata(Channel target, MetadataResult source)
        {
            target.Overview = source.Overview;
            target.BackdropUrl = source.BackdropUrl;
            target.LogoUrl = source.PosterUrl;
            target.ImdbId = source.ImdbId;
        }
    }
}
