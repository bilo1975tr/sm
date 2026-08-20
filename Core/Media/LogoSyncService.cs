using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StreamMesh.Core.Database;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class LogoSyncService
    {
        private static readonly HttpClient _client = new HttpClient();
        private readonly DatabaseEngine _db = new DatabaseEngine();

        public async Task SyncIfNecessaryAsync()
        {
            // 1. Always scan local logos folder first
            ScanLocalLogosFolder();

            string last = _db.GetSetting("LogoSyncDate", "");
            if (DateTime.TryParse(last, out DateTime dt) && (DateTime.Now - dt).TotalDays < 30) return;
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

                        // Also add raw clean key
                        string rawClean = ChannelUtils.ToNormalizedKey(fileNameNoExt);
                        if (!string.IsNullOrEmpty(rawClean) && rawClean != normalizedKey)
                        {
                            localLogos.Add((rawClean, relativePath));
                        }
                    }
                }

                if (localLogos.Count > 0)
                {
                    _db.UpdateLogoIndex(localLogos);
                    LogService.LogInfo($"[LogoSync] {localLogos.Count} adet yerel logo indekse kaydedildi.");
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

                LogService.LogInfo("[LogoSync] GitHub üzerinden logo verileri kontrol ediliyor...");

                _client.DefaultRequestHeaders.UserAgent.Clear();
                _client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                string[] countries = { "turkey", "germany" };
                var allLogos = new List<(string key, string file)>();

                foreach (var country in countries)
                {
                    try
                    {
                        var response = await _client.GetStringAsync($"https://api.github.com/repos/tv-logo/tv-logos/contents/countries/{country}");
                        var items = JArray.Parse(response);
                        foreach (var item in items)
                        {
                            string fileName = item["name"]?.ToString() ?? "";
                            if (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                            {
                                string key = fileName.ToLowerInvariant().Replace(".png", "");
                                string fullUrl = $"https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/{country}/{fileName}";
                                allLogos.Add((key, fullUrl));
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
                    _db.SetSetting("LogoSyncDate", DateTime.Now.ToString("o"));
                    LogService.LogInfo($"[LogoSync] {allLogos.Count} adet yeni standartta logo güncellendi.");
                }
                else
                {
                    LogService.LogWarning("[LogoSync] Yeni logo bulunamadı veya API erişim sınırı aşıldı. Mevcut logo veritabanı korundu.");
                }
            }
            catch (Exception ex) { LogService.LogError("LogoSync Error", ex); }
        }
    }
}
