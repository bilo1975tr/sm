using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StreamMesh.Services;

namespace StreamMesh.Services.P2P
{
    public class P2pNode
    {
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public DateTime LastSeen { get; set; }
        public string Version { get; set; }

        [JsonIgnore]
        public string Status { get; set; } = "Bekleniyor";

        [JsonIgnore]
        public int ReceivedChannels { get; set; }

        [JsonIgnore]
        public string StatusColor 
        { 
            get 
            {
                if (Status.Contains("Hata")) return "#ef4444";
                if (Status.Contains("Bekleniyor") || Status.Contains("Eşitleniyor")) return "#facc15";
                return "#10b981";
            }
        }
    }

    public static class P2pNodeManager
    {
        private static readonly string NodeFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nodes.dat");
        private static ConcurrentDictionary<string, P2pNode> _nodes = new ConcurrentDictionary<string, P2pNode>();
        // Demo/public firebase for seed node fallback if needed. In production replace with real.
        // It saves { IpAddress: { Port: 12555, LastSeen: "date" } }
        private static readonly string FirebaseUrl = "https://streammesh-p2p-default-rtdb.europe-west1.firebasedatabase.app/aktif_dugumler.json";
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public static void LoadNodes()
        {
            if (File.Exists(NodeFilePath))
            {
                try
                {
                    byte[] compressed = File.ReadAllBytes(NodeFilePath);
                    string json = CompressionService.Decompress(compressed);
                    var list = JsonConvert.DeserializeObject<List<P2pNode>>(json);
                    
                    DateTime oneWeekAgo = DateTime.UtcNow.AddDays(-7);
                    
                    foreach (var node in list)
                    {
                        if (node.LastSeen >= oneWeekAgo)
                        {
                            string key = $"{node.IpAddress}:{node.Port}";
                            _nodes.AddOrUpdate(key, node, (k, existing) => {
                                // Sadece kalıcı verileri güncelle, Status gibi çalışma zamanı verilerini koru
                                existing.LastSeen = node.LastSeen;
                                existing.Version = node.Version;
                                return existing;
                            });
                        }
                    }
                    LogService.Log($"[{_nodes.Count}] aktif yerel P2P node arşivden yüklendi.");
                }
                catch (Exception ex)
                {
                    LogService.Log($"Node okuma hatası: {ex.Message}");
                }
            }
        }

        public static async Task PerformFirebaseFallbackAsync()
        {
            if (_nodes.IsEmpty)
            {
                LogService.Log("Arşivde P2P düğümü (node) bulunamadı. Hibrit ağ: Firebase'den tohum (seed) düğümler çekiliyor...");
                try
                {
                    var response = await _httpClient.GetAsync(FirebaseUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        if (!string.IsNullOrWhiteSpace(json) && json != "null")
                        {
                            var dict = JsonConvert.DeserializeObject<Dictionary<string, P2pNode>>(json);
                            if (dict != null)
                            {
                                foreach (var kvp in dict)
                                {
                                    // Sadece son 7 günde aktif olanları kabul et
                                    if (kvp.Value.LastSeen >= DateTime.UtcNow.AddDays(-7))
                                    {
                                        AddOrUpdateNode(kvp.Value.IpAddress, kvp.Value.Port);
                                    }
                                }
                                LogService.Log($"Firebase'den {_nodes.Count} aktif düğüm çekildi ve arşive eklendi.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.Log($"Firebase erişim hatası: {ex.Message}");
                }
            }
        }

        public static void SaveNodes()
        {
            try
            {
                var list = _nodes.Values.ToList();
                string json = JsonConvert.SerializeObject(list);
                byte[] compressed = CompressionService.Compress(json);
                File.WriteAllBytes(NodeFilePath, compressed);
            }
            catch (Exception ex)
            {
                LogService.Log($"Node kaydetme hatası: {ex.Message}");
            }
        }

        public static void AddOrUpdateNode(string ip, int port)
        {
            ip = NormalizeIp(ip);
            string key = $"{ip}:{port}";
            _nodes.AddOrUpdate(key, 
                new P2pNode { IpAddress = ip, Port = port, LastSeen = DateTime.UtcNow },
                (k, existing) => { existing.LastSeen = DateTime.UtcNow; existing.Port = port; return existing; });
            
            SaveNodes();
        }

        public static string NormalizeIp(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return ip;
            if (ip.Contains("::ffff:")) return ip.Replace("::ffff:", "");
            if (ip == "::1") return "127.0.0.1";
            return ip;
        }

        public static P2pNode GetNode(string ip, int port)
        {
            ip = NormalizeIp(ip);
            string key = $"{ip}:{port}";
            if (_nodes.TryGetValue(key, out var node)) return node;
            return null;
        }

        public static async Task ReportToFirebaseAsync(string ip, int port)
        {
            try
            {
                // Dış IP'mizi bilmediğimiz için bu sadece router vb. tespiti yapılabildiyse veya STUN varsa çalışır.
                // Basitlik adına node bilgilerini Firebase'e atıyoruz
                var node = new P2pNode { IpAddress = ip, Port = port, LastSeen = DateTime.UtcNow };
                string safeIp = $"{ip}_{port}".Replace(".", "_").Replace(":", "_"); // Firebase key'de . olmaz
                string json = JsonConvert.SerializeObject(node);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                string url = FirebaseUrl.Replace(".json", $"/{safeIp}.json");
                var response = await _httpClient.PutAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    LogService.Log("P2P Düğüm bilgimiz Firebase'e (Hibrit Yedek) kaydedildi.");
                }
            }
            catch { }
        }

        public static List<P2pNode> GetActiveNodes()
        {
            // Sadece güncel nodeları gönder (örnek son 7 gün)
            return _nodes.Values.Where(n => n.LastSeen >= DateTime.UtcNow.AddDays(-7)).Take(50).ToList();
        }
    }
}
