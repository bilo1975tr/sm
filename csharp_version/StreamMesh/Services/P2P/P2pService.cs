using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Open.Nat;
using StreamMesh.Services;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace StreamMesh.Services.P2P
{
    public class P2pMessage
    {
        public string Type { get; set; } // "HELLO", "NODELIST", "DATA"
        public string Payload { get; set; } 
        // Payload could be encrypted channel data or nodes
    }

    public static class P2pService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private const int DefaultPort = 12555;
        private static TcpListener _listener;
        private static bool _isRunning = false;
        private static int _currentTcpPort = DefaultPort;
        private static string _localAppVersion = "v0.0.0";

        public static bool IsRunning => _isRunning;

        public static async Task StartAsync()
        {
            try
            {
                string versionFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VERSION");
                if (!System.IO.File.Exists(versionFile))
                    versionFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../VERSION");
                
                if (System.IO.File.Exists(versionFile))
                    _localAppVersion = System.IO.File.ReadAllText(versionFile).Trim();
            }
            catch { }

            try
            {
                // UPnP Port Transfer
                var discoverer = new NatDiscoverer();
                var device = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp, new CancellationTokenSource(10000));
                await device.CreatePortMapAsync(new Mapping(Protocol.Tcp, DefaultPort, DefaultPort, "StreamMesh P2P"));
                LogService.Log("UPnP Port Yönlendirme Başarılı: " + DefaultPort);
            }
            catch(Exception ex)
            {
                LogService.Log("UPnP Yönlendirme hatası (Modem desteklemiyor olabilir): " + ex.Message);
            }

            int currentPort = DefaultPort;
            bool started = false;
            int maxAttempts = 10;
            
            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    _listener = new TcpListener(IPAddress.IPv6Any, currentPort);
                    _listener.Server.DualMode = true;
                    _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    _listener.Start();
                    started = true;
                    break;
                }
                catch
                {
                    currentPort++;
                }
            }

            if (!started)
            {
                LogService.Log("Kritik Hata: P2P için uygun TCP portu bulunamadı!");
                return;
            }

            _isRunning = true;
            _currentTcpPort = currentPort;
            LogService.Log($"P2P TCP Dinleyicisi {currentPort} portunda başladı.");

            // Kendi dinlediğimiz portu UdpDiscoveryService'e söyleyelim ki etrafa duyursun
            UdpDiscoveryService.SetTcpPort(currentPort);
            UdpDiscoveryService.Start();

            _ = Task.Run(ListenLoop);
            
            // Connect to known nodes
            _ = Task.Run(ConnectToKnownNodesAsync);
        }

        private static async Task ListenLoop()
        {
            while (_isRunning)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClientAsync(client));
                }
                catch { }
            }
        }

        private static async Task SendMessageAsync(NetworkStream stream, P2pMessage msg)
        {
            try
            {
                string json = JsonConvert.SerializeObject(msg);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                byte[] lengthPrefix = BitConverter.GetBytes(bytes.Length);
                await stream.WriteAsync(lengthPrefix, 0, 4);
                await stream.WriteAsync(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                LogService.Log("P2P Veri Gönderim Hatası: " + ex.Message);
            }
        }

        private static async Task<P2pMessage> ReceiveMessageAsync(NetworkStream stream)
        {
            try
            {
                byte[] lengthBuffer = new byte[4];
                int read = await stream.ReadAsync(lengthBuffer, 0, 4);
                if (read < 4) return null;
                int length = BitConverter.ToInt32(lengthBuffer, 0);
                if (length <= 0 || length > 100 * 1024 * 1024) return null; // limit 100MB

                byte[] buffer = new byte[length];
                int totalRead = 0;
                while (totalRead < length)
                {
                    int r = await stream.ReadAsync(buffer, totalRead, length - totalRead);
                    if (r == 0) break;
                    totalRead += r;
                }

                if (totalRead == length)
                {
                    string json = Encoding.UTF8.GetString(buffer);
                    return JsonConvert.DeserializeObject<P2pMessage>(json);
                }
            }
            catch (Exception ex)
            {
                LogService.Log("P2P Veri Alım Hatası: " + ex.Message);
            }
            return null;
        }

        private static async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var ep = client.Client.RemoteEndPoint as IPEndPoint;
                string remoteIp = P2pNodeManager.NormalizeIp(ep?.Address.ToString());

                bool firstResponse = true;
                while (true)
                {
                    var msg = await ReceiveMessageAsync(stream);
                    if (msg == null) break;

                    if (msg.Type == "HELLO")
                    {
                        if (!string.IsNullOrWhiteSpace(msg.Payload))
                        {
                            ProcessNodesAndChannels(msg.Payload, remoteIp);
                        }

                        _ = Task.Run(async () => {
                            try
                            {
                                var nodes = P2pNodeManager.GetActiveNodes();
                                var db = new DatabaseService();
                                int offset = 0;
                                int limit = 5000;
                                bool done = false;

                                while (!done)
                                {
                                    var chunk = db.GetVerifiedChannelsChunk(offset, limit);
                                    if (chunk.Count < limit) done = true;

                                    var responseMsg = new {
                                        Version = _localAppVersion,
                                        MyPort = _currentTcpPort,
                                        Nodes = firstResponse ? nodes : new List<P2pNode>(),
                                        Channels = chunk
                                    };
                                    
                                    var replyMsg = new P2pMessage { Type = "NODES_AND_CHANNELS", Payload = JsonConvert.SerializeObject(responseMsg) };
                                    await SendMessageAsync(stream, replyMsg);
                                    
                                    firstResponse = false;
                                    offset += limit;
                                    if (chunk.Count == 0 && done) break; 
                                }
                                
                                LogService.Log($"P2P Veri Başarıyla Gönderildi: {remoteIp}");
                            }
                            catch (Exception ex)
                            {
                                LogService.Log($"P2P Veri Gönderim Task Hatası: {ex.Message}");
                            }
                        });
                    }
                    else if (msg.Type == "NODES_AND_CHANNELS")
                    {
                        ProcessNodesAndChannels(msg.Payload, remoteIp);
                    }
                }
            }
        }

        private static async Task ConnectToKnownNodesAsync()
        {
            while (_isRunning)
            {
                P2pNodeManager.LoadNodes();
                await P2pNodeManager.PerformFirebaseFallbackAsync();
                
                string myIp = null;
                int extPort = _currentTcpPort;
                try
                {
                    var endpoint = await StunService.GetExternalEndpointAsync(_currentTcpPort);
                    if (endpoint != null)
                    {
                        myIp = endpoint.Address.ToString();
                        extPort = endpoint.Port;
                        LogService.Log($"STUN ile Dış IP bulundu: {myIp}:{extPort}");
                    }
                    else
                    {
                        myIp = (await _httpClient.GetStringAsync("https://api.ipify.org"))?.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(myIp))
                    {
                        await P2pNodeManager.ReportToFirebaseAsync(myIp, extPort);
                    }
                }
                catch { }

                var nodes = P2pNodeManager.GetActiveNodes().Where(n => !string.IsNullOrEmpty(n.IpAddress)).ToList();
                var tasks = nodes.Select(async node =>
                {
                    if (!string.IsNullOrEmpty(myIp) && node.IpAddress == myIp) 
                        return; // Aynı modeme bağlıyız (Hairpin NAT sorunu). Kendi dış IP'mize bağlanmaya çalışmayalım, yerel UDP Discovery zaten bulacaktır.


                    try
                    {
                        if (!IPAddress.TryParse(node.IpAddress, out IPAddress parsedIp)) return;

                        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                        using (var client = new TcpClient(parsedIp.AddressFamily))
                        {
                            node.Status = "Eşitleniyor";
                            node.ReceivedChannels = 0; // Reset for new sync session
                            await client.ConnectAsync(node.IpAddress, node.Port, cts.Token).AsTask();
                            using (var stream = client.GetStream())
                            {
                                var sendTask = Task.Run(async () => {
                                    try
                                    {
                                        var db = new DatabaseService();
                                        int offset = 0;
                                        int limit = 5000;
                                        bool done = false;

                                        while (!done)
                                        {
                                            var chunk = db.GetVerifiedChannelsChunk(offset, limit);
                                            if (chunk.Count < limit) done = true;

                                            var payloadData = new {
                                                Version = _localAppVersion,
                                                MyPort = _currentTcpPort,
                                                Nodes = offset == 0 ? nodes : new List<P2pNode>(),
                                                Channels = chunk
                                            };
                                            
                                            var msgType = (offset == 0) ? "HELLO" : "NODES_AND_CHANNELS";
                                            var hello = new P2pMessage { Type = msgType, Payload = JsonConvert.SerializeObject(payloadData) };
                                            await SendMessageAsync(stream, hello);

                                            offset += limit;
                                            if (chunk.Count == 0 && done) break;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        LogService.Log($"P2P İstemci Gönderim Task Hatası: {ex.Message}");
                                    }
                                });

                                var receiveTask = Task.Run(async () => {
                                    try
                                    {
                                        while (true)
                                        {
                                            var reply = await ReceiveMessageAsync(stream);
                                            if (reply == null) break;
                                            
                                            if (reply.Type == "NODES_AND_CHANNELS")
                                            {
                                                ProcessNodesAndChannels(reply.Payload, node.IpAddress);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        LogService.Log($"P2P İstemci Alım Task Hatası: {ex.Message}");
                                    }
                                });

                                await Task.WhenAll(sendTask, receiveTask);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex is OperationCanceledException || ex.InnerException is OperationCanceledException)
                            node.Status = "Hata (Zaman Aşımı)";
                        else if (ex.Message.Contains("refused"))
                            node.Status = "Hata (Reddedildi)";
                        else
                            node.Status = "Hata (Bağlantı Yok)";

                        LogService.Log($"P2P Node {node.IpAddress}:{node.Port} bağlantı hatası: {ex.Message}");
                    }
                });

                await Task.WhenAll(tasks);
                
                await Task.Delay(30000); // 30 seconds
            }
        }

        private static void ProcessNodesAndChannels(string payload, string peerIp = null)
        {
            try
            {
                peerIp = P2pNodeManager.NormalizeIp(peerIp);
                var data = JsonConvert.DeserializeAnonymousType(payload, new { 
                    Version = "",
                    MyPort = 0,
                    Nodes = new List<P2pNode>(), 
                    Channels = new List<StreamMesh.Models.Channel>() 
                });

                if (data == null) return;

                if (!string.IsNullOrEmpty(peerIp))
                {
                    // Try to find the node accurately
                    var senderNode = P2pNodeManager.GetNode(peerIp, data.MyPort);
                    
                    // Fallback: If port changed or we only have IP, find by IP
                    if (senderNode == null && data.MyPort > 0)
                    {
                        P2pNodeManager.AddOrUpdateNode(peerIp, data.MyPort);
                        senderNode = P2pNodeManager.GetNode(peerIp, data.MyPort);
                    }

                    if (senderNode != null)
                    {
                        senderNode.Version = (string.IsNullOrEmpty(data.Version) || data.Version == "v0.0.0") ? senderNode.Version : data.Version;
                        senderNode.Status = "Eşitlendi";
                        senderNode.LastSeen = DateTime.UtcNow;
                        if (data.Channels != null)
                            senderNode.ReceivedChannels += data.Channels.Count;
                    }
                }

                if (data.Nodes != null)
                {
                    foreach (var rn in data.Nodes) P2pNodeManager.AddOrUpdateNode(rn.IpAddress, rn.Port);
                }

                if (data.Channels != null && data.Channels.Count > 0)
                {
                    var db = new DatabaseService();
                    
                    // Count before
                    int beforeCount = db.GetTotalChannelCount();
                    
                    db.SyncIncomingP2PChannels(data.Channels);

                    // Count after
                    int afterCount = db.GetTotalChannelCount();
                    int newCount = afterCount - beforeCount;

                    LogService.Log($"P2P Eşitleme: Gelen: {data.Channels.Count}, Yeni Eklenen: {newCount}");
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"P2P Veri İşleme Hatası: {ex.Message}");
            }
        }

        public static void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
        }
    }
}
