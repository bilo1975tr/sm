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
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        private readonly DatabaseEngine _db = new DatabaseEngine();

        public async Task<bool> SyncAccountAsync(IptvAccount acc)
        {
            try
            {
                string baseUrl = acc.ServerUrl.TrimEnd('/');
                if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    baseUrl = "http://" + baseUrl;
                }
                acc.ServerUrl = baseUrl;

                // 1. Önce player_api.php ile dene (hem u/p hem username/password)
                bool isAuth = false;
                long exp = 0;

                try
                {
                    string loginUrl = $"{baseUrl}/player_api.php?username={Uri.EscapeDataString(acc.Username)}&password={Uri.EscapeDataString(acc.Password)}";
                    var req = new HttpRequestMessage(HttpMethod.Get, loginUrl);
                    req.Headers.TryAddWithoutValidation("User-Agent", "IPTVSmartersPro/1.0.0 (Linux;Android 11) StreamMesh/1.0");
                    var resp = await _httpClient.SendAsync(req);
                    
                    if (resp.IsSuccessStatusCode)
                    {
                        var response = await resp.Content.ReadAsStringAsync();
                        if (response.TrimStart().StartsWith("{"))
                        {
                            var info = JObject.Parse(response);
                            var authVal = info["user_info"]?["auth"];
                            if (authVal != null && (authVal.ToString() == "1" || authVal.ToString().Equals("true", StringComparison.OrdinalIgnoreCase)))
                            {
                                isAuth = true;
                                exp = info["user_info"]?["exp_date"]?.Value<long>() ?? 0;

                                // Kullanıcı ve Sunucu Bilgilerini Detaylıca Çek
                                var uInfo = info["user_info"];
                                var sInfo = info["server_info"];

                                if (uInfo != null)
                                {
                                    string? maxConnStr = uInfo["max_connections"]?.ToString() 
                                                      ?? uInfo["max_cons"]?.ToString() 
                                                      ?? uInfo["connections_limit"]?.ToString();

                                    if (!string.IsNullOrEmpty(maxConnStr) && int.TryParse(maxConnStr, out int maxConn))
                                    {
                                        acc.MaxConnections = maxConn;
                                    }
                                    else
                                    {
                                        acc.MaxConnections = null; // Sınırsız / Belirtilmemiş
                                    }

                                    string? actConnStr = uInfo["active_cons"]?.ToString() 
                                                      ?? uInfo["active_connections"]?.ToString();
                                    if (!string.IsNullOrEmpty(actConnStr) && int.TryParse(actConnStr, out int actConn))
                                    {
                                        acc.ActiveConnections = actConn;
                                    }

                                    acc.IsTrial = uInfo["is_trial"]?.ToString() == "1" || uInfo["is_trial"]?.ToString().Equals("true", StringComparison.OrdinalIgnoreCase) == true;
                                    var formats = uInfo["allowed_output_formats"];
                                    if (formats is JArray arr) acc.AllowedFormats = string.Join(", ", arr.Select(x => x.ToString()));
                                    else if (formats != null) acc.AllowedFormats = formats.ToString();
                                }

                                if (sInfo != null)
                                {
                                    acc.ServerVersion = sInfo["version"]?.ToString() ?? "";
                                    acc.ServerTimezone = sInfo["timezone"]?.ToString() ?? "";
                                }
                            }
                        }
                    }
                }
                catch { }

                // 2. player_api başarısız olduysa veya desteklenmiyorsa get.php m3u_plus ile dene
                if (!isAuth)
                {
                    try
                    {
                        string m3uTestUrl = $"{baseUrl}/get.php?username={Uri.EscapeDataString(acc.Username)}&password={Uri.EscapeDataString(acc.Password)}&type=m3u_plus";
                        var req = new HttpRequestMessage(HttpMethod.Get, m3uTestUrl);
                        req.Headers.TryAddWithoutValidation("User-Agent", "IPTVSmartersPro/1.0.0 (Linux;Android 11) StreamMesh/1.0");
                        var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                        if (resp.IsSuccessStatusCode)
                        {
                            // En azından başlık başarılı döndü, içerik kontrolü
                            var stream = await resp.Content.ReadAsStreamAsync();
                            var buffer = new byte[1024];
                            int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                            string headerText = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
                            if (headerText.Contains("#EXTM3U") || headerText.Contains("#EXTINF"))
                            {
                                isAuth = true;
                            }
                        }
                    }
                    catch { }
                }

                if (isAuth)
                {
                    acc.Status = "Aktif";
                    acc.ExpiryDate = exp > 0 ? DateTimeOffset.FromUnixTimeSeconds(exp).DateTime : DateTime.Now.AddYears(1);
                    acc.LastChecked = DateTime.Now;

                    // Otomatik XMLTV EPG Kaynağını Ekle
                    if (!string.IsNullOrWhiteSpace(acc.Username) && !string.IsNullOrWhiteSpace(acc.Password))
                    {
                        try
                        {
                            string epgUrl = $"{baseUrl}/xmltv.php?username={Uri.EscapeDataString(acc.Username)}&password={Uri.EscapeDataString(acc.Password)}";
                            _db.AddEpgSource(epgUrl);
                        }
                        catch { }
                    }

                    // 1. Veritabanındaki mevcut eşleşen kanalların sayısını hesapla
                    try
                    {
                        var accInfo = new IptvAccountInfo
                        {
                            HostWithPort = new Uri(acc.ServerUrl).Authority,
                            Username = acc.Username,
                            Password = acc.Password
                        };
                        var (totalLocalCh, _, _) = await IptvAccountHelper.CountAccountOccurrencesAsync(accInfo).ConfigureAwait(false);
                        acc.LocalChannelsCount = totalLocalCh;
                    }
                    catch { }

                    // 2. Canlı, VOD ve Dizi istatistiklerini player_api üzerinden sorgula (get_live_streams, get_vod_streams, get_series)
                    bool hasApiStreams = false;
                    try
                    {
                        string authP = $"username={Uri.EscapeDataString(acc.Username)}&password={Uri.EscapeDataString(acc.Password)}";

                        // Canlı yayın sayısı
                        try
                        {
                            var liveResp = await _httpClient.GetStringAsync($"{baseUrl}/player_api.php?{authP}&action=get_live_streams").ConfigureAwait(false);
                            if (liveResp.TrimStart().StartsWith("["))
                            {
                                var liveArr = JArray.Parse(liveResp);
                                acc.TotalLiveStreams = liveArr.Count;
                                if (acc.TotalLiveStreams > 0) hasApiStreams = true;
                            }
                        }
                        catch { }

                        // VOD Film sayısı
                        try
                        {
                            var vodResp = await _httpClient.GetStringAsync($"{baseUrl}/player_api.php?{authP}&action=get_vod_streams").ConfigureAwait(false);
                            if (vodResp.TrimStart().StartsWith("["))
                            {
                                var vodArr = JArray.Parse(vodResp);
                                acc.TotalVodStreams = vodArr.Count;
                                if (acc.TotalVodStreams > 0) hasApiStreams = true;
                            }
                        }
                        catch { }

                        // Dizi sayısı
                        try
                        {
                            var seriesResp = await _httpClient.GetStringAsync($"{baseUrl}/player_api.php?{authP}&action=get_series").ConfigureAwait(false);
                            if (seriesResp.TrimStart().StartsWith("["))
                            {
                                var serArr = JArray.Parse(seriesResp);
                                acc.TotalSeriesStreams = serArr.Count;
                                if (acc.TotalSeriesStreams > 0) hasApiStreams = true;
                            }
                        }
                        catch { }
                    }
                    catch { }

                    acc.HasServerIndex = hasApiStreams || acc.TotalLiveStreams > 0;

                    if (acc.TotalLiveStreams == 0)
                    {
                        acc.TotalLiveStreams = acc.LocalChannelsCount;
                    }

                    _db.SaveIptvAccount(acc);
                    return true;
                }
                else
                {
                    try { _db.RemoveIptvAccount(acc.Id); } catch { }
                    return false;
                }
            }
            catch (Exception)
            {
                try { _db.RemoveIptvAccount(acc.Id); } catch { }
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
