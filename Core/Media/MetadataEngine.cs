using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StreamMesh.Core.Database;
using StreamMesh.Core.Network;
using StreamMesh.Core.Plugins;
using StreamMesh.Core.Utils;
using StreamMesh.Models;

namespace StreamMesh.Core.Media
{
    public class MetadataEngine
    {
        private readonly DatabaseEngine _db = DatabaseEngine.Instance;

        public static bool IsLikelyLiveTv(Channel channel)
        {
            if (channel == null) return true;
            if (channel is SeriesGroup) return false;

            string cat = (channel.Category ?? "").Trim().ToUpperInvariant();
            if (cat == "TV" || cat == "CANLI" || cat == "LIVE" || cat == "RADYO" || cat == "RADIO" || cat == "GENEL")
                return true;

            string urlLower = (channel.PrimaryUrl ?? channel.Url ?? "").ToLowerInvariant();
            bool isVodFileOrPath = urlLower.Contains("/movie/") || urlLower.Contains("/movies/") || urlLower.Contains("/vod/") || urlLower.Contains("/series/") ||
                                   urlLower.Contains(".mkv") || urlLower.Contains(".mp4") || urlLower.Contains(".avi");
            if (isVodFileOrPath) return false;

            if (urlLower.Contains("/live/") || urlLower.EndsWith(".ts") || urlLower.Contains(".ts?") ||
                urlLower.EndsWith(".m3u8") || urlLower.Contains(".m3u8?") || urlLower.StartsWith("acestream://") ||
                !string.IsNullOrWhiteSpace(channel.EpgId))
            {
                return true;
            }

            return false;
        }

        public async Task EnrichChannelAsync(Channel channel)
        {
            if (channel == null || IsLikelyLiveTv(channel)) return;

            string cat = (channel.Category ?? "").Trim().ToUpperInvariant();
            bool isMedia = cat == "FILM" || cat == "FILMLER" || cat == "MOVIE" || cat == "MOVIES" || cat == "VOD" ||
                           cat == "DIZI" || cat == "DİZİ" || cat == "DIZILER" || cat == "SERIES" ||
                           !string.IsNullOrWhiteSpace(channel.SeriesBaseName) ||
                           channel.SeasonNumber > 0 || channel is SeriesGroup;

            if (!isMedia) return;

            // If already enriched with overview, backdrop and cast, skip
            if (!string.IsNullOrWhiteSpace(channel.Overview) &&
                !string.IsNullOrWhiteSpace(channel.BackdropUrl) &&
                !string.IsNullOrWhiteSpace(channel.Cast))
            {
                return;
            }

            // 1. Determine clean search query
            string query = !string.IsNullOrWhiteSpace(channel.SeriesBaseName) ? channel.SeriesBaseName : channel.CleanName;
            if (string.IsNullOrWhiteSpace(query)) query = channel.Name;

            query = CleanQueryForMetadata(query);
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2) return;

            // 2. Check local pool first
            var pooled = await _db.GetMetadataPoolForQueryAsync(query).ConfigureAwait(false);
            if (pooled != null && pooled.Count > 0)
            {
                ApplyMetadata(channel, pooled[0]);
                await _db.SaveChannelAsync(channel, notifyUpdated: false).ConfigureAwait(false);
                return;
            }

            // 3. Check Daily Limit
            var metaPlugin = PluginManager.Instance.GetPluginById("metadata_enricher") as MetadataPlugin;
            int dailyLimit = Math.Max(10, metaPlugin?.GetInt("DailyQueryLimit", 1000) ?? 1000);
            string preferredLang = metaPlugin?.GetString("Language", "tr-TR") ?? "tr-TR";
            string langPrefix = preferredLang.Split('-')[0].ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(langPrefix)) langPrefix = "tr";

            var stats = _db.GetDailyQueryStats();
            if (stats.count >= dailyLimit) return;

            // 4. Fetch from free keyless public APIs (TVMaze, IMDb Suggestion, Wikipedia)
            bool isSeries = cat == "DIZI" || cat == "SERIES" || !string.IsNullOrWhiteSpace(channel.SeriesBaseName) || channel is SeriesGroup;

