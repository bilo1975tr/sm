using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using StreamMesh.Core.Network;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    /// <summary>
    /// Duhnet.tv, Kanal D VOD, PuhuTV ve benzeri CDN/VOD servisleri için
    /// akıllı adres türetme, alternatif sunucu havuzu ve index çözümleme motoru.
    /// </summary>
    public static class VodSmartResolver
    {
        // Duhnet / Kanal D VOD regex:
        // https://kanaldvod.duhnet.tv/S11/HLS_VOD/277929_08ea/index.m3u8
        private static readonly Regex DuhnetVodRegex = new Regex(
            @"https?://(?<subdomain>[^.]+)\.duhnet\.tv/(?<server>S\d+)/(?<folder>[^/]+)/(?<item>[^/]+)/(?<playlist>[^/?#]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Genel VOD CDN regex:
        // https://.../vod/.../index.m3u8
        private static readonly Regex GeneralVodRegex = new Regex(
            @"https?://(?<host>[^/]+)/(?<prefix>.*/)?(?<item>[a-zA-Z0-9_\-]+)/(?<playlist>(?:index|master|playlist|chunklist|video)\.m3u8)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Verilen URL'nin Duhnet veya benzeri bir CDN VOD adresi olup olmadığını belirler.
        /// </summary>
        public static bool IsVodCdnUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return url.Contains("duhnet.tv", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains("HLS_VOD", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains("/vod/", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains("kanaldvod", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Bir VOD adresinden alternatif sunucu, playlist ve protokol varyasyonlarını türetir.
        /// </summary>
        public static List<string> GenerateVodCandidates(string originalUrl)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(originalUrl)) return candidates.ToList();

            candidates.Add(originalUrl.Trim());

            // 1. Duhnet VOD Türetme (Kanal D, Teve2, CNN Türk vb.)
            var duhnetMatch = DuhnetVodRegex.Match(originalUrl);
            if (duhnetMatch.Success)
            {
                string subdomain = duhnetMatch.Groups["subdomain"].Value;
                string currentServer = duhnetMatch.Groups["server"].Value; // örn: S11
                string folder = duhnetMatch.Groups["folder"].Value;       // örn: HLS_VOD
                string item = duhnetMatch.Groups["item"].Value;           // örn: 277929_08ea
                string currentPl = duhnetMatch.Groups["playlist"].Value;  // örn: index.m3u8

                // Sade ID çıkarımı (örn: 277929_08ea -> 277929)
                string rawId = item;
                if (item.Contains('_'))
                {
                    rawId = item.Split('_')[0];
                }

                // Olası alternatif sunucu havuzu (S1'den S15'e)
                var servers = new List<string> { currentServer };
                var commonServers = new[] { "S1", "S10", "S2", "S9", "S8", "S3", "S12", "S4", "S7", "S5", "S6", "S13", "S14", "S15" };
                foreach (var s in commonServers)
                {
                    if (!servers.Contains(s, StringComparer.OrdinalIgnoreCase)) servers.Add(s);
                }

                // Olası playlist adları
                var playlists = new List<string> { currentPl, "index.m3u8", "master.m3u8", "playlist.m3u8", $"{item}.m3u8", $"{rawId}.m3u8" };
                playlists = playlists.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                // İlk olarak mevcut sunucu üzerindeki playlist varyantları
                foreach (var pl in playlists)
                {
                    candidates.Add($"https://{subdomain}.duhnet.tv/{currentServer}/{folder}/{item}/{pl}");
                    if (rawId != item)
                    {
                        candidates.Add($"https://{subdomain}.duhnet.tv/{currentServer}/{folder}/{rawId}/{pl}");
                    }
                }

                // Ardından alternatif sunuculardaki index.m3u8 ve master.m3u8
                foreach (var s in servers.Take(6))
                {
                    candidates.Add($"https://{subdomain}.duhnet.tv/{s}/{folder}/{item}/index.m3u8");
                    candidates.Add($"https://{subdomain}.duhnet.tv/{s}/{folder}/{item}/master.m3u8");
                    if (rawId != item)
                    {
                        candidates.Add($"https://{subdomain}.duhnet.tv/{s}/{folder}/{rawId}/index.m3u8");
                    }
                }

                // HTTP fallback (bazen SSL sertifikası olmayan eski VOD sunucuları)
                candidates.Add($"http://{subdomain}.duhnet.tv/{currentServer}/{folder}/{item}/index.m3u8");
            }
            else
            {
                // 2. Genel VOD CDN / HLS_VOD Türetme
                if (originalUrl.Contains("index.m3u8", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(Regex.Replace(originalUrl, @"index\.m3u8", "master.m3u8", RegexOptions.IgnoreCase));
                    candidates.Add(Regex.Replace(originalUrl, @"index\.m3u8", "playlist.m3u8", RegexOptions.IgnoreCase));
                    candidates.Add(Regex.Replace(originalUrl, @"index\.m3u8", "chunklist.m3u8", RegexOptions.IgnoreCase));
                }
                else if (originalUrl.Contains("master.m3u8", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(Regex.Replace(originalUrl, @"master\.m3u8", "index.m3u8", RegexOptions.IgnoreCase));
                    candidates.Add(Regex.Replace(originalUrl, @"master\.m3u8", "playlist.m3u8", RegexOptions.IgnoreCase));
                }
            }

            return candidates.ToList();
        }

        /// <summary>
        /// Orijinal VOD adresi çalışmadığında, arka planda alternatifleri hızlıca test eder
        /// ve geçerli HLS manifesti dönen ilk çalışan adresi bulur.
        /// </summary>
        public static async Task<string?> FindWorkingVodAlternativeAsync(string originalUrl, CancellationToken ct)
        {
            if (!IsVodCdnUrl(originalUrl)) return null;

            var candidates = GenerateVodCandidates(originalUrl);
            if (candidates.Count <= 1) return null;

            LogService.LogInfo($"[VodResolver] '{originalUrl}' için {candidates.Count} adet VOD adresi türetildi. Doğrulanıyor...");

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(4000); // En fazla 4 saniye probe

            foreach (var cand in candidates)
            {
                if (linkedCts.Token.IsCancellationRequested) break;
                if (cand.Equals(originalUrl, StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, cand);
                    req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/122.0.0.0 Safari/537.36");
                    req.Headers.TryAddWithoutValidation("Accept", "*/*");

                    using var resp = await MediaHttpClient.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        using var stream = await resp.Content.ReadAsStreamAsync(linkedCts.Token).ConfigureAwait(false);
                        byte[] buf = new byte[512];
                        int read = await stream.ReadAsync(buf, 0, buf.Length, linkedCts.Token).ConfigureAwait(false);
                        string head = System.Text.Encoding.UTF8.GetString(buf, 0, read);

                        if (head.Contains("#EXTM3U", StringComparison.OrdinalIgnoreCase) && !head.Contains("<html", StringComparison.OrdinalIgnoreCase))
                        {
                            LogService.LogInfo($"[VodResolver] Başarılı alternatif bulundu: {cand}");
                            return cand;
                        }
                    }
                }
                catch
                {
                    // Sıradakini dene
                }
            }

            return null;
        }
    }
}
