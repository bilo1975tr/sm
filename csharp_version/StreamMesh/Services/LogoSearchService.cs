using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using StreamMesh.Services.Auth;

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

        private static string GetCountryFolderName(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            string norm = code.ToLowerInvariant().Trim();
            switch (norm)
            {
                case "tr": return "turkey";
                case "de": return "germany";
                case "us": return "united-states";
                case "gb": return "united-kingdom";
                case "fr": return "france";
                case "it": return "italy";
                case "es": return "spain";
                case "ru": return "russia";
                case "gr": return "greece";
                case "nl": return "netherlands";
                case "ar": return "argentina";
                case "at": return "austria";
                case "au": return "australia";
                case "be": return "belgium";
                case "ca": return "canada";
                case "cr": return "costa-rica";
                case "hr": return "croatia";
                case "hk": return "hong-kong";
                case "in": return "india";
                case "id": return "indonesia";
                case "my": return "malaysia";
                case "mt": return "malta";
                case "mx": return "mexico";
                case "nz": return "new-zealand";
                case "pl": return "poland";
                case "pt": return "portugal";
                case "rs": return "serbia";
                case "sg": return "singapore";
                case "za": return "south-africa";
                case "ch": return "switzerland";
                case "ae": return "united-arab-emirates";
                default: return norm;
            }
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

            LogService.Log($"[LogoSearchService] EnsureLoadedAsync: Logo veritabanı yükleme işlemi başlatıldı. Önbellek dosyası yolu: '{CacheFilePath}'");

            try
            {
                if (File.Exists(CacheFilePath))
                {
                    var fileInfo = new FileInfo(CacheFilePath);
                    var cacheAge = DateTime.Now - fileInfo.LastWriteTime;
                    LogService.Log($"[LogoSearchService] Yerel önbellek dosyası mevcut. Dosya tarihi: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss} (Ömür: {cacheAge.TotalDays:F1} gün)");

                    if (cacheAge < TimeSpan.FromDays(7))
                    {
                        var cachedText = await File.ReadAllTextAsync(CacheFilePath);
                        _cachedLogos = JsonSerializer.Deserialize<List<LogoSearchResult>>(cachedText);
                        if (_cachedLogos != null && _cachedLogos.Count > 0)
                        {
                            LogService.Log($"[LogoSearchService] Yerel önbellekten {_cachedLogos.Count} adet logo başarıyla belleğe yüklendi. GitHub sorgulamasına gerek kalmadı.");
                            return;
                        }
                    }
                    else
                    {
                        LogService.Log($"[LogoSearchService] Yerel önbellek süresi (7 gün) dolmuş. GitHub API üzerinden güncel logolar sorgulanacak.");
                    }
                }
                else
                {
                    LogService.Log($"[LogoSearchService] Yerel önbellek dosyası bulunamadı. Sıfırdan GitHub sorgusu yapılacak.");
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("[LogoSearchService] Yerel önbellek okunurken hata oluştu", ex);
            }

            try
            {
                var countryCodes = new HashSet<string>();

                try
                {
                    var profile = UserService.CurrentUser;
                    if (profile != null)
                    {
                        LogService.Log($"[LogoSearchService] Kullanıcı profili tespit edildi. Ülke: '{profile.Country}', Diller: '{string.Join(", ", profile.Languages ?? new List<string>())}'");
                        var codeCountry = GetCountryCodeFromLanguageOrCountry(profile.Country);
                        if (codeCountry != null) 
                        {
                            countryCodes.Add(codeCountry);
                            LogService.Log($"[LogoSearchService] Profil ülkesinden '{profile.Country}' çıkarılan ülke kodu: '{codeCountry}'");
                        }

                        if (profile.Languages != null)
                        {
                            foreach (var lang in profile.Languages)
                            {
                                var codeLang = GetCountryCodeFromLanguageOrCountry(lang);
                                if (codeLang != null)
                                {
                                    countryCodes.Add(codeLang);
                                    LogService.Log($"[LogoSearchService] Profil dilinden '{lang}' çıkarılan ülke kodu: '{codeLang}'");
                                }
                            }
                        }
                    }
                    else
                    {
                        LogService.Log($"[LogoSearchService] Aktif kullanıcı oturumu bulunamadı. Varsayılan ülke kodları kullanılacak.");
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("[LogoSearchService] Kullanıcı profilinden ülke kodları çözümlenirken hata oluştu", ex);
                }

                if (countryCodes.Count == 0)
                {
                    countryCodes.Add("tr");
                    LogService.Log($"[LogoSearchService] Belirlenmiş ülke kodu bulunamadı, varsayılan olarak 'tr' (Türkiye) eklendi.");
                }

                LogService.Log($"[LogoSearchService] Sorgulanacak ülke kodları kümesi: [{string.Join(", ", countryCodes)}]");
                var tempLogos = new List<LogoSearchResult>();

                foreach (var code in countryCodes)
                {
                    int startCount = tempLogos.Count;
                    string folderName = GetCountryFolderName(code);
                    if (string.IsNullOrEmpty(folderName)) continue;

                    // 1. tv-logo/tv-logos Source
                    try
                    {
                        string tvLogosApiUrl = $"https://api.github.com/repos/tv-logo/tv-logos/contents/countries/{folderName}";
                        LogService.Log($"[LogoSearchService] tv-logo/tv-logos reposu sorgulanıyor. Ülke: '{code}' (Klasör: '{folderName}'), URL: '{tvLogosApiUrl}'");
                        
                        var request = new HttpRequestMessage(HttpMethod.Get, tvLogosApiUrl);
                        request.Headers.Add("User-Agent", "StreamMeshApp/1.0");

                        var response = await HttpClientInstance.SendAsync(request);
                        LogService.Log($"[LogoSearchService] tv-logo/tv-logos yanıt döndü. Durum Kodu: {response.StatusCode} ({(int)response.StatusCode})");

                        if (response.IsSuccessStatusCode)
                        {
                            string json = await response.Content.ReadAsStringAsync();
                            int addedCount = 0;
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
                                            addedCount++;
                                        }
                                    }
                                }
                            }
                            LogService.Log($"[LogoSearchService] tv-logo/tv-logos reposundan '{code}' ülkesi için {addedCount} adet logo başarıyla ayrıştırıldı.");
                        }
                        else
                        {
                            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                            {
                                LogService.Log($"[LogoSearchService] tv-logo/tv-logos FORBIDDEN (403) hatası aldı. GitHub API Hız Sınırına (Rate Limit) takılmış olabilirsiniz!", "WARN");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError($"[LogoSearchService] tv-logos fetch for code '{code}' failed", ex);
                    }

                    int midCount = tempLogos.Count;

                    // 2. Fourqui/tv Source
                    try
                    {
                        string fourquiApiUrl = $"https://api.github.com/repos/Fourqui/tv/contents/countries/{folderName}";
                        LogService.Log($"[LogoSearchService] Fourqui/tv reposu sorgulanıyor. Ülke: '{code}' (Klasör: '{folderName}'), URL: '{fourquiApiUrl}'");
                        
                        var request = new HttpRequestMessage(HttpMethod.Get, fourquiApiUrl);
                        request.Headers.Add("User-Agent", "StreamMeshApp/1.0");

                        var response = await HttpClientInstance.SendAsync(request);
                        LogService.Log($"[LogoSearchService] Fourqui/tv yanıt döndü. Durum Kodu: {response.StatusCode} ({(int)response.StatusCode})");

                        if (response.IsSuccessStatusCode)
                        {
                            string json = await response.Content.ReadAsStringAsync();
                            int addedCount = 0;
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
                                            addedCount++;
                                        }
                                    }
                                }
                            }
                            LogService.Log($"[LogoSearchService] Fourqui/tv reposundan '{code}' ülkesi için {addedCount} adet logo başarıyla ayrıştırıldı.");
                        }
                        else
                        {
                            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                            {
                                LogService.Log($"[LogoSearchService] Fourqui/tv FORBIDDEN (403) hatası aldı. GitHub API Hız Sınırına (Rate Limit) takılmış olabilirsiniz!", "WARN");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError($"[LogoSearchService] Fourqui/tv fetch for code '{code}' failed", ex);
                    }

                    int endCount = tempLogos.Count;
                    LogService.Log($"[LogoSearchService] Ülke '{code}' için toplamda {endCount - startCount} yeni logo adresi toplandı.");
                }

                if (tempLogos.Count > 0)
                {
                    _cachedLogos = tempLogos
                        .GroupBy(x => x.Name.ToLowerInvariant())
                        .Select(g => g.First())
                        .ToList();

                    LogService.Log($"[LogoSearchService] Tekilleştirme sonrası toplam {_cachedLogos.Count} benzersiz logo belleğe yerleştirildi. Önbelleğe yazılıyor...");

                    try
                    {
                        string serialized = JsonSerializer.Serialize(_cachedLogos);
                        await File.WriteAllTextAsync(CacheFilePath, serialized);
                        LogService.Log($"[LogoSearchService] Yeni logolar başarıyla yerel önbelleğe kaydedildi: '{CacheFilePath}'");
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError("[LogoSearchService] Yeni önbellek dosyası yazılırken hata oluştu", ex);
                    }
                }
                else
                {
                    LogService.Log($"[LogoSearchService] GitHub API üzerinden hiçbir yeni logo çekilemedi. Eski önbellek kontrol ediliyor...");
                    if (File.Exists(CacheFilePath))
                    {
                        var cachedText = await File.ReadAllTextAsync(CacheFilePath);
                        _cachedLogos = JsonSerializer.Deserialize<List<LogoSearchResult>>(cachedText);
                        LogService.Log($"[LogoSearchService] GitHub başarısız olunca eski yerel önbellek dosyası geri yüklendi. Logo sayısı: {_cachedLogos?.Count ?? 0}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("[LogoSearchService] EnsureLoadedAsync completely failed", ex);
            }

            if (_cachedLogos == null) _cachedLogos = new List<LogoSearchResult>();
        }

        public static async Task<List<LogoSearchResult>> SearchLogosAsync(string channelName)
        {
            if (string.IsNullOrEmpty(channelName)) return new List<LogoSearchResult>();

            LogService.Log($"[LogoSearchService] SearchLogosAsync: '{channelName}' kanalı için logo araması tetiklendi.");
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

            LogService.Log($"[LogoSearchService] SearchLogosAsync: '{channelName}' araması tamamlandı. {_cachedLogos.Count} logo arasından {results.Count} adet eşleşme listelendi.");
            if (results.Count > 0)
            {
                var top = results.First();
                LogService.Log($"[LogoSearchService] SearchLogosAsync: En yüksek puanlı eşleşme: '{top.Name}' -> {top.LogoUrl}");
            }
            else
            {
                LogService.Log($"[LogoSearchService] SearchLogosAsync: '{channelName}' için hiç logo eşleşmesi bulunamadı.");
            }

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
                LogService.Log("[LogoSearchService] AutoMatchAllMissingLogosAsync tetiklendi.");
                onProgress?.Invoke("Logo veritabanı yükleniyor...");
                await EnsureLoadedAsync();

                if (_cachedLogos == null || _cachedLogos.Count == 0)
                {
                    LogService.Log("[LogoSearchService] Otomatik eşleştirme başarısız: Logo veritabanı boş veya indirilemedi.", "WARN");
                    onProgress?.Invoke("Logo veritabanı boş veya indirilemedi.");
                    return 0;
                }

                var db = new DatabaseService();
                var channels = db.GetAllChannels();
                var missingChannels = channels.Where(c => string.IsNullOrEmpty(c.LogoUrl) || c.LogoUrl == "null").ToList();

                LogService.Log($"[LogoSearchService] Toplam kanal sayısı: {channels.Count}, Logosu eksik kanal sayısı: {missingChannels.Count}");

                if (missingChannels.Count == 0)
                {
                    LogService.Log("[LogoSearchService] Logosu eksik olan hiçbir kanal bulunamadı. Eşleştirme sonlandırıldı.");
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
                    LogService.Log($"[LogoSearchService] Eşleştiriliyor [{counter}/{missingChannels.Count}]: Kanal Adı: '{ch.Name}' (Temiz: '{cleanName}')");
                    
                    // 1. Aşama: Birebir eşleşme
                    var matched = _cachedLogos.FirstOrDefault(x => x.Name.ToLowerInvariant() == cleanName);
                    if (matched != null)
                    {
                        LogService.Log($"[LogoSearchService] -> Birebir Eşleşme Bulundu! '{ch.Name}' == '{matched.Name}' -> Logo URL: {matched.LogoUrl}");
                    }
                    else
                    {
                        // 2. Aşama: Normalize ederek eşleştirme (HD/SD/FHD, özel karakterler temizlenerek)
                        string normalized = NormalizeChannelNameForLogo(cleanName);
                        matched = _cachedLogos.FirstOrDefault(x => NormalizeChannelNameForLogo(x.Name.ToLowerInvariant()) == normalized);
                        if (matched != null)
                        {
                            LogService.Log($"[LogoSearchService] -> Normalize Eşleşme Bulundu! '{ch.Name}' (Normalize: '{normalized}') == '{matched.Name}' (Normalize: '{NormalizeChannelNameForLogo(matched.Name)}') -> Logo URL: {matched.LogoUrl}");
                        }
                    }

                    if (matched != null)
                    {
                        ch.LogoUrl = matched.LogoUrl;
                        db.SaveChannel(ch);
                        matchedCount++;
                    }
                    else
                    {
                        LogService.Log($"[LogoSearchService] -> '{ch.Name}' için eşleşen logo bulunamadı.");
                    }
                }

                LogService.Log($"[LogoSearchService] Otomatik logo eşleştirme bitti. Toplam eşleşen: {matchedCount}/{missingChannels.Count}");
                onProgress?.Invoke($"İşlem tamamlandı! Toplam {matchedCount} kanalın logosu otomatik bulundu ve güncellendi.");
                return matchedCount;
            }
            catch (Exception ex)
            {
                onProgress?.Invoke($"Hata oluştu: {ex.Message}");
                LogService.LogError("[LogoSearchService] AutoMatchAllMissingLogosAsync error", ex);
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
