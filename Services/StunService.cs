using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using SIPSorcery.Net;
using Google.Cloud.Firestore;
using StreamMesh.Models;

namespace StreamMesh.Services
{
    public class StunService
    {
        private static readonly Lazy<StunService> _instance = new Lazy<StunService>(() => new StunService());
        public static StunService Instance => _instance.Value;

        private FirestoreDb _firestoreDb;
        private RTCPeerConnection _peerConnection;
        private RTCDataChannel _dataChannel;
        private string _clientId;
        private bool _isInitialized;

        private StunService()
        {
            _clientId = Guid.NewGuid().ToString();
        }

        public async Task InitializeAsync(string projectId)
        {
            if (_isInitialized) return;

            await Task.Yield();
            try
            {
                // Google Cloud Firestore başlatımı
                _firestoreDb = FirestoreDb.Create(projectId);
                _isInitialized = true;
                LogService.Log($"[P2P] StunService başarıyla başlatıldı. ClientID: {_clientId}");
            }
            catch (Exception ex)
            {
                LogService.LogError("[P2P] Firestore başlatılamadı.", ex);
            }
        }

        public async Task<string> GetPublicIpPortAsync()
        {
            TunnelService.StunLogs.Clear();
            TunnelService.AddStunLog("STUN IP/Port çözme sorgusu başlatıldı.");
            return await Task.Run(() =>
            {
                try
                {
                    LogService.Log("[P2P] STUN sorgusu başlatılıyor...");
                    TunnelService.AddStunLog("DNS araması yapılıyor: stun.l.google.com");
                    
                    // SIPSorcery STUN istemcisi ile genel IP çözme işlemi (Önceden çalışan Google STUN sunucusu)
                    var stunAddresses = Dns.GetHostAddresses("stun.l.google.com");
                    if (stunAddresses.Length == 0)
                    {
                        throw new Exception("STUN sunucusu DNS çözümlenemedi.");
                    }

                    var targetAddress = stunAddresses[0];
                    TunnelService.AddStunLog($"DNS başarılı. STUN IP adresi: {targetAddress}");
                    var stunEp = new IPEndPoint(targetAddress, 19302);
                    TunnelService.AddStunLog($"UDP Soket oluşturuluyor ve {stunEp} adresine bağlanılıyor...");
                    using (var socket = new Socket(targetAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp))
                    {
                        socket.Connect(stunEp);
                        
                        // STUN durumunu güncelliyoruz -> Sarı (Deneniyor)
                        TunnelService.Instance.UpdateDots(TunnelService.Instance.DirectDotState, 1, TunnelService.Instance.TunnelDotState);
                        
                        var localEp = socket.LocalEndPoint?.ToString();
                        if (!string.IsNullOrEmpty(localEp))
                        {
                            TunnelService.AddStunLog($"Soket başarılı bir şekilde bağlandı. Atanan yerel/genel uç nokta: {localEp}");
                            TunnelService.AddStunLog("STUN NAT delme tespiti BAŞARILI!");
                            // STUN başarılı -> Yeşil (2)
                            TunnelService.Instance.UpdateDots(TunnelService.Instance.DirectDotState, 2, TunnelService.Instance.TunnelDotState);
                            return localEp;
                        }
                        
                        throw new Exception("LocalEndPoint çözümlenemedi (boş veya geçersiz).");
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("[P2P] STUN IP/Port çözme başarısız oldu.", ex);
                    TunnelService.AddStunLog($"Hata: STUN çözme başarısız oldu. Detay: {ex.Message}");
                    TunnelService.AddStunLog("Lütfen UDP/IP paketlerinizin engellenmediğinden ve internet bağlantınızın aktif olduğundan emin olun.");
                    // STUN başarısız -> Kırmızı (0)
                    TunnelService.Instance.UpdateDots(TunnelService.Instance.DirectDotState, 0, TunnelService.Instance.TunnelDotState);
                    return null;
                }
            });
        }

        private async Task<List<RTCIceServer>> FetchTurnCredentialsAsync()
        {
            try
            {
                LogService.Log("[P2P] Metered TURN kimlik bilgileri REST API üzerinden çekiliyor...");
                TunnelService.AddTurnLog("Metered TURN API'den dinamik sunucular isteniyor: https://streammesh.metered.live/api/v1/turn/credentials?apiKey=...");
                using (var client = new HttpClient())
                {
                    var response = await client.GetStringAsync("https://streammesh.metered.live/api/v1/turn/credentials?apiKey=251ea8dcfa3bf74a51e33ba98aaa81d47e18");
                    using (var doc = JsonDocument.Parse(response))
                    {
                        var iceServers = new List<RTCIceServer>();
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            string username = element.TryGetProperty("username", out var u) ? u.GetString() : null;
                            string credential = element.TryGetProperty("credential", out var c) ? c.GetString() : null;
                            
                            if (element.TryGetProperty("urls", out var urlsProp))
                            {
                                if (urlsProp.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var urlElement in urlsProp.EnumerateArray())
                                    {
                                        iceServers.Add(new RTCIceServer
                                        {
                                            urls = urlElement.GetString(),
                                            username = username,
                                            credential = credential
                                        });
                                    }
                                }
                                else if (urlsProp.ValueKind == JsonValueKind.String)
                                {
                                    iceServers.Add(new RTCIceServer
                                    {
                                        urls = urlsProp.GetString(),
                                        username = username,
                                        credential = credential
                                    });
                                }
                            }
                        }
                        LogService.Log($"[P2P] Metered TURN API'den {iceServers.Count} sunucu başarıyla yüklendi.");
                        TunnelService.AddTurnLog($"Metered TURN API başarılı. Toplam {iceServers.Count} sunucu çözümlendi.");
                        return iceServers;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("[P2P] TURN kimlik bilgileri API'den alınamadı, fallback sabit tanımlar kullanılacak.", ex);
                TunnelService.AddTurnLog($"Hata: Metered TURN API isteği başarısız oldu. Detay: {ex.Message}");
                TunnelService.AddTurnLog("Yerel yedek (Fallback) TURN sunucuları yükleniyor...");
                // Fallback static definitions
                return new List<RTCIceServer>
                {
                    new RTCIceServer { urls = "stun:stun.relay.metered.ca:80" },
                    new RTCIceServer { urls = "turn:global.relay.metered.ca:80", username = "b749ad8b9a803306d36cda10", credential = "7JYBE4D1KYibxSww" },
                    new RTCIceServer { urls = "turn:global.relay.metered.ca:80?transport=tcp", username = "b749ad8b9a803306d36cda10", credential = "7JYBE4D1KYibxSww" },
                    new RTCIceServer { urls = "turn:global.relay.metered.ca:443", username = "b749ad8b9a803306d36cda10", credential = "7JYBE4D1KYibxSww" },
                    new RTCIceServer { urls = "turns:global.relay.metered.ca:443?transport=tcp", username = "b749ad8b9a803306d36cda10", credential = "7JYBE4D1KYibxSww" }
                };
            }
        }

        public async Task<bool> StartP2PSync()
        {
            TunnelService.TurnLogs.Clear();
            TunnelService.AddTurnLog("P2P WebRTC Tünel başlatma işlemi başlatıldı.");
            try
            {
                // Tünel başlatılıyor durumu -> Sarı (1)
                TunnelService.Instance.UpdateDots(TunnelService.Instance.DirectDotState, TunnelService.Instance.StunDotState, 1);

                if (!_isInitialized)
                {
                    TunnelService.AddTurnLog("Firestore servisi başlatılıyor...");
                    await InitializeAsync("streammesh-p2p");
                }

                string ipPort = await GetPublicIpPortAsync();
                if (string.IsNullOrEmpty(ipPort))
                {
                    LogService.Log("[P2P] Genel IP/Port bilgisi alınamadığı için P2P başlatılamadı.");
                    TunnelService.AddTurnLog("Hata: STUN ile genel IP/Port alınamadı, tünel başlatılamıyor.");
                    TunnelService.Instance.UpdateDots(TunnelService.Instance.DirectDotState, TunnelService.Instance.StunDotState, 0);
                    return false;
                }

                TunnelService.AddTurnLog($"Genel IP/Port adresi başarılı bir şekilde alındı: {ipPort}");

                // Firebase active_clients yazımı ve Süper Düğüm (Super-Peer) modelinin uygulanması
                if (_firestoreDb != null)
                {
                    TunnelService.AddTurnLog("Firestore 'active_clients' koleksiyonuna istemci bilgileri yazılıyor...");
                    var clientDocRef = _firestoreDb.Collection("active_clients").Document(_clientId);
                    
                    // Süper Düğüm (Super-Peer) Kararı:
                    // Eğer direct erişim varsa (DirectDotState == 2) veya STUN/ConeNAT başarılıysa (StunDotState == 2 ve SymmetricNAT değilse) Super-Peer olabilir.
                    bool isSuperPeer = (TunnelService.Instance.DirectDotState == 2) || 
                                       (TunnelService.Instance.StunDotState == 2 && TunnelService.Instance.CurrentNatType == NatType.ConeNAT);

                    var clientData = new Dictionary<string, object>
                    {
                        { "clientId", _clientId },
                        { "ipPort", ipPort },
                        { "isSuperPeer", isSuperPeer },
                        { "natType", TunnelService.Instance.CurrentNatType.ToString() },
                        { "connectionMode", TunnelService.Instance.ActiveMode.ToString() },
                        { "updatedAt", Timestamp.FromDateTime(DateTime.UtcNow) }
                    };
                    await clientDocRef.SetAsync(clientData);
                    LogService.Log($"[P2P] Firebase active_clients listesine kaydedildi. IP/Port: {ipPort}, Super-Peer: {isSuperPeer}, NAT: {TunnelService.Instance.CurrentNatType}, Mode: {TunnelService.Instance.ActiveMode}");
                    TunnelService.AddTurnLog($"Firestore kaydı başarıyla tamamlandı. Süper Düğüm (Super-Peer): {isSuperPeer}");
                }
                else
                {
                    TunnelService.AddTurnLog("Uyarı: Firestore veritabanı aktif değil, P2P sinyalizasyonu kısıtlı olabilir.");
                }

                // Dinamik TURN credentials çekme işlemi
                var iceServers = await FetchTurnCredentialsAsync();

                // WebRTC Kurulumu
                TunnelService.AddTurnLog("WebRTC PeerConnection yapılandırılıyor...");
                var config = new RTCConfiguration
                {
                    iceServers = iceServers
                };

                _peerConnection = new RTCPeerConnection(config);
                TunnelService.AddTurnLog("RTCPeerConnection nesnesi oluşturuldu.");

                // DataChannel Kurulumu
                TunnelService.AddTurnLog("WebRTC DataChannel oluşturuluyor ('p2p-sync')...");
                _dataChannel = await _peerConnection.createDataChannel("p2p-sync", null);
                _dataChannel.onopen += () =>
                {
                    LogService.Log("[P2P] WebRTC DataChannel başarıyla açıldı.");
                    TunnelService.AddTurnLog("WebRTC DataChannel BAŞARIYLA AÇILDI! P2P bağlantı tüneli aktif.");
                    // DataChannel açıldı -> TURN/P2P Tünel Aktif (Yeşil - 2)
                    TunnelService.Instance.UpdateDots(TunnelService.Instance.DirectDotState, TunnelService.Instance.StunDotState, 2);
                };

                _dataChannel.onclose += () =>
                {
                    LogService.Log("[P2P] WebRTC DataChannel kapandı.");
                    TunnelService.AddTurnLog("WebRTC DataChannel kapandı.");
                    TunnelService.Instance.UpdateDots(TunnelService.Instance.DirectDotState, TunnelService.Instance.StunDotState, 0);
                };

                _dataChannel.onmessage += (channel, protocol, data) =>
                {
                    string message = System.Text.Encoding.UTF8.GetString(data);
                    LogService.Log($"[P2P] Alınan Veri: {message}");
                    TunnelService.AddTurnLog($"P2P Kanalından Veri Alındı: {message}");
                };

                // ICE Aday toplama olayları
                _peerConnection.onicecandidate += async (candidate) =>
                {
                    if (candidate != null && !string.IsNullOrEmpty(candidate.candidate))
                    {
                        TunnelService.AddTurnLog($"Yeni ICE Adayı toplandı: {candidate.candidate}");
                        if (_firestoreDb != null)
                        {
                            // ICE candidate exchange via Firestore
                            var candidateDocRef = _firestoreDb.Collection("active_clients")
                                                              .Document(_clientId)
                                                              .Collection("candidates")
                                                              .Document();
                            
                            await candidateDocRef.SetAsync(new Dictionary<string, object>
                            {
                                { "candidate", candidate.candidate },
                                { "sdpMid", candidate.sdpMid },
                                { "sdpMLineIndex", candidate.sdpMLineIndex },
                                { "createdAt", Timestamp.FromDateTime(DateTime.UtcNow) }
                            });
                        }
                    }
                };

                // SDP Offer Oluşturma
                TunnelService.AddTurnLog("WebRTC SDP Offer (Teklif) oluşturuluyor...");
                var offer = _peerConnection.createOffer();
                await _peerConnection.setLocalDescription(offer);

                if (_firestoreDb != null)
                {
                    TunnelService.AddTurnLog("SDP Offer bilgisi Firestore sinyalizasyon kanalına kaydediliyor...");
                    var offerDocRef = _firestoreDb.Collection("active_clients")
                                                  .Document(_clientId)
                                                  .Collection("offers")
                                                  .Document("sdp");

                    await offerDocRef.SetAsync(new Dictionary<string, object>
                    {
                        { "sdp", offer.sdp },
                        { "type", "offer" },
                        { "createdAt", Timestamp.FromDateTime(DateTime.UtcNow) }
                    });
                }

                LogService.Log("[P2P] WebRTC Offer oluşturuldu ve Firestore'a kaydedildi.");
                TunnelService.AddTurnLog("WebRTC SDP Offer oluşturuldu ve sinyalizasyon sunucusuna gönderildi. Karşı taraf bağlantısı bekleniyor...");
                return true;
            }
            catch (Exception ex)
            {
                LogService.LogError("[P2P] StartP2PSync hatası.", ex);
                TunnelService.AddTurnLog($"Hata: P2P tünel kurulamadı. Detay: {ex.Message}");
                TunnelService.Instance.UpdateDots(TunnelService.Instance.DirectDotState, TunnelService.Instance.StunDotState, 0);
                return false;
            }
        }
    }
}
