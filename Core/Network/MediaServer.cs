using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media;
using StreamMesh.Core.Plugins;
using StreamMesh.Core.Utils;
using StreamMesh.Models;

namespace StreamMesh.Core.Network
{
    public class MediaServer
    {
        private HttpListener? _listener;
        private readonly DatabaseEngine _db = DatabaseEngine.Instance;
        private bool _isRunning = false;
        private int _port = 8080;

        // Shared HttpClient for proxying and smart routing to prevent socket exhaustion
        private static readonly HttpClient _proxyClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = true,
            MaxConnectionsPerServer = 100
        }) { Timeout = TimeSpan.FromSeconds(30) };

        // "Last Known Good" source tracker for multi-source channels
        private static readonly ConcurrentDictionary<string, int> _lastKnownGoodIndex = new(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Text.RegularExpressions.Regex UriAttrRegex = new("URI=\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static async Task WriteTextAsync(HttpListenerResponse res, string content, string contentType = "text/plain; charset=utf-8", int statusCode = 200)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(content);
            res.StatusCode = statusCode;
            res.ContentType = contentType;
            res.ContentLength64 = buffer.Length;
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private static Task WriteJsonAsync(HttpListenerResponse res, object payload, int statusCode = 200, Newtonsoft.Json.Formatting formatting = Newtonsoft.Json.Formatting.None)
        {
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload, formatting);
            return WriteTextAsync(res, json, "application/json; charset=utf-8", statusCode);
        }

        public MediaServer(int port = 0)
        {
            if (port <= 0)
            {
                try
                {
                    string savedPort = _db.GetSetting("ServerPort", "8088");
                    port = int.TryParse(savedPort, out int p) ? p : 8088;
                }
                catch
                {
                    port = 8088;
                }
            }
            _port = port;
        }

        public int Port => _port;
        public bool IsRunning => _isRunning;
        public bool IsLanAccessible { get; private set; } = false;
        public string BindingMode { get; private set; } = "Kapalı";

        private static string _cachedPrimaryIp = "127.0.0.1";
        private static DateTime _lastIpCheckUtc = DateTime.MinValue;

        public static List<string> GetLocalIPv4Addresses()
        {
            var list = new List<string>();
            try
            {
                // 1. Prioritize active physical Ethernet and Wi-Fi adapters with a Default Gateway
                var activeInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                 ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .OrderByDescending(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet || ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    .ThenByDescending(ni => ni.GetIPProperties().GatewayAddresses.Count > 0)
                    .ToList();

                foreach (var ni in activeInterfaces)
                {
                    var props = ni.GetIPProperties();
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr.Address))
                        {
                            string ipStr = addr.Address.ToString();
                            if (!list.Contains(ipStr))
                            {
                                list.Add(ipStr);
                            }
                        }
                    }
                }

                // 2. Non-blocking routing fallback if interface scan yielded nothing
                if (list.Count == 0)
                {
                    try
                    {
                        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                        socket.SendTimeout = 500;
                        socket.ReceiveTimeout = 500;
                        socket.Connect("8.8.8.8", 65530);
                        if (socket.LocalEndPoint is IPEndPoint endPoint && !IPAddress.IsLoopback(endPoint.Address))
                        {
                            string ipStr = endPoint.Address.ToString();
                            if (!list.Contains(ipStr)) list.Add(ipStr);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"GetLocalIPv4Addresses error: {ex.Message}");
            }
            return list;
        }

        public static string GetPrimaryLocalIPv4Address()
        {
            if ((DateTime.UtcNow - _lastIpCheckUtc).TotalSeconds < 30 && !string.IsNullOrEmpty(_cachedPrimaryIp))
            {
                return _cachedPrimaryIp;
            }

            var ips = GetLocalIPv4Addresses();
            _cachedPrimaryIp = ips.FirstOrDefault() ?? "127.0.0.1";
            _lastIpCheckUtc = DateTime.UtcNow;
            return _cachedPrimaryIp;
        }

        public bool Start()
        {
            if (_isRunning) return true;

            int attempts = 0;
            int basePort = _port;
            int maxAttempts = 5;

            var localIps = GetLocalIPv4Addresses();
            bool tryLanBinding = localIps.Count > 0;

            while (attempts < maxAttempts)
            {
                try
                {
                    if (_listener != null)
                    {
                        try { _listener.Close(); } catch { }
                    }

                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://localhost:{_port}/");
                    _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                    LogService.LogInfo($"MediaServer: Registered localhost & 127.0.0.1 prefixes on port {_port}.");

                    // 1. Try binding Wildcard prefix so ANY incoming IP/host (including LAN, direct IP, mDNS) succeeds
                    bool wildcardAdded = false;
                    try
                    {
                        _listener.Prefixes.Add($"http://+:{_port}/");
                        wildcardAdded = true;
                        LogService.LogInfo($"MediaServer: Registered plus-wildcard prefix http://+:{_port}/");
                    }
                    catch
                    {
                        try
                        {
                            _listener.Prefixes.Add($"http://*:{_port}/");
                            wildcardAdded = true;
                            LogService.LogInfo($"MediaServer: Registered wildcard prefix http://*:{_port}/");
                        }
                        catch { }
                    }

                    // 2. Also register specific detected LAN IPs if wildcard wasn't accepted
                    if (!wildcardAdded && tryLanBinding)
                    {
                        foreach (var ip in localIps)
                        {
                            try
                            {
                                _listener.Prefixes.Add($"http://{ip}:{_port}/");
                                LogService.LogInfo($"MediaServer: Registered LAN IP prefix: http://{ip}:{_port}/");
                            }
                            catch { }
                        }
                    }

                    _listener.Start();
                    _isRunning = true;
                    IsLanAccessible = true;
                    BindingMode = wildcardAdded ? "Tüm Ağ Cihazları (Wildcard + LAN)" : "Ağ Yerel IP Dinleyici";
                    Task.Run(ListenLoop);
                    LogService.LogInfo($"MediaServer: Started successfully on Port: {_port} [Bound to: localhost, 127.0.0.1{(wildcardAdded ? ", +" : (tryLanBinding ? $", {string.Join(", ", localIps)}" : ""))}]");

                    // Background asynchronous ACL and firewall registration so startup is instantaneous
                    int boundPort = _port;
                    Task.Run(() =>
                    {
                        TryRegisterUrlAcl(boundPort);
                        TryRegisterFirewallRule(boundPort);
                    });

                    return true;
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 5 || ex.NativeErrorCode == 5)
                {
                    // Access Denied (Win32 Error 5 / ERROR_ACCESS_DENIED) -> URL ACL restriction for wildcard / LAN IP
                    LogService.LogWarning($"MediaServer: Wildcard/LAN prefix binding hit Windows HTTP.sys URL ACL restrictions on port {_port}. Falling back to localhost/127.0.0.1.");

                    // Localhost fallback
                    try
                    {
                        if (_listener != null)
                        {
                            try { _listener.Close(); } catch { }
                        }

                        _listener = new HttpListener();
                        _listener.Prefixes.Add($"http://localhost:{_port}/");
                        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");

                        _listener.Start();
                        _isRunning = true;
                        IsLanAccessible = false;
                        BindingMode = "Sadece Bu Bilgisayar (Localhost - Güvenlik Duvarı Engelli)";
                        Task.Run(ListenLoop);
                        LogService.LogInfo($"MediaServer: Started in fallback mode on Port: {_port} [Bound to: localhost, 127.0.0.1].");
                        return true;
                    }
                    catch (HttpListenerException fallbackEx) when (fallbackEx.ErrorCode == 183 || fallbackEx.ErrorCode == 32 || fallbackEx.ErrorCode == 48 || fallbackEx.NativeErrorCode == 183 || fallbackEx.NativeErrorCode == 32 || fallbackEx.NativeErrorCode == 48)
                    {
                        attempts++;
                        _port = basePort + attempts;
                        LogService.LogWarning($"MediaServer: Port in use, retrying on port {_port} (attempt {attempts}/{maxAttempts})...");
                    }
                    catch (Exception fallbackEx)
                    {
                        LogService.LogError($"MediaServer Start Localhost Fallback Error (Win32/Ex: {fallbackEx.Message})", fallbackEx);
                        return false;
                    }
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 183 || ex.ErrorCode == 32 || ex.ErrorCode == 48 || ex.NativeErrorCode == 183 || ex.NativeErrorCode == 32 || ex.NativeErrorCode == 48)
                {
                    attempts++;
                    _port = basePort + attempts;
                    LogService.LogWarning($"MediaServer: Port in use, retrying on port {_port} (attempt {attempts}/{maxAttempts})...");
                }
                catch (Exception ex)
                {
                    LogService.LogError($"MediaServer Start Unexpected Error: {ex.Message}", ex);
                    return false;
                }
            }
            return false;
        }

        private static void TryRegisterUrlAcl(int port)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"http add urlacl url=http://+:{port}/ sddl=\"D:(A;;GX;;;WD)\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(500);
                }
            }
            catch { }
        }

        private static void TryRegisterFirewallRule(int port)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"advfirewall firewall add rule name=\"StreamMesh Media Server (Port {port})\" dir=in action=allow protocol=TCP localport={port}",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(500);
                }
            }
            catch { }
        }

        public static bool RepairNetworkAclAndFirewall(int port)
        {
            try
            {
                if (!OperatingSystem.IsWindows()) return true;

                LogService.LogInfo($"MediaServer: Executing elevated network ACL and firewall repair for Port {port}...");

                // Run elevated netsh commands to open Firewall TCP Port & HTTP.sys URL ACL
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c netsh http add urlacl url=http://+:{port}/ sddl=\"D:(A;;GX;;;WD)\" & netsh advfirewall firewall add rule name=\"StreamMesh Media Server (Port {port})\" dir=in action=allow protocol=TCP localport={port}",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(4000);
                return true;
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"MediaServer RepairNetworkAclAndFirewall warning: {ex.Message}");
                return false;
            }
        }

        public void Stop()
        {
            try
            {
                _isRunning = false;
                if (_listener != null && _listener.IsListening)
                {
                    _listener.Stop();
                    _listener.Close();
                }
            }
            catch { }
        }

        private async Task ListenLoop()
        {
            while (_isRunning && _listener != null && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context));
                }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) { if (!_isRunning) break; }
                catch { }
            }
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            var req = context.Request;
            var res = context.Response;
            string rawPath = req.Url?.AbsolutePath ?? "/";
            string path = rawPath.ToLowerInvariant();

            // Enable CORS for web players without duplicates
            res.Headers["Access-Control-Allow-Origin"] = "*";
            res.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS, HEAD";
            res.Headers["Access-Control-Allow-Headers"] = "*";
            res.Headers["Access-Control-Expose-Headers"] = "*";

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 204;
                res.Close();
                return;
            }

            LogService.LogInfo($"MediaServer: Request {req.HttpMethod} {rawPath} from {req.RemoteEndPoint}");

            try
            {
                // Smart Router: /stream/{channelId}
                if (path.StartsWith("/stream/"))
                {
                    string channelId = rawPath.Substring(8).Trim('/');
                    await HandleSmartRouterStreamAsync(channelId, req, res);
                    return;
                }
                else if (path == "/desc.xml")
                {
                    await ServeDeviceDescription(res);
                }
                else if (path == "/playlist.m3u" || path == "/api/playlist.m3u" || path == "/direct.m3u" || path == "/api/direct.m3u")
                {
                    bool isDirect = path.Contains("direct") || (req.QueryString["direct"] == "1" || string.Equals(req.QueryString["direct"], "true", StringComparison.OrdinalIgnoreCase));
                    await ServeM3u(req, res, isDirect);
                }
                else if (path == "/web" || path == "/web/" || path == "/")
                {
                    await ServeHtmlPlayer(req, res);
                }
                else if (path == "/channels" || path == "/api/channels")
                {
                    await ServeChannelsJson(res);
                }
                else if (path == "/proxy")
                {
                    await ServeProxyStream(req, res);
                }
                else if (path == "/ping" || path == "/api/ping")
                {
                    await WriteTextAsync(res, "pong", "text/plain");
                }
                else if (path == "/api/version")
                {
                    await ServeVersionJson(res);
                }
                else if (path == "/logs")
                {
                    await ServeLogs(req, res);
                }
                else if (path == "/debug")
                {
                    await ServeDebugInfo(res);
                }
                else if (path == "/api/play")
                {
                    await ServeApiPlay(req, res);
                }
                else if (path == "/api/epg" || path == "/api/epg/query")
                {
                    await ServeApiEpgQuery(req, res);
                }
                else if (path == "/api/epg/batch")
                {
                    await ServeApiEpgBatch(req, res);
                }
                else if (path == "/api/epg/guide")
                {
                    await ServeApiEpgGuide(req, res);
                }
                else if (path == "/api/yt/resolve")
                {
                    await ServeApiYtResolve(req, res);
                }
                else if (path == "/api/logos/find")
                {
                    await ServeApiLogosFind(req, res);
                }
                else if (path == "/api/channels/search")
                {
                    await ServeApiChannelsSearch(req, res);
                }
                else if (path == "/api/logs/errors")
                {
                    await ServeApiLogsErrors(res);
                }
                else if (path == "/api/m3u/sources")
                {
                    await ServeApiM3uSources(res);
                }
                else if (path == "/api/plugins" && req.HttpMethod == "GET")
                {
                    await ServePluginsList(res);
                }
                else if (path == "/api/plugins/toggle" && req.HttpMethod == "POST")
                {
                    await HandlePluginToggle(req, res);
                }
                else if (path == "/api/plugins/settings" && req.HttpMethod == "POST")
                {
                    await HandlePluginSettings(req, res);
                }
                else if (path == "/api/plugins/action" && req.HttpMethod == "POST")
                {
                    await HandlePluginAction(req, res);
                }
                else if (await TryServeStaticAsset(rawPath, res))
                {
                    return;
                }
                else
                {
                    res.StatusCode = 404;
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"MediaServer: Handler exception on {path}", ex);
                res.StatusCode = 500;
            }
            finally
            {
                try { res.Close(); } catch { }
            }
        }

        /// <summary>
        /// Smart Stream Router: Dispatches stream playback requests based on SourceType:
        /// - YOUTUBE: Resolved stream URL via YoutubeEngine + HLS/MPEG-TS forwarding
        /// - MULTI-SOURCE / PROTECTED / M3U: Last Known Good failover + Custom Header injection
        /// </summary>
        private async Task HandleSmartRouterStreamAsync(string channelId, HttpListenerRequest req, HttpListenerResponse res)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                res.StatusCode = 400;
                return;
            }

            var ch = await _db.GetChannelByIdAsync(channelId);

            if (ch == null)
            {
                LogService.LogWarning($"SmartRouter: Channel '{channelId}' not found.");
                res.StatusCode = 404;
                return;
            }

            LogService.LogInfo($"SmartRouter: Routing channel '{ch.PrimaryName}' (Type: {ch.SourceType}, Sources: {ch.SourcesCount})");

            using var cts = new CancellationTokenSource();

            // Unified routing via StreamResolverRegistry
            try
            {
                string resolvedUrl = await StreamResolverRegistry.Instance.ResolveStreamAsync(ch.Url, ch, cts.Token);
                if (!string.IsNullOrEmpty(resolvedUrl))
                {
                    if (resolvedUrl.Contains(":6878/ace/"))
                    {
                        var ace = new AceEngine();
                        AceStreamService.Instance.RegisterSession(ace.ExtractAceId(ch.Url));
                    }

                    res.Redirect(resolvedUrl);
                    res.StatusCode = (int)HttpStatusCode.Redirect;
                    return;
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"SmartRouter: Route error for {ch.PrimaryName}", ex);
            }

            // 3. MULTI-SOURCE & DIRECT IPTV ROUTE (With Failover & Custom Headers)
            var urls = ch.GetUrlList();
            if (urls.Count == 0 && !string.IsNullOrEmpty(ch.Url)) urls.Add(ch.Url);

            if (urls.Count == 0)
            {
                res.StatusCode = 404;
                return;
            }

            // Determine start index using "Last Known Good"
            int startIndex = 0;
            if (_lastKnownGoodIndex.TryGetValue(ch.Id, out int lkg) && lkg >= 0 && lkg < urls.Count)
            {
                startIndex = lkg;
            }

            bool streamEstablished = false;

            for (int i = 0; i < urls.Count; i++)
            {
                int currentIndex = (startIndex + i) % urls.Count;
                string targetUrl = urls[currentIndex];

                try
                {
                    LogService.LogInfo($"SmartRouter: Trying source [{currentIndex + 1}/{urls.Count}]: {targetUrl}");

                    // Extract pipe headers from URL if present
                    var (cleanUrl, pipeHeaders) = HlsProxyEngine.ExtractHeadersFromUrl(targetUrl);

                    var request = new HttpRequestMessage(HttpMethod.Get, cleanUrl);
                    ApplyHeadersToRequest(request, ch, pipeHeaders);

                    // Forward client Range header if requested (essential for HTML5 video seeking & VOD MP4/MKV)
                    if (!string.IsNullOrEmpty(req.Headers["Range"]))
                    {
                        request.Headers.TryAddWithoutValidation("Range", req.Headers["Range"]);
                    }

                    var response = await _proxyClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        _lastKnownGoodIndex[ch.Id] = currentIndex;
                        await RelayHttpResponseAsync(response, cleanUrl, req, res, cts.Token);
                        streamEstablished = true;
                        LogService.LogInfo($"SmartRouter: Stream active from source [{currentIndex + 1}] for '{ch.PrimaryName}'");
                        break;
                    }
                    else
                    {
                        LogService.LogWarning($"SmartRouter: Source [{currentIndex + 1}] returned HTTP {response.StatusCode}, attempting failover...");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogService.LogWarning($"SmartRouter: Source [{currentIndex + 1}] failed ({ex.Message}), failover to next source...");
                }
            }

            if (!streamEstablished)
            {
                // Intelligent fallback: check if VodSmartResolver can resolve an alternative CDN URL
                string primaryTarget = urls.Count > 0 ? urls[0] : ch.Url;
                var (primaryClean, _) = HlsProxyEngine.ExtractHeadersFromUrl(primaryTarget);

                if (VodSmartResolver.IsVodCdnUrl(primaryClean))
                {
                    try
                    {
                        string? workingVod = await VodSmartResolver.FindWorkingVodAlternativeAsync(primaryClean, cts.Token);
                        if (!string.IsNullOrEmpty(workingVod))
                        {
                            LogService.LogInfo($"SmartRouter: VodSmartResolver redirected to working alternative: {workingVod}");
                            res.StatusCode = (int)HttpStatusCode.Redirect;
                            res.Redirect(workingVod);
                            return;
                        }
                    }
                    catch { }
                }

                // If proxy failed due to CDN/token/ip restrictions, redirect the client browser directly to the stream source
                if (!string.IsNullOrWhiteSpace(primaryClean))
                {
                    LogService.LogInfo($"SmartRouter: Proxy unable to stream, redirecting browser directly (302) to: {primaryClean}");
                    res.StatusCode = (int)HttpStatusCode.Redirect;
                    res.Redirect(primaryClean);
                    return;
                }

                res.StatusCode = 502;
            }
        }

        private string RewriteHlsManifest(string manifestText, string baseUrl, string host)
        {
            var lines = manifestText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var rewritten = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();

                if (string.IsNullOrEmpty(trimmed))
                {
                    rewritten.Add(line);
                    continue;
                }

                if (trimmed.StartsWith("#EXT-X-KEY") || trimmed.StartsWith("#EXT-X-MAP") || trimmed.StartsWith("#EXT-X-MEDIA"))
                {
                    line = UriAttrRegex.Replace(line, match =>
                    {
                        try
                        {
                            string uri = match.Groups[1].Value;
                            var abs = new Uri(new Uri(baseUrl), uri).ToString();
                            return $"URI=\"http://{host}/proxy?url={Uri.EscapeDataString(abs)}\"";
                        }
                        catch
                        {
                            return match.Value;
                        }
                    });
                    rewritten.Add(line);
                    continue;
                }

                if (trimmed.StartsWith("#"))
                {
                    rewritten.Add(line);
                    continue;
                }

                try
                {
                    var absUrl = new Uri(new Uri(baseUrl), trimmed).ToString();
                    rewritten.Add($"http://{host}/proxy?url={Uri.EscapeDataString(absUrl)}");
                }
                catch
                {
                    rewritten.Add(line);
                }
            }

            return string.Join("\n", rewritten);
        }

        private async Task RelayHttpResponseAsync(HttpResponseMessage response, string targetUrl, HttpListenerRequest req, HttpListenerResponse res, CancellationToken ct)
        {
            string host = req.Headers["Host"] ?? $"127.0.0.1:{_port}";
            string contentType = response.Content.Headers.ContentType?.ToString() ?? "";
            bool isM3u8 = contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) ||
                          contentType.Contains("application/x-mpegurl", StringComparison.OrdinalIgnoreCase) ||
                          targetUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase);

            if (isM3u8)
            {
                string rawManifest = await response.Content.ReadAsStringAsync(ct);
                string rewritten = RewriteHlsManifest(rawManifest, targetUrl, host);
                byte[] manifestBytes = Encoding.UTF8.GetBytes(rewritten);
                res.ContentType = "application/vnd.apple.mpegurl; charset=utf-8";
                res.StatusCode = (int)response.StatusCode;
                res.ContentLength64 = manifestBytes.Length;
                await res.OutputStream.WriteAsync(manifestBytes, 0, manifestBytes.Length, ct);
                return;
            }

            res.ContentType = !string.IsNullOrEmpty(contentType) ? contentType :
                              targetUrl.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? "video/mp4" :
                              targetUrl.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ? "video/x-matroska" : "video/mp2t";
            res.StatusCode = (int)response.StatusCode;
            res.Headers.Add("Accept-Ranges", "bytes");

            if (response.Content.Headers.ContentRange != null)
            {
                res.Headers.Add("Content-Range", response.Content.Headers.ContentRange.ToString());
            }

            if (response.Content.Headers.ContentLength.HasValue)
            {
                res.ContentLength64 = response.Content.Headers.ContentLength.Value;
                res.SendChunked = false;
            }
            else
            {
                res.SendChunked = true;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            await stream.CopyToAsync(res.OutputStream, ct);
        }

        private async Task ProxyDirectUrlAsync(string targetUrl, HttpListenerRequest req, HttpListenerResponse res)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
                string ua = req.Headers["User-Agent"] ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";
                request.Headers.TryAddWithoutValidation("User-Agent", ua);
                if (!string.IsNullOrEmpty(req.Headers["Range"]))
                {
                    request.Headers.TryAddWithoutValidation("Range", req.Headers["Range"]);
                }

                using var response = await _proxyClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                await RelayHttpResponseAsync(response, targetUrl, req, res, cts.Token);
            }
            catch (Exception ex)
            {
                LogService.LogError($"ProxyDirectUrlAsync failed for {targetUrl}", ex);
                res.StatusCode = 502;
            }
        }

        private void ApplyHeadersToRequest(HttpRequestMessage request, Channel ch, Dictionary<string, string> pipeHeaders)
        {
            // User-Agent
            string ua = !string.IsNullOrWhiteSpace(ch.HttpUserAgent) ? ch.HttpUserAgent :
                        pipeHeaders.TryGetValue("User-Agent", out var pUa) ? pUa :
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";
            request.Headers.Remove("User-Agent");
            request.Headers.TryAddWithoutValidation("User-Agent", ua);

            // Referer
            string? referer = !string.IsNullOrWhiteSpace(ch.HttpReferer) ? ch.HttpReferer :
                              pipeHeaders.TryGetValue("Referer", out var pRef) ? pRef : null;
            if (!string.IsNullOrWhiteSpace(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refUri))
            {
                request.Headers.Referrer = refUri;
            }

            // Cookie
            string? cookie = !string.IsNullOrWhiteSpace(ch.HttpCookie) ? ch.HttpCookie :
                             pipeHeaders.TryGetValue("Cookie", out var pCk) ? pCk : null;
            if (!string.IsNullOrWhiteSpace(cookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            }

            // Origin
            string? origin = !string.IsNullOrWhiteSpace(ch.HttpOrigin) ? ch.HttpOrigin :
                             pipeHeaders.TryGetValue("Origin", out var pOg) ? pOg : null;
            if (!string.IsNullOrWhiteSpace(origin))
            {
                request.Headers.TryAddWithoutValidation("Origin", origin);
            }

            // Custom Headers
            if (ch.CustomHeaders != null)
            {
                foreach (var kv in ch.CustomHeaders)
                {
                    request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }
            foreach (var kv in pipeHeaders)
            {
                request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            }
        }

        private async Task ServeM3u(HttpListenerRequest req, HttpListenerResponse res, bool isDirect = false)
        {
            var channels = await _db.GetAllChannelsAsync();
            string? reqHost = req.Headers["Host"];
            string lanIp = GetPrimaryLocalIPv4Address();
            string host = !string.IsNullOrWhiteSpace(reqHost) ? reqHost : $"{lanIp}:{_port}";

            var sb = new StringBuilder();
            string playlistName = isDirect ? "StreamMesh Direct Playlist" : "StreamMesh Smart Router Playlist";
            sb.AppendLine($"#EXTM3U name=\"{playlistName}\"");

            foreach (var ch in channels)
            {
                bool isYt = string.Equals(ch.SourceType, "YOUTUBE", StringComparison.OrdinalIgnoreCase) || ch.Url.Contains("youtube.com") || ch.Url.Contains("youtu.be");
                bool isMulti = ch.SourcesCount > 1;

                string groupTitle = ch.GroupTitle ?? "Genel";
                if (!isDirect)
                {
                    string groupSuffix = isYt ? " [StreamMesh YouTube]" :
                                         isMulti ? " [StreamMesh Smart Router]" :
                                         " [Doğrudan IPTV]";
                    groupTitle = $"{groupTitle}{groupSuffix}";
                }

                string streammeshRequired = (isYt || isMulti) ? "true" : "false";
                string streamType = isYt ? "YOUTUBE" : (isMulti ? "MULTI_SOURCE" : "DIRECT");

                // Playback URL: Direct internet URL or Universal Smart Router Endpoint
                string playbackUrl;
                if (isDirect)
                {
                    playbackUrl = ch.GetOrderedUrlList().FirstOrDefault() ?? ch.GetUrlList().FirstOrDefault() ?? ch.Url;
                    if (string.IsNullOrWhiteSpace(playbackUrl))
                    {
                        playbackUrl = $"http://{host}/stream/{ch.Id}";
                    }
                }
                else
                {
                    playbackUrl = $"http://{host}/stream/{ch.Id}";
                }

                sb.AppendLine($"#EXTINF:-1 tvg-id=\"{ch.EpgId}\" tvg-name=\"{ch.PrimaryName}\" tvg-logo=\"{ch.PrimaryLogoUrl}\" group-title=\"{groupTitle}\" streammesh-required=\"{streammeshRequired}\" streammesh-type=\"{streamType}\",{ch.PrimaryName}");
                sb.AppendLine(playbackUrl);
            }

            byte[] buffer = Encoding.UTF8.GetBytes(sb.ToString());
            res.ContentType = "application/x-mpegurl; charset=utf-8";
            string filename = isDirect ? "StreamMesh-Direct.m3u" : "StreamMesh.m3u";
            res.Headers.Add("Content-Disposition", $"attachment; filename=\"{filename}\"");
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task ServeChannelsJson(HttpListenerResponse res)
        {
            var channels = await _db.GetAllChannelsAsync();
            var payload = channels.Select(c => new
            {
                c.Id,
                Name = c.PrimaryName,
                c.GroupTitle,
                c.Category,
                c.Language,
                c.SourceType,
                SourcesCount = c.SourcesCount,
                LogoUrl = c.PrimaryLogoUrl,
                Url = c.PrimaryUrl ?? c.Url,
                StreamUrl = $"/stream/{c.Id}",
                IsStreamMeshRequired = string.Equals(c.SourceType, "YOUTUBE", StringComparison.OrdinalIgnoreCase) ||
                                       c.SourcesCount > 1
            });

            await WriteJsonAsync(res, payload);
        }

        private static string? _cachedHtmlPath = null;

        private static string ResolveWebIndexPath()
        {
            if (_cachedHtmlPath != null && File.Exists(_cachedHtmlPath))
            {
                return _cachedHtmlPath;
            }

            var candidates = new List<string>
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web", "index.html"),
                Path.Combine(Directory.GetCurrentDirectory(), "Web", "index.html"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Web", "index.html"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "Web", "index.html"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Web", "index.html"),
                Path.Combine("Web", "index.html")
            };

            foreach (var path in candidates)
            {
                try
                {
                    string fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath))
                    {
                        _cachedHtmlPath = fullPath;
                        return fullPath;
                    }
                }
                catch { }
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web", "index.html");
        }

        private async Task ServeHtmlPlayer(HttpListenerRequest req, HttpListenerResponse res)
        {
            try
            {
                string filePath = ResolveWebIndexPath();
                if (File.Exists(filePath))
                {
                    byte[] buffer = await File.ReadAllBytesAsync(filePath);
                    res.ContentType = "text/html; charset=utf-8";
                    res.StatusCode = 200;
                    res.ContentLength64 = buffer.Length;
                    await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    return;
                }

                LogService.LogWarning($"MediaServer: Web player dosyası bulunamadı ({filePath})");
                res.StatusCode = 404;
                byte[] err = Encoding.UTF8.GetBytes("<html><body><h3>StreamMesh Web Player bulunamadı (Web/index.html).</h3></body></html>");
                res.ContentType = "text/html; charset=utf-8";
                await res.OutputStream.WriteAsync(err, 0, err.Length);
            }
            catch (Exception ex)
            {
                LogService.LogError("MediaServer: ServeHtmlPlayer error", ex);
                res.StatusCode = 500;
            }
        }

        private async Task ServeVersionJson(HttpListenerResponse res)
        {
            try
            {
                string ver = UpdateService.GetCurrentVersion();
                var verObj = new
                {
                    version = ver,
                    status = "online",
                    engine = "StreamMesh MediaServer & Smart Router",
                    features = new[] { "20-Item Paginated Unified Player", "Universal HLS Manifest Rewriter", "SmartRouter", "Zero-Crash Safe Logos" }
                };
                await WriteJsonAsync(res, verObj);
            }
            catch (Exception ex)
            {
                LogService.LogError("MediaServer: ServeVersionJson error", ex);
                res.StatusCode = 500;
            }
        }

        private async Task ServeLogs(HttpListenerRequest req, HttpListenerResponse res)
        {
            try
            {
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamMesh", "app.log");
                if (!File.Exists(logPath))
                {
                    byte[] buffer = Encoding.UTF8.GetBytes("Log dosyası henüz oluşturulmadı.");
                    await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    return;
                }

                bool full = req.QueryString["full"] == "true";
                int limit = 500;
                if (int.TryParse(req.QueryString["limit"], out int l))
                    limit = Math.Clamp(l, 1, 10000);

                res.ContentType = "text/plain; charset=utf-8";

                if (full)
                {
                    using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        await fs.CopyToAsync(res.OutputStream);
                    }
                }
                else
                {
                    string content = await ReadLastLinesAsync(logPath, limit);
                    byte[] buffer = Encoding.UTF8.GetBytes(content);
                    await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
            }
            catch (Exception ex)
            {
                byte[] buffer = Encoding.UTF8.GetBytes("Log okuma hatası: " + ex.Message);
                try { await res.OutputStream.WriteAsync(buffer, 0, buffer.Length); } catch { }
            }
        }

        private async Task<string> ReadLastLinesAsync(string path, int lineCount)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length == 0) return "";

                long position = fs.Length;
                int count = 0;
                var result = new List<string>();
                var buffer = new byte[65536];
                var leftover = "";

                while (position > 0 && count < lineCount)
                {
                    int toRead = (int)Math.Min(position, buffer.Length);
                    position -= toRead;
                    fs.Seek(position, SeekOrigin.Begin);
                    await fs.ReadAsync(buffer, 0, toRead);

                    string text = Encoding.UTF8.GetString(buffer, 0, toRead) + leftover;
                    string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                    leftover = lines[0];

                    for (int i = lines.Length - 1; i >= 1; i--)
                    {
                        result.Add(lines[i]);
                        count++;
                        if (count >= lineCount) break;
                    }
                }

                if (count < lineCount && !string.IsNullOrEmpty(leftover))
                {
                    result.Add(leftover);
                }

                result.Reverse();
                return string.Join(Environment.NewLine, result);
            }
        }

        private Task ServeDebugInfo(HttpListenerResponse res)
        {
            var info = new
            {
                OS = Environment.OSVersion.ToString(),
                DotNetVersion = Environment.Version.ToString(),
                CurrentDirectory = Environment.CurrentDirectory,
                Is64Bit = Environment.Is64BitProcess,
                ProcessName = Process.GetCurrentProcess().ProcessName,
                Port = _port,
                ActiveAceSessions = AceStreamService.Instance.GetActiveSessionsSnapshot().Count
            };
            return WriteJsonAsync(res, info, formatting: Newtonsoft.Json.Formatting.Indented);
        }

        private Task ServeDeviceDescription(HttpListenerResponse res)
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<root xmlns=""urn:schemas-upnp-org:device-1-0"">
  <specVersion>
    <major>1</major>
    <minor>0</minor>
  </specVersion>
  <device>
    <deviceType>urn:schemas-upnp-org:device:MediaServer:1</deviceType>
    <friendlyName>StreamMesh Media Server</friendlyName>
    <manufacturer>StreamMesh</manufacturer>
    <modelName>StreamMesh Smart Router DLNA Server</modelName>
    <modelNumber>2.0</modelNumber>
    <UDN>uuid:STREAMMESH-MEDIA-SERVER-01</UDN>
    <serviceList>
      <service>
        <serviceType>urn:schemas-upnp-org:service:ContentDirectory:1</serviceType>
        <serviceId>urn:upnp-org:serviceId:ContentDirectory</serviceId>
        <controlURL>/upnp/control/content_directory</controlURL>
        <eventSubURL>/upnp/event/content_directory</eventSubURL>
        <SCPDURL>/upnp/scpd/content_directory.xml</SCPDURL>
      </service>
    </serviceList>
  </device>
