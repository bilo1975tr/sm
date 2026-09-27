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
                    req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
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
                        req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
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

                    // 3. Eğer player_api eylemleri kapalıysa / 0 döndüyse, sunucunun ana M3U Plus indeks akışına bağlan ve gerçek kanal sayısını hızlıca say
                    if (!hasApiStreams || acc.TotalLiveStreams == 0)
                    {
                        try
                        {
                            string m3uIndexUrl = $"{baseUrl}/get.php?username={Uri.EscapeDataString(acc.Username)}&password={Uri.EscapeDataString(acc.Password)}&type=m3u_plus";
                            var req = new HttpRequestMessage(HttpMethod.Get, m3uIndexUrl);
                            req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                            using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                            if (resp.IsSuccessStatusCode)
                            {
                                using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                                using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);

                                int countedChannels = 0;
                                int countedVod = 0;
                                int countedSeries = 0;
                                string? line;
                                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                                {
                                    if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (line.Contains("/movie/", StringComparison.OrdinalIgnoreCase) || 
                                            line.Contains("group-title=\"VOD", StringComparison.OrdinalIgnoreCase) || 
                                            line.Contains("group-title=\"Filmler", StringComparison.OrdinalIgnoreCase) ||
                                            line.Contains("group-title=\"FILM", StringComparison.OrdinalIgnoreCase))
                                        {
                                            countedVod++;
                                        }
                                        else if (line.Contains("/series/", StringComparison.OrdinalIgnoreCase) || 
                                                 line.Contains("group-title=\"Diziler", StringComparison.OrdinalIgnoreCase) || 
                                                 line.Contains("group-title=\"SERIES", StringComparison.OrdinalIgnoreCase))
                                        {
                                            countedSeries++;
                                        }
                                        else
                                        {
                                            countedChannels++;
                                        }
                                    }
                                }

                                if (countedChannels > 0 || countedVod > 0 || countedSeries > 0)
                                {
                                    acc.TotalLiveStreams = countedChannels;
                                    if (countedVod > 0) acc.TotalVodStreams = countedVod;
                                    if (countedSeries > 0) acc.TotalSeriesStreams = countedSeries;
                                    acc.HasServerIndex = true;
                                }
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        acc.HasServerIndex = true;
                    }

                    // 4. Eğer hem API hem sunucu M3U indeksi ulaşılamazsa (erişim kısıtı vb.), veritabanındaki mevcut kanalları göster
                    if (!acc.HasServerIndex)
                    {
                        acc.TotalLiveStreams = acc.LocalChannelsCount;
                    }

                    _db.SaveIptvAccount(acc);

                    // Arka planda sağlayıcının tam paketini ve VOD'larını çek
                    var accInfoImport = new IptvAccountInfo
                    {
                        HostWithPort = new Uri(acc.ServerUrl).Authority,
                        Username = acc.Username,
                        Password = acc.Password
                    };
                    _ = Task.Run(async () =>
                    {
                        await IptvAccountHelper.FetchAndImportFullAccountAsync(accInfoImport);
                    });
                    return true;
                }
                else
                {
                    acc.Status = "Giriş Başarısız / Bağlanılamadı";
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
