using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace StreamMesh.Services.P2P
{
    public static class StunService
    {
        public static async Task<IPEndPoint> GetExternalEndpointAsync(int localPort)
        {
            try
            {
                using (var udpClient = new UdpClient())
                {
                    udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, localPort));
                    
                    byte[] stunRequest = new byte[20];
                    stunRequest[0] = 0;
                    stunRequest[1] = 1; // Binding request
                    Guid.NewGuid().ToByteArray().CopyTo(stunRequest, 4);

                    await udpClient.SendAsync(stunRequest, stunRequest.Length, "stun.l.google.com", 19302);
                    
                    // Timeout ile bekle
                    var receiveTask = udpClient.ReceiveAsync();
                    if (await Task.WhenAny(receiveTask, Task.Delay(3000)) == receiveTask)
                    {
                        var result = await receiveTask;
                        byte[] response = result.Buffer;

                        if (response.Length >= 20 && response[0] == 0x01 && response[1] == 0x01)
                        {
                            int i = 20;
                            while (i < response.Length)
                            {
                                int attrType = (response[i] << 8) | response[i + 1];
                                int attrLen = (response[i + 2] << 8) | response[i + 3];
                                
                                if (attrType == 0x0001 || attrType == 0x0020) // MAPPED-ADDRESS veya XOR-MAPPED-ADDRESS
                                {
                                    int port = (response[i + 6] << 8) | response[i + 7];
                                    if (attrType == 0x0020) port ^= 0x2112; 
                                    
                                    int ipPos = i + 8;
                                    byte[] ipBytes = new byte[4];
                                    Array.Copy(response, ipPos, ipBytes, 0, 4);
                                    if (attrType == 0x0020) 
                                    {
                                        ipBytes[0] ^= 0x21;
                                        ipBytes[1] ^= 0x12;
                                        ipBytes[2] ^= 0xA4;
                                        ipBytes[3] ^= 0x42;
                                    }

                                    return new IPEndPoint(new IPAddress(ipBytes), port);
                                }
                                i += 4 + attrLen;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"STUN Sorgu Hatası: {ex.Message}");
            }
            return null;
        }
    }
}
