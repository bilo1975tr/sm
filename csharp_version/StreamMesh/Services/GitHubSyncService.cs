using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StreamMesh.Models;

namespace StreamMesh.Services
{
    public class GitHubSyncService
    {
        // Okuma İşlemi (Sınırsız/Kotasız) - GitHub Raw CDN
        // Yazma Havuzu (Yeni Kanallar) - Firebase
        private static readonly string FirebasePoolUrl = AppConfig.GetFirebasePoolUrl();
        
        public static int TotalChannelsPushedToFirebase { get; private set; } = 0;
        public static int LastPulledGitHubChannelCount { get; private set; } = 0;
        public static DateTime LastGitHubPullTime { get; private set; } = DateTime.MinValue;
        
        private static bool _isRunning = false;

        public static void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            Task.Run(SyncLoop);
        }

        private static async Task SyncLoop()
        {
            LogService.Log("Bulut Senkronizasyon Servisi başlatıldı (Okuma: GitHub, Yazma: Firebase).");
            
            // İlk açılışta hemen çek
            await PullFromGitHubAsync();

            while (_isRunning)
            {
                await Task.Delay(TimeSpan.FromHours(1));
                
                try
                {
                    await PullFromGitHubAsync();
                }
                catch (Exception ex)
                {
                    LogService.LogError("Bulut senkronizasyon döngüsü hatası", ex);
                }
            }
        }

        public static string GetIsoLanguageCode(string langName)
        {
            if (string.IsNullOrWhiteSpace(langName)) return "bilinmiyor";

            string trimmed = langName.Trim();
            // Handle special/unknown cases explicitly first
            if (trimmed.Equals("Hiçbiri", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Bilinmiyor", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                return "bilinmiyor";
            }

            // Standardize/Normalize name to match our primary dictionary
            // E.g. "Türkçe (Türkiye)" -> "Türkçe"
            string cleanName = trimmed;
            int parenIdx = cleanName.IndexOf('(');
            if (parenIdx >= 0)
            {
                cleanName = cleanName.Substring(0, parenIdx).Trim();
            }

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Türkçe", "tr" },
                { "İngilizce", "en" },
                { "English", "en" },
                { "Almanca", "de" },
                { "Deutsch", "de" },
                { "Fransızca", "fr" },
                { "Français", "fr" },
                { "İspanyolca", "es" },
                { "Español", "es" },
                { "İtalyanca", "it" },
                { "Italiano", "it" },
                { "Rusça", "ru" },
                { "Pусский", "ru" },
                { "Arapça", "ar" },
                { "العربية", "ar" },
                { "Çince", "zh" },
                { "中文", "zh" }
            };

            if (dict.TryGetValue(cleanName, out string isoCode))
            {
                return isoCode.ToLowerInvariant();
            }

            if (dict.TryGetValue(trimmed, out string fullIsoCode))
            {
                return fullIsoCode.ToLowerInvariant();
            }

            // System.Globalization.CultureInfo check
            try
            {
                var cultures = System.Globalization.CultureInfo.GetCultures(System.Globalization.CultureTypes.AllCultures);
                foreach (var culture in cultures)
                {
                    if (culture.DisplayName.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                        culture.EnglishName.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                        culture.NativeName.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                        culture.DisplayName.StartsWith(cleanName, StringComparison.OrdinalIgnoreCase) ||
                        culture.EnglishName.StartsWith(cleanName, StringComparison.OrdinalIgnoreCase) ||
                        culture.NativeName.StartsWith(cleanName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(culture.TwoLetterISOLanguageName) && culture.TwoLetterISOLanguageName.Length == 2)
                        {
                            return culture.TwoLetterISOLanguageName.ToLowerInvariant();
                        }
                    }
                }
            }
            catch
            {
                // Ignore culture resolution errors
            }

            return "bilinmiyor";
        }

        private static string GetLegacyLanguageFilename(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return "bilinmiyor";
            string normalized = Channel.NormalizeLanguage(lang);
            var s = normalized.ToLower(new System.Globalization.CultureInfo("tr-TR"));
            s = s.Replace("(", "").Replace(")", "");
            var sb = new System.Text.StringBuilder();
            foreach (var c in s)
            {
                if (char.IsLetterOrDigit(c) || c == 'ı' || c == 'ğ' || c == 'ü' || c == 'ş' || c == 'ö' || c == 'ç' || c == ' ' || c == '-')
                    sb.Append(c);
            }
            string result = sb.ToString().Replace("  ", " ").Trim().Replace(" ", "_");
            return string.IsNullOrEmpty(result) ? "bilinmiyor" : result;
        }

