using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using StreamMesh.Core.Database;
using StreamMesh.Core.Network;
using StreamMesh.Core.Utils;
using StreamMesh.Models;

namespace StreamMesh.Core.Media.Iptv
{
    /// <summary>
    /// Handles full IPTV package ingestion, XMLTV EPG discovery, and VOD/series syncing.
    /// </summary>
    public static class IptvAccountImporter
    {
        public static string GenerateFullM3uUrl(IptvAccountInfo? account, string? outputFormat = null)
        {
            if (account == null) return string.Empty;
            string scheme = account.HostWithPort.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || 
                            account.HostWithPort.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "" : "http://";
            string host = IptvUrlParser.ExtractHostAndPort(account.HostWithPort);
            string baseQuery = $"{scheme}{host}/get.php?username={Uri.EscapeDataString(account.Username)}&password={Uri.EscapeDataString(account.Password)}&type=m3u_plus";
            if (!string.IsNullOrEmpty(outputFormat))
            {
                return $"{baseQuery}&output={outputFormat}";
            }
            return baseQuery;
        }

        public static bool AddOrRefreshProviderEpg(IptvAccountInfo? account)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.Username)) return false;
            try
            {
                var db = new DatabaseEngine();
                string scheme = account.HostWithPort.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || 
                                account.HostWithPort.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "" : "http://";
                string cleanHost = IptvUrlParser.ExtractHostAndPort(account.HostWithPort);
                string providerXmltvUrl = $"{scheme}{cleanHost}/xmltv.php?username={Uri.EscapeDataString(account.Username)}&password={Uri.EscapeDataString(account.Password)}";
                db.AddEpgSource(providerXmltvUrl);
                LogService.LogInfo($"[IptvAccountImporter] EPG kaynağı başarıyla eklendi: {providerXmltvUrl}");
                return true;
            }
            catch (Exception ex)
            {
                LogService.LogError($"[IptvAccountImporter] EPG kaynağı ekleme hatası: {ex.Message}", ex);
                return false;
            }
        }

        public static async Task<(bool success, int importedCount, string message)> FetchAndImportFullAccountAsync(
            IptvAccountInfo account, 
            Action<string, double>? progress = null)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.Username) || string.IsNullOrWhiteSpace(account.Password))
                return (false, 0, "Geçersiz hesap bilgisi.");

            string scheme = account.HostWithPort.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || 
                            account.HostWithPort.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "" : "http://";
            string host = account.HostWithPort.TrimEnd('/');
            string apiUrl = $"{scheme}{host}/player_api.php?username={Uri.EscapeDataString(account.Username)}&password={Uri.EscapeDataString(account.Password)}";

            bool isVerified = await IptvAccountVerifier.FastVerifyAccountAsync(account, 4000).ConfigureAwait(false);
            if (!isVerified)
            {
                LogService.LogInfo($"[IptvAccountImporter] Sağlayıcı doğrulanamadı veya aktif değil: {account.DisplaySummary}");
                return (false, 0, "Sağlayıcı aktif bir Xtream IPTV sunucusu değil veya yanıt vermiyor.");
            }

            progress?.Invoke($"Sağlayıcıya bağlanılıyor ({account.HostWithPort})...", 10);
            var allChannels = new List<Channel>();

            try
            {
                string[] candidateUrls = new[]
                {
                    GenerateFullM3uUrl(account, null),
                    GenerateFullM3uUrl(account, "ts"),
                    GenerateFullM3uUrl(account, "m3u8"),
                    $"{scheme}{host}/get.php?username={Uri.EscapeDataString(account.Username)}&password={Uri.EscapeDataString(account.Password)}&type=m3u"
                };

                string successfulM3uUrl = candidateUrls[0];
                var m3uEngine = new M3uEngine();

                foreach (var tryUrl in candidateUrls)
                {
                    try
                    {
                        LogService.LogInfo($"[IptvAccountImporter] IPTV sağlayıcısından tam liste isteniyor: {account.DisplaySummary} -> {tryUrl}");
                        var m3uChannels = await m3uEngine.ParseM3uAsync(tryUrl, "TV", false, (msg, pct) =>
                        {
                            progress?.Invoke(msg, pct * 0.7);
                        });

                        if (m3uChannels != null && m3uChannels.Count > 0)
                        {
                            allChannels.AddRange(m3uChannels);
                            successfulM3uUrl = tryUrl;
                            LogService.LogInfo($"[IptvAccountImporter] Başarılı M3U formatı teyit edildi ({account.DisplaySummary}): {m3uChannels.Count} kanal.");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.LogWarning($"[IptvAccountImporter] Format deneme uyarısı ({tryUrl}): {ex.Message}");
                    }
                }

                // API Canlı Yayın Fallback
                if (allChannels.Count == 0)
                {
                    try
                    {
                        progress?.Invoke("Xtream API: Canlı yayın listesi sorgulanıyor...", 40);
                        string liveJson = await MediaHttpClient.GetStringAsync($"{apiUrl}&action=get_live_streams", 20).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(liveJson) && liveJson.StartsWith("[") && liveJson.Length > 10)
                        {
                            using var doc = JsonDocument.Parse(liveJson);
                            foreach (var elem in doc.RootElement.EnumerateArray())
                            {
                                string name = elem.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                                string streamId = elem.TryGetProperty("stream_id", out var sid) ? sid.ToString() : "";
                                string icon = elem.TryGetProperty("stream_icon", out var si) ? si.GetString() ?? "" : "";
                                string grp = elem.TryGetProperty("category_name", out var cn) ? cn.GetString() ?? "Genel" : "Genel";

                                if (!string.IsNullOrEmpty(streamId) && !string.IsNullOrEmpty(name))
                                {
                                    string liveUrl = $"{scheme}{host}/live/{account.Username}/{account.Password}/{streamId}.m3u8";
                                    var ch = new Channel
                                    {
                                        Name = name,
                                        Category = "TV",
                                        GroupTitle = grp,
                                        Url = liveUrl,
                                        LogoUrl = icon
                                    };
                                    allChannels.Add(ch);
                                }
                            }
                        }
                    }
                    catch { }
                }

                // VOD Filmleri Çek
                progress?.Invoke("Xtream API: Film ve Dizi arşivi sorgulanıyor...", 75);
                try
                {
                    var existingUrls = new HashSet<string>(allChannels.SelectMany(c => c.GetUrlList()), StringComparer.OrdinalIgnoreCase);
                    string vodJson = await MediaHttpClient.GetStringAsync($"{apiUrl}&action=get_vod_streams", 15).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(vodJson) && vodJson.StartsWith("[") && vodJson.Length > 10)
                    {
                        using var doc = JsonDocument.Parse(vodJson);
                        foreach (var elem in doc.RootElement.EnumerateArray())
                        {
                            string name = elem.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            string streamId = elem.TryGetProperty("stream_id", out var sid) ? sid.ToString() : "";
                            string ext = elem.TryGetProperty("container_extension", out var ce) ? ce.GetString() ?? "mp4" : "mp4";
                            string icon = elem.TryGetProperty("stream_icon", out var si) ? si.GetString() ?? "" : "";

                            if (!string.IsNullOrEmpty(streamId) && !string.IsNullOrEmpty(name))
                            {
                                string movieUrl = $"{scheme}{host}/movie/{account.Username}/{account.Password}/{streamId}.{ext}";
                                if (!existingUrls.Contains(movieUrl))
                                {
                                    var ch = new Channel
                                    {
                                        Name = name,
                                        Category = "Filmler",
                                        GroupTitle = "VOD Filmler",
                                        Url = movieUrl,
                                        LogoUrl = icon
                                    };
                                    allChannels.Add(ch);
                                    existingUrls.Add(movieUrl);
                                }
                            }
                        }
                    }
                }
                catch { }

                if (allChannels.Count == 0)
                {
                    return (false, 0, "Sağlayıcıdan içerik alınamadı (Hesabın süresi dolmuş veya banlanmış olabilir).");
                }

                progress?.Invoke("Eski parçalı kanallar temizleniyor...", 85);
                var db = new DatabaseEngine();
                await IptvAccountPurger.PurgeAccountFromDatabaseAsync(account);

                progress?.Invoke($"{allChannels.Count} adet güncel kanal kütüphaneye kaydediliyor...", 90);
                await db.SaveChannelsBatchAsync(allChannels, clearFirst: false, notifyUpdated: true);

                db.AddM3uSource(successfulM3uUrl);

                // EPG
                try
                {
                    string cleanHost = IptvUrlParser.ExtractHostAndPort(account.HostWithPort);
                    string providerXmltvUrl = $"{scheme}{cleanHost}/xmltv.php?username={Uri.EscapeDataString(account.Username)}&password={Uri.EscapeDataString(account.Password)}";
                    db.AddEpgSource(providerXmltvUrl);
                }
                catch { }

                // IptvAccounts tablosuna kaydet
                try
                {
                    string cleanHost = IptvUrlParser.ExtractHostAndPort(account.HostWithPort);
                    string serverUrl = $"{scheme}{cleanHost}";

                    var iptvAcc = new IptvAccount
                    {
                        Name = $"{account.Username} ({cleanHost})",
                        ServerUrl = serverUrl,
                        Username = account.Username,
                        Password = account.Password,
                        Status = "Aktif",
                        ExpiryDate = DateTime.Now.AddYears(1),
                        HasServerIndex = true,
                        LocalChannelsCount = allChannels.Count
                    };
                    db.SaveIptvAccount(iptvAcc);

                    try
                    {
                        var xtream = new XtreamService();
                        await xtream.SyncAccountAsync(iptvAcc).ConfigureAwait(false);
                    }
                    catch { }
                }
                catch { }

                progress?.Invoke("Tamamlandı!", 100);
                return (true, allChannels.Count, $"Başarılı! Sağlayıcıdan {allChannels.Count} adet güncel canlı TV kanalı ve film arşivi eklendi.");
            }
            catch (Exception ex)
            {
                LogService.LogError($"[IptvAccountImporter] Tam paket çekilirken hata: {account.DisplaySummary}", ex);
                return (false, 0, $"Sağlayıcıya bağlanırken hata: {ex.Message}");
            }
        }
    }
}
