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

        private int _port = 8080;

        public void Start(int port = 8080)
        {
            if (_isRunning) return;
            _port = port;
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
            using (var listener = new UdpClient())
            {
                listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.Client.Bind(new IPEndPoint(IPAddress.Any, 1900));
                try
                {
                    listener.JoinMulticastGroup(IPAddress.Parse("239.255.255.250"));
                }
                catch { }

                while (_isRunning)
                {
                    try
                    {
                        var result = await listener.ReceiveAsync();
                        string msg = Encoding.UTF8.GetString(result.Buffer);
                        if (msg.Contains("ssdp:discover", StringComparison.OrdinalIgnoreCase))
                        {
                            string response = $@"HTTP/1.1 200 OK
CACHE-CONTROL: max-age=1800
DATE: {DateTime.UtcNow:R}
EXT:
LOCATION: http://{GetLocalIp()}:{_port}/desc.xml
SERVER: Windows/10 UPnP/1.0 StreamMesh/1.8
ST: upnp:rootdevice
USN: uuid:{_uuid}::upnp:rootdevice

";
                            byte[] respBytes = Encoding.UTF8.GetBytes(response.Replace("\r\n", "\n").Replace("\n", "\r\n"));
                            await listener.SendAsync(respBytes, respBytes.Length, result.RemoteEndPoint);
                        }
                    }
                    catch { }
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