        private static string NormalizeLanguageFilename(string lang)
        {
            return GetIsoLanguageCode(lang);
        }

        /// <summary>
        /// Milyonlarca uygulamanın kotasız şekilde güncel listeyi çektiği metot
        /// </summary>
        public static async Task PullFromGitHubAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(15);

                var profile = StreamMesh.Services.P2P.UserService.GetProfile();
                var langs = profile?.Languages;
                if (langs == null || langs.Count == 0)
                {
                    langs = new List<string> { "Türkçe (Türkiye)" };
                }

                var allRemoteChannels = new List<Channel>();

                foreach (var originalLang in langs)
                {
                    if (string.IsNullOrEmpty(originalLang) || originalLang == "Hiçbiri") continue;
                    
                    // 1. Try ISO (e.g., tr)
                    string safeLang = NormalizeLanguageFilename(originalLang);
                    string targetUrl = AppConfig.GetGitHubLanguageUrl(safeLang);

                    LogService.Log($"GitHub'dan kanal verisi çekiliyor (ISO: {safeLang})...");
                    var response = await client.GetAsync(targetUrl);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var remoteChannels = JsonConvert.DeserializeObject<List<Channel>>(json) ?? new List<Channel>();
                        allRemoteChannels.AddRange(remoteChannels);
                        continue;
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        // 2. Try Legacy Name (e.g., türkçe)
                        string legacyLang = GetLegacyLanguageFilename(originalLang);
                        if (legacyLang != safeLang)
                        {
                            string legacyUrl = AppConfig.GetGitHubLanguageUrl(legacyLang);
                            LogService.Log($"ISO bulunamadı, Legacy deneniyor: {legacyLang}...");
                            var legacyResponse = await client.GetAsync(legacyUrl);
                            if (legacyResponse.IsSuccessStatusCode)
                            {
                                var json = await legacyResponse.Content.ReadAsStringAsync();
                                var remoteChannels = JsonConvert.DeserializeObject<List<Channel>>(json) ?? new List<Channel>();
                                allRemoteChannels.AddRange(remoteChannels);
                                continue;
                            }
                        }
                    }

                    // 3. Try channels_bilinmiyor.json if neither succeeded
                    string unknownUrl = AppConfig.GetGitHubLanguageUrl("bilinmiyor");
                    LogService.Log($"Yayın dili yüklenemedi, 'bilinmiyor' deneniyor...");
                    var unknownResponse = await client.GetAsync(unknownUrl);
                    if (unknownResponse.IsSuccessStatusCode)
                    {
                        var json = await unknownResponse.Content.ReadAsStringAsync();
                        var remoteChannels = JsonConvert.DeserializeObject<List<Channel>>(json) ?? new List<Channel>();
                        allRemoteChannels.AddRange(remoteChannels);
                    }
                    else
                    {
                        LogService.Log($"GitHub'da '{safeLang}', legacy '{GetLegacyLanguageFilename(originalLang)}' ve 'bilinmiyor' için channels JSON bulunamadı.");
                    }
                }

                if (allRemoteChannels.Count > 0)
                {
                    LastPulledGitHubChannelCount = allRemoteChannels.Count;
                    LastGitHubPullTime = DateTime.Now;
                    var db = new DatabaseService();
                    db.SyncIncomingP2PChannels(allRemoteChannels);
                    LogService.Log($"GitHub'dan toplam {allRemoteChannels.Count} dil bazlı kanal çekildi ve yerel ile eşitlendi.");
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("GitHub'dan veri çekilirken hata", ex);
            }
        }

        public static void IncrementTotalChannelsPushed(int count)
        {
            TotalChannelsPushedToFirebase += count;
        }

        /// <summary>
        /// Sadece uygulamada YENİ bir kanal bulunduğunda veya doğrulandığında GitHub'a değil Firebase havuzuna kalıcı kuyruk üzerinden yollar.
        /// </summary>
        public static async Task PushNewChannelsToFirebasePoolAsync(List<Channel> newChannels)
        {
            if (newChannels == null || newChannels.Count == 0) return;

            try
            {
                await FirebaseQueueService.Instance.EnqueueChannelsAsync(newChannels);
            }
            catch (Exception ex)
            {
                LogService.LogError("Firebase kanal havuzu kuyruğa ekleme hatası", ex);
            }
        }
    }
}
