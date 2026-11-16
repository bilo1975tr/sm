using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StreamMesh.Core.Network;
using StreamMesh.Core.Utils;
using StreamMesh.Models;

namespace StreamMesh.Core.Media
{
    public static class StalkerPortalService
    {
        private const string MagUserAgent = "Mozilla/5.0 (QtEmbedded; U; Linux; C) AppleWebKit/533.3 (KHTML, like Gecko) MAG200 stbapp ver: 2 rev: 250 Safari/533.3";

        public static string NormalizePortalEndpoint(string rawPortalUrl)
        {
            string url = (rawPortalUrl ?? "").Trim();
            if (string.IsNullOrWhiteSpace(url)) return "";
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "http://" + url;
            }

            url = url.TrimEnd('/');
            if (url.EndsWith("/c", StringComparison.OrdinalIgnoreCase))
            {
                url = url.Substring(0, url.Length - 2);
            }

            if (!url.EndsWith(".php", StringComparison.OrdinalIgnoreCase))
            {
                url = url + "/portal.php";
            }

            return url;
        }

        public static string NormalizeMac(string rawMac)
        {
            string hex = Regex.Replace(rawMac ?? "", "[^0-9A-Fa-f]", "").ToUpperInvariant();
            if (hex.Length != 12) return (rawMac ?? "").Trim().ToUpperInvariant();
            return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
        }

