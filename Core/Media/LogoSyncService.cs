using System;
using System.Collections.Generic;
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
            string last = _db.GetSetting("LogoSyncDate", "");
            if (DateTime.TryParse(last, out DateTime dt) && (DateTime.Now - dt).TotalDays < 30) return;
            await SyncNowAsync();
        }

        public async Task SyncNowAsync()
        {
            try
            {
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
