using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace StreamMesh.Services
{
    public enum NatType
    {
        Unknown,
        Open,
        ConeNAT, // Hole Punching Possible
        SymmetricNAT // Hole Punching Impossible
    }

    public enum ConnectionMode
    {
        Direct,
        StunP2P,
        PlayitTunnel
    }

    public class TunnelService
    {
        private static TunnelService _instance;
        public static TunnelService Instance => _instance ?? (_instance = new TunnelService());

        private Process _playitProcess;
        private bool _isTunnelRunning = false;
        public bool IsTunnelRunning => _isTunnelRunning;

        public NatType CurrentNatType { get; private set; } = NatType.Unknown;
        public ConnectionMode ActiveMode { get; private set; } = ConnectionMode.Direct;
        public string ExternalAddress { get; private set; }
        public string PlayitClaimUrl { get; private set; }

        public int DirectDotState { get; set; } = 0; // 0=Red, 1=Yellow, 2=Green
        public int StunDotState { get; set; } = 0;
        public int TunnelDotState { get; set; } = 0;

        public static readonly System.Collections.Generic.List<string> DirectLogs = new System.Collections.Generic.List<string>();
        public static readonly System.Collections.Generic.List<string> StunLogs = new System.Collections.Generic.List<string>();
        public static readonly System.Collections.Generic.List<string> TurnLogs = new System.Collections.Generic.List<string>();

        public static void AddDirectLog(string msg)
        {
            lock (DirectLogs)
            {
                DirectLogs.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
            }
        }

        public static void AddStunLog(string msg)
        {
            lock (StunLogs)
            {
                StunLogs.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
            }
        }

        public static void AddTurnLog(string msg)
        {
            lock (TurnLogs)
            {
                TurnLogs.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
            }
        }

        public event Action<string> OnStatusMessage;
        public event Action<bool, string> OnTunnelStateChanged;
        public event Action<int, int, int> OnStatusDotsUpdated;

        public void UpdateDots(int direct, int stun, int tunnel)
        {
            DirectDotState = direct;
            StunDotState = stun;
            TunnelDotState = tunnel;
            OnStatusDotsUpdated?.Invoke(direct, stun, tunnel);
        }

        private readonly string PlayitBinaryPath;

        public TunnelService()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string toolsDir = Path.Combine(baseDir, "tools");
            if (!Directory.Exists(toolsDir)) Directory.CreateDirectory(toolsDir);

            bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
            PlayitBinaryPath = Path.Combine(toolsDir, isWindows ? "playit.exe" : "playit");
        }

        public async Task<NatType> DetectNatTypeAsync()
        {
            StunLogs.Clear();
            AddStunLog("STUN NAT Tipi analizi başlatıldı.");
            UpdateDots(DirectDotState, 1, TunnelDotState);
            OnStatusMessage?.Invoke("NAT tipi analiz ediliyor...");
            try
            {
                // STUN servers
                string stun1 = "stun.l.google.com";
                int port1 = 19302;
                string stun2 = "stun.sipgate.net";
                int port2 = 10000;

                AddStunLog($"Birinci STUN sunucusu sorgulanıyor: {stun1}:{port1}");
                var endpoint1 = await QueryStunServerAsync(stun1, port1);
                if (endpoint1 == null)
                {
                    CurrentNatType = NatType.Unknown;
                    AddStunLog($"Hata: Birinci STUN sunucusundan ({stun1}:{port1}) yanıt alınamadı.");
                    OnStatusMessage?.Invoke("STUN sunucusuna erişilemedi.");
                    UpdateDots(DirectDotState, 0, TunnelDotState);
                    return NatType.Unknown;
                }
                AddStunLog($"Birinci STUN başarılı. Çözümlenen Dış IP/Port: {endpoint1}");

                AddStunLog($"İkinci STUN sunucusu sorgulanıyor (Simetrik/Cone NAT tespiti için): {stun2}:{port2}");
                var endpoint2 = await QueryStunServerAsync(stun2, port2);
                if (endpoint2 == null)
                {
                    // If first succeeded but second failed, we assume normal cone NAT
                    CurrentNatType = NatType.ConeNAT;
                    AddStunLog($"İkinci STUN sunucusundan yanıt alınamadı. Varsayılan olarak Cone NAT kabul ediliyor.");
                    OnStatusMessage?.Invoke("NAT Tipi: Cone NAT (Hole Punching Destekleniyor)");
                    UpdateDots(DirectDotState, 2, TunnelDotState);
                    return NatType.ConeNAT;
                }
                AddStunLog($"İkinci STUN başarılı. Çözümlenen Dış IP/Port: {endpoint2}");

                // If external ports are different for different destinations, it's Symmetric NAT
                if (endpoint1.Port != endpoint2.Port)
                {
                    CurrentNatType = NatType.SymmetricNAT;
                    AddStunLog($"Uyarı: STUN portları uyuşmuyor ({endpoint1.Port.ToString()} != {endpoint2.Port.ToString()}). Simetrik NAT tespit edildi.");
                    AddStunLog("Simetrik NAT durumunda doğrudan UDP deliği açmak (Hole punching) imkansızdır. TURN tüneli gereklidir.");
                    OnStatusMessage?.Invoke("NAT Tipi: Simetrik NAT (Hole Punching İMKANSIZ)");
                    UpdateDots(DirectDotState, 0, TunnelDotState);
                }
                else
                {
                    CurrentNatType = NatType.ConeNAT;
                    AddStunLog($"STUN portları eşleşti ({endpoint1.Port.ToString()} == {endpoint2.Port.ToString()}). Cone NAT tespit edildi.");
                    AddStunLog("Hole punching ve doğrudan P2P bağlantı kurulabilir.");
                    OnStatusMessage?.Invoke("NAT Tipi: Cone NAT (Hole Punching Aktif Edilebilir)");
                    UpdateDots(DirectDotState, 2, TunnelDotState);
                }

                return CurrentNatType;
            }
            catch (Exception ex)
            {
                LogService.LogError("NAT Detection failed", ex);
                AddStunLog($"İstisna oluştu: {ex.Message}");
                CurrentNatType = NatType.Unknown;
                UpdateDots(DirectDotState, 0, TunnelDotState);
                return NatType.Unknown;
            }
        }

        private async Task<IPEndPoint> QueryStunServerAsync(string host, int port)
        {
            AddStunLog($"[Soket] UDP istemci oluşturuluyor. Hedef: {host}:{port}");
            using (var client = new UdpClient())
            {
                client.Client.SendTimeout = 1500;
                client.Client.ReceiveTimeout = 1500;

                try
                {
                    AddStunLog($"[DNS] STUN sunucu adresi çözümleniyor: {host}");
                    var addresses = await Dns.GetHostAddressesAsync(host);
                    if (addresses.Length == 0)
                    {
                        AddStunLog($"[DNS] Hata: {host} için DNS kaydı bulunamadı.");
                        return null;
                    }

                    var ep = new IPEndPoint(addresses[0], port);
                    AddStunLog($"[DNS] Başarılı. Çözümlenen IP: {addresses[0]}");
                    
                    // STUN Binding Request Header (20 bytes)
                    byte[] stunRequest = new byte[20];
                    stunRequest[0] = 0x00; stunRequest[1] = 0x01; // Message Type: Binding Request
                    stunRequest[2] = 0x00; stunRequest[3] = 0x00; // Message Length: 0
                    // Magic Cookie
                    stunRequest[4] = 0x21; stunRequest[5] = 0x12; stunRequest[6] = 0xA4; stunRequest[7] = 0x42;
                    // Transaction ID (12 bytes)
                    Guid.NewGuid().ToByteArray().CopyTo(stunRequest, 8);

                    AddStunLog("[STUN] Binding Request paketi gönderiliyor (20 bayt)...");
                    await client.SendAsync(stunRequest, stunRequest.Length, ep);
                    
                    AddStunLog("[STUN] Yanıt bekleniyor (Zaman aşımı: 1500ms)...");
                    var receiveResult = await client.ReceiveAsync();

                    byte[] stunResponse = receiveResult.Buffer;
                    AddStunLog($"[STUN] Yanıt alındı: {stunResponse.Length} bayt.");
                    if (stunResponse.Length < 20)
                    {
                        AddStunLog("[STUN] Hata: Alınan paket boyutu STUN başlık boyutundan küçük.");
                        return null;
                    }

                    // Parse MAPPED-ADDRESS attributes
                    int pos = 20;
                    while (pos < stunResponse.Length)
                    {
                        if (pos + 4 > stunResponse.Length) break;
                        int type = (stunResponse[pos] << 8) | stunResponse[pos + 1];
                        int length = (stunResponse[pos + 2] << 8) | stunResponse[pos + 3];
                        pos += 4;

                        if (type == 0x0001 || type == 0x0020) // MAPPED-ADDRESS or XOR-MAPPED-ADDRESS
                        {
                            if (pos + length > stunResponse.Length) break;
                            int family = stunResponse[pos + 1];
                            int extPort = (stunResponse[pos + 2] << 8) | stunResponse[pos + 3];
                            
                            if (type == 0x0020) // XOR decode
                            {
                                extPort ^= 0x2112;
                            }

                            byte[] ipBytes;
                            if (family == 0x01) // IPv4
                            {
                                ipBytes = new byte[4];
                                Array.Copy(stunResponse, pos + 4, ipBytes, 0, 4);
                                if (type == 0x0020)
                                {
                                    ipBytes[0] ^= 0x21;
                                    ipBytes[1] ^= 0x12;
                                    ipBytes[2] ^= 0xA4;
                                    ipBytes[3] ^= 0x42;
                                }
                            }
                            else if (family == 0x02) // IPv6
                            {
                                ipBytes = new byte[16];
                                Array.Copy(stunResponse, pos + 4, ipBytes, 0, 16);
                                // XOR mapping for IPv6 would require transaction ID XOR, keep simple
                            }
                            else
                            {
                                break;
                            }

                            var extIp = new IPAddress(ipBytes);
                            var resolvedEp = new IPEndPoint(extIp, extPort);
                            AddStunLog($"[STUN] MAPPED-ADDRESS özniteliği çözümlendi: {resolvedEp}");
                            return resolvedEp;
                        }
                        pos += length;
                    }
                    AddStunLog("[STUN] Uyarı: STUN yanıtında geçerli bir MAPPED-ADDRESS özniteliği bulunamadı.");
                }
                catch (Exception ex)
                {
                    AddStunLog($"[STUN] Hata: İstisna oluştu veya soket zaman aşımına uğradı. Detay: {ex.Message}");
                }
            }
            return null;
        }

        public async Task<bool> CheckDirectAccessAsync(int localPort)
        {
            DirectLogs.Clear();
            AddDirectLog("Doğrudan dış bağlantı testi başlatıldı.");
            AddDirectLog($"Yerel port: {localPort}");
            UpdateDots(1, StunDotState, TunnelDotState);
            OnStatusMessage?.Invoke("Doğrudan dış bağlantı testi yapılıyor (IPv4 & IPv6)...");
            
            // Try to resolve our external IP via public service
            string externalIp = null;
            AddDirectLog("Dış IP adresi çözümlenmeye çalışılıyor (https://api.ipify.org)...");
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) })
                {
                    externalIp = (await http.GetStringAsync("https://api.ipify.org")).Trim();
                    AddDirectLog($"api.ipify.org başarılı. Çözümlenen Dış IP: {externalIp}");
                }
            }
            catch (Exception ex)
            {
                AddDirectLog($"api.ipify.org hatası: {ex.Message}");
                AddDirectLog("Alternatif servis deneniyor (https://icanhazip.com)...");
                try
                {
                    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) })
                    {
                        externalIp = (await http.GetStringAsync("https://icanhazip.com")).Trim();
                        AddDirectLog($"icanhazip.com başarılı. Çözümlenen Dış IP: {externalIp}");
                    }
                }
                catch (Exception ex2)
                {
                    AddDirectLog($"icanhazip.com hatası: {ex2.Message}");
                }
            }

            if (string.IsNullOrEmpty(externalIp))
            {
                AddDirectLog("Hata: Dış IP adresi hiçbir servisten çözümlenemedi. İnternet bağlantınızı veya DNS ayarlarınızı kontrol edin.");
                OnStatusMessage?.Invoke("Dış IP adresi çözümlenemedi.");
                UpdateDots(0, StunDotState, TunnelDotState);
                return false;
            }

            // Test TCP connection to ourselves using external address
            AddDirectLog($"Dış IP ve Port üzerinden yerel TCP soketine geri bağlantı (Loopback) test ediliyor -> {externalIp}:{localPort}");
            try
            {
                using (var tcp = new TcpClient())
                {
                    var connectTask = tcp.ConnectAsync(externalIp, localPort);
                    if (await Task.WhenAny(connectTask, Task.Delay(2000)) == connectTask)
                    {
                        await connectTask; // throw exception if failed
                        ExternalAddress = $"{externalIp}:{localPort}";
                        ActiveMode = ConnectionMode.Direct;
                        AddDirectLog($"TCP Geri Bağlantısı BAŞARILI! Dış dünyadan IP:Port ({ExternalAddress}) adresinize doğrudan erişim sağlanabiliyor.");
                        OnStatusMessage?.Invoke($"Doğrudan bağlantı BAŞARILI: {ExternalAddress}");
                        UpdateDots(2, StunDotState, TunnelDotState);
                        return true;
                    }
                    else
                    {
                        AddDirectLog("Hata: TCP bağlantı isteği 2000ms zaman aşımına uğradı. Port dışarıya kapalı.");
                    }
                }
            }
            catch (Exception ex)
            {
                AddDirectLog($"Hata: TCP bağlantısı kurulamadı. Detay: {ex.Message}");
            }

            AddDirectLog("Olası Nedenler:");
            AddDirectLog("1. Modem/Router ayarlarınızda Port Yönlendirme (Port Forwarding) etkinleştirilmemiş.");
            AddDirectLog("2. Windows Güvenlik Duvarı veya bir Antivirüs programı bu portu (TCP/UDP) engelliyor.");
            AddDirectLog("3. İnternet Servis Sağlayıcınız (ISS) sizi CGN-NAT (Ortak IP) havuzuna dahil etmiş (Hole punching veya tünel gerekir).");
            AddDirectLog("Sonuç: Doğrudan dış bağlantı başarısız. P2P STUN veya TURN tüneli katmanına geçiliyor.");

            OnStatusMessage?.Invoke("Doğrudan dış bağlantı başarısız. NAT delme veya tünel gereklidir.");
            UpdateDots(0, StunDotState, TunnelDotState);
            return false;
        }

        public async Task<bool> StartPlayitTunnelAsync(int localPort)
        {
            await Task.Yield();
            if (_isTunnelRunning) return true;

            AddTurnLog("Playit tüneli başlatma isteği alındı.");
            UpdateDots(DirectDotState, StunDotState, 1);
            OnStatusMessage?.Invoke("Playit.gg Tünel başlatılıyor...");

            // Step 1: Ensure local binary is present
            string localFileName = Environment.OSVersion.Platform == PlatformID.Win32NT ? "playit-windows-x86_64-signed.exe" : "playit-linux-amd64";
            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "evn", localFileName);

            AddTurnLog($"Yerel tünel istemci dosyası aranıyor: {localPath}");
            if (!File.Exists(localPath))
            {
                localPath = Path.Combine(Environment.CurrentDirectory, "evn", localFileName);
            }

            if (!File.Exists(localPath))
            {
                LogService.Log("Local playit binary not found.");
                AddTurnLog("Hata: Yerel playit.gg tünel dosyası bulunamadı. Tünel başlatılamıyor.");
                OnStatusMessage?.Invoke("Yerel playit dosyası bulunamadı — tünel geçildi.");
                UpdateDots(DirectDotState, StunDotState, 0);
                return false;
            }

            AddTurnLog($"Yerel tünel dosyası bulundu: {localPath}. Çalıştırılıyor...");

            // Step 2: Start playit agent process from local path
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = localPath,
                    Arguments = "",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(localPath)
                };

                _playitProcess = new Process { StartInfo = startInfo };
                _playitProcess.OutputDataReceived += (s, e) => ProcessPlayitLog(e.Data);
                _playitProcess.ErrorDataReceived += (s, e) => ProcessPlayitLog(e.Data);

                _playitProcess.Start();
                _playitProcess.BeginOutputReadLine();
                _playitProcess.BeginErrorReadLine();

                _isTunnelRunning = true;
                ActiveMode = ConnectionMode.PlayitTunnel;
                OnTunnelStateChanged?.Invoke(true, "Aktif");

                AddTurnLog("Playit.gg arka plan tünel süreci başarıyla başlatıldı. Sinyalleşme ve tünel adresi bekleniyor...");
                OnStatusMessage?.Invoke("Tünel aktif edildi. Bağlantı adresi çözümleniyor...");
                
                // Monitor output for external address or claim URL
                _ = Task.Run(async () =>
                {
                    for (int i = 0; i < 20; i++)
                    {
                        if (!_isTunnelRunning) break;
                        await Task.Delay(1000);
                        if (!string.IsNullOrEmpty(ExternalAddress)) break;
                    }
                    if (string.IsNullOrEmpty(ExternalAddress))
                    {
                        AddTurnLog("Uyarı: Tünel süreci çalışıyor ancak dış tünel adresi henüz çözümlenemedi (Zaman Aşımı).");
                        OnStatusMessage?.Invoke("Tünel kuruldu ancak bağlantı adresi henüz alınamadı.");
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                AddTurnLog($"Hata: Playit.gg tüneli çalıştırılırken istisna oluştu. Detay: {ex.Message}");
                OnStatusMessage?.Invoke($"Tünel başlatılamadı: {ex.Message}");
                LogService.LogError("Failed to start playit tunnel", ex);
                _isTunnelRunning = false;
                UpdateDots(DirectDotState, StunDotState, 0);
                return false;
            }
        }

        private void ProcessPlayitLog(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            LogService.Log($"[Playit] {line}");
            AddTurnLog($"[Playit Log] {line}");

            // Look for playit external address e.g. "assigned address: 123.45.67.89:1234" or "xxx.playit.gg:1234" or "Tunnel ready: xxx.localto.net:1234"
            var addressMatch = Regex.Match(line, @"(assigned address|tunnel address|address is|tunnel ready:?)\s+([a-zA-Z0-9\.\-]+:\d+)", RegexOptions.IgnoreCase);
            if (addressMatch.Success)
            {
                ExternalAddress = addressMatch.Groups[2].Value;
                AddTurnLog($"BAŞARILI: Tünel kuruldu! Dış Erişim Adresiniz: {ExternalAddress}");
                OnStatusMessage?.Invoke($"Tünel Bağlantı Adresiniz: {ExternalAddress}");
                UpdateDots(DirectDotState, StunDotState, 2);
            }

            // Look for claim link if playit needs setup
            var claimMatch = Regex.Match(line, @"(https://playit\.gg/claim/[a-zA-Z0-9\-]+)", RegexOptions.IgnoreCase);
            if (claimMatch.Success)
            {
                PlayitClaimUrl = claimMatch.Groups[1].Value;
                AddTurnLog($"Kurulum Gerekli: Tüneli eşleştirmek için şu linke tıklamalısınız: {PlayitClaimUrl}");
                OnStatusMessage?.Invoke($"Lütfen tünelinizi eşleştirin: {PlayitClaimUrl}");
            }
        }

        public void StopPlayitTunnel()
        {
            if (!_isTunnelRunning) return;

            AddTurnLog("Tünel kapatılıyor...");
            OnStatusMessage?.Invoke("Tünel durduruluyor...");
            try
            {
                if (_playitProcess != null && !_playitProcess.HasExited)
                {
                    _playitProcess.Kill();
                }
                AddTurnLog("Tünel süreci sonlandırıldı.");
            }
            catch (Exception ex)
            {
                AddTurnLog($"Tünel süreci sonlandırılırken hata: {ex.Message}");
            }

            _isTunnelRunning = false;
            ExternalAddress = null;
            PlayitClaimUrl = null;
            ActiveMode = ConnectionMode.Direct;
            
            OnTunnelStateChanged?.Invoke(false, "Kapalı");
            OnStatusMessage?.Invoke("Tünel kapatıldı.");
            UpdateDots(DirectDotState, StunDotState, 0);
        }

        public async Task<string> EstablishBestConnectionAsync(int localPort)
        {
            // 1. Step: Try direct connection first
            bool directOk = await CheckDirectAccessAsync(localPort);
            if (directOk)
            {
                ActiveMode = ConnectionMode.Direct;
                return ExternalAddress;
            }

            // 2. Step: Query STUN to detect NAT.
            var nat = await DetectNatTypeAsync();
            if (nat == NatType.ConeNAT)
            {
                ActiveMode = ConnectionMode.StunP2P;
                OnStatusMessage?.Invoke("STUN/P2P Modu aktif edildi (Cone NAT tespit edildi). Bağlantılar doğrudan kurulabilir.");
                return "STUN_P2P";
            }

            // 3. Step: Start the modern WebRTC P2P & TURN (Metered TURN) connection (Symmetric NAT or STUN failed)
            OnStatusMessage?.Invoke("Cone NAT tespit edilemedi veya STUN başarısız oldu. WebRTC P2P & TURN (Metered) tüneli başlatılıyor...");
            bool p2pStarted = await StunService.Instance.StartP2PSync();
            if (p2pStarted)
            {
                ActiveMode = ConnectionMode.StunP2P;
                OnStatusMessage?.Invoke("Metered TURN & P2P Tüneli başarıyla kuruldu.");
                return "STUN_P2P_TURN_ACTIVE";
            }

            OnStatusMessage?.Invoke("Hata: Hiçbir bağlantı katmanı kurulamadı!");
            return null;
        }
    }
}