        private static HttpRequestMessage BuildStalkerRequest(string requestUrl, string macAddress, string? token = null, string? referer = null)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            string encodedMac = Uri.EscapeDataString(macAddress);
            req.Headers.TryAddWithoutValidation("User-Agent", MagUserAgent);
            req.Headers.TryAddWithoutValidation("X-User-Agent", "Model: MAG250; Link: WiFi");
            req.Headers.TryAddWithoutValidation("Cookie", $"mac={encodedMac}; stb_lang=en; timezone=Europe%2FIstanbul");
            if (!string.IsNullOrWhiteSpace(token))
            {
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            }
            if (!string.IsNullOrWhiteSpace(referer))
            {
                req.Headers.TryAddWithoutValidation("Referer", referer);
            }
            return req;
        }

        private static async Task<JObject?> SendStalkerGetAsync(string url, string mac, string? token, string? referer, CancellationToken ct)
        {
            using var req = BuildStalkerRequest(url, mac, token, referer);
            using var resp = await MediaHttpClient.Client.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                return JObject.Parse(body);
            }
            catch
            {
                return null;
            }
        }

        public static async Task<(string? token, string endpointUsed)> HandshakeAsync(string portalUrl, string macAddress, CancellationToken ct = default)
        {
            string primaryEndpoint = NormalizePortalEndpoint(portalUrl);
            if (string.IsNullOrEmpty(primaryEndpoint)) return (null, "");

            string mac = NormalizeMac(macAddress);
            var candidates = new List<string> { primaryEndpoint };
            if (primaryEndpoint.EndsWith("/portal.php", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(primaryEndpoint.Replace("/portal.php", "/server/load.php"));
            }

            foreach (var endpoint in candidates)
            {
                try
                {
                    string hsUrl = $"{endpoint}?type=stb&action=handshake&token=&JsHttpRequest=1-xml";
                    var json = await SendStalkerGetAsync(hsUrl, mac, null, endpoint, ct).ConfigureAwait(false);
                    string? token = json?["js"]?["token"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        // Profile request required by many portals to activate session
                        string profUrl = $"{endpoint}?type=stb&action=get_profile&hd=1&ver=ImageDescription:0.2.18-r14-pub-250;PORTAL%20version:5.5.0&num_banks=2&sn=0000000000000&stb_type=MAG250&client_type=STB&JsHttpRequest=1-xml";
                        _ = await SendStalkerGetAsync(profUrl, mac, token, endpoint, ct).ConfigureAwait(false);
                        return (token, endpoint);
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogWarning($"[Stalker] Handshake hatası ({endpoint}): {ex.Message}");
                }
            }

            return (null, primaryEndpoint);
        }

        public static async Task<(bool success, List<Channel> channels, string message)> FetchChannelsAsync(
            string portalUrl,
            string macAddress,
            CancellationToken ct = default)
        {
            string mac = NormalizeMac(macAddress);
            if (string.IsNullOrWhiteSpace(mac) || mac.Length < 12)
            {
                return (false, new List<Channel>(), "Geçerli bir MAC adresi girin (örn: 00:1A:79:XX:XX:XX).");
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(25));

            var (token, endpoint) = await HandshakeAsync(portalUrl, mac, timeoutCts.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
            {
                return (false, new List<Channel>(), "Portal handshake başarısız oldu. Sunucu adresi veya MAC adresini kontrol edin.");
            }

            // 1. Fetch genres map
            var genreMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var genreJson = await SendStalkerGetAsync($"{endpoint}?type=itv&action=get_genres&JsHttpRequest=1-xml", mac, token, endpoint, timeoutCts.Token).ConfigureAwait(false);
                if (genreJson?["js"] is JArray gArr)
                {
                    foreach (var g in gArr)
                    {
                        string gid = g["id"]?.ToString() ?? "";
                        string title = g["title"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(gid) && !string.IsNullOrEmpty(title))
                        {
                            genreMap[gid] = title;
                        }
                    }
                }
            }
            catch { }

            // 2. Fetch all ITV channels
            var chJson = await SendStalkerGetAsync($"{endpoint}?type=itv&action=get_all_channels&JsHttpRequest=1-xml", mac, token, endpoint, timeoutCts.Token).ConfigureAwait(false);
            var dataArr = chJson?["js"]?["data"] as JArray;
            if (dataArr == null || dataArr.Count == 0)
            {
                return (false, new List<Channel>(), "Portal doğrulandı fakat kanal listesi boş döndü.");
            }

            var channels = new List<Channel>();
            string basePlaylist = $"stalker://{endpoint}|{mac}";

            foreach (var item in dataArr)
            {
                string name = item["name"]?.ToString()?.Trim() ?? "";
                string cmd = item["cmd"]?.ToString()?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(cmd)) continue;

                string genreId = item["tv_genre_id"]?.ToString() ?? "";
                string groupTitle = genreMap.TryGetValue(genreId, out var gName) ? gName : "Stalker TV";
                string logo = item["logo"]?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(logo) && !logo.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var uri = new Uri(endpoint);
                        logo = $"{uri.Scheme}://{uri.Authority}/stalker_portal/misc/logos/320/{logo.TrimStart('/')}";
                    }
                    catch { }
                }

                string encodedCmd = Uri.EscapeDataString(cmd);
                string stalkerUrl = $"stalker://{endpoint}|{mac}|{encodedCmd}";

                channels.Add(new Channel
                {
                    Name = name,
                    Url = stalkerUrl,
                    LogoUrl = logo,
                    GroupTitle = groupTitle,
                    Category = "TV",
                    SourceType = "STALKER",
                    PlaylistUrl = basePlaylist,
                    Language = Channel.ExtractLanguageFromText(name + " " + groupTitle),
                    HttpUserAgent = MagUserAgent
                });
            }

            return (true, channels, $"{channels.Count:N0} kanal Stalker Portal'dan başarıyla alındı.");
        }

        public static async Task<string> ResolveStreamLinkAsync(string stalkerUrl, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(stalkerUrl) || !stalkerUrl.StartsWith("stalker://", StringComparison.OrdinalIgnoreCase))
            {
                return stalkerUrl;
            }

            try
            {
                string raw = stalkerUrl.Substring("stalker://".Length);
                var parts = raw.Split('|', 3);
                if (parts.Length < 3) return "";

                string endpoint = parts[0];
                string mac = NormalizeMac(parts[1]);
                string cmd = Uri.UnescapeDataString(parts[2]);

                // Strip common Stalker prefix "ffmpeg " or "ffrt " when sending create_link
                string cleanCmd = cmd;
                if (cleanCmd.StartsWith("ffmpeg ", StringComparison.OrdinalIgnoreCase)) cleanCmd = cleanCmd.Substring(7).Trim();
                else if (cleanCmd.StartsWith("ffrt ", StringComparison.OrdinalIgnoreCase)) cleanCmd = cleanCmd.Substring(5).Trim();

                // If cmd is already a standalone non-localhost HTTP link without tokenized create_link requirement
                if (cleanCmd.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !cleanCmd.Contains("localhost", StringComparison.OrdinalIgnoreCase) && !cleanCmd.Contains("127.0.0.1"))
                {
                    return cleanCmd;
                }

                var (token, resolvedEndpoint) = await HandshakeAsync(endpoint, mac, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token)) return cleanCmd;

                string reqUrl = $"{resolvedEndpoint}?type=itv&action=create_link&cmd={Uri.EscapeDataString(cmd)}&series=&forced_storage=undefined&disable_ad=0&download=0&JsHttpRequest=1-xml";
                var json = await SendStalkerGetAsync(reqUrl, mac, token, resolvedEndpoint, ct).ConfigureAwait(false);
                string? finalCmd = json?["js"]?["cmd"]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(finalCmd)) return cleanCmd;

                if (finalCmd.StartsWith("ffmpeg ", StringComparison.OrdinalIgnoreCase)) finalCmd = finalCmd.Substring(7).Trim();
                else if (finalCmd.StartsWith("ffrt ", StringComparison.OrdinalIgnoreCase)) finalCmd = finalCmd.Substring(5).Trim();

                return finalCmd;
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[Stalker] ResolveStreamLinkAsync hatası: {ex.Message}");
                return "";
            }
        }
    }
}
