using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media;
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

            try
            {
                if (path == "/playlist.m3u")
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
                else
                {
                    res.StatusCode = 404;
                }
            }
            catch { res.StatusCode = 500; }
            finally { try { res.Close(); } catch { } }
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

        private async Task ServeProxyStream(HttpListenerRequest req, HttpListenerResponse res)
        {
            string id = req.QueryString["id"] ?? "";
            var channels = await _db.GetAllChannelsAsync();
            var ch = channels.FirstOrDefault(x => x.Id == id);
            if (ch == null) { res.StatusCode = 404; return; }

            string url = ch.Url.Split(',')[0];

            using (var client = new System.Net.Http.HttpClient())
            {
                if (url.StartsWith("acestream://"))
                {
                    url = new AceEngine().GetHttpUrl(url);
                }

                var stream = await client.GetStreamAsync(url);
                res.ContentType = "video/mp2t";
                await stream.CopyToAsync(res.OutputStream);
            }
        }
    }
}
