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
            
            // Tam eşleşmeler veya sınırlandırılmış kelime eşleşmeleri daha güvenlidir
            if (norm == "tr" || norm == "türkiye" || norm == "turkey" || norm.Contains("türkçe") || norm.Contains("turkish")) return "tr";
            if (norm == "de" || norm == "almanya" || norm == "germany" || norm.Contains("almanca") || norm.Contains("deutsch") || norm.Contains("german")) return "de";
            if (norm == "en" || norm == "us" || norm == "gb" || norm == "uk" || norm == "ingiltere" || norm == "united kingdom" || norm == "united states" || norm.Contains("ingilizce") || norm.Contains("english")) return "us";
            if (norm == "fr" || norm == "fransa" || norm == "france" || norm.Contains("fransızca") || norm.Contains("french")) return "fr";
            if (norm == "it" || norm == "italya" || norm == "italy" || norm.Contains("italyanca") || norm.Contains("italian")) return "it";
            if (norm == "es" || norm == "ispanya" || norm == "spain" || norm.Contains("ispanyolca") || norm.Contains("spanish")) return "es";
            if (norm == "ru" || norm == "rusya" || norm == "russia" || norm.Contains("rusça") || norm.Contains("russian")) return "ru";
            if (norm == "az" || norm == "azerbaycan" || norm == "azerbaijan" || norm.Contains("azerice") || norm.Contains("azerbaijani")) return "az";
            if (norm == "ar" || norm == "arapça" || norm == "arabic" || norm.Contains("arabistan")) return "ar";
            if (norm == "nl" || norm == "hollanda" || norm == "netherlands" || norm.Contains("felemenkçe") || norm.Contains("dutch")) return "nl";

            // Grup başlıklarındaki yaygın ülke kodlarını yakalamak için (örn. "[DE] Cinema", "DE: Action")
            if (System.Text.RegularExpressions.Regex.IsMatch(norm, @"\btr\b")) return "tr";
            if (System.Text.RegularExpressions.Regex.IsMatch(norm, @"\bde\b")) return "de";
            if (System.Text.RegularExpressions.Regex.IsMatch(norm, @"\b(en|us|gb|uk)\b")) return "us";
            if (System.Text.RegularExpressions.Regex.IsMatch(norm, @"\bfr\b")) return "fr";
            if (System.Text.RegularExpressions.Regex.IsMatch(norm, @"\bit\b")) return "it";
            if (System.Text.RegularExpressions.Regex.IsMatch(norm, @"\bes\b")) return "es";
            if (System.Text.RegularExpressions.Regex.IsMatch(norm, @"\bru\b")) return "ru";
            if (System.Text.RegularExpressions.Regex.IsMatch(norm, @"\baz\b")) return "az";
            if (System.Text.RegularExpressions.Regex.IsMatch(norm, @"\bar\b")) return "ar";
            if (System.Text.RegularExpressions.Regex.IsMatch(norm, @"\bnl\b")) return "nl";

            if (norm.Length == 2) return norm;
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

                            // DETAYLI LOG: Önbellekten yüklenen tüm logoları her ihtimale karşı logs klasörüne de yedekle/yaz
                            try
                            {
                                string logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                                if (!Directory.Exists(logsDir)) Directory.CreateDirectory(logsDir);
                                string cacheDebugPath = Path.Combine(logsDir, "logo_cache_loaded_debug.json");
                                await File.WriteAllTextAsync(cacheDebugPath, cachedText);
                                LogService.Log($"[LogoSearchService] [DETAYLI LOG] Önbellekten yüklenen tüm logoların ham listesi '{cacheDebugPath}' dosyasına yazıldı.");
                            }
                            catch (Exception ex)
                            {
                                LogService.LogError("[LogoSearchService] Önbellek debug dosyası yazılırken hata", ex);
                            }

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

                // Kanal veritabanından ek dilleri/ülkeleri de tara
                try
                {
                    var db = new DatabaseService();
                    var allChannels = db.GetAllChannels();
                    if (allChannels != null && allChannels.Count > 0)
                    {
                        int scanned = 0;
                        int detectedCount = 0;
                        foreach (var ch in allChannels)
                        {
                            if (!string.IsNullOrEmpty(ch.Language))
                            {
                                var codeLang = GetCountryCodeFromLanguageOrCountry(ch.Language);
                                if (codeLang != null && GetCountryFolderName(codeLang) != null)
                                {
                                    if (countryCodes.Add(codeLang)) detectedCount++;
                                }
                            }
                            if (!string.IsNullOrEmpty(ch.GroupTitle))
                            {
                                var codeGroup = GetCountryCodeFromLanguageOrCountry(ch.GroupTitle);
                                if (codeGroup != null && GetCountryFolderName(codeGroup) != null)
                                {
                                    if (countryCodes.Add(codeGroup)) detectedCount++;
                                }
                            }
                            scanned++;
                            if (scanned > 1000) break;
                        }
                        if (detectedCount > 0)
                        {
                            LogService.Log($"[LogoSearchService] Kanal veritabanı taramasından {detectedCount} yeni benzersiz ülke kodu tespit edildi.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("[LogoSearchService] Kanal veritabanından ülke kodları taranırken hata oluştu", ex);
                }

                if (countryCodes.Count == 0)
                {
                    countryCodes.Add("tr");
                    LogService.Log($"[LogoSearchService] Belirlenmiş ülke kodu bulunamadı, varsayılan olarak 'tr' (Türkiye) eklendi.");
                }

                // GitHub API Hız Sınırını (Rate Limit) aşmamak için ülke kodlarını limitliyoruz
                var finalCountryCodes = countryCodes.ToList();
                if (finalCountryCodes.Count > 6)
                {
                    var priorityCodes = new List<string> { "tr", "de", "us", "gb" };
                    finalCountryCodes = finalCountryCodes.OrderByDescending(c => priorityCodes.Contains(c)).Take(6).ToList();
                    LogService.Log($"[LogoSearchService] Sınır aşımını önlemek için ülke kodları limitlendi. Orijinal: [{string.Join(", ", countryCodes)}], Seçilen: [{string.Join(", ", finalCountryCodes)}]");
                }

                LogService.Log($"[LogoSearchService] Sorgulanacak ülke kodları kümesi: [{string.Join(", ", finalCountryCodes)}]");
                var tempLogos = new List<LogoSearchResult>();

                foreach (var code in finalCountryCodes)
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

                            // DETAYLI LOG: Karşı taraftan alınan ham dosyanın tamamını kaydet
                            try
                            {
                                string logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                                if (!Directory.Exists(logsDir)) Directory.CreateDirectory(logsDir);
                                string responseFilePath = Path.Combine(logsDir, $"logo_api_response_{code}_tvlogos_raw.json");
                                await File.WriteAllTextAsync(responseFilePath, json);
                                LogService.Log($"[LogoSearchService] [DETAYLI LOG] tv-logo/tv-logos'tan alınan ham JSON yanıt '{responseFilePath}' dosyasına kaydedildi. Karakter sayısı: {json.Length}");
                            }
                            catch (Exception fileEx)
                            {
                                LogService.LogError("[LogoSearchService] tv-logos ham JSON yanıt dosyası kaydedilirken hata", fileEx);
                            }

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

                            // DETAYLI LOG: Karşı taraftan alınan ham dosyanın tamamını kaydet
                            try
                            {
                                string logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                                if (!Directory.Exists(logsDir)) Directory.CreateDirectory(logsDir);
                                string responseFilePath = Path.Combine(logsDir, $"logo_api_response_{code}_fourqui_raw.json");
                                await File.WriteAllTextAsync(responseFilePath, json);
                                LogService.Log($"[LogoSearchService] [DETAYLI LOG] Fourqui/tv'den alınan ham JSON yanıt '{responseFilePath}' dosyasına kaydedildi. Karakter sayısı: {json.Length}");
                            }
                            catch (Exception fileEx)
                            {
                                LogService.LogError("[LogoSearchService] Fourqui/tv ham JSON yanıt dosyası kaydedilirken hata", fileEx);
                            }

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

        private class ScoredTraceResult
        {
            public LogoSearchResult Item { get; set; }
            public double Score { get; set; }
            public string Reason { get; set; }
        }

        public static async Task<List<LogoSearchResult>> SearchLogosAsync(string channelName)
        {
            if (string.IsNullOrEmpty(channelName)) return new List<LogoSearchResult>();

            LogService.Log($"[LogoSearchService] SearchLogosAsync: '{channelName}' kanalı için logo araması tetiklendi.");
            await EnsureLoadedAsync();

            string query = channelName.ToLowerInvariant().Trim();
            string normalizedQuery = NormalizeChannelNameForLogo(query);

            string logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            if (!Directory.Exists(logsDir)) Directory.CreateDirectory(logsDir);

            // Güvenli ve temiz bir dosya adı oluşturma
            string safeQueryForFile = string.Concat(query.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            if (string.IsNullOrEmpty(safeQueryForFile)) safeQueryForFile = "default_search";
            string traceFilePath = Path.Combine(logsDir, $"logo_search_trace_{safeQueryForFile}.log");

            try
            {
                using (var writer = new StreamWriter(traceFilePath, false, System.Text.Encoding.UTF8))
                {
                    writer.WriteLine("==================================================================");
                    writer.WriteLine("=== LOGO ARAMA İZLEME DETAYLI RAPORU (A'DAN Z'YE DETAYLAR) ===");
                    writer.WriteLine("==================================================================");
                    writer.WriteLine($"Tarih/Saat: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    writer.WriteLine($"Aranan Kanal Adı (Orijinal): '{channelName}'");
                    writer.WriteLine($"Aranan Kanal Adı (Küçük Harf / Trim): '{query}'");
                    writer.WriteLine($"Aranan Kanal Adı (Normalize Edilmiş): '{normalizedQuery}'");
                    writer.WriteLine($"Veritabanındaki Toplam Logo Sayısı: {_cachedLogos?.Count ?? 0}");
                    writer.WriteLine("Önbellek Dosya Yolu: " + CacheFilePath);
                    writer.WriteLine("------------------------------------------------------------------");
                    writer.WriteLine();

                    if (_cachedLogos == null || _cachedLogos.Count == 0)
                    {
                        writer.WriteLine("HATA: Logo veritabanı boş veya yüklenemedi!");
                    }
                    else
                    {
                        writer.WriteLine("Eşleşme Hesaplama Kuralları ve Skor Değerleri:");
                        writer.WriteLine("1. Birebir Tam Eşleşme (Skor: 1.0)");
                        writer.WriteLine("2. Logo adının aranan kanal adıyla başlaması (Skor: 0.9)");
                        writer.WriteLine("3. Logo adının aranan kanal adını içermesi (Skor: 0.7)");
                        writer.WriteLine("4. Aranan kanal adının logo adını içermesi (Skor: 0.65)");
                        writer.WriteLine("5. Normalize edilmiş isimlerin tam eşleşmesi (Skor: 0.85)");
                        writer.WriteLine("6. Normalize edilmiş isimlerin birbirini içermesi (Skor: 0.6)");
                        writer.WriteLine("------------------------------------------------------------------");
                        writer.WriteLine();

                        var scoredItems = new List<ScoredTraceResult>();

                        writer.WriteLine("A'DAN Z'YE TÜM KARŞILAŞTIRMALAR VE DETAYLI ANALİZ:");
                        writer.WriteLine("==================================================================");

                        foreach (var x in _cachedLogos)
                        {
                            string targetName = x.Name;
                            string targetLower = targetName.ToLowerInvariant().Trim();
                            string targetNormalized = NormalizeChannelNameForLogo(targetLower);

                            double score = 0.0;
                            string matchReason = "Uyuşmuyor";

                            // Karşılaştırma senaryoları
                            if (query == targetLower)
                            {
                                score = 1.0;
                                matchReason = "Tam Eşleşme (Küçük harf duyarlı)";
                            }
                            else if (normalizedQuery == targetNormalized && !string.IsNullOrEmpty(normalizedQuery))
                            {
                                score = 0.85;
                                matchReason = $"Normalize edilmiş halleri birebir eşleşiyor ('{normalizedQuery}' == '{targetNormalized}')";
                            }
                            else if (targetLower.StartsWith(query))
                            {
                                score = 0.9;
                                matchReason = $"Logo adı '{targetName}', sorgu '{query}' ile başlıyor.";
                            }
                            else if (targetLower.Contains(query))
                            {
                                score = 0.7;
                                matchReason = $"Logo adı '{targetName}', sorgu '{query}' değerini içeriyor.";
                            }
                            else if (query.Contains(targetLower))
                            {
                                score = 0.65;
                                matchReason = $"Sorgu '{query}', logo adı '{targetName}' değerini içeriyor.";
                            }
                            else if (!string.IsNullOrEmpty(normalizedQuery) && !string.IsNullOrEmpty(targetNormalized) && 
                                     (targetNormalized.Contains(normalizedQuery) || normalizedQuery.Contains(targetNormalized)))
                            {
                                score = 0.6;
                                matchReason = $"Normalize edilmiş haller birbirini içeriyor (Sorgu: '{normalizedQuery}' <-> Logo: '{targetNormalized}')";
                            }

                            if (score > 0)
                            {
                                scoredItems.Add(new ScoredTraceResult { Item = x, Score = score, Reason = matchReason });
                            }

                            writer.WriteLine($"[KONTROL] Logo: '{targetName}' | Normalize: '{targetNormalized}'");
                            writer.WriteLine($"          Sorgu: '{channelName}' | Normalize: '{normalizedQuery}'");
                            writer.WriteLine($"          Durum: {(score > 0 ? $"EŞLEŞTİ (Skor: {score:F2} - {matchReason})" : "UYUŞMUYOR")}");
                            writer.WriteLine($"          Detay: Logo adresi: {x.LogoUrl}");
                            writer.WriteLine();
                        }

                        writer.WriteLine("==================================================================");
                        writer.WriteLine($"=== SKOR ALAN EN YÜKSEK PUANLI ADAYLAR (TOP 40) ===");
                        writer.WriteLine("==================================================================");
                        var sortedResults = scoredItems.OrderByDescending(x => x.Score).Take(40).ToList();
                        if (sortedResults.Count == 0)
                        {
                            writer.WriteLine("HİÇBİR LOGO EŞLEŞMEDİ VEYA UYGUN BULUNMADI.");
                        }
                        else
                        {
                            for (int i = 0; i < sortedResults.Count; i++)
                            {
                                var r = sortedResults[i];
                                writer.WriteLine($"{i + 1}. Sıra [Skor: {r.Score:F2}] - Logo: '{r.Item.Name}' -> URL: {r.Item.LogoUrl}");
                                writer.WriteLine($"          Eşleşme Gerekçesi: {r.Reason}");
                                writer.WriteLine();
                            }
                        }
                    }
                }
                LogService.Log($"[LogoSearchService] [DETAYLI LOG] A'dan Z'ye tüm karşılaştırma ve analiz detayları içeren log dosyası başarıyla oluşturuldu: '{traceFilePath}'");
            }
            catch (Exception ex)
            {
                LogService.LogError("[LogoSearchService] Logo arama izleme log dosyası oluşturulurken hata", ex);
            }

            // Gelişmiş, esnek ve akıllı arama algoritmasıyla sonuç listesini döndür
            var results = _cachedLogos
                .Select(x => {
                    double score = CalculateSimilarityEnhanced(query, normalizedQuery, x.Name.ToLowerInvariant().Trim(), NormalizeChannelNameForLogo(x.Name));
                    return new { Item = x, Score = score };
                })
                .Where(x => x.Score > 0)
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

        private static double CalculateSimilarityEnhanced(string query, string normalizedQuery, string targetLower, string targetNormalized)
        {
            if (query == targetLower) return 1.0;
            if (normalizedQuery == targetNormalized && !string.IsNullOrEmpty(normalizedQuery)) return 0.85;
            if (targetLower.StartsWith(query)) return 0.9;
            if (targetLower.Contains(query)) return 0.7;
            if (query.Contains(targetLower)) return 0.65;
            if (!string.IsNullOrEmpty(normalizedQuery) && !string.IsNullOrEmpty(targetNormalized) && 
                (targetNormalized.Contains(normalizedQuery) || normalizedQuery.Contains(targetNormalized))) return 0.6;
            return 0.0;
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

            // Türkçe, Almanca ve diğer yaygın aksanlı karakterleri ASCII karşılıklarına dönüştür
            name = name.Replace("ı", "i")
                       .Replace("ğ", "g")
                       .Replace("ü", "u")
                       .Replace("ş", "s")
                       .Replace("ö", "o")
                       .Replace("ç", "c")
                       .Replace("ä", "a")
                       .Replace("ß", "ss")
                       .Replace("é", "e")
                       .Replace("è", "e")
                       .Replace("ê", "e")
                       .Replace("à", "a")
                       .Replace("â", "a")
                       .Replace("ô", "o")
                       .Replace("û", "u")
                       .Replace("î", "i")
                       .Replace("ï", "i");

            // Kalite belirteçlerini (hd, sd, fhd, uhd, hq, vb.) kelime sınırlarında temizle
            name = System.Text.RegularExpressions.Regex.Replace(name, @"\b(hd|sd|fhd|uhd|hq)\b", "");
            // Eğer kelime sonuna yapışık hd/sd varsa (örn: "ActionHD") onları da temizleyelim
            name = System.Text.RegularExpressions.Regex.Replace(name, @"(hd|sd|fhd|uhd|hq)$", "");

            // Sadece a-z ve 0-9 arası karakterleri tut, diğer tüm karakterleri (boşluklar dahil) temizle
            name = System.Text.RegularExpressions.Regex.Replace(name, @"[^a-z0-9]", "");
            return name;
        }
    }
}
