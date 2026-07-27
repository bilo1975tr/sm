using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Sockets;

namespace StreamMesh.Core.Network
{
    public class TunnelEngine
    {
        public string ExternalIp { get; private set; }

        public async Task<string> RefreshExternalIpAsync()
        {
            try
            {
                // 1. Try ipify (HTTPS)
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
                {
                    string ip = await client.GetStringAsync("https://api.ipify.org");
                    if (IPAddress.TryParse(ip.Trim(), out _))
                    {
                        ExternalIp = ip.Trim();
                        return ExternalIp;
                    }
                }
            }
            catch { }

            try
            {
                // 2. Try STUN (UDP) - Basic implementation
                ExternalIp = await GetIpFromStunAsync("stun.l.google.com", 19302);
            }
            catch { }

            return ExternalIp;
        }

        private async Task<string> GetIpFromStunAsync(string host, int port)
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            var targetAddr = addresses[0];
            var ep = new IPEndPoint(targetAddr, port);

            using (var client = new UdpClient(targetAddr.AddressFamily))
            {
                client.Client.SendTimeout = 2000;
                client.Client.ReceiveTimeout = 2000;
                byte[] request = new byte[20];
                request[1] = 0x01; // Binding Request
                Array.Copy(Guid.NewGuid().ToByteArray(), 0, request, 8, 12);

                await client.SendAsync(request, request.Length, ep);
                var result = await client.ReceiveAsync();

                // Simplified STUN parsing for MAPPED-ADDRESS (v4)
                for (int i = 20; i < result.Buffer.Length - 8; i++)
                {
                    if (result.Buffer[i] == 0x00 && result.Buffer[i + 1] == 0x01) // MAPPED-ADDRESS
                    {
                        return $"{result.Buffer[i + 8]}.{result.Buffer[i + 9]}.{result.Buffer[i + 10]}.{result.Buffer[i + 11]}";
                    }
                }
            }
            return null;
        }
    }
}
