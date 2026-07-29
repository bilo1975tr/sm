using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace StreamMesh.Core.Network
{
    public class SsdpService
    {
        private UdpClient? _udp;
        private bool _isRunning = false;
        private readonly string _uuid = Guid.NewGuid().ToString();

        public void Start(int port = 8080)
        {
            if (_isRunning) return;
            _isRunning = true;
            _udp = new UdpClient();
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            Task.Run(() => ListenLoop());
            Task.Run(() => AnnounceLoop(port));
        }

        public void Stop()
        {
            _isRunning = false;
            _udp?.Close();
        }

        private async Task ListenLoop()
        {
            var remoteEP = new IPEndPoint(IPAddress.Any, 1900);
            using (var listener = new UdpClient(1900))
            {
                listener.JoinMulticastGroup(IPAddress.Parse("239.255.255.250"));
                while (_isRunning)
                {
                    try
                    {
                        var result = await listener.ReceiveAsync();
                        string msg = Encoding.UTF8.GetString(result.Buffer);
                        if (msg.Contains("ssdp:discover"))
                        {
                            // Respond to discovery
                        }
                    } catch { }
                }
            }
        }

        private async Task AnnounceLoop(int port)
        {
            var ep = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
            string msg = $@"NOTIFY * HTTP/1.1
HOST: 239.255.255.250:1900
CACHE-CONTROL: max-age=1800
LOCATION: http://{GetLocalIp()}:{port}/desc.xml
NT: upnp:rootdevice
NTS: ssdp:alive
USN: uuid:{_uuid}::upnp:rootdevice";

            byte[] buffer = Encoding.UTF8.GetBytes(msg);
            while (_isRunning && _udp != null)
            {
                await _udp.SendAsync(buffer, buffer.Length, ep);
                await Task.Delay(30000);
            }
        }

        private string GetLocalIp()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork) return ip.ToString();
            }
            return "127.0.0.1";
        }
    }
}
