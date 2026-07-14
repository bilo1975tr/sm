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
        private string _clientId;
        public string ClientId => _clientId;
        private bool _isInitialized;
        private bool _isLoopRunning;

        // Multi-peer state
        private readonly Dictionary<string, RTCPeerConnection> _peerConnections = new Dictionary<string, RTCPeerConnection>();
        private readonly Dictionary<string, RTCDataChannel> _dataChannels = new Dictionary<string, RTCDataChannel>();
        
        // Peer States (P2P Mesh): ClientId -> ActiveChannelId
        private readonly Dictionary<string, string> _peerActiveChannels = new Dictionary<string, string>();
        private readonly Dictionary<string, DateTime> _peerLastSeen = new Dictionary<string, DateTime>();

        // Current viewed channel of the local user
        private string _localActiveChannelId = "";

        private StunService()
        {
            _clientId = Guid.NewGuid().ToString("N").Substring(0, 8); // Short & elegant Client ID
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

        public static byte[] CompressString(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<byte>();
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new System.IO.Compression.GZipStream(mso, System.IO.Compression.CompressionMode.Compress))
                {
                    msi.CopyTo(gs);
                }
                return mso.ToArray();
            }
        }

        public static string DecompressString(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new System.IO.Compression.GZipStream(msi, System.IO.Compression.CompressionMode.Decompress))
                {
                    gs.CopyTo(mso);
                }
                return System.Text.Encoding.UTF8.GetString(mso.ToArray());
            }
        }

        public void BroadcastStatus(string activeChannelId)
        {
            _localActiveChannelId = activeChannelId;

            var payload = new P2PPayload
            {
                Type = "status",
                ClientId = _clientId,
                ChannelId = activeChannelId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            string json = System.Text.Json.JsonSerializer.Serialize(payload);
            byte[] compressed = CompressString(json);

            LogService.Log($"[P2P] BroadcastStatus: Sıkıştırılmamış: {json.Length} bytes, Sıkıştırılmış: {compressed.Length} bytes.");

            lock (_dataChannels)
            {
                foreach (var kp in _dataChannels)
                {
                    try
                    {
                        var channel = kp.Value;
                        channel.send(compressed);
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError($"[P2P] BroadcastStatus to peer {kp.Key} failed", ex);
                    }
                }
            }
        }

        public void BroadcastMeshSync()
        {
            Dictionary<string, string> currentStates;
            lock (_peerActiveChannels)
            {
                currentStates = new Dictionary<string, string>(_peerActiveChannels);
                currentStates[_clientId] = _localActiveChannelId;
            }

            var payload = new P2PPayload
            {
                Type = "mesh_sync",
                ClientId = _clientId,
                States = currentStates,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            string json = System.Text.Json.JsonSerializer.Serialize(payload);
            byte[] compressed = CompressString(json);

            lock (_dataChannels)
            {
                foreach (var kp in _dataChannels)
                {
                    try
                    {
                        var channel = kp.Value;
                        channel.send(compressed);
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError($"[P2P] BroadcastMeshSync to peer {kp.Key} failed", ex);
                    }
                }
            }
        }

        private void ProcessIncomingP2PMessage(byte[] rawData)
        {
            try
            {
                string decompressed = DecompressString(rawData);
                var payload = System.Text.Json.JsonSerializer.Deserialize<P2PPayload>(decompressed);
                if (payload == null) return;

                lock (_peerActiveChannels)
                {
                    if (payload.Type == "status")
                    {
                        _peerActiveChannels[payload.ClientId] = payload.ChannelId;
                        _peerLastSeen[payload.ClientId] = DateTime.UtcNow;
                    }
                    else if (payload.Type == "mesh_sync" && payload.States != null)
                    {
                        foreach (var kp in payload.States)
                        {
                            string peerId = kp.Key;
                            string channelId = kp.Value;

                            if (peerId == _clientId) continue;

                            _peerActiveChannels[peerId] = channelId;
                            _peerLastSeen[peerId] = DateTime.UtcNow;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("[P2P] ProcessIncomingP2PMessage failed", ex);
            }
        }

        public Dictionary<string, int> GetP2PViewerCounts()
        {
            lock (_peerActiveChannels)
            {
                var counts = new Dictionary<string, int>();

                if (!string.IsNullOrEmpty(_localActiveChannelId))
                {
                    counts[_localActiveChannelId] = 1;
                }

                var cutoff = DateTime.UtcNow.AddSeconds(-90);
                var activePeers = _peerLastSeen
                    .Where(kp => kp.Value > cutoff)
                    .Select(kp => kp.Key)
                    .ToList();

                foreach (var peerId in activePeers)
                {
                    if (_peerActiveChannels.TryGetValue(peerId, out var channelId) && !string.IsNullOrEmpty(channelId))
                    {
                        if (counts.ContainsKey(channelId))
                            counts[channelId]++;
                        else
                            counts[channelId] = 1;
                    }
                }

                return counts;
            }
        }

        public int GetP2POnlineCount()
        {
            lock (_peerActiveChannels)
            {
                var cutoff = DateTime.UtcNow.AddSeconds(-90);
                int activePeersCount = _peerLastSeen.Count(kp => kp.Value > cutoff);
                return activePeersCount + 1; // Include ourselves
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

                if (!_isLoopRunning)
                {
                    _isLoopRunning = true;
                    _ = Task.Run(() => SignalingAndGossipLoopAsync(ipPort));
                }

                TunnelService.AddTurnLog("P2P Arka Plan Koordinasyon ve Sinyalleşme Döngüsü Başlatıldı.");
                // Tünel aktif -> Yeşil (2)
                TunnelService.Instance.UpdateDots(TunnelService.Instance.DirectDotState, TunnelService.Instance.StunDotState, 2);
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

        private async Task SignalingAndGossipLoopAsync(string ipPort)
        {
            int loopCount = 0;
            while (_isLoopRunning)
            {
                try
                {
                    if (_firestoreDb == null)
                    {
                        await Task.Delay(10000);
                        continue;
                    }

                    var nowUtc = DateTime.UtcNow;

                    // Step 1: Register/Heartbeat in active_clients
                    var myDoc = _firestoreDb.Collection("active_clients").Document(_clientId);
                    bool isSuperPeer = (TunnelService.Instance.DirectDotState == 2) || 
                                       (TunnelService.Instance.StunDotState == 2 && TunnelService.Instance.CurrentNatType == NatType.ConeNAT);

                    await myDoc.SetAsync(new Dictionary<string, object>
                    {
                        { "clientId", _clientId },
                        { "ipPort", ipPort },
                        { "isSuperPeer", isSuperPeer },
                        { "natType", TunnelService.Instance.CurrentNatType.ToString() },
                        { "connectionMode", TunnelService.Instance.ActiveMode.ToString() },
                        { "updatedAt", Timestamp.FromDateTime(nowUtc) }
                    });

                    // Step 2: Query active clients and prune dead ones
                    var allClientsQuery = await _firestoreDb.Collection("active_clients").GetSnapshotAsync();
                    var activePeerIds = new List<string>();

                    foreach (var doc in allClientsQuery.Documents)
                     {
                        if (doc.Id == _clientId) continue;

                        if (doc.TryGetValue<Timestamp>("updatedAt", out var updatedAt))
                        {
                            var age = nowUtc - updatedAt.ToDateTime();
                            if (age.TotalSeconds > 90)
                            {
                                try { await doc.Reference.DeleteAsync(); } catch {}
                                try
                                {
                                    string sigId1 = $"{_clientId}_{doc.Id}";
                                    string sigId2 = $"{doc.Id}_{_clientId}";
                                    await _firestoreDb.Collection("p2p_signals").Document(sigId1).DeleteAsync();
                                    await _firestoreDb.Collection("p2p_signals").Document(sigId2).DeleteAsync();
                                }
                                catch {}
                            }
                            else
                            {
                                activePeerIds.Add(doc.Id);
                            }
                        }
                    }

                    // Step 3: Gossip/Mesh Sync over open channels
                    BroadcastMeshSync();

                    // Step 4: Signaling and Handshake with active peers
                    var iceServers = await FetchTurnCredentialsAsync();
                    var config = new RTCConfiguration { iceServers = iceServers };

                    foreach (var peerId in activePeerIds)
                    {
                        bool alreadyConnectedOrConnecting;
                        lock (_peerConnections)
                        {
                            alreadyConnectedOrConnecting = _peerConnections.ContainsKey(peerId);
                        }

                        if (alreadyConnectedOrConnecting) continue;

                        if (string.Compare(_clientId, peerId) < 0)
                        {
                            _ = Task.Run(() => InitiateOfferAsync(peerId, config));
                        }
                        else
                        {
                            _ = Task.Run(() => CheckIncomingOffersAsync(peerId, config));
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("[P2P] SignalingAndGossipLoopAsync error", ex);
                }

                loopCount++;
                await Task.Delay(15000); // Run every 15 seconds
            }
        }

        private async Task InitiateOfferAsync(string peerId, RTCConfiguration config)
        {
            try
            {
                LogService.Log($"[P2P] Peer {peerId} için WebRTC bağlantı teklifi (Offer) başlatılıyor...");
                TunnelService.AddTurnLog($"Peer {peerId} ile bağlantı kuruluyor (Biz: Offerer)...");

                var pc = new RTCPeerConnection(config);
                lock (_peerConnections)
                {
                    _peerConnections[peerId] = pc;
                }

                var dc = await pc.createDataChannel("p2p-sync", null);
                SetupDataChannel(peerId, dc);

                var signalDocRef = _firestoreDb.Collection("p2p_signals").Document($"{_clientId}_{peerId}");
                
                pc.onicecandidate += async (candidate) =>
                {
                    if (candidate != null && !string.IsNullOrEmpty(candidate.candidate))
                    {
                        try
                        {
                            await signalDocRef.UpdateAsync("offerCandidates", FieldValue.ArrayUnion(candidate.candidate));
                        }
                        catch {}
                    }
                };

                var offer = pc.createOffer();
                await pc.setLocalDescription(offer);

                await signalDocRef.SetAsync(new Dictionary<string, object>
                {
                    { "offererId", _clientId },
                    { "answererId", peerId },
                    { "offerSdp", offer.sdp },
                    { "offerCandidates", new List<string>() },
                    { "answerCandidates", new List<string>() },
                    { "updatedAt", Timestamp.FromDateTime(DateTime.UtcNow) }
                });

                bool answerFound = false;
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(2000);
                    var snap = await signalDocRef.GetSnapshotAsync();
                    if (snap.Exists && snap.TryGetValue<string>("answerSdp", out var answerSdp) && !string.IsNullOrEmpty(answerSdp))
                    {
                        LogService.Log($"[P2P] Peer {peerId} için Answer SDP bulundu! Uzak bağlantı kuruluyor...");
                        pc.setRemoteDescription(new RTCSessionDescriptionInit { sdp = answerSdp, type = RTCSdpType.answer });
                        answerFound = true;

                        if (snap.TryGetValue<List<string>>("answerCandidates", out var answerCandidates) && answerCandidates != null)
                        {
                            foreach (var cand in answerCandidates)
                            {
                                pc.addIceCandidate(new RTCIceCandidateInit { candidate = cand });
                            }
                        }
                        break;
                    }
                }

                if (!answerFound)
                {
                    LogService.Log($"[P2P] Peer {peerId} teklife cevap vermedi (Zaman Aşımı).");
                    CleanupPeer(peerId);
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"[P2P] InitiateOfferAsync to peer {peerId} failed", ex);
                CleanupPeer(peerId);
            }
        }

        private async Task CheckIncomingOffersAsync(string peerId, RTCConfiguration config)
        {
            try
            {
                var signalDocRef = _firestoreDb.Collection("p2p_signals").Document($"{peerId}_{_clientId}");
                var snap = await signalDocRef.GetSnapshotAsync();
                if (!snap.Exists) return;

                if (snap.TryGetValue<string>("offerSdp", out var offerSdp) && !string.IsNullOrEmpty(offerSdp))
                {
                    if (snap.TryGetValue<string>("answerSdp", out var existingAnswer) && !string.IsNullOrEmpty(existingAnswer))
                    {
                        return;
                    }

                    LogService.Log($"[P2P] Peer {peerId} tarafından gelen WebRTC teklifi tespit edildi! Kabul ediliyor...");
                    TunnelService.AddTurnLog($"Peer {peerId} ile gelen bağlantı kabul ediliyor (Biz: Answerer)...");

                    var pc = new RTCPeerConnection(config);
                    lock (_peerConnections)
                    {
                        _peerConnections[peerId] = pc;
                    }

                    pc.ondatachannel += (dc) =>
                    {
                        SetupDataChannel(peerId, dc);
                    };

                    pc.onicecandidate += async (candidate) =>
                    {
                        if (candidate != null && !string.IsNullOrEmpty(candidate.candidate))
                        {
                            try
                            {
                                await signalDocRef.UpdateAsync("answerCandidates", FieldValue.ArrayUnion(candidate.candidate));
                            }
                            catch {}
                        }
                    };

                    pc.setRemoteDescription(new RTCSessionDescriptionInit { sdp = offerSdp, type = RTCSdpType.offer });

                    var answer = pc.createAnswer();
                    await pc.setLocalDescription(answer);

                    await signalDocRef.UpdateAsync(new Dictionary<string, object>
                    {
                        { "answerSdp", answer.sdp },
                        { "updatedAt", Timestamp.FromDateTime(DateTime.UtcNow) }
                    });

                    if (snap.TryGetValue<List<string>>("offerCandidates", out var offerCandidates) && offerCandidates != null)
                    {
                        foreach (var cand in offerCandidates)
                        {
                            pc.addIceCandidate(new RTCIceCandidateInit { candidate = cand });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"[P2P] CheckIncomingOffersAsync from peer {peerId} failed", ex);
                CleanupPeer(peerId);
            }
        }

        private void SetupDataChannel(string peerId, RTCDataChannel dc)
        {
            lock (_dataChannels)
            {
                _dataChannels[peerId] = dc;
            }

            dc.onopen += () =>
            {
                LogService.Log($"[P2P] Peer {peerId} ile DataChannel açıldı.");
                TunnelService.AddTurnLog($"Peer {peerId} ile P2P veri kanalı başarıyla bağlandı!");
                SendStatusToPeer(peerId, _localActiveChannelId);
            };

            dc.onclose += () =>
            {
                LogService.Log($"[P2P] Peer {peerId} ile DataChannel kapandı.");
                CleanupPeer(peerId);
            };

            dc.onmessage += (channel, protocol, data) =>
            {
                ProcessIncomingP2PMessage(data);
            };
        }

        private void CleanupPeer(string peerId)
        {
            try
            {
                lock (_peerConnections)
                {
                    if (_peerConnections.TryGetValue(peerId, out var pc))
                    {
                        pc.Close("Cleanup");
                        _peerConnections.Remove(peerId);
                    }
                }
                lock (_dataChannels)
                {
                    _dataChannels.Remove(peerId);
                }
                lock (_peerActiveChannels)
                {
                    _peerActiveChannels.Remove(peerId);
                    _peerLastSeen.Remove(peerId);
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"[P2P] CleanupPeer for {peerId} failed", ex);
            }
        }

        private void SendStatusToPeer(string peerId, string activeChannelId)
        {
            try
            {
                var payload = new P2PPayload
                {
                    Type = "status",
                    ClientId = _clientId,
                    ChannelId = activeChannelId,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                string json = System.Text.Json.JsonSerializer.Serialize(payload);
                byte[] compressed = CompressString(json);

                lock (_dataChannels)
                {
                    if (_dataChannels.TryGetValue(peerId, out var dc))
                    {
                        dc.send(compressed);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"[P2P] SendStatusToPeer {peerId} failed", ex);
            }
        }

        public class P2PPayload
        {
            public string Type { get; set; }
            public string ClientId { get; set; }
            public string ChannelId { get; set; }
            public long Timestamp { get; set; }
            public Dictionary<string, string> States { get; set; }
        }
    }
}
