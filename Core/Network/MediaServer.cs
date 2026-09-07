using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using StreamMesh.Core.Utils;
using StreamMesh.Models;

namespace StreamMesh.Core.Network
{
    public class MediaServer
    {
        private HttpListener? _listener;
        private readonly DatabaseEngine _db = new DatabaseEngine();
        private bool _isRunning = false;
        private int _port = 8080;

        // Shared HttpClient for proxying and smart routing to prevent socket exhaustion
        private static readonly HttpClient _proxyClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = true,
            MaxConnectionsPerServer = 100,
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        }) { Timeout = TimeSpan.FromSeconds(30) };

        // "Last Known Good" source tracker for multi-source channels
        private static readonly ConcurrentDictionary<string, int> _lastKnownGoodIndex = new(StringComparer.OrdinalIgnoreCase);

        public MediaServer(int port = 8080)
        {
            _port = port;
        }

        public int Port => _port;
        public bool IsRunning => _isRunning;

        public static List<string> GetLocalIPv4Addresses()
        {
            var list = new List<string>();
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        list.Add(ip.ToString());
                    }
                }
            }
            catch { }
            return list;
        }

        public bool Start()
        {
            if (_isRunning) return true;

            int attempts = 0;
            int basePort = _port;

            while (attempts < 5)
            {
                var localIps = GetLocalIPv4Addresses();
                bool tryLanBinding = localIps.Count > 0;

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

                    // Attempt LAN IP prefixes for local network accessibility (Android TV, TiviMate, etc.)
                    if (tryLanBinding)
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
                    Task.Run(ListenLoop);
                    LogService.LogInfo($"MediaServer: Started successfully on Port: {_port} [Bound to: localhost, 127.0.0.1{(tryLanBinding ? $", {string.Join(", ", localIps)}" : "")}]");
                    return true;
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 5 || ex.NativeErrorCode == 5)
                {
                    // Access Denied (Win32 Error 5 / ERROR_ACCESS_DENIED) -> URL ACL restriction for LAN IP
                    LogService.LogWarning($"MediaServer: LAN IP prefix binding failed due to Windows HTTP.sys URL ACL restrictions (Win32 Error 5: Access Denied). Falling back to localhost/127.0.0.1 binding on port {_port}...");

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
                        Task.Run(ListenLoop);
                        LogService.LogInfo($"MediaServer: Started successfully in fallback mode on Port: {_port} [Bound to: localhost, 127.0.0.1]. Note: Direct LAN IP access requires Administrator or 'netsh http add urlacl' configuration.");
                        return true;
                    }
                    catch (HttpListenerException fallbackEx) when (fallbackEx.ErrorCode == 183 || fallbackEx.ErrorCode == 32 || fallbackEx.ErrorCode == 48 || fallbackEx.NativeErrorCode == 183 || fallbackEx.NativeErrorCode == 32 || fallbackEx.NativeErrorCode == 48)
                    {
                        attempts++;
                        _port = basePort + attempts;
                        LogService.LogWarning($"MediaServer: Port {_port - 1} in use, retrying on port {_port} (attempt {attempts}/5)...");
                    }
                    catch (Exception fallbackEx)
                    {
                        LogService.LogError($"MediaServer Start Localhost Fallback Error (Win32/Ex: {fallbackEx.Message})", fallbackEx);
                        return false;
                    }
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 183 || ex.ErrorCode == 32 || ex.ErrorCode == 48 || ex.NativeErrorCode == 183 || ex.NativeErrorCode == 32 || ex.NativeErrorCode == 48)
                {
                    // Port Conflict (183: ERROR_ALREADY_EXISTS, 32: ERROR_SHARING_VIOLATION, 48: EADDRINUSE)
                    attempts++;
                    _port = basePort + attempts;
                    LogService.LogWarning($"MediaServer: Port {_port - 1} in use, retrying on port {_port} (attempt {attempts}/5)...");
                }
                catch (Exception ex)
                {
                    LogService.LogError($"MediaServer Start Unexpected Error: {ex.Message}", ex);
                    return false;
                }
            }
            return false;
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

            // Enable CORS for web players
            res.Headers.Add("Access-Control-Allow-Origin", "*");
            res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS, HEAD");
            res.Headers.Add("Access-Control-Allow-Headers", "*");

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
                else if (path == "/playlist.m3u" || path == "/api/playlist.m3u")
                {
                    await ServeM3u(req, res);
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
                    byte[] buffer = Encoding.UTF8.GetBytes("pong");
                    res.ContentType = "text/plain";
                    await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
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
                else if (path == "/api/epg/query")
                {
                    await ServeApiEpgQuery(req, res);
                }
                else if (path == "/api/ace/diagnostics")
                {
                    await ServeApiAceDiagnostics(res);
                }
                else if (path == "/api/ace/sessions")
                {
                    await ServeApiAceSessions(res);
                }
                else if (path == "/api/yt/resolve")
                {
                    await ServeApiYtResolve(req, res);
                }
                else if (path == "/api/system/stats")
                {
                    await ServeApiSystemStats(res);
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
        /// - ACESTREAM: Multiplexed shared session via AceStreamService
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

            // If not found by Id, check if channelId is a raw 40-char AceStream hash
            if (ch == null && channelId.Length == 40 && System.Text.RegularExpressions.Regex.IsMatch(channelId, @"^[a-fA-F0-9]{40}$"))
            {
                ch = new Channel
                {
                    Id = channelId,
                    Name = $"AceStream ({channelId.Substring(0, 8)}...)",
                    Url = $"acestream://{channelId}",
                    SourceType = "ACESTREAM"
                };
            }

            if (ch == null)
            {
                LogService.LogWarning($"SmartRouter: Channel '{channelId}' not found.");
                res.StatusCode = 404;
                return;
            }

            LogService.LogInfo($"SmartRouter: Routing channel '{ch.PrimaryName}' (Type: {ch.SourceType}, Sources: {ch.SourcesCount})");

            using var cts = new CancellationTokenSource();

            // 1. ACESTREAM ROUTE
            if (string.Equals(ch.SourceType, "ACESTREAM", StringComparison.OrdinalIgnoreCase) ||
                ch.Url.Contains("acestream://", StringComparison.OrdinalIgnoreCase) ||
                ch.Url.Contains(":6878/ace/") ||
                (ch.Url.Length == 40 && System.Text.RegularExpressions.Regex.IsMatch(ch.Url, @"^[a-fA-F0-9]{40}$")))
            {
                res.ContentType = "video/mp2t";
                res.StatusCode = 200;
                res.SendChunked = true;

                try
                {
                    await AceStreamService.Instance.StreamAceChannelAsync(ch.Url, res.OutputStream, cts.Token);
                }
                catch (Exception ex)
                {
                    LogService.LogError($"SmartRouter: AceStream bridge failed for {ch.PrimaryName}", ex);
                    res.StatusCode = 502;
                }
                return;
            }

            // 2. YOUTUBE ROUTE
            if (string.Equals(ch.SourceType, "YOUTUBE", StringComparison.OrdinalIgnoreCase) ||
                ch.Url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                ch.Url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var yt = new YoutubeEngine();
                    string? resolvedStream = await yt.GetStreamUrlAsync(ch.Url);

                    if (string.IsNullOrEmpty(resolvedStream))
                    {
                        LogService.LogWarning($"SmartRouter: Failed to resolve YouTube stream for {ch.Url}");
                        res.StatusCode = 502;
                        return;
                    }

                    // Forward client to resolved stream or proxy stream
                    res.Redirect(resolvedStream);
                    res.StatusCode = (int)HttpStatusCode.Redirect;
                    return;
                }
                catch (Exception ex)
                {
                    LogService.LogError($"SmartRouter: YouTube route error for {ch.PrimaryName}", ex);
                    res.StatusCode = 502;
                    return;
                }
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

                    var response = await _proxyClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        // Update Last Known Good Index
                        _lastKnownGoodIndex[ch.Id] = currentIndex;

                        string host = req.Headers["Host"] ?? $"127.0.0.1:{_port}";
                        string contentType = response.Content.Headers.ContentType?.ToString() ?? "";
                        bool isM3u8 = contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) || 
                                     contentType.Contains("application/x-mpegurl", StringComparison.OrdinalIgnoreCase) || 
                                     cleanUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase);

                        if (isM3u8)
                        {
                            string rawManifest = await response.Content.ReadAsStringAsync(cts.Token);
                            string rewritten = RewriteHlsManifest(rawManifest, cleanUrl, host);
                            byte[] manifestBytes = Encoding.UTF8.GetBytes(rewritten);

                            res.ContentType = "application/vnd.apple.mpegurl; charset=utf-8";
                            res.StatusCode = 200;
                            res.Headers.Add("Access-Control-Allow-Origin", "*");
                            res.ContentLength64 = manifestBytes.Length;
                            await res.OutputStream.WriteAsync(manifestBytes, 0, manifestBytes.Length, cts.Token);
                        }
                        else
                        {
                            res.ContentType = !string.IsNullOrEmpty(contentType) ? contentType : "video/mp2t";
                            res.StatusCode = (int)response.StatusCode;
                            res.SendChunked = true;
                            res.Headers.Add("Access-Control-Allow-Origin", "*");

                            using (var stream = await response.Content.ReadAsStreamAsync(cts.Token))
                            {
                                await stream.CopyToAsync(res.OutputStream, cts.Token);
                            }
                        }

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
                    line = System.Text.RegularExpressions.Regex.Replace(line, "URI=\"([^\"]+)\"", match =>
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

        private async Task ProxyDirectUrlAsync(string targetUrl, HttpListenerRequest req, HttpListenerResponse res)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
                request.Headers.TryAddWithoutValidation("User-Agent", "StreamMesh/2.1 (Desktop; SmartRouter)");
                if (!string.IsNullOrEmpty(req.Headers["Range"]))
                {
                    request.Headers.TryAddWithoutValidation("Range", req.Headers["Range"]);
                }

                var response = await _proxyClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                string host = req.Headers["Host"] ?? $"127.0.0.1:{_port}";
                string contentType = response.Content.Headers.ContentType?.ToString() ?? "";
                bool isM3u8 = contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) || 
                             contentType.Contains("application/x-mpegurl", StringComparison.OrdinalIgnoreCase) || 
                             targetUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase);

                res.Headers.Add("Access-Control-Allow-Origin", "*");

                if (isM3u8)
                {
                    string raw = await response.Content.ReadAsStringAsync(cts.Token);
                    string rewritten = RewriteHlsManifest(raw, targetUrl, host);
                    byte[] bytes = Encoding.UTF8.GetBytes(rewritten);
                    res.ContentType = "application/vnd.apple.mpegurl; charset=utf-8";
                    res.StatusCode = (int)response.StatusCode;
                    res.ContentLength64 = bytes.Length;
                    await res.OutputStream.WriteAsync(bytes, 0, bytes.Length, cts.Token);
                }
                else
                {
                    res.ContentType = !string.IsNullOrEmpty(contentType) ? contentType : (targetUrl.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ? "video/mp2t" : "video/mp4");
                    res.StatusCode = (int)response.StatusCode;
                    res.SendChunked = true;

                    using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                    await stream.CopyToAsync(res.OutputStream, cts.Token);
                }
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

        private async Task ServeM3u(HttpListenerRequest req, HttpListenerResponse res)
        {
            var channels = await _db.GetAllChannelsAsync();
            string host = req.Headers["Host"] ?? $"127.0.0.1:{_port}";

            var sb = new StringBuilder();
            sb.AppendLine("#EXTM3U name=\"StreamMesh Smart Router Playlist\"");

            foreach (var ch in channels)
            {
                bool isAce = string.Equals(ch.SourceType, "ACESTREAM", StringComparison.OrdinalIgnoreCase) || ch.Url.Contains("acestream://");
                bool isYt = string.Equals(ch.SourceType, "YOUTUBE", StringComparison.OrdinalIgnoreCase) || ch.Url.Contains("youtube.com") || ch.Url.Contains("youtu.be");
                bool isMulti = ch.SourcesCount > 1;

                string groupSuffix = isAce ? " [StreamMesh P2P]" :
                                     isYt ? " [StreamMesh YouTube]" :
                                     isMulti ? " [StreamMesh Smart Router]" :
                                     " [Doğrudan IPTV]";

                string groupTitle = $"{ch.GroupTitle}{groupSuffix}";
                string streammeshRequired = (isAce || isYt || isMulti) ? "true" : "false";
                string streamType = isAce ? "ACESTREAM" : (isYt ? "YOUTUBE" : (isMulti ? "MULTI_SOURCE" : "DIRECT"));

                // Universal Smart Router Endpoint
                string routedPlaybackUrl = $"http://{host}/stream/{ch.Id}";

                sb.AppendLine($"#EXTINF:-1 tvg-id=\"{ch.EpgId}\" tvg-name=\"{ch.PrimaryName}\" tvg-logo=\"{ch.PrimaryLogoUrl}\" group-title=\"{groupTitle}\" streammesh-required=\"{streammeshRequired}\" streammesh-type=\"{streamType}\",{ch.PrimaryName}");
                sb.AppendLine(routedPlaybackUrl);
            }

            byte[] buffer = Encoding.UTF8.GetBytes(sb.ToString());
            res.ContentType = "application/x-mpegurl; charset=utf-8";
            res.Headers.Add("Content-Disposition", "attachment; filename=\"StreamMesh.m3u\"");
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
                StreamUrl = $"/stream/{c.Id}",
                IsStreamMeshRequired = string.Equals(c.SourceType, "ACESTREAM", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(c.SourceType, "YOUTUBE", StringComparison.OrdinalIgnoreCase) ||
                                       c.SourcesCount > 1
            });

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            res.ContentType = "application/json; charset=utf-8";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
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
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(verObj);
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                res.ContentType = "application/json; charset=utf-8";
                res.StatusCode = 200;
                res.ContentLength64 = buffer.Length;
                await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                LogService.LogError("MediaServer: ServeVersionJson error", ex);
                res.StatusCode = 500;
            }
        }

        private async Task ServeApiAceSessions(HttpListenerResponse res)
        {
            var sessions = AceStreamService.Instance.GetActiveSessionsSnapshot();
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(sessions, Newtonsoft.Json.Formatting.Indented);
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            res.ContentType = "application/json; charset=utf-8";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
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

        private async Task ServeDebugInfo(HttpListenerResponse res)
        {
            var info = new
            {
                OS = Environment.OSVersion.ToString(),
                DotNetVersion = Environment.Version.ToString(),
                CurrentDirectory = Environment.CurrentDirectory,
                Is64Bit = Environment.Is64BitProcess,
                ProcessName = System.Diagnostics.Process.GetCurrentProcess().ProcessName,
                Port = _port,
                ActiveAceSessions = AceStreamService.Instance.GetActiveSessionsSnapshot().Count
            };
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(info, Newtonsoft.Json.Formatting.Indented);
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            res.ContentType = "application/json; charset=utf-8";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task ServeDeviceDescription(HttpListenerResponse res)
        {
            string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
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
            byte[] buffer = Encoding.UTF8.GetBytes(xml);
            res.ContentType = "text/xml; charset=utf-8";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task ServeApiPlay(HttpListenerRequest req, HttpListenerResponse res)
        {
            string id = req.QueryString["id"] ?? "";
            var ch = !string.IsNullOrWhiteSpace(id) ? await _db.GetChannelByIdAsync(id) : null;

            if (ch != null)
            {
                byte[] buffer = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(new { success = true, channel = ch.PrimaryName, streamUrl = $"/stream/{ch.Id}" }));
                res.ContentType = "application/json; charset=utf-8";
                await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            else
            {
                res.StatusCode = 404;
            }
        }

        private async Task ServeApiEpgQuery(HttpListenerRequest req, HttpListenerResponse res)
        {
            string name = req.QueryString["name"] ?? "";
            var epgService = new EpgService();
            var dummyChannel = new Channel { Name = name };
            var programs = await epgService.GetChannelEpgHistoryAsync(dummyChannel);

            byte[] buffer = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(programs));
            res.ContentType = "application/json; charset=utf-8";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task ServeApiAceDiagnostics(HttpListenerResponse res)
        {
            var ace = new AceEngine();
            bool running = await ace.IsEngineRunningAsync();
            string token = await ace.GetApiAccessTokenAsync() ?? "None";
            string path = AceEngine.GetEngineExecutablePath();

            string testHash = "0a48b895ed0994a11fccf487aada3808446bb932";
            bool idWorks = await ace.WaitForStreamReadyAsync($"http://127.0.0.1:6878/ace/getstream?id={testHash}", 2);

            var diag = new
            {
                EngineRunning = running,
                Token = token,
                ExecutablePath = path,
                Formats = new
                {
                    IdParam = idWorks ? "Working" : "Failed (500/Timeout)"
                },
                ActiveSessions = AceStreamService.Instance.GetActiveSessionsSnapshot(),
                Timestamp = DateTime.Now
            };

            byte[] buffer = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(diag, Newtonsoft.Json.Formatting.Indented));
            res.ContentType = "application/json; charset=utf-8";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task ServeApiYtResolve(HttpListenerRequest req, HttpListenerResponse res)
        {
            string url = req.QueryString["url"] ?? "";
            var yt = new YoutubeEngine();
            string? resolved = await yt.GetStreamUrlAsync(url);

            var result = new { Original = url, Resolved = resolved, Success = !string.IsNullOrEmpty(resolved) };
            byte[] buffer = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(result));
            res.ContentType = "application/json; charset=utf-8";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task ServeApiSystemStats(HttpListenerResponse res)
        {
            var channels = await _db.GetAllChannelsAsync();
            var stats = new
            {
                TotalChannels = channels.Count,
                SourceTypes = channels.GroupBy(c => c.SourceType).ToDictionary(g => g.Key ?? "Unknown", g => g.Count()),
                ActiveAceSessions = AceStreamService.Instance.GetActiveSessionsSnapshot().Count
            };

            byte[] buffer = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(stats, Newtonsoft.Json.Formatting.Indented));
            res.ContentType = "application/json; charset=utf-8";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task ServeApiLogosFind(HttpListenerRequest req, HttpListenerResponse res)
        {
            string q = req.QueryString["q"] ?? "";
            var results = await LogoSearchEngine.SearchLogosAsync(q);

            byte[] buffer = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(results));
            res.ContentType = "application/json; charset=utf-8";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task ServeApiChannelsSearch(HttpListenerRequest req, HttpListenerResponse res)
        {
            string q = req.QueryString["q"] ?? "";
            var searchEngine = new GlobalSearchEngine();
            var results = await searchEngine.SearchGlobalAsync(q);

            byte[] buffer = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(results));
            res.ContentType = "application/json; charset=utf-8";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task ServeApiLogsErrors(HttpListenerResponse res)
        {
            try
            {
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamMesh", "app.log");
                if (File.Exists(logPath))
                {
                    var lines = File.ReadAllLines(logPath);
                    var errors = lines.Where(l => l.Contains("[ERROR]")).TakeLast(50).ToList();
                    byte[] buffer = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(errors));
                    res.ContentType = "application/json; charset=utf-8";
                    await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
                else { res.StatusCode = 404; }
            }
            catch { res.StatusCode = 500; }
        }

        private async Task ServeApiM3uSources(HttpListenerResponse res)
        {
            var sources = _db.GetM3uSources();
            byte[] buffer = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(sources));
            res.ContentType = "application/json; charset=utf-8";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
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
    }
}
