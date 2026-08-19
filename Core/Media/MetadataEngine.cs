using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StreamMesh.Models;
using StreamMesh.Core.Database;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class MetadataEngine
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private readonly DatabaseEngine _db = new DatabaseEngine();

        public async Task EnrichChannelAsync(Channel channel)
        {
            if (channel == null) return;

            string cat = (channel.Category ?? "").Trim().ToUpperInvariant();
            bool isMedia = cat == "FILM" || cat == "MOVIE" || cat == "DIZI" || cat == "SERIES" ||
                           !string.IsNullOrWhiteSpace(channel.SeriesBaseName) ||
                           channel.SeasonNumber > 0 || channel is SeriesGroup;

            if (!isMedia) return;

            // If already enriched with overview and backdrop, skip
            if (!string.IsNullOrWhiteSpace(channel.Overview) && !string.IsNullOrWhiteSpace(channel.BackdropUrl) && !string.IsNullOrWhiteSpace(channel.Cast)) return;

            // 1. Determine clean search query
            string query = !string.IsNullOrWhiteSpace(channel.SeriesBaseName) ? channel.SeriesBaseName : channel.CleanName;
            if (string.IsNullOrWhiteSpace(query)) query = channel.Name;

            query = CleanQueryForMetadata(query);
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2) return;

            // 2. Check local pool first
            var pooled = await _db.GetMetadataPoolForQueryAsync(query);
            if (pooled != null && pooled.Count > 0)
            {
                ApplyMetadata(channel, pooled[0]);
                await _db.SaveChannelAsync(channel);
                return;
            }

            // 3. Check Daily Limit
            var stats = _db.GetDailyQueryStats();
            if (stats.count >= 1000) return;

            // 4. Fetch from API (TMDB)
            string apiKey = _db.GetSetting("TmdbApiKey", "");
            if (string.IsNullOrEmpty(apiKey))
            {
                LogService.LogWarning("MetadataEngine: TMDB API Key is not set. Skipping enrich.");
                return;
            }
            bool isSeries = cat == "DIZI" || cat == "SERIES" || !string.IsNullOrWhiteSpace(channel.SeriesBaseName) || channel is SeriesGroup;

            string endpoint = isSeries ? "search/tv" : "search/multi";
            string url = $"https://api.themoviedb.org/3/{endpoint}?api_key={apiKey}&query={Uri.EscapeDataString(query)}&language=tr-TR";

            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                var results = json["results"] as JArray;

                if ((results == null || results.Count == 0) && isSeries)
                {
                    // Fallback to multi search if TV search had no results
                    url = $"https://api.themoviedb.org/3/search/multi?api_key={apiKey}&query={Uri.EscapeDataString(query)}&language=tr-TR";
                    response = await _httpClient.GetStringAsync(url);
                    json = JObject.Parse(response);
                    results = json["results"] as JArray;
                }

                if (results != null && results.Count > 0)
                {
                    _db.IncrementDailyQueryCount();
                    var metaResults = new List<MetadataResult>();

                    foreach (var item in results)
                    {
                        string title = item["title"]?.ToString() ?? item["name"]?.ToString() ?? "";
                        string overview = item["overview"]?.ToString() ?? "";
                        string posterPath = item["poster_path"]?.ToString() ?? "";
                        string backdropPath = item["backdrop_path"]?.ToString() ?? "";
                        string releaseDate = item["release_date"]?.ToString() ?? item["first_air_date"]?.ToString() ?? "";
                        double voteAvg = item["vote_average"]?.Value<double>() ?? 0;
                        string tmdbId = item["id"]?.ToString() ?? "";

                        string posterUrl = !string.IsNullOrEmpty(posterPath) ? "https://image.tmdb.org/t/p/w500" + posterPath : "";
                        string backdropUrl = !string.IsNullOrEmpty(backdropPath) ? "https://image.tmdb.org/t/p/original" + backdropPath : "";

                        var res = new MetadataResult
                        {
                            ImdbId = tmdbId,
                            Title = title,
                            Overview = overview,
                            PosterUrl = posterUrl,
                            BackdropUrl = backdropUrl,
                            ReleaseDate = releaseDate,
                            VoteAverage = voteAvg,
                            MediaType = item["media_type"]?.ToString() ?? (isSeries ? "tv" : "movie")
                        };

                        // Optionally fetch credits/cast if tmdbId is present
                        if (!string.IsNullOrEmpty(tmdbId))
                        {
                            try
                            {
                                string mediaType = res.MediaType == "tv" ? "tv" : "movie";
                                string creditsUrl = $"https://api.themoviedb.org/3/{mediaType}/{tmdbId}/credits?api_key={apiKey}&language=tr-TR";
                                var creditsResp = await _httpClient.GetStringAsync(creditsUrl);
                                var creditsJson = JObject.Parse(creditsResp);
                                var castArray = creditsJson["cast"] as JArray;
                                if (castArray != null && castArray.Count > 0)
                                {
                                    var castNames = new List<string>();
                                    for (int i = 0; i < Math.Min(5, castArray.Count); i++)
                                    {
                                        string actorName = castArray[i]["name"]?.ToString() ?? "";
                                        if (!string.IsNullOrWhiteSpace(actorName)) castNames.Add(actorName);
                                    }
                                    res.Cast = string.Join(", ", castNames);
                                }
                            }
                            catch { }
                        }

                        metaResults.Add(res);
                    }

                    // Save all results to pool for caching
                    await _db.SaveMetadataPoolResultsAsync(query, metaResults);

                    // Apply first result to target channel
                    ApplyMetadata(channel, metaResults[0]);
                    await _db.SaveChannelAsync(channel);
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"MetadataEngine.EnrichChannelAsync failed for '{query}'", ex);
            }
        }

        public static string CleanQueryForMetadata(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return "";
            string cleaned = rawName;
            cleaned = Regex.Replace(cleaned, @"(?i)\b(1080p|720p|4k|2160p|hd|fhd|uhd|sd|hevc|x264|x265|web-dl|webrip|bluray|dvdrip)\b", "");
            cleaned = Regex.Replace(cleaned, @"(?i)\b(tr|eng|ger|fra|dublaj|altyazılı|altyazili|dual|multi)\b", "");
            cleaned = Regex.Replace(cleaned, @"\[.*?\]", "");
            cleaned = Regex.Replace(cleaned, @"(?i)s(\d+)\s?e(\d+)|(\d+)x(\d+)|sezon\s*\d+|bölüm\s*\d+", "");
            cleaned = Regex.Replace(cleaned, @"\((19\d{2}|20\d{2})\)", "");
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', '-', ':', '_', '.', ',');
            return cleaned;
        }

        private void ApplyMetadata(Channel target, MetadataResult source)
        {
            if (source == null) return;
            if (!string.IsNullOrWhiteSpace(source.Overview)) target.Overview = source.Overview;
            if (!string.IsNullOrWhiteSpace(source.BackdropUrl)) target.BackdropUrl = source.BackdropUrl;
            if (!string.IsNullOrWhiteSpace(source.PosterUrl) && (string.IsNullOrWhiteSpace(target.LogoUrl) || target.LogoUrl.Contains("resimlink"))) target.LogoUrl = source.PosterUrl;
            if (!string.IsNullOrWhiteSpace(source.ImdbId)) target.ImdbId = source.ImdbId;
            if (!string.IsNullOrWhiteSpace(source.Cast)) target.Cast = source.Cast;
        }
    }
}