            try
            {
                var combined = new MetadataResult
                {
                    Title = query,
                    MediaType = isSeries ? "tv" : "movie"
                };

                Task<MetadataResult?>? tvMazeTask = isSeries ? FetchFromTvMazeAsync(query) : null;
                Task<MetadataResult?> imdbTask = FetchFromImdbSuggestionAsync(query, isSeries);
                Task<MetadataResult?> wikiPrimaryTask = FetchFromWikipediaAsync(query, langPrefix, isSeries);

                if (tvMazeTask != null)
                {
                    await Task.WhenAll(tvMazeTask, imdbTask, wikiPrimaryTask).ConfigureAwait(false);
                }
                else
                {
                    await Task.WhenAll(imdbTask, wikiPrimaryTask).ConfigureAwait(false);
                }

                var tvMazeRes = tvMazeTask != null ? await tvMazeTask.ConfigureAwait(false) : null;
                var imdbRes = await imdbTask.ConfigureAwait(false);
                var wikiRes = await wikiPrimaryTask.ConfigureAwait(false);

                // Fallback to English Wikipedia if localized Wikipedia returned no overview
                if ((wikiRes == null || string.IsNullOrWhiteSpace(wikiRes.Overview)) && langPrefix != "en")
                {
                    var wikiEn = await FetchFromWikipediaAsync(query, "en", isSeries).ConfigureAwait(false);
                    if (wikiEn != null)
                    {
                        wikiRes ??= wikiEn;
                        if (string.IsNullOrWhiteSpace(wikiRes.Overview)) wikiRes.Overview = wikiEn.Overview;
                        if (string.IsNullOrWhiteSpace(wikiRes.BackdropUrl)) wikiRes.BackdropUrl = wikiEn.BackdropUrl;
                        if (string.IsNullOrWhiteSpace(wikiRes.PosterUrl)) wikiRes.PosterUrl = wikiEn.PosterUrl;
                    }
                }

                // Merge best metadata fields from free sources
                MergeSource(combined, wikiRes, preferOverview: langPrefix != "en");
                MergeSource(combined, tvMazeRes, preferOverview: langPrefix == "en");
                MergeSource(combined, imdbRes, preferOverview: false);

                bool hasUsefulData = !string.IsNullOrWhiteSpace(combined.Overview) ||
                                     !string.IsNullOrWhiteSpace(combined.PosterUrl) ||
                                     !string.IsNullOrWhiteSpace(combined.BackdropUrl) ||
                                     !string.IsNullOrWhiteSpace(combined.Cast);

                if (hasUsefulData)
                {
                    _db.IncrementDailyQueryCount();
                    var metaResults = new List<MetadataResult> { combined };
                    await _db.SaveMetadataPoolResultsAsync(query, metaResults).ConfigureAwait(false);
                    ApplyMetadata(channel, combined);
                    await _db.SaveChannelAsync(channel, notifyUpdated: false).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"MetadataEngine.EnrichChannelAsync failed for '{query}'", ex);
            }
        }

        private static void MergeSource(MetadataResult target, MetadataResult? source, bool preferOverview)
        {
            if (source == null) return;

            if (!string.IsNullOrWhiteSpace(source.Title) && string.IsNullOrWhiteSpace(target.Title))
                target.Title = source.Title;

            if (!string.IsNullOrWhiteSpace(source.Overview) && (preferOverview || string.IsNullOrWhiteSpace(target.Overview)))
                target.Overview = source.Overview;

            if (!string.IsNullOrWhiteSpace(source.PosterUrl) && string.IsNullOrWhiteSpace(target.PosterUrl))
                target.PosterUrl = source.PosterUrl;

            if (!string.IsNullOrWhiteSpace(source.BackdropUrl) && string.IsNullOrWhiteSpace(target.BackdropUrl))
                target.BackdropUrl = source.BackdropUrl;

            if (!string.IsNullOrWhiteSpace(source.Cast) && string.IsNullOrWhiteSpace(target.Cast))
                target.Cast = source.Cast;

            if (!string.IsNullOrWhiteSpace(source.ImdbId) && string.IsNullOrWhiteSpace(target.ImdbId))
                target.ImdbId = source.ImdbId;

            if (!string.IsNullOrWhiteSpace(source.ReleaseDate) && string.IsNullOrWhiteSpace(target.ReleaseDate))
                target.ReleaseDate = source.ReleaseDate;

            if (source.VoteAverage > 0 && target.VoteAverage <= 0)
                target.VoteAverage = source.VoteAverage;
        }

        private static async Task<MetadataResult?> FetchFromTvMazeAsync(string query)
        {
            try
            {
                string url = $"https://api.tvmaze.com/singlesearch/shows?q={Uri.EscapeDataString(query)}&embed=cast";
                using var resp = await MediaHttpClient.GetAsync(url, HttpCompletionOption.ResponseContentRead, 10).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;

                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var json = JObject.Parse(body);

                string summaryHtml = json["summary"]?.ToString() ?? "";
                string overview = WebUtility.HtmlDecode(Regex.Replace(summaryHtml, "<.*?>", " ")).Trim();
                overview = Regex.Replace(overview, @"\s+", " ");

                string imageOrig = json["image"]?["original"]?.ToString() ?? json["image"]?["medium"]?.ToString() ?? "";
                string imdbId = json["externals"]?["imdb"]?.ToString() ?? "";
                double rating = json["rating"]?["average"]?.Value<double?>() ?? 0;

                var castNames = new List<string>();
                if (json["_embedded"]?["cast"] is JArray castArr)
                {
                    foreach (var c in castArr.Take(6))
                    {
                        string actor = c["person"]?["name"]?.ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(actor) && !castNames.Contains(actor))
                            castNames.Add(actor);
                    }
                }

                return new MetadataResult
                {
                    Title = json["name"]?.ToString() ?? query,
                    Overview = overview,
                    PosterUrl = imageOrig,
                    BackdropUrl = imageOrig,
                    ImdbId = imdbId,
                    ReleaseDate = json["premiered"]?.ToString() ?? "",
                    VoteAverage = rating,
                    Cast = string.Join(", ", castNames),
                    MediaType = "tv"
                };
            }
            catch
            {
                return null;
            }
        }

        private static async Task<MetadataResult?> FetchFromImdbSuggestionAsync(string query, bool isSeries)
        {
            try
            {
                string url = $"https://v3.sg.media-imdb.com/suggestion/x/{Uri.EscapeDataString(query)}.json";
                using var resp = await MediaHttpClient.GetAsync(url, HttpCompletionOption.ResponseContentRead, 10).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;

                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var json = JObject.Parse(body);
                if (json["d"] is not JArray items || items.Count == 0) return null;

                var best = items.FirstOrDefault(item =>
                {
                    string qid = (item["qid"]?.ToString() ?? "").ToLowerInvariant();
                    return isSeries
                        ? (qid == "tvseries" || qid == "tvminiseries")
                        : (qid == "movie" || qid == "tvmovie");
                }) ?? items.FirstOrDefault(item =>
                {
                    string id = item["id"]?.ToString() ?? "";
                    return id.StartsWith("tt", StringComparison.OrdinalIgnoreCase);
                });

                if (best == null) return null;

                string imageUrl = best["i"]?["imageUrl"]?.ToString() ?? "";
                return new MetadataResult
                {
                    ImdbId = best["id"]?.ToString() ?? "",
                    Title = best["l"]?.ToString() ?? query,
                    Cast = best["s"]?.ToString() ?? "",
                    ReleaseDate = best["y"]?.ToString() ?? "",
                    PosterUrl = imageUrl,
                    BackdropUrl = imageUrl,
                    MediaType = isSeries ? "tv" : "movie"
                };
            }
            catch
            {
                return null;
            }
        }

        private static async Task<MetadataResult?> FetchFromWikipediaAsync(string query, string langPrefix, bool isSeries)
        {
            try
            {
                string suffix = langPrefix == "tr"
                    ? (isSeries ? " dizi" : " film")
                    : (isSeries ? " TV series" : " film");

                string searchQuery = $"{query}{suffix}";
                string url = $"https://{langPrefix}.wikipedia.org/w/api.php?action=query&generator=search&gsrsearch={Uri.EscapeDataString(searchQuery)}&gsrlimit=1&prop=extracts|pageimages&exintro=1&explaintext=1&piprop=original|thumbnail&pithumbsize=800&format=json";

                using var resp = await MediaHttpClient.GetAsync(
                    url,
                    HttpCompletionOption.ResponseContentRead,
                    10,
                    default,
                    "StreamMesh/1.6 (https://github.com/bilo1975tr/sm)").ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode) return null;

                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var json = JObject.Parse(body);
                var pages = json["query"]?["pages"] as JObject;
                if (pages == null || !pages.HasValues) return null;

                var firstPage = pages.Properties().FirstOrDefault()?.Value as JObject;
                if (firstPage == null) return null;

                string title = firstPage["title"]?.ToString() ?? "";
                string extract = firstPage["extract"]?.ToString() ?? "";
                if (extract.Length > 900)
                {
                    extract = extract.Substring(0, 897).TrimEnd() + "...";
                }

                string origImg = firstPage["original"]?["source"]?.ToString() ?? "";
                string thumbImg = firstPage["thumbnail"]?["source"]?.ToString() ?? "";
                string img = !string.IsNullOrWhiteSpace(origImg) ? origImg : thumbImg;

                return new MetadataResult
                {
                    Title = title,
                    Overview = extract,
                    PosterUrl = thumbImg,
                    BackdropUrl = img,
                    MediaType = isSeries ? "tv" : "movie"
                };
            }
            catch
            {
                return null;
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
            if (source == null || IsLikelyLiveTv(target)) return;
            if (!string.IsNullOrWhiteSpace(source.Overview) && string.IsNullOrWhiteSpace(target.Overview)) target.Overview = source.Overview;
            if (!string.IsNullOrWhiteSpace(source.BackdropUrl) && string.IsNullOrWhiteSpace(target.BackdropUrl)) target.BackdropUrl = source.BackdropUrl;
            if (!string.IsNullOrWhiteSpace(source.PosterUrl) && (string.IsNullOrWhiteSpace(target.LogoUrl) || target.LogoUrl.Contains("resimlink"))) target.LogoUrl = source.PosterUrl;
            if (!string.IsNullOrWhiteSpace(source.ImdbId) && string.IsNullOrWhiteSpace(target.ImdbId)) target.ImdbId = source.ImdbId;
            if (!string.IsNullOrWhiteSpace(source.Cast) && string.IsNullOrWhiteSpace(target.Cast)) target.Cast = source.Cast;
        }
    }
}
