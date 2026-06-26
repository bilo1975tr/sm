using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.NetworkInformation;
using Newtonsoft.Json;
using StreamMesh.Models;

namespace StreamMesh.Services
{
    public class FirebaseQueueService
    {
        private static readonly string FirebasePoolUrl = AppConfig.GetFirebasePoolUrl();
        private static readonly Lazy<FirebaseQueueService> _instance = new Lazy<FirebaseQueueService>(() => new FirebaseQueueService());
        public static FirebaseQueueService Instance => _instance.Value;

        private readonly DatabaseService _db;
        private readonly HttpClient _client;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts;
        private Task _processTask = null;

        private FirebaseQueueService()
        {
            _db = new DatabaseService();
            _client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            
            try
            {
                NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
            }
            catch (Exception ex)
            {
                LogService.LogError("NetworkChange event subscription failed", ex);
            }
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            _processTask = Task.Run(() => ProcessQueueLoopAsync(_cts.Token));
            LogService.Log("Firebase Kalıcı Kuyruk Servisi başlatıldı.");
            
            // Başlangıçta hemen bekleyen kayıtları göndermeyi dene
            TriggerSync();
        }

        public void Stop()
        {
            if (_cts == null) return;
            try
            {
                _cts.Cancel();
                // UI thread'ini bloklamamak için senkron .Wait(2000) kaldırılmıştır. 
                // İptal sinyali alan arka plan döngüsü kendiliğinden güvenle duracaktır.
            }
            catch (Exception ex)
            {
                LogService.LogError("FirebaseQueueService stop error", ex);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                _processTask = null;
            }
            LogService.Log("Firebase Kalıcı Kuyruk Servisi durduruldu.");
        }

        private void OnNetworkAddressChanged(object sender, EventArgs e)
        {
            LogService.Log("Ağ bağlantısı değişti, bekleyen Firebase gönderimleri tetikleniyor...");
            TriggerSync();
        }

        public void TriggerSync()
        {
            _ = Task.Run(async () =>
            {
                await _semaphore.WaitAsync();
                try
                {
                    await ProcessQueueOnceAsync();
                }
                catch (Exception ex)
                {
                    LogService.LogError("TriggerSync failed", ex);
                }
                finally
                {
                    _semaphore.Release();
                }
            });
        }

        public async Task EnqueueChannelsAsync(List<Channel> channels)
        {
            if (channels == null || channels.Count == 0) return;

            await Task.Run(() =>
            {
                foreach (var channel in channels)
                {
                    if (string.IsNullOrEmpty(channel.Url)) continue;
                    if (channel.IsPremium) continue;

                    channel.Language = Channel.NormalizeLanguage(channel.Language);
                    string safeKey = CreateSafeFirebaseKey(channel.Url);

                    var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
                    string json = JsonConvert.SerializeObject(channel, Formatting.None, settings);

                    _db.AddPendingFirebasePush(safeKey, json);
                }
            });

            TriggerSync();
        }

        private async Task ProcessQueueLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 5 dakikalık periyotlarla kontrol et
                    await Task.Delay(TimeSpan.FromMinutes(5), token);
                    
                    await _semaphore.WaitAsync(token);
                    try
                    {
                        await ProcessQueueOnceAsync();
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogService.LogError("ProcessQueueLoopAsync exception", ex);
                }
            }
        }

        private async Task ProcessQueueOnceAsync()
        {
            int pendingCount = _db.GetPendingFirebasePushesCount();
            if (pendingCount == 0) return;

            int batchSize = AppConfig.FirebaseBatchSize;
            const int maxRetries = 10;

            // Backoff'u geçmiş adayları seçebilmek için batchSize'ın 3 katı kadar kayıt çekip süzgeçten geçiyoruz
            var candidatePushes = _db.GetPendingFirebasePushes(batchSize * 3);
            if (candidatePushes == null || candidatePushes.Count == 0) return;

            var pendingPushes = new List<DatabaseService.PendingFirebasePush>();
            foreach (var push in candidatePushes)
            {
                if (pendingPushes.Count >= batchSize) break;

                // Gerçek Exponential Backoff Hesabı (30s * 2^RetryCount)
                double backoffSeconds = Math.Pow(2, push.RetryCount) * 30;
                if (push.RetryCount > 0 && DateTime.TryParse(push.LastAttemptAt, out DateTime lastAttempt))
                {
                    if (DateTime.UtcNow < lastAttempt.AddSeconds(backoffSeconds))
                    {
                        // Henüz bekleme süresi dolmamış, pas geçiliyor
                        continue;
                    }
                }

                pendingPushes.Add(push);
            }

            if (pendingPushes.Count == 0) return;

            LogService.Log($"Firebase kuyruğunda {pendingCount} bekleyen var. Bu dalgada {pendingPushes.Count} kayıt işlenmeye uygun.");

            var patchData = new Dictionary<string, object>();
            var idsToProcess = new List<int>();

            foreach (var push in pendingPushes)
            {
                try
                {
                    var ch = JsonConvert.DeserializeObject<Channel>(push.JsonPayload);
                    patchData[push.SafeKey] = ch;
                    idsToProcess.Add(push.Id);
                }
                catch (Exception ex)
                {
                    LogService.LogError($"Kuyrukta bozuk JSON payload tespit edildi, ID: {push.Id} 'failed' olarak işaretleniyor", ex);
                    _db.UpdatePendingFirebasePushStatus(push.Id, "failed");
                }
            }

            if (patchData.Count == 0) return;

            bool success = false;
            try
            {
                var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
                var json = JsonConvert.SerializeObject(patchData, Formatting.None, settings);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(new HttpMethod("PATCH"), FirebasePoolUrl)
                {
                    Content = content
                };

                var response = await _client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    success = true;
                }
                else
                {
                    LogService.Log($"Firebase kuyruğu PATCH gönderimi başarısız oldu: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("Firebase kuyruk gönderiminde bağlantı/ağ hatası", ex);
            }

            if (success)
            {
                _db.DeletePendingFirebasePushes(idsToProcess);
                GitHubSyncService.IncrementTotalChannelsPushed(idsToProcess.Count);
                LogService.Log($"Firebase kuyruğundaki {idsToProcess.Count} kayıt başarıyla gönderildi ve temizlendi.");
            }
            else
            {
                foreach (var push in pendingPushes)
                {
                    int nextRetry = push.RetryCount + 1;
                    if (nextRetry >= maxRetries)
                    {
                        LogService.Log($"Kayıt ID: {push.Id} ({push.SafeKey}) maksimum deneme sayısını ({maxRetries}) aştı. Kayıt 'failed' olarak işaretleniyor (Veri kaybı önlendi).");
                        _db.UpdatePendingFirebasePushStatus(push.Id, "failed");
                    }
                    else
                    {
                        _db.UpdatePendingFirebasePushAttempt(push.Id, nextRetry);
                    }
                }
            }
        }

        private string CreateSafeFirebaseKey(string input)
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
