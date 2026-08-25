using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json.Linq;
using StreamMesh.Models;
using StreamMesh.Core.Database;

namespace StreamMesh.Core.Media
{
    public class XtreamService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private readonly DatabaseEngine _db = new DatabaseEngine();

        public async Task<bool> SyncAccountAsync(IptvAccount acc)
        {
            try
            {
                string baseUrl = acc.ServerUrl.TrimEnd('/');
                string loginUrl = $"{baseUrl}/player_api.php?u={acc.Username}&p={acc.Password}";

                var response = await _httpClient.GetStringAsync(loginUrl);
                var info = JObject.Parse(response);

                if (info["user_info"]?["auth"]?.Value<int>() == 1)
                {
                    acc.Status = "Aktif";
                    long exp = info["user_info"]?["exp_date"]?.Value<long>() ?? 0;
                    acc.ExpiryDate = exp > 0 ? DateTimeOffset.FromUnixTimeSeconds(exp).DateTime : DateTime.MaxValue;
                    _db.SaveIptvAccount(acc);

                    // Start background sync for contents
                    _ = Task.Run(() => FetchAllContentsAsync(acc));
                    return true;
                }
                else
                {
                    acc.Status = "Hatalı Giriş";
                    _db.SaveIptvAccount(acc);
                    return false;
                }
            }
            catch (Exception ex)
            {
                acc.Status = $"Hata: {ex.Message}";
                _db.SaveIptvAccount(acc);
                return false;
            }
        }

        private async Task FetchAllContentsAsync(IptvAccount acc)
        {
            string baseUrl = acc.ServerUrl.TrimEnd('/');
            string authParams = $"u={acc.Username}&p={acc.Password}";

            await ProcessAction(acc, $"{baseUrl}/player_api.php?{authParams}&action=get_live_streams", "TV", "live");
            await ProcessAction(acc, $"{baseUrl}/player_api.php?{authParams}&action=get_vod_streams", "Film", "movie");
            await ProcessAction(acc, $"{baseUrl}/player_api.php?{authParams}&action=get_series", "Dizi", "series");
        }

        private async Task ProcessAction(IptvAccount acc, string url, string category, string type)
        {
            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var items = JArray.Parse(response);
                var allItems = new List<Channel>();

                string baseUrl = acc.ServerUrl.TrimEnd('/');

                foreach (var item in items)
                {
                    string rawName = item["name"]?.ToString() ?? "İsimsiz";
                    string streamIcon = item["stream_icon"]?.ToString() ?? item["cover"]?.ToString() ?? "";

                    var ch = new Channel
                    {
                        Id = $"xt_{acc.Id}_{item["stream_id"] ?? item["series_id"]}",
                        Name = rawName,
                        GroupTitle = $"IPTV: {acc.Name} ({item["category_name"] ?? category})",
                        Category = category,
                        LogoUrl = streamIcon,
                        SourceType = "M3U",
                        PlaylistUrl = acc.ServerUrl
                    };

                    string streamId = item["stream_id"]?.ToString() ?? item["series_id"]?.ToString() ?? string.Empty;
                    string ext = item["container_extension"]?.ToString() ?? "m3u8";

                    if (type == "live") ch.Url = $"{baseUrl}/live/{acc.Username}/{acc.Password}/{streamId}.ts";
                    else if (type == "movie") ch.Url = $"{baseUrl}/movie/{acc.Username}/{acc.Password}/{streamId}.{ext}";
                    else if (type == "series") ch.Url = $"{baseUrl}/series/{acc.Username}/{acc.Password}/{streamId}.{ext}";

                    SmartNormalizationEngine.Instance.NormalizeChannel(ch);

                    // Automatic logo fallback from index if missing
                    if (string.IsNullOrWhiteSpace(ch.LogoUrl))
                    {
                        string? indexedLogo = ChannelEnricher.GetLogoFromIndex(ch.Name);
                        if (!string.IsNullOrEmpty(indexedLogo)) ch.LogoUrl = indexedLogo;
                    }

                    allItems.Add(ch);
                }

                // Chunked Saving to Database (Avoid UI freeze)
                int chunkSize = 250;
                for (int i = 0; i < allItems.Count; i += chunkSize)
                {
                    var chunk = allItems.Skip(i).Take(chunkSize).ToList();
                    await _db.SyncIncomingChannelsAsync(chunk);
                    GitHubSyncEngine.RaiseSyncCompleted(); // Auto-refresh UI
                }
            }
            catch { }
        }
    }
}
