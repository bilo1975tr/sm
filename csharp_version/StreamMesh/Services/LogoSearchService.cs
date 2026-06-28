using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using StreamMesh.Services.P2P;

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

        public static List<LogoSearchResult> CachedLogos => _cachedLogos;

        private static string GetCountryCodeFromLanguageOrCountry(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string norm = name.ToLowerInvariant().Trim();
            if (norm.Contains("türk") || norm.Contains("turk") || norm.Contains("tr")) return "tr";
            if (norm.Contains("alm") || norm.Contains("ger") || norm.Contains("de")) return "de";
            if (norm.Contains("ing") || norm.Contains("eng") || norm.Contains("en") || norm.Contains("us") || norm.Contains("gb") || norm.Contains("united kingdom") || norm.Contains("united states")) return "us";
            if (norm.Contains("fra") || norm.Contains("fre") || norm.Contains("fr")) return "fr";
            if (norm.Contains("ita") || norm.Contains("it")) return "it";
            if (norm.Contains("esp") || norm.Contains("spa") || norm.Contains("es")) return "es";
            if (norm.Contains("rus") || norm.Contains("ru")) return "ru";
            if (norm.Contains("aze") || norm.Contains("az")) return "az";
            if (norm.Contains("ara") || norm.Contains("ar")) return "ar";
            if (norm.Contains("hol") || norm.Contains("dut") || norm.Contains("nl")) return "nl";
            
            if (name.Length == 2) return norm;
            return null;
        }

        private static string CleanLogoFileNameToChannelName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return string.Empty;
            string name = Path.GetFileNameWithoutExtension(fileName);
            name = name.Replace('-', ' ').Replace('_', ' ');
            name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ").Trim();
            return name;
        }

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
                var countryCodes = new HashSet<string>();

                try
                {
                    var profile = UserService.CurrentUser;
                    if (profile != null)
                    {
                        var codeCountry = GetCountryCodeFromLanguageOrCountry(profile.Country);
                        if (codeCountry != null) countryCodes.Add(codeCountry);

                        if (profile.Languages != null)
                        {
                            foreach (var lang in profile.Languages)
                            {
                                var codeLang = GetCountryCodeFromLanguageOrCountry(lang);
                                if (codeLang != null) countryCodes.Add(codeLang);
                            }
                        }
                    }
                }
                catch { }

                if (countryCodes.Count == 0)
                {
                    countryCodes.Add("tr");
                }

                var tempLogos = new List<LogoSearchResult>();

                foreach (var code in countryCodes)
                {
                    // 1. tv-logo/tv-logos Source
                    try
                    {
                        string tvLogosApiUrl = $"https://api.github.com/repos/tv-logo/tv-logos/contents/countries/{code}";
                        var request = new HttpRequestMessage(HttpMethod.Get, tvLogosApiUrl);
                        request.Headers.Add("User-Agent", "StreamMeshApp/1.0");

                        var response = await HttpClientInstance.SendAsync(request);
                        if (response.IsSuccessStatusCode)
                        {
                            string json = await response.Content.ReadAsStringAsync();
                            using (var doc = JsonDocument.Parse(json))
                            {
                                foreach (var item in doc.RootElement.EnumerateArray())
                                {
                                    if (item.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "file" &&
                                        item.TryGetProperty("name", out var nameProp) &&
                                        item.TryGetProperty("download_url", out var downloadUrlProp))
                                    {
                                        string name = CleanLogoFileNameToChannelName(nameProp.GetString());
                                        string url = downloadUrlProp.GetString();
                                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url))
                                        {
                                            tempLogos.Add(new LogoSearchResult { Name = name, LogoUrl = url });
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError($"LogoSearchService: tv-logos fetch for code {code} failed", ex);
                    }

                    // 2. Fourqui/tv Source
                    try
                    {
                        string fourquiApiUrl = $"https://api.github.com/repos/Fourqui/tv/contents/countries/{code}";
                        var request = new HttpRequestMessage(HttpMethod.Get, fourquiApiUrl);
                        request.Headers.Add("User-Agent", "StreamMeshApp/1.0");

                        var response = await HttpClientInstance.SendAsync(request);
                        if (response.IsSuccessStatusCode)
                        {
                            string json = await response.Content.ReadAsStringAsync();
                            using (var doc = JsonDocument.Parse(json))
                            {
                                foreach (var item in doc.RootElement.EnumerateArray())
                                {
                                    if (item.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "file" &&
                                        item.TryGetProperty("name", out var nameProp) &&
                                        item.TryGetProperty("download_url", out var downloadUrlProp))
                                    {
                                        string name = CleanLogoFileNameToChannelName(nameProp.GetString());
                                        string url = downloadUrlProp.GetString();
                                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url))
                                        {
                                            tempLogos.Add(new LogoSearchResult { Name = name, LogoUrl = url });
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError($"LogoSearchService: Fourqui/tv fetch for code {code} failed", ex);
                    }
                }

                if (tempLogos.Count > 0)
                {
                    _cachedLogos = tempLogos
                        .GroupBy(x => x.Name.ToLowerInvariant())
                        .Select(g => g.First())
                        .ToList();

                    try
                    {
                        string serialized = JsonSerializer.Serialize(_cachedLogos);
                        await File.WriteAllTextAsync(CacheFilePath, serialized);
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError("LogoSearchService: Write cache failed", ex);
                    }
                }
                else
                {
                    if (File.Exists(CacheFilePath))
                    {
                        var cachedText = await File.ReadAllTextAsync(CacheFilePath);
                        _cachedLogos = JsonSerializer.Deserialize<List<LogoSearchResult>>(cachedText);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("LogoSearchService: EnsureLoadedAsync completely failed", ex);
            }

            if (_cachedLogos == null) _cachedLogos = new List<LogoSearchResult>();
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
                .Take(40)
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
            name = name.ToLowerInvariant();
            name = name.Replace(" hd", "").Replace(" sd", "").Replace(" fhd", "").Replace(" uhd", "").Replace(" hq", "");
            name = System.Text.RegularExpressions.Regex.Replace(name, @"[^a-zA-Z0-9]", "");
            return name;
        }
    }
}
