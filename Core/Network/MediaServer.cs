using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media;
using StreamMesh.Models;
using System.Collections.Generic;

namespace StreamMesh.Core.Network
{
    public class MediaServer
    {
        private HttpListener _listener;
        private readonly DatabaseEngine _db = new DatabaseEngine();
        private bool _isRunning = false;
        private int _port = 8080;

        public MediaServer(int port = 8080)
        {
            _port = port;
            _listener = new HttpListener();
            // Using localhost and specific IP to avoid "Access Denied" which often happens with "*"
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        }

        public void Start()
        {
            if (_isRunning) return;
            try
            {
                _listener.Start();
                _isRunning = true;
                Task.Run(ListenLoop);
                Utils.LogService.LogInfo($"MediaServer: Başlatıldı. Port: {_port}");
            }
            catch (Exception ex)
            {
                Utils.LogService.LogError("MediaServer Start Error", ex);
                // Try fallback to higher port if occupied
            }
        }

        public void Stop()
        {
            try
            {
                _isRunning = false;
                if (_listener.IsListening) _listener.Stop();
            } catch { }
        }

        private async Task ListenLoop()
        {
            while (_isRunning)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context));
                }
                catch (ObjectDisposedException) { break; }
                catch { }
            }
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            var req = context.Request;
            var res = context.Response;
            string path = req.Url?.AbsolutePath.ToLower() ?? "/";

            Utils.LogService.LogInfo($"MediaServer: Incoming request: {req.HttpMethod} {path} from {req.RemoteEndPoint}");

            try
            {
                if (path == "/desc.xml")
                {
                    await ServeDeviceDescription(res);
                }
                else if (path == "/playlist.m3u")
                {
                    await ServeM3u(res);
                }
                else if (path == "/web")
                {
                    await ServeHtmlPlayer(res);
                }
                else if (path == "/channels")
                {
                    await ServeChannelsJson(res);
                }
                else if (path == "/proxy")
                {
                    await ServeProxyStream(req, res);
                }
                else if (path == "/ping")
                {
                    byte[] buffer = Encoding.UTF8.GetBytes("pong");
                    res.ContentType = "text/plain";
                    await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
                else if (path == "/logs")
                {
                    await ServeLogs(res);
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
            catch { res.StatusCode = 500; }
            finally { try { res.Close(); } catch { } }
        }

        private async Task ServeLogs(HttpListenerResponse res)
        {
            try
            {
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamMesh", "app.log");
                if (File.Exists(logPath))
                {
                    byte[] buffer;
                    using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(fs))
                    {
                        string content = await reader.ReadToEndAsync();
                        buffer = Encoding.UTF8.GetBytes(content);
                    }
                    res.ContentType = "text/plain; charset=utf-8";
                    await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
                else
                {
                    byte[] buffer = Encoding.UTF8.GetBytes("Log dosyası henüz oluşturulmadı.");
                    await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
            }
            catch (Exception ex)
            {
                byte[] buffer = Encoding.UTF8.GetBytes("Log okuma hatası: " + ex.Message);
                await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
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
                Port = _port
            };
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(info, Newtonsoft.Json.Formatting.Indented);
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            res.ContentType = "application/json";
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
    <modelName>StreamMesh DLNA Server</modelName>
    <modelNumber>1.8</modelNumber>
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

        private async Task ServeM3u(HttpListenerResponse res)
        {
            var channels = await _db.GetAllChannelsAsync();
            var sb = new StringBuilder();
            sb.AppendLine("#EXTM3U");

            foreach (var ch in channels)
            {
                if (ch.SourceType == "M3U" && !ch.Url.Contains("acestream://"))
                {
                    sb.AppendLine($"#EXTINF:-1 tvg-logo=\"{ch.LogoUrl}\" group-title=\"{ch.GroupTitle}\",{ch.Name}");
                    sb.AppendLine(ch.Url.Split(',')[0]);
                }
            }

            byte[] buffer = Encoding.UTF8.GetBytes(sb.ToString());
            res.ContentType = "application/x-mpegurl";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task ServeChannelsJson(HttpListenerResponse res)
        {
            var channels = await _db.GetAllChannelsAsync();
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(channels.Select(c => new { c.Id, c.Name }));
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            res.ContentType = "application/json";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task ServeHtmlPlayer(HttpListenerResponse res)
        {
            string html = @"
                <!DOCTYPE html>
                <html>
                <head><title>StreamMesh Web Player</title>
                <style>body{background:#0f172a;color:white;font-family:sans-serif;margin:0;display:flex}
                #sidebar{width:300px;height:100vh;overflow-y:auto;background:#1e293b;padding:20px}
                #player-area{flex:1;display:flex;flex-direction:column;justify-content:center;align-items:center}
                video{width:80%;max-height:80%;background:black}
                .ch-item{padding:10px;cursor:pointer;border-bottom:1px solid #334155}
                .ch-item:hover{background:#334155}</style></head>
                <body>
                <div id='sidebar'><h2>StreamMesh Web</h2><div id='list'>Loading...</div></div>
                <div id='player-area'><h1 id='title'>Kanal Seçin</h1><video id='vid' controls autoplay></video></div>
                <script>
                    async function load(){
                        try {
                            const res = await fetch('/channels');
                            const channels = await res.json();
                            const list = document.getElementById('list');
                            list.innerHTML = '';
                            channels.forEach(ch => {
                                const div = document.createElement('div');
                                div.className = 'ch-item';
                                div.innerText = ch.Name;
                                div.onclick = () => {
                                    document.getElementById('vid').src = '/proxy?id=' + ch.Id;
                                    document.getElementById('title').innerText = ch.Name;
                                };
                                list.appendChild(div);
                            });
                        } catch(e) { document.getElementById('list').innerText = 'Error loading channels'; }
                    }
                    load();
                </script>
                </body></html>";

            byte[] buffer = Encoding.UTF8.GetBytes(html);
            res.ContentType = "text/html";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task ServeApiPlay(HttpListenerRequest req, HttpListenerResponse res)
        {
            string id = req.QueryString["id"] ?? "";
            var channels = await _db.GetAllChannelsAsync();
            var ch = channels.FirstOrDefault(x => x.Id == id);

            if (ch != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    UI.Windows.MainWindow.Instance?.LoadChannelToPlayer(ch);
                });
                byte[] buffer = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(new { success = true, channel = ch.Name }));
                res.ContentType = "application/json";
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
            res.ContentType = "application/json";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task ServeApiAceDiagnostics(HttpListenerResponse res)
        {
            var ace = new AceEngine();
            bool running = await ace.IsEngineRunningAsync();
            string token = await ace.GetApiAccessTokenAsync() ?? "None";
            string path = AceEngine.GetEngineExecutablePath();

            // Check specific formats
            string testHash = "0a48b895ed0994a11fccf487aada3808446bb932";
            bool idWorks = await ace.WaitForStreamReadyAsync($"http://127.0.0.1:6878/ace/getstream?id={testHash}", 2);
            bool infohashWorks = await ace.WaitForStreamReadyAsync($"http://127.0.0.1:6878/ace/getstream?infohash={testHash}", 2);

            var diag = new {
                EngineRunning = running,
                Token = token,
                ExecutablePath = path,
                Formats = new {
                    IdParam = idWorks ? "Working" : "Failed (500/Timeout)",
                    InfohashParam = infohashWorks ? "Working" : "Failed (500/Timeout)"
                },
                Timestamp = DateTime.Now
            };

            byte[] buffer = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(diag, Newtonsoft.Json.Formatting.Indented));
            res.ContentType = "application/json";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task ServeApiYtResolve(HttpListenerRequest req, HttpListenerResponse res)
        {
            string url = req.QueryString["url"] ?? "";
            var yt = new YoutubeEngine();
            string? resolved = await yt.GetStreamUrlAsync(url);

            var result = new { Original = url, Resolved = resolved, Success = !string.IsNullOrEmpty(resolved) };
            byte[] buffer = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(result));
            res.ContentType = "application/json";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task ServeApiSystemStats(HttpListenerResponse res)
        {
            var channels = await _db.GetAllChannelsAsync();
            var stats = new {
                TotalChannels = channels.Count,
                SourceTypes = channels.GroupBy(c => c.SourceType).ToDictionary(g => g.Key ?? "Unknown", g => g.Count()),
                DatabaseSize = new FileInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "database_v2.db")).Length / 1024 / 1024 + " MB"
            };

            byte[] buffer = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(stats, Newtonsoft.Json.Formatting.Indented));
            res.ContentType = "application/json";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task ServeApiLogosFind(HttpListenerRequest req, HttpListenerResponse res)
        {
            string q = req.QueryString["q"] ?? "";
            var results = await LogoSearchEngine.SearchLogosAsync(q);

            byte[] buffer = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(results));
            res.ContentType = "application/json";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task ServeApiChannelsSearch(HttpListenerRequest req, HttpListenerResponse res)
        {
            string q = req.QueryString["q"] ?? "";
            var searchEngine = new GlobalSearchEngine();
            var results = await searchEngine.SearchGlobalAsync(q);

            byte[] buffer = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(results));
            res.ContentType = "application/json";
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
                    res.ContentType = "application/json";
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
            res.ContentType = "application/json";
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task ServeProxyStream(HttpListenerRequest req, HttpListenerResponse res)
        {
            string id = req.QueryString["id"] ?? "";
            Utils.LogService.LogInfo($"MediaServer: Proxying stream for channel ID: {id}");

            var channels = await _db.GetAllChannelsAsync();
            var ch = channels.FirstOrDefault(x => x.Id == id);
            if (ch == null)
            {
                Utils.LogService.LogWarning($"MediaServer: Channel ID {id} not found in database.");
                res.StatusCode = 404;
                return;
            }

            string url = ch.Url.Split(',')[0];
            Utils.LogService.LogInfo($"MediaServer: Original URL: {url}");

            using (var client = new System.Net.Http.HttpClient())
            {
                var ace = new AceEngine();
                if (ace.IsAceStreamUrl(url))
                {
                    Utils.LogService.LogInfo("MediaServer: AceStream detected, fetching specialized URLs...");
                    var aceUrls = await ace.GetHttpUrlsWithTokenAsync(url);
                    if (aceUrls.Count > 0)
                    {
                        url = aceUrls[0];
                        Utils.LogService.LogInfo($"MediaServer: Proxying to AceStream internal URL: {url}");
                    }
                }

                try
                {
                    var stream = await client.GetStreamAsync(url);
                    res.ContentType = "video/mp2t";
                    Utils.LogService.LogInfo("MediaServer: Stream successfully established. Sending to output...");
                    await stream.CopyToAsync(res.OutputStream);
                }
                catch (Exception ex)
                {
                    Utils.LogService.LogError($"MediaServer: Proxy transmission error for {url}", ex);
                    res.StatusCode = 500;
                }
            }
        }
    }
}
