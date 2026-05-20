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
        private static readonly string GitHubRawUrl = "https://raw.githubusercontent.com/bilo1975tr/sm/main/channels.json";
        
        // Yazma Havuzu (Yeni Kanallar) - Firebase
        private static readonly string FirebasePoolUrl = "https://streammesh-p2p-default-rtdb.europe-west1.firebasedatabase.app/new_channels.json";
        
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

        /// <summary>
        /// Milyonlarca uygulamanın kotasız şekilde güncel listeyi çektiği metot
        /// </summary>
        public static async Task PullFromGitHubAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(15);

                var response = await client.GetAsync(GitHubRawUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var remoteChannels = JsonConvert.DeserializeObject<List<Channel>>(json) ?? new List<Channel>();

                    if (remoteChannels.Count > 0)
                    {
                        LastPulledGitHubChannelCount = remoteChannels.Count;
                        LastGitHubPullTime = DateTime.Now;
                        var db = new DatabaseService();
                        db.SyncIncomingP2PChannels(remoteChannels);
                        LogService.Log($"GitHub'dan {remoteChannels.Count} kanal çekildi ve yerel ile eşitlendi.");
                    }
                }
                else
                {
                    LogService.Log($"GitHub'da henüz channels.json yok veya ulaşılamıyor (Status: {response.StatusCode}).");
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("GitHub'dan veri çekilirken hata", ex);
            }
        }

        /// <summary>
        /// Sadece uygulamada YENİ bir kanal bulunduğunda veya doğrulandığında GitHub'a değil Firebase havuzuna yollar.
        /// </summary>
        public static async Task PushNewChannelsToFirebasePoolAsync(List<Channel> newChannels)
        {
            if (newChannels == null || newChannels.Count == 0) return;

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                var patchData = new Dictionary<string, Channel>();
                
                foreach (var channel in newChannels)
                {
                    if (string.IsNullOrEmpty(channel.Url)) continue;
                    
                    // URL'den Firebase için güvenli ve eşsiz (MD5) bir ID (Key) oluşturuyoruz
                    // Bu sayede aynı adrese sahip kanal milyonlarca kullanıcıdan gelse bile Firebase'de üst üste (tek kayıt) yazar, mükerrerliği engeller.
                    string safeKey = CreateSafeFirebaseKey(channel.Url);
                    patchData[safeKey] = channel;
                }
                
                if (patchData.Count == 0) return;

                var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
                var json = JsonConvert.SerializeObject(patchData, Formatting.None, settings);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Firebase'e yeni kanalları PATCH atıyoruz.
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), FirebasePoolUrl)
                {
                    Content = content
                };
                
                var response = await client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    TotalChannelsPushedToFirebase += patchData.Count;
                    LogService.Log($"Firebase havuzuna {patchData.Count} güncel kanal bildirildi (URL Hash ile mükerrerlik önlendi).");
                }
                else
                {
                    LogService.Log($"Firebase havuza gönderme başarısız: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("Firebase kanal havuzu gönderme hatası", ex);
            }
        }

        private static string CreateSafeFirebaseKey(string input)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(input ?? "");
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }
        }
    }
}