</root>";
            return WriteTextAsync(res, xml, "text/xml; charset=utf-8");
        }

        private async Task ServeApiPlay(HttpListenerRequest req, HttpListenerResponse res)
        {
            string id = req.QueryString["id"] ?? "";
            var ch = !string.IsNullOrWhiteSpace(id) ? await _db.GetChannelByIdAsync(id) : null;

            if (ch != null)
            {
                await WriteJsonAsync(res, new { success = true, channel = ch.PrimaryName, streamUrl = $"/stream/{ch.Id}" });
            }
            else
            {
                res.StatusCode = 404;
            }
        }

        private async Task ServeApiEpgQuery(HttpListenerRequest req, HttpListenerResponse res)
        {
            string id = req.QueryString["id"] ?? "";
            string name = req.QueryString["name"] ?? "";
            var epgService = new EpgService();

            Channel? ch = null;
            if (!string.IsNullOrEmpty(id))
            {
                ch = await _db.GetChannelByIdAsync(id);
            }
            if (ch == null && !string.IsNullOrEmpty(name))
            {
                ch = new Channel { Name = name };
            }

            if (ch == null)
            {
                res.StatusCode = 400;
                return;
            }

            var programs = await epgService.GetChannelEpgHistoryAsync(ch);
            var now = DateTime.Now;
            var currentProg = programs.FirstOrDefault(p => now >= p.StartTime && now <= p.EndTime);
            var nextProg = programs.FirstOrDefault(p => p.StartTime > now);

            var result = new
            {
                channel = ch.PrimaryName,
                current = currentProg != null ? new
                {
                    title = currentProg.Title,
                    desc = currentProg.Description,
                    start = currentProg.StartTime.ToString("HH:mm"),
                    end = currentProg.EndTime.ToString("HH:mm"),
                    timeRange = $"{currentProg.StartTime:HH:mm} - {currentProg.EndTime:HH:mm}"
                } : null,
                next = nextProg != null ? new
                {
                    title = nextProg.Title,
                    desc = nextProg.Description,
                    start = nextProg.StartTime.ToString("HH:mm"),
                    end = nextProg.EndTime.ToString("HH:mm"),
                    timeRange = $"{nextProg.StartTime:HH:mm} - {nextProg.EndTime:HH:mm}"
                } : null,
                programs = programs.Take(25).Select(p => new
                {
                    title = p.Title,
                    desc = p.Description,
                    startTime = p.StartTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                    endTime = p.EndTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                    timeRange = $"{p.StartTime:HH:mm} - {p.EndTime:HH:mm}"
                })
            };

            await WriteJsonAsync(res, result);
        }

        private async Task ServeApiEpgBatch(HttpListenerRequest req, HttpListenerResponse res)
        {
            var channels = await _db.GetAllChannelsAsync();
            var epgService = new EpgService();
            var epgDict = await epgService.GetCurrentEpgsAsync(channels);

            var result = new Dictionary<string, object>();
            foreach (var ch in channels)
            {
                if (epgDict.TryGetValue(ch.Id, out var prog))
                {
                    result[ch.Id] = new
                    {
                        title = prog.Title,
                        time = $"{prog.StartTime:HH:mm} - {prog.EndTime:HH:mm}"
                    };
                }
            }

            await WriteJsonAsync(res, result);
        }

        private async Task ServeApiEpgGuide(HttpListenerRequest req, HttpListenerResponse res)
        {
            DateTime now = DateTime.Now;
            var epgPlugin = PluginManager.Instance.GetPluginById("epg_guide");
            if (epgPlugin != null && !epgPlugin.IsEnabled)
            {
                await WriteJsonAsync(res, new
                {
                    disabled = true,
                    generatedAt = now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    items = Array.Empty<object>()
                });
                return;
            }

            var channels = (await _db.GetAllChannelsAsync())
                .Where(c => string.Equals(c.Category, "TV", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(c.Category))
                .Take(200)
                .ToList();

            var programs = await _db.GetEpgProgramsInRangeAsync(now.AddHours(-3), now.AddHours(9));
            var progLookup = new Dictionary<string, List<EpgProgram>>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in programs)
            {
                string key = ChannelUtils.NormalizeForMatch(p.ChannelName);
                if (string.IsNullOrEmpty(key)) continue;
                if (!progLookup.TryGetValue(key, out var list))
                {
                    list = new List<EpgProgram>();
                    progLookup[key] = list;
                }
                list.Add(p);
            }

            var items = new List<object>();
            foreach (var ch in channels)
            {
                var matched = new List<EpgProgram>();
                foreach (var idCandidate in ch.GetEpgIdList())
                {
                    string k = ChannelUtils.NormalizeForMatch(idCandidate);
                    if (!string.IsNullOrEmpty(k) && progLookup.TryGetValue(k, out var list)) matched.AddRange(list);
                }
                if (matched.Count == 0)
                {
                    string nameKey = ChannelUtils.NormalizeForMatch(ch.CleanName ?? ch.PrimaryName);
                    if (!string.IsNullOrEmpty(nameKey) && progLookup.TryGetValue(nameKey, out var list)) matched.AddRange(list);
                }

                var ordered = matched.OrderBy(p => p.StartTime).GroupBy(p => p.StartTime).Select(g => g.First()).ToList();
                var cur = ordered.FirstOrDefault(p => now >= p.StartTime && now <= p.EndTime) ?? ordered.FirstOrDefault();
                var nxt = ordered.FirstOrDefault(p => p.StartTime > now);

                items.Add(new
                {
                    id = ch.Id,
                    name = ch.PrimaryName,
                    logo = ch.PrimaryLogoUrl,
                    group = ch.GroupTitle,
                    currentTitle = cur != null ? cur.Title : (ch.CurrentEpgTitle ?? "Canlı Yayın"),
                    currentTime = cur != null ? $"{cur.StartTime:HH:mm} - {cur.EndTime:HH:mm}" : "--:--",
                    nextTitle = nxt != null ? $"{nxt.StartTime:HH:mm} {nxt.Title}" : "--:--",
                    desc = cur?.Description ?? ""
                });
            }

            await WriteJsonAsync(res, new
            {
                generatedAt = now.ToString("yyyy-MM-ddTHH:mm:ss"),
                items
            });
        }

        private async Task ServeApiYtResolve(HttpListenerRequest req, HttpListenerResponse res)
        {
            string url = req.QueryString["url"] ?? "";
            var yt = new YoutubeEngine();
            string? resolved = await yt.GetStreamUrlAsync(url);

            await WriteJsonAsync(res, new { Original = url, Resolved = resolved, Success = !string.IsNullOrEmpty(resolved) });
        }

        private async Task ServeApiLogosFind(HttpListenerRequest req, HttpListenerResponse res)
        {
            string q = req.QueryString["q"] ?? "";
            var results = await LogoSearchEngine.SearchLogosAsync(q);
            await WriteJsonAsync(res, results);
        }

        private async Task ServeApiChannelsSearch(HttpListenerRequest req, HttpListenerResponse res)
        {
            string q = req.QueryString["q"] ?? "";
            var results = await new GlobalSearchEngine().SearchGlobalAsync(q);
            await WriteJsonAsync(res, results);
        }

        private async Task ServeApiLogsErrors(HttpListenerResponse res)
        {
            try
            {
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamMesh", "app.log");
                if (File.Exists(logPath))
                {
                    var errors = File.ReadAllLines(logPath).Where(l => l.Contains("[ERROR]")).TakeLast(50).ToList();
                    await WriteJsonAsync(res, errors);
                }
                else { res.StatusCode = 404; }
            }
            catch { res.StatusCode = 500; }
        }

        private Task ServeApiM3uSources(HttpListenerResponse res)
        {
            var sources = _db.GetM3uSources();
            return WriteJsonAsync(res, sources);
        }

        private async Task ServeProxyStream(HttpListenerRequest req, HttpListenerResponse res)
        {
            string url = req.QueryString["url"] ?? "";
            string id = req.QueryString["id"] ?? "";

            if (!string.IsNullOrEmpty(url))
            {
                await ProxyDirectUrlAsync(url, req, res);
            }
            else if (!string.IsNullOrEmpty(id))
            {
                await HandleSmartRouterStreamAsync(id, req, res);
            }
            else
            {
                res.StatusCode = 400;
            }
        }

        private static object SerializePlugin(IPlugin p) => new
        {
            id = p.Id,
            name = p.Name,
            category = p.Category,
            version = p.Version,
            author = p.Author,
            icon = p.Icon,
            isEnabled = p.IsEnabled,
            status = p.Status,
            statusMessage = p.StatusMessage,
            description = p.Description,
            settings = p.Settings.Select(s => new
            {
                key = s.Key,
                label = s.Label,
                value = s.Value,
                defaultValue = s.DefaultValue,
                type = s.Type.ToString().ToLowerInvariant(),
                options = s.Options,
                description = s.Description
            }).ToList(),
            stats = p.GetStatistics()
        };

        private Task ServePluginsList(HttpListenerResponse res)
        {
            var all = PluginManager.Instance.GetAllPlugins().ToList();
            return WriteJsonAsync(res, new
            {
                total = all.Count,
                active = all.Count(p => p.IsEnabled),
                plugins = all.Select(SerializePlugin).ToList()
            });
        }

        private static async Task<Newtonsoft.Json.Linq.JObject?> ReadJsonRequestAsync(HttpListenerRequest req)
        {
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
            string body = await reader.ReadToEndAsync();
            return string.IsNullOrWhiteSpace(body) ? null : Newtonsoft.Json.Linq.JObject.Parse(body);
        }

        private async Task HandlePluginToggle(HttpListenerRequest req, HttpListenerResponse res)
        {
            var body = await ReadJsonRequestAsync(req);
            string id = body?["id"]?.ToString() ?? "";
            var plugin = PluginManager.Instance.GetPluginById(id);
            if (plugin == null)
            {
                res.StatusCode = 404;
                return;
            }

            bool enabled = (body?["enabled"] != null && bool.TryParse(body["enabled"]!.ToString(), out bool parsedEnabled))
                ? parsedEnabled
                : !plugin.IsEnabled;
            PluginManager.Instance.SetPluginEnabled(id, enabled);
            await WriteJsonAsync(res, new { success = true, plugin = SerializePlugin(plugin) });
        }

        private async Task HandlePluginSettings(HttpListenerRequest req, HttpListenerResponse res)
        {
            var body = await ReadJsonRequestAsync(req);
            string id = body?["id"]?.ToString() ?? "";
            var plugin = PluginManager.Instance.GetPluginById(id);
            if (plugin == null)
            {
                res.StatusCode = 404;
                return;
            }

            if (body?["settings"] is Newtonsoft.Json.Linq.JArray settingsArr)
            {
                foreach (var item in settingsArr)
                {
                    string key = item["key"]?.ToString() ?? "";
                    string val = item["value"]?.ToString() ?? "";
                    var target = plugin.Settings.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
                    if (target != null) target.Value = val;
                }
                PluginManager.Instance.SavePluginSettings(plugin);
            }

            await WriteJsonAsync(res, new { success = true, message = "Ayarlar kaydedildi", plugin = SerializePlugin(plugin) });
        }

        private async Task HandlePluginAction(HttpListenerRequest req, HttpListenerResponse res)
        {
            var body = await ReadJsonRequestAsync(req);
            string id = body?["id"]?.ToString() ?? "";
            string action = body?["action"]?.ToString() ?? "TestConnection";
            var plugin = PluginManager.Instance.GetPluginById(id);
            if (plugin == null)
            {
                res.StatusCode = 404;
                return;
            }

            string result = await PluginManager.Instance.ExecutePluginActionAsync(id, action);
            await WriteJsonAsync(res, new { success = true, result, plugin = SerializePlugin(plugin) });
        }

        private static string ResolveAppRootForStatic()
        {
            string webIndex = ResolveWebIndexPath();
            string? webDir = Path.GetDirectoryName(webIndex);
            return webDir != null ? Path.GetFullPath(Path.Combine(webDir, "..")) : AppDomain.CurrentDomain.BaseDirectory;
        }

        private async Task<bool> TryServeStaticAsset(string rawPath, HttpListenerResponse res)
        {
            string relPath = rawPath.TrimStart('/');
            if (string.Equals(rawPath, "/favicon.ico", StringComparison.OrdinalIgnoreCase))
            {
                relPath = "logos/StreamMesh_Icon.ico";
            }

            if (!relPath.StartsWith("Web/", StringComparison.OrdinalIgnoreCase) &&
                !relPath.StartsWith("logos/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string root = ResolveAppRootForStatic();
            string webRoot = Path.GetFullPath(Path.Combine(root, "Web")) + Path.DirectorySeparatorChar;
            string logosRoot = Path.GetFullPath(Path.Combine(root, "logos")) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(Path.Combine(root, relPath.Replace('/', Path.DirectorySeparatorChar)));

            if (!fullPath.StartsWith(webRoot, StringComparison.OrdinalIgnoreCase) &&
                !fullPath.StartsWith(logosRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!File.Exists(fullPath)) return false;

            string ext = Path.GetExtension(fullPath).ToLowerInvariant();
            res.ContentType = ext switch
            {
                ".html" => "text/html; charset=utf-8",
                ".js" => "application/javascript; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".json" => "application/json; charset=utf-8",
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                _ => "application/octet-stream"
            };

            byte[] bytes = await File.ReadAllBytesAsync(fullPath);
            res.StatusCode = 200;
            res.ContentLength64 = bytes.Length;
            await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            return true;
        }
    }
}
