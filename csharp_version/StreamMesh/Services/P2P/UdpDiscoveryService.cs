using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using StreamMesh.Services;

namespace StreamMesh.Services.P2P
{
    public static class UdpDiscoveryService
    {
        private const int DiscoveryPort = 12556;
        private static UdpClient _udpClient;
        private static bool _isRunning;

        private static int _tcpPort = 12555;

        public static void SetTcpPort(int port)
        {
            _tcpPort = port;
        }

        public static void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            try
            {
                var ep = new IPEndPoint(IPAddress.Any, DiscoveryPort);
                _udpClient = new UdpClient(AddressFamily.InterNetwork);
                _udpClient.ExclusiveAddressUse = false;
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(ep);
            }
            catch (Exception ex)
            {
                LogService.Log("UDP Discovery başlatılamadı: " + ex.Message);
            }
            
            _ = Task.Run(ListenLoop);
            _ = Task.Run(BroadcastLoop);
        }

        private static bool IsLocalIpAddress(string ip)
        {
            try
            {
                var hostIPs = Dns.GetHostAddresses(Dns.GetHostName());
                var remoteIp = IPAddress.Parse(ip);
                if (IPAddress.IsLoopback(remoteIp)) return true;
                foreach (var hostIP in hostIPs)
                {
                    if (hostIP.Equals(remoteIp)) return true;
                }
            }
            catch { }
            return false;
        }

        private static async Task ListenLoop()
        {
            while (_isRunning && _udpClient != null)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    string msg = Encoding.UTF8.GetString(result.Buffer);
                    if (msg.StartsWith("STREAMMESH_HELLO:"))
                    {
                        var ip = result.RemoteEndPoint.Address.ToString();

                        var portStr = msg.Substring("STREAMMESH_HELLO:".Length);
                        if (int.TryParse(portStr, out int tcpPort))
                        {
                            // Kendi bilgisayarımızdan kendi portumuza seken paketleri atla
                            if (IsLocalIpAddress(ip) && tcpPort == _tcpPort) continue;

                            P2pNodeManager.AddOrUpdateNode(ip, tcpPort);
                        }
                    }
                }
                catch { }
            }
        }

        private static async Task BroadcastLoop()
        {
            while (_isRunning)
            {
                byte[] msg = Encoding.UTF8.GetBytes($"STREAMMESH_HELLO:{_tcpPort}");
                try
                {
                    using (var broadcastClient = new UdpClient())
                    {
                        broadcastClient.EnableBroadcast = true;
                        
                        // Default broadcast
                        try { await broadcastClient.SendAsync(msg, msg.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort)); } catch { }

                        // Interface specific broadcasts
                        var nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                        foreach (var nic in nics)
                        {
                            if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                            
                            var ipProps = nic.GetIPProperties();
                            foreach (var addr in ipProps.UnicastAddresses)
                            {
                                if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                                {
                                    /* Sınıfın IPv4 yayın adresini (Broadcast address) hesapla ve o adrese de paket gönder */
                                    var ipBytes = addr.Address.GetAddressBytes();
                                    var maskBytes = addr.IPv4Mask?.GetAddressBytes();
                                    if (maskBytes != null && maskBytes.Length == 4)
                                    {
                                        var broadcastBytes = new byte[4];
                                        for (int i = 0; i < 4; i++) broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                                        try
                                        {
                                            var bcastIp = new IPAddress(broadcastBytes);
                                            await broadcastClient.SendAsync(msg, msg.Length, new IPEndPoint(bcastIp, DiscoveryPort));
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
                await Task.Delay(5000); // Broadcast every 5 seconds
            }
        }

        public static void Stop()
        {
            _isRunning = false;
            _udpClient?.Close();
        }
    }
}
