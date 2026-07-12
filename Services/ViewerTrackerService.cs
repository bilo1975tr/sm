using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StreamMesh.Models;
using StreamMesh.Services.Auth;

namespace StreamMesh.Services
{
    public class ViewerTrackerService
    {
        private static readonly string FirebaseUrl = AppConfig.FirebaseDatabaseUrl;
        private static readonly Lazy<ViewerTrackerService> _instance = new Lazy<ViewerTrackerService>(() => new ViewerTrackerService());
        public static ViewerTrackerService Instance => _instance.Value;

        private readonly HttpClient _client;
        private string _activeChannelId = null;
        private CancellationTokenSource _cts;
        private string _userId;

        private ViewerTrackerService()
        {
            _client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _userId = GetOrCreateUserId();
        }

        private string GetOrCreateUserId()
        {
            try
            {
                string email = UserService.CurrentUser?.Email ?? Environment.MachineName;
                using (var md5 = MD5.Create())
                {
                    byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(email.ToLowerInvariant()));
                    var builder = new StringBuilder();
                    foreach (var b in bytes) builder.Append(b.ToString("x2"));
                    return builder.ToString();
                }
            }
            catch
            {
                return Guid.NewGuid().ToString("N");
            }
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            Task.Run(() => HeartbeatLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            if (_cts == null) return;
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        public void SetActiveChannel(string channelId)
        {
            string oldId = _activeChannelId;
            if (oldId == channelId) return;

            _activeChannelId = channelId;

            if (!string.IsNullOrEmpty(oldId))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        string url = $"{FirebaseUrl}/viewers/{oldId}/{_userId}.json";
                        await _client.DeleteAsync(url);
                    }
                    catch {}
                });
            }

            if (!string.IsNullOrEmpty(channelId))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        long epochSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        string url = $"{FirebaseUrl}/viewers/{channelId}/{_userId}.json";
                        var content = new StringContent(epochSeconds.ToString(), Encoding.UTF8, "application/json");
                        await _client.PutAsync(url, content);
                    }
                    catch {}
                });
            }
        }

        public void ClearActiveChannel()
        {
            string oldId = _activeChannelId;
            _activeChannelId = null;
            if (!string.IsNullOrEmpty(oldId))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        string url = $"{FirebaseUrl}/viewers/{oldId}/{_userId}.json";
                        await _client.DeleteAsync(url);
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError("ClearActiveChannel failed to delete viewer node", ex);
                    }
                });
            }
        }

        private async Task HeartbeatLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    string activeId = _activeChannelId;
                    if (!string.IsNullOrEmpty(activeId))
                    {
                        long epochSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        string url = $"{FirebaseUrl}/viewers/{activeId}/{_userId}.json";
                        var content = new StringContent(epochSeconds.ToString(), Encoding.UTF8, "application/json");
                        await _client.PutAsync(url, content, token);
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("ViewerTracker heartbeat error", ex);
                }

                try
                {
                    await Task.Delay(30000, token); // 30 seconds
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        public async Task<Dictionary<string, int>> FetchViewerCountsAsync()
        {
            var counts = new Dictionary<string, int>();
            try
            {
                string url = $"{FirebaseUrl}/viewers.json";
                var response = await _client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    if (json != "null" && !string.IsNullOrWhiteSpace(json))
                    {
                        var viewersData = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, long>>>(json);
                        if (viewersData != null)
                        {
                            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            foreach (var kp in viewersData)
                            {
                                string channelId = kp.Key;
                                var channelViewers = kp.Value;
                                int activeCount = 0;
                                foreach (var vKp in channelViewers)
                                {
                                    long lastHeartbeat = vKp.Value;
                                    
                                    // Milisaniye cinsinden (13 haneli) kaydedilmişse saniyeye dönüştür
                                    if (lastHeartbeat > 9999999999)
                                    {
                                        lastHeartbeat /= 1000;
                                    }

                                    long diff = now - lastHeartbeat;
                                    // Sadece son 90 saniye içinde güncellenmiş ve geçerli (gelecekte olmayan) olanları say
                                    if (diff >= 0 && diff <= 90)
                                    {
                                        activeCount++;
                                    }
                                }
                                if (activeCount > 0)
                                {
                                    counts[channelId] = activeCount;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("FetchViewerCountsAsync failed", ex);
            }
            return counts;
        }
    }
}
