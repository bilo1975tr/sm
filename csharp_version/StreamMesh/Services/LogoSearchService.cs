using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace StreamMesh.Services
{
    public class LogoSearchResult
    {
        public string Name { get; set; }
        public string LogoUrl { get; set; }
    }

    public class LogoSearchService
    {
        private static readonly string CacheFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logos_cache.json");
        private static readonly HttpClient HttpClientInstance = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private static List<LogoSearchResult> _cachedLogos = null;

        public static async Task EnsureLoadedAsync()
        {
            if (_cachedLogos != null && _cachedLogos.Count > 0) return;

            try
            {
                if (File.Exists(CacheFilePath))
                {
                    var fileInfo = new FileInfo(CacheFilePath);
                    if (DateTime.Now - fileInfo.LastWriteTime < TimeSpan.FromDays(7))
                    {
                        var cachedText = await File.ReadAllTextAsync(CacheFilePath);
                        _cachedLogos = JsonSerializer.Deserialize<List<LogoSearchResult>>(cachedText);
                        if (_cachedLogos != null && _cachedLogos.Count > 0)
                        {
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("LogoSearchService: Read local cache failed", ex);
            }

            try
            {
                var responseJson = await HttpClientInstance.GetStringAsync("https://iptv-org.github.io/api/channels.json");
                using (JsonDocument doc = JsonDocument.Parse(responseJson))
                {
                    var temp = new List<LogoSearchResult>();
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        if (element.TryGetProperty("name", out JsonElement nameProp) &&
                            element.TryGetProperty("logo", out JsonElement logoProp))
                        {
                            string n = nameProp.GetString();
                            string l = logoProp.GetString();
                            if (!string.IsNullOrEmpty(n) && !string.IsNullOrEmpty(l))
                            {
                                temp.Add(new LogoSearchResult { Name = n, LogoUrl = l });
                            }
                        }
                    }
                    _cachedLogos = temp.DistinctBy(x => x.Name).ToList();
                }

                string serialized = JsonSerializer.Serialize(_cachedLogos);
                await File.WriteAllTextAsync(CacheFilePath, serialized);
            }
            catch (Exception ex)
            {
                LogService.LogError("LogoSearchService: Download channels.json failed", ex);
                if (_cachedLogos == null) _cachedLogos = new List<LogoSearchResult>();
            }
        }

        public static async Task<List<LogoSearchResult>> SearchLogosAsync(string channelName)
        {
            if (string.IsNullOrEmpty(channelName)) return new List<LogoSearchResult>();

            await EnsureLoadedAsync();

            string query = channelName.ToLowerInvariant().Trim();
            var results = _cachedLogos
                .Where(x => x.Name.ToLowerInvariant().Contains(query))
                .Select(x => new 
                {
                    Item = x,
                    Score = CalculateSimilarity(query, x.Name.ToLowerInvariant())
                })
                .OrderByDescending(x => x.Score)
                .Select(x => x.Item)
                .Take(12)
                .ToList();

            return results;
        }

        private static double CalculateSimilarity(string source, string target)
        {
            if (source == target) return 1.0;
            if (target.StartsWith(source)) return 0.9;
            if (target.Contains(source)) return 0.7;
            return 0.0;
        }

        public static async Task<int> AutoMatchAllMissingLogosAsync(Action<string> onProgress)
        {
            try
            {
                onProgress?.Invoke("Logo veritabanı yükleniyor...");
                await EnsureLoadedAsync();

                if (_cachedLogos == null || _cachedLogos.Count == 0)
                {
                    onProgress?.Invoke("Logo veritabanı boş veya indirilemedi.");
                    return 0;
                }

                var db = new DatabaseService();
                var channels = db.GetAllChannels();
                var missingChannels = channels.Where(c => string.IsNullOrEmpty(c.LogoUrl) || c.LogoUrl == "null").ToList();

                if (missingChannels.Count == 0)
                {
                    onProgress?.Invoke("Logosu eksik olan hiçbir kanal bulunamadı.");
                    return 0;
                }

                int matchedCount = 0;
                int counter = 0;

                onProgress?.Invoke($"Bulunan {missingChannels.Count} logosuz kanal için eşleştirme başlatılıyor...");

                foreach (var ch in missingChannels)
                {
                    counter++;
                    if (counter % 10 == 0 || counter == missingChannels.Count)
                    {
                        onProgress?.Invoke($"İşleniyor: {counter}/{missingChannels.Count} ({matchedCount} logo eşleştirildi)");
                    }

                    string cleanName = ch.Name.ToLowerInvariant().Trim();
                    
                    var matched = _cachedLogos.FirstOrDefault(x => x.Name.ToLowerInvariant() == cleanName);
                    if (matched == null)
                    {
                        string normalized = NormalizeChannelNameForLogo(cleanName);
                        matched = _cachedLogos.FirstOrDefault(x => NormalizeChannelNameForLogo(x.Name.ToLowerInvariant()) == normalized);
                    }

                    if (matched != null)
                    {
                        ch.LogoUrl = matched.LogoUrl;
                        db.SaveChannel(ch);
                        matchedCount++;
                    }
                }

                onProgress?.Invoke($"İşlem tamamlandı! Toplam {matchedCount} kanalın logosu otomatik bulundu ve güncellendi.");
                return matchedCount;
            }
            catch (Exception ex)
            {
                onProgress?.Invoke($"Hata oluştu: {ex.Message}");
                LogService.LogError("AutoMatchAllMissingLogosAsync error", ex);
                return 0;
            }
        }

        private static string NormalizeChannelNameForLogo(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            name = name.Replace(" hd", "").Replace(" sd", "").Replace(" fhd", "").Replace(" hq", "").Replace(" tr", "");
            name = System.Text.RegularExpressions.Regex.Replace(name, @"[^a-zA-Z0-9]", "");
            return name;
        }
    }
}
