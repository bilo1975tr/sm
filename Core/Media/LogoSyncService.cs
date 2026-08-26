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
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class LogoSyncService
    {
        private static readonly HttpClient _client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private readonly DatabaseEngine _db = new DatabaseEngine();

        public async Task SyncIfNecessaryAsync()
        {
            // 1. Always scan local logos folder first (instant)
            ScanLocalLogosFolder();

            // 2. Check if remote sync was performed within the last 30 days
            string last = _db.GetSetting("LogoSyncDate", "");
            if (DateTime.TryParse(last, out DateTime dt) && (DateTime.Now - dt).TotalDays < 30)
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
                    ".png", ".jpg", ".jpeg", ".svg", ".ico", ".webp"
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

                        // Relative path from base directory or pack / direct file path
                        string relativePath = "logos/" + Path.GetRelativePath(dir, file).Replace("\\", "/");
                        
                        localLogos.Add((normalizedKey, relativePath));

                        // Add clean name key
                        string rawClean = ChannelUtils.ToNormalizedKey(fileNameNoExt);
                        if (!string.IsNullOrEmpty(rawClean) && rawClean != normalizedKey)
                        {
                            localLogos.Add((rawClean, relativePath));
                        }

                        // Add slug without special characters
                        string slug = Regex.Replace(fileNameNoExt.ToLowerInvariant().Replace("&", "and").Replace("+", "plus").Replace(" ", "-"), @"[^a-z0-9-]", "").Trim('-');
                        if (!string.IsNullOrEmpty(slug) && slug != normalizedKey && slug != rawClean)
                        {
                            localLogos.Add((slug, relativePath));
                        }
                    }
                }

                if (localLogos.Count > 0)
                {
                    _db.UpdateLogoIndex(localLogos);
                    ChannelEnricher.InvalidateLogoCache();
                    LogService.LogInfo($"[LogoSync] {localLogos.Count} adet yerel logo anahtarı indekse kaydedildi.");
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[LogoSync] Yerel logolar taranırken hata: {ex.Message}");
            }
        }

        public async Task SyncNowAsync()
        {
            try
            {
                // Local scan first
                ScanLocalLogosFolder();

                LogService.LogInfo("[LogoSync] GitHub (tv-logos) üzerinden logo verileri güncelleniyor...");

                _client.DefaultRequestHeaders.UserAgent.Clear();
                _client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) StreamMesh/1.0");

                string[] countries = { "turkey", "germany", "united-kingdom", "united-states", "france", "italy", "spain", "azerbaijan", "netherlands" };
                var allLogos = new List<(string key, string file)>();

                foreach (var country in countries)
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        var response = await _client.GetStringAsync($"https://api.github.com/repos/tv-logo/tv-logos/contents/countries/{country}", cts.Token);
                        var items = JArray.Parse(response);
                        foreach (var item in items)
                        {
                            string fileName = item["name"]?.ToString() ?? "";
                            if (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                            {
                                string baseName = fileName.Substring(0, fileName.Length - 4);
                                string rawKey = baseName.ToLowerInvariant();
                                string downloadUrl = item["download_url"]?.ToString() 
                                    ?? $"https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/{country}/{fileName}";

                                allLogos.Add((rawKey, downloadUrl));

                                // Add normalized key variant
                                string normKey = ChannelUtils.ToNormalizedKey(baseName);
                                if (!string.IsNullOrEmpty(normKey) && normKey != rawKey)
                                {
                                    allLogos.Add((normKey, downloadUrl));
                                }

                                // Add slug variant
                                string slug = Regex.Replace(baseName.ToLowerInvariant().Replace("&", "and").Replace("+", "plus").Replace(" ", "-"), @"[^a-z0-9-]", "").Trim('-');
                                if (!string.IsNullOrEmpty(slug) && slug != rawKey && slug != normKey)
                                {
                                    allLogos.Add((slug, downloadUrl));
                                }
                            }
                        }
                    }
                    catch (Exception countryEx)
                    {
                        LogService.LogWarning($"[LogoSync] {country} logoları indirilemedi: {countryEx.Message}");
                    }
                }

                if (allLogos.Count > 0)
                {
                    _db.UpdateLogoIndex(allLogos);
                    ChannelEnricher.InvalidateLogoCache();
                    _db.SetSetting("LogoSyncDate", DateTime.Now.ToString("o"));
                    LogService.LogInfo($"[LogoSync] {allLogos.Count} adet yeni standart logo indeksi kaydedildi.");

                    // Auto-enrich any existing channels that currently have empty LogoUrl
                    try
                    {
                        var channels = await _db.GetAllChannelsAsync();
                        var missingLogos = channels.Where(c => string.IsNullOrWhiteSpace(c.LogoUrl)).ToList();
                        if (missingLogos.Count > 0)
                        {
                            var enricher = new ChannelEnricher();
                            await enricher.EnrichChannelsAsync(missingLogos);
                            int enrichedCount = missingLogos.Count(c => !string.IsNullOrWhiteSpace(c.LogoUrl));
                            if (enrichedCount > 0)
                            {
                                LogService.LogInfo($"[LogoSync] {enrichedCount} kanalın logosu indeks üzerinden otomatik tamamlandı.");
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
                    LogService.LogWarning("[LogoSync] Yeni logo bulunamadı veya API erişim sınırı aşıldı. Mevcut logo veritabanı korundu.");
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("LogoSync Error", ex);
            }
        }
    }
}
