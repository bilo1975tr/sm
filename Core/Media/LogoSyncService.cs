using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StreamMesh.Core.Database;
using StreamMesh.Core.Network;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class LogoSyncService
    {
        private readonly DatabaseEngine _db = DatabaseEngine.Instance;

        public async Task SyncIfNecessaryAsync()
        {
            // 1. Always scan local logos folder first (instant)
            ScanLocalLogosFolder();

            // 2. Check if remote sync with v3 iptv-org + tv-logos was performed within the last 30 days
            string syncVer = _db.GetSetting("LogoSyncVersion", "");
            string last = _db.GetSetting("LogoSyncDate", "");
            if (syncVer == "v3-iptvorg" && DateTime.TryParse(last, out DateTime dt) && (DateTime.Now - dt).TotalDays < 30)
            {
                LogService.LogInfo($"[LogoSync] Son logo senkronizasyon tarihi: {dt:yyyy-MM-dd}. Periyodik tarama atlandı.");
                return;
            }

            await SyncNowAsync();
        }

        public void ScanLocalLogosFolder()
        {
            try
            {
                var localLogos = new List<(string key, string file)>();
                var baseDirs = new List<string>
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logos"),
                    Path.Combine(Directory.GetCurrentDirectory(), "logos")
                };

                var validExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".png", ".jpg", ".jpeg", ".ico", ".webp"
                };

                foreach (var dir in baseDirs.Distinct())
                {
                    if (!Directory.Exists(dir)) continue;

                    var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        string ext = Path.GetExtension(file);
                        if (!validExtensions.Contains(ext)) continue;

                        string fileNameNoExt = Path.GetFileNameWithoutExtension(file);
                        string normalizedKey = fileNameNoExt.ToLowerInvariant().Replace(" ", "-").Trim('-');
                        string relativePath = "logos/" + Path.GetRelativePath(dir, file).Replace("\\", "/");
                        
                        localLogos.Add((normalizedKey, relativePath));

                        string rawClean = ChannelUtils.ToNormalizedKey(fileNameNoExt);
                        if (!string.IsNullOrEmpty(rawClean) && rawClean != normalizedKey)
                        {
                            localLogos.Add((rawClean, relativePath));
                        }

                        string slug = Regex.Replace(fileNameNoExt.ToLowerInvariant().Replace("&", "and").Replace("+", "plus").Replace(" ", "-"), @"[^a-z0-9-]", "").Trim('-');
                        if (!string.IsNullOrEmpty(slug) && slug != normalizedKey && slug != rawClean)
                        {
                            localLogos.Add((slug, relativePath));
                        }
                    }
                }

                if (localLogos.Count > 0)
                {
                    var existingIndex = _db.GetAllLogoIndex();
                    var newOrModified = localLogos.Where(l => !existingIndex.TryGetValue(l.key, out var f) || f != l.file).ToList();
                    if (newOrModified.Count > 0)
                    {
                        _db.UpdateLogoIndex(newOrModified);
                        ChannelEnricher.InvalidateLogoCache();
                        LogService.LogInfo($"[LogoSync] {newOrModified.Count} adet yeni yerel logo anahtarı indekse kaydedildi.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[LogoSync] Yerel logolar taranırken hata: {ex.Message}");
            }
        }

        private static string ToLogoSlug(string name)
        {
            string s = (name ?? "").ToLowerInvariant().Replace("&", "and").Replace("+", "plus").Replace(".", "-");
            s = Regex.Replace(s, @"\s+", "-");
            s = Regex.Replace(s, @"[^a-z0-9-]", "");
            s = Regex.Replace(s, @"-{2,}", "-");
            return s.Trim('-');
        }

        public async Task SyncNowAsync()
        {
            try
            {
                ScanLocalLogosFolder();
                var allLogos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // 1. iptv-org public static JSON database (No API key, no rate limit, exact country & alt_names)
                try
                {
                    LogService.LogInfo("[LogoSync] iptv-org açık logo veritabanı indiriliyor...");
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                    string channelsJson = await MediaHttpClient.GetStringAsync("https://iptv-org.github.io/api/channels.json", 20, cts.Token).ConfigureAwait(false);
                    string logosJson = await MediaHttpClient.GetStringAsync("https://iptv-org.github.io/api/logos.json", 20, cts.Token).ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(channelsJson) && !string.IsNullOrWhiteSpace(logosJson))
                    {
                        var bestLogoByChannelId = new Dictionary<string, (string Url, int Score)>(StringComparer.OrdinalIgnoreCase);
                        var logosArr = JArray.Parse(logosJson);
                        foreach (var l in logosArr)
                        {
                            string chId = l["channel"]?.ToString() ?? "";
                            string url = l["url"]?.ToString() ?? "";
                            string fmt = l["format"]?.ToString() ?? "";
                            if (string.IsNullOrWhiteSpace(chId) || string.IsNullOrWhiteSpace(url)) continue;

                            // WPF BitmapImage decodes PNG/JPG/WebP natively; strongly prefer non-SVG
                            bool isSvg = fmt.Equals("SVG", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
                            if (isSvg) continue;

                            int score = fmt.Equals("PNG", StringComparison.OrdinalIgnoreCase) ? 100 : 80;
                            if (!bestLogoByChannelId.TryGetValue(chId, out var existing) || score > existing.Score)
                            {
                                bestLogoByChannelId[chId] = (url, score);
                            }
                        }

                        var channelsArr = JArray.Parse(channelsJson);
                        foreach (var ch in channelsArr)
                        {
                            string chId = ch["id"]?.ToString() ?? "";
                            if (!bestLogoByChannelId.TryGetValue(chId, out var logoEntry)) continue;

                            string country = (ch["country"]?.ToString() ?? "").Trim().ToLowerInvariant();
                            string name = ch["name"]?.ToString() ?? "";
                            var names = new List<string>();
                            if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                            if (ch["alt_names"] is JArray alts)
                            {
                                foreach (var a in alts)
                                {
                                    string alt = a?.ToString() ?? "";
                                    if (!string.IsNullOrWhiteSpace(alt)) names.Add(alt);
                                }
                            }

                            foreach (var n in names)
                            {
                                string slug = ToLogoSlug(n);
                                string compact = Regex.Replace(n.ToLowerInvariant().Replace("&", "and").Replace("+", "plus"), @"[^a-z0-9]", "");
                                if (!string.IsNullOrEmpty(country))
                                {
                                    if (!string.IsNullOrEmpty(slug)) allLogos.TryAdd($"{slug}-{country}", logoEntry.Url);
                                    if (!string.IsNullOrEmpty(compact)) allLogos.TryAdd($"{compact}-{country}", logoEntry.Url);
                                }
                                if (!string.IsNullOrEmpty(slug)) allLogos.TryAdd(slug, logoEntry.Url);
                                if (!string.IsNullOrEmpty(compact)) allLogos.TryAdd(compact, logoEntry.Url);
                            }
                        }
                        LogService.LogInfo($"[LogoSync] iptv-org üzerinden {allLogos.Count} logo anahtarı hazırlandı.");
                    }
                }
                catch (Exception iptvEx)
                {
                    LogService.LogWarning($"[LogoSync] iptv-org logo veritabanı alınamadı: {iptvEx.Message}");
                }

                // 2. tv-logo/tv-logos full repository tree in a single GitHub API request (overrides with crisp transparent PNGs)
                try
                {
                    LogService.LogInfo("[LogoSync] tv-logos indeksi güncelleniyor...");
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/tv-logo/tv-logos/git/trees/main?recursive=1");
                    req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) StreamMesh/1.0");
                    using var resp = await MediaHttpClient.Client.SendAsync(req, cts.Token).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        var body = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                        var treeObj = JObject.Parse(body);
                        if (treeObj["tree"] is JArray treeArr)
                        {
                            foreach (var node in treeArr)
                            {
                                string path = node["path"]?.ToString() ?? "";
                                if (!path.StartsWith("countries/", StringComparison.OrdinalIgnoreCase) ||
                                    !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                string fileName = Path.GetFileNameWithoutExtension(path);
                                string downloadUrl = $"https://raw.githubusercontent.com/tv-logo/tv-logos/main/{path}";
                                string rawKey = fileName.ToLowerInvariant();
                                string slug = ToLogoSlug(fileName);
                                string compact = Regex.Replace(rawKey, @"[^a-z0-9]", "");

                                if (!string.IsNullOrEmpty(rawKey)) allLogos[rawKey] = downloadUrl;
                                if (!string.IsNullOrEmpty(slug)) allLogos[slug] = downloadUrl;
                                if (!string.IsNullOrEmpty(compact)) allLogos.TryAdd(compact, downloadUrl);
                            }
                        }
                    }
                }
                catch (Exception tvLogoEx)
                {
                    LogService.LogWarning($"[LogoSync] tv-logos ağacı alınamadı: {tvLogoEx.Message}");
                }

                if (allLogos.Count > 0)
                {
                    var batch = allLogos.Select(kvp => (kvp.Key, kvp.Value)).ToList();
                    _db.UpdateLogoIndex(batch);
                    ChannelEnricher.InvalidateLogoCache();
                    _db.SetSetting("LogoSyncDate", DateTime.Now.ToString("o"));
                    _db.SetSetting("LogoSyncVersion", "v3-iptvorg");
                    LogService.LogInfo($"[LogoSync] {batch.Count} adet standart TV logo indeksi kaydedildi.");

                    // Auto-enrich live TV channels missing LogoUrl
                    try
                    {
                        var channels = await _db.GetAllChannelsAsync().ConfigureAwait(false);
                        var missingLogos = channels.Where(c => string.IsNullOrWhiteSpace(c.LogoUrl) && MetadataEngine.IsLikelyLiveTv(c)).ToList();
                        if (missingLogos.Count > 0)
                        {
                            var enricher = new ChannelEnricher();
                            await enricher.EnrichChannelsAsync(missingLogos).ConfigureAwait(false);
                            int enrichedCount = missingLogos.Count(c => !string.IsNullOrWhiteSpace(c.LogoUrl));
                            if (enrichedCount > 0)
                            {
                                LogService.LogInfo($"[LogoSync] {enrichedCount} canlı TV kanalının logosu tamamlandı.");
                                DatabaseEngine.NotifyDatabaseUpdated();
                            }
                        }
                    }
                    catch (Exception enrichEx)
                    {
                        LogService.LogWarning($"[LogoSync] Mevcut kanalları zenginleştirme sırasında uyarı: {enrichEx.Message}");
                    }
                }
                else
                {
                    LogService.LogWarning("[LogoSync] Yeni logo alınamadı. Mevcut logo veritabanı korundu.");
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("LogoSync Error", ex);
            }
        }
    }
}
