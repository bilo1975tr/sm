using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StreamMesh.Models;

namespace StreamMesh.Services
{
    public class ServerService
    {
        private HttpListener _listener;
        private Thread _serverThread;
        private bool _isRunning = false;
        private DatabaseService _databaseService;
        private YoutubeService _youtubeService;
        private AceStreamService _aceStreamService;

        public int Port { get; private set; } = 5000;
        public string LocalIp { get; private set; }

        public event Action<bool, string, string> OnStatusChanged;

        public ServerService()
        {
            _databaseService = new DatabaseService();
            _youtubeService = new YoutubeService();
            _aceStreamService = new AceStreamService();
            LocalIp = GetLocalIp();
        }

        private string GetLocalIp()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            return "127.0.0.1";
        }

        public void StartServer()
        {
            if (_isRunning) return;

            LogService.Log($"Server starting on port {Port}...");
            try
            {
                int internalPort = Port + 1;
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{internalPort}/");
                _listener.Start();
                _isRunning = true;

                _serverThread = new Thread(Listen);
                _serverThread.IsBackground = true;
                _serverThread.Start();

                // Start TCP Relay to bypass Windows HttpListener Admin (URL ACL) restrictions
                Task.Run(() => StartTcpRelay(Port, internalPort));

                LogService.Log($"Server started successfully on port {Port} (Relay to {internalPort})");
                OnStatusChanged?.Invoke(true, LocalIp, Port.ToString());
            }
            catch (Exception ex)
            {
                LogService.LogError($"Server start error.", ex);
                _isRunning = false;
                OnStatusChanged?.Invoke(false, "", "");
            }
        }

        private void StartTcpRelay(int publicPort, int internalPort)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Any, publicPort);
                listener.Start();
                LogService.Log($"TCP Relay started listening on 0.0.0.0:{publicPort} -> 127.0.0.1:{internalPort}");
                while (_isRunning)
                {
                    var client = listener.AcceptTcpClient();
                    Task.Run(() => HandleRelayClient(client, internalPort));
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("TCP Relay bound failed", ex);
            }
        }

        private async Task HandleRelayClient(TcpClient client, int internalPort)
        {
            try
            {
                using (client)
                using (var target = new TcpClient("127.0.0.1", internalPort))
                {
                    using (var stream1 = client.GetStream())
                    using (var stream2 = target.GetStream())
                    {
                        var task1 = stream1.CopyToAsync(stream2);
                        var task2 = stream2.CopyToAsync(stream1);
                        await Task.WhenAny(task1, task2);
                    }
                }
            }
            catch { }
        }

        public void StopServer()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _listener?.Stop();
            _listener?.Close();
            OnStatusChanged?.Invoke(false, "", "");
        }

        private async void Listen()
        {
            while (_isRunning)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    ProcessRequest(context);
                }
                catch (HttpListenerException)
                {
                    break; // Listener stopped or disposed
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Listen error: " + ex.Message);
                }
            }
        }

        private async void ProcessRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            string path = request.Url.AbsolutePath;

            try
            {
                if (path == "/playlist.m3u")
                {
                    await HandlePlaylistAsync(response);
                }
                else if (path == "/stream")
                {
                    string idStr = request.QueryString["id"];
                    await HandleStreamAsync(response, idStr);
                }
                else if (path == "/")
                {
                    await HandleHomeAsync(response);
                }
                else
                {
                    response.StatusCode = 404;
                    CloseResponse(response);
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                Console.WriteLine("Server error: " + ex.Message);
                CloseResponse(response);
            }
        }

        private async Task HandlePlaylistAsync(HttpListenerResponse response)
        {
            var channels = _databaseService.GetAllChannels();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("#EXTM3U");

            foreach (var ch in channels)
            {
                string logo = ch.LogoUrl ?? "";
                string group = ch.GroupTitle ?? "Genel";
                string title = ch.Name ?? "Kanal";
                string streamUrl = $"http://{LocalIp}:{Port}/stream?id={ch.Id}";

                // Group fix for categories
                if (!string.IsNullOrEmpty(ch.Category) && ch.Category != "TV")
                {
                    if (ch.Category == "Film" && !group.StartsWith("Film"))
                        group = $"Film / {group}";
                    else if (ch.Category == "Dizi" && !group.StartsWith("Dizi"))
                        group = $"Dizi / {group}";
                }

                sb.AppendLine($"#EXTINF:-1 tvg-logo=\"{logo}\" group-title=\"{group}\" tvg-language=\"{ch.Language}\",{title}");
                sb.AppendLine(streamUrl);
            }

            string content = sb.ToString();
            byte[] buffer = Encoding.UTF8.GetBytes(content);

            response.ContentType = "audio/mpegurl";
            response.ContentLength64 = buffer.Length;
            response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            CloseResponse(response);
        }

        private async Task HandleStreamAsync(HttpListenerResponse response, string idStr)
        {
            if (!string.IsNullOrEmpty(idStr))
            {
                var channel = _databaseService.GetAllChannels().FirstOrDefault(c => c.Id == idStr);
                if (channel != null && !string.IsNullOrEmpty(channel.Url))
                {
                    string url = channel.Url;

                    if (channel.SourceType == "YOUTUBE")
                    {
                        var directUrl = await _youtubeService.GetSingleMuxedStreamUrlAsync(url);
                        if (!string.IsNullOrEmpty(directUrl)) url = directUrl;
                    }
                    else if (channel.SourceType == "ACESTREAM")
                    {
                        await _aceStreamService.StartEngineAsync();
                        string aceUrl = _aceStreamService.GetHttpUrl(url); // the TS stream url from AceStream
                        
                        try
                        {
                            response.ContentType = "video/mp4";
                            response.Headers.Add("Access-Control-Allow-Origin", "*");
                            
                            var psi = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "ffmpeg",
                                Arguments = $"-i \"{aceUrl}\" -c:v libx264 -preset superfast -crf 28 -vf \"scale='min(1920,iw)':-2\" -c:a aac -b:a 128k -f mp4 -movflags frag_keyframe+empty_moov pipe:1",
                                RedirectStandardOutput = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };

                            using (var process = System.Diagnostics.Process.Start(psi))
                            {
                                try
                                {
                                    await process.StandardOutput.BaseStream.CopyToAsync(response.OutputStream);
                                }
                                finally
                                {
                                    if (!process.HasExited)
                                    {
                                        try { process.Kill(); } catch { }
                                    }
                                }
                            }
                            return; // Wait for completion
                        }
                        catch (Exception ex)
                        {
                            LogService.LogError("FFmpeg Hatası, doğrudan yönlendirmeye düşülüyor: " + ex.Message, ex);
                            // If ffmpeg fails, fallback to direct TS stream url (might not play in browser, but VLC handles it)
                            url = aceUrl.Replace("/ace/getstream", "/ace/manifest.m3u8").Replace("127.0.0.1", LocalIp);
                        }
                    }

                    response.Redirect(url);
                    CloseResponse(response);
                    return;
                }
            }

            response.StatusCode = 404;
            CloseResponse(response);
        }

        private async Task HandleHomeAsync(HttpListenerResponse response)
        {
            var channels = _databaseService.GetAllChannels();
            
            StringBuilder jsonBuilder = new StringBuilder();
            jsonBuilder.Append("[");
            for (int i = 0; i < channels.Count; i++)
            {
                var ch = channels[i];
                string fallbackLogo = "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSIxNTAiIGhlaWdodD0iODAiPjxwYXRoIGQ9Ik0wIDBoMTUwdjgwaC0xNTB6IiBmaWxsPSIjMzMzIi8+PHRleHQgeD0iNzUiIHk9IjQ1IiBmaWxsPSIjOTk5IiBmb250LWZhbWlseT0ic2Fucy1zZXJpZiIgZm9udC1zaXplPSIxMiIgdGV4dC1hbmNob3I9Im1pZGRsZSI+TmV0U3RyZWFtPC90ZXh0Pjwvc3ZnPg==";
                string logoStr = string.IsNullOrEmpty(ch.LogoUrl) ? fallbackLogo : ch.LogoUrl.Split(',')[0].Trim();
                string safeName = ch.Name?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "") ?? "Kanal";
                string safeGroup = ch.GroupTitle?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "") ?? "Genel";
                string srcType = ch.SourceType ?? "M3U";
                string url = ch.Url?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "").Replace("\r", "") ?? "";
                
                jsonBuilder.Append($"{{\"id\":\"{ch.Id}\", \"name\":\"{safeName}\", \"logo\":\"{logoStr}\", \"group\":\"{safeGroup}\", \"cat\":\"{ch.Category}\", \"srcType\":\"{srcType}\", \"url\":\"{url}\"}}");
                if (i < channels.Count - 1) jsonBuilder.Append(",");
            }
            jsonBuilder.Append("]");
            string jsonChannels = jsonBuilder.ToString();

            string html = $@"<!DOCTYPE html>
<html lang='tr'>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <title>StreamMesh Web Oynatıcı</title>
    <script src='https://cdn.jsdelivr.net/npm/hls.js@latest'></script>
    <style>
        :root {{
            --bg: #0f172a;
            --card-bg: #1e293b;
            --text: #f8fafc;
            --text-muted: #94a3b8;
            --primary: #38bdf8;
        }}
        body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background: var(--bg); color: var(--text); padding: 0; margin: 0; display: flex; flex-direction: column; height: 100vh; overflow: hidden; }}
        header {{ padding: 20px; background: var(--card-bg); display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid #334155; flex-shrink: 0; gap: 10px; flex-wrap: wrap; }}
        header h2 {{ margin: 0; color: var(--primary); font-size: 20px; }}
        #search-box {{ padding: 8px 12px; border-radius: 6px; border: 1px solid #334155; background: #0f172a; color: white; width: 250px; outline: none; }}
        #search-box:focus {{ border-color: var(--primary); }}
        .btn {{ background: #22c55e; color: white; padding: 10px 15px; border-radius: 6px; text-decoration: none; font-weight: bold; font-size: 14px; transition: background 0.2s; white-space: nowrap; }}
        .btn:hover {{ background: #16a34a; }}
        
        .main-content {{ display: flex; flex-direction: column; flex-grow: 1; overflow: hidden; }}
        
        .player-container {{ width: 100%; background: #000; display: flex; flex-direction: column; align-items: center; padding: 20px 0; border-bottom: 1px solid #334155; flex-shrink: 0; }}
        video {{ max-width: 100%; width: 640px; height: 360px; background: #111; border-radius: 8px; box-shadow: 0 4px 6px -1px rgb(0 0 0 / 0.1); border: none; }}
        #current-channel {{ margin-top: 10px; font-weight: bold; color: var(--primary); font-size: 18px; }}

        .grid-container {{ padding: 20px; flex-grow: 1; overflow-y: auto; }}
        .grid {{ display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 15px; }}
        
        .channel-card {{ background: var(--card-bg); border-radius: 8px; padding: 15px; text-align: center; cursor: pointer; transition: transform 0.2s, box-shadow 0.2s; border: 1px solid transparent; }}
        .channel-card:hover {{ transform: scale(1.03); border-color: var(--primary); box-shadow: 0 10px 15px -3px rgb(0 0 0 / 0.1); }}
        .channel-card img {{ max-width: 100%; height: 80px; object-fit: contain; margin-bottom: 10px; border-radius: 4px; }}
        .channel-card h4 {{ margin: 0 0 5px 0; font-size: 16px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }}
        .channel-card p {{ margin: 0 0 10px 0; font-size: 12px; color: var(--text-muted); }}
        .badge {{ background: #334155; color: white; padding: 3px 8px; border-radius: 12px; font-size: 10px; text-transform: uppercase; font-weight: bold; }}
        .load-more {{ background: #334155; color: white; padding: 12px; text-align: center; border-radius: 8px; margin-top: 20px; cursor: pointer; font-weight: bold; transition: background 0.2s; }}
        .load-more:hover {{ background: #475569; }}
    </style>
</head>
<body>
    <header>
        <h2>StreamMesh Web Oynatıcı</h2>
        <input type='text' id='search-box' placeholder='Kanal ara...' onkeyup='filterSearch()'>
        <div>
            <span style='margin-right:15px; color:var(--text-muted); font-size:14px;' id='total-text'>Toplam: 0</span>
            <a href='/playlist.m3u' class='btn'>📂 M3U İndir</a>
        </div>
    </header>

    <div class='main-content'>
        <div class='player-container' id='player-container' style='display: none;'>
            <video id='video' controls autoplay style='display: none;'></video>
            <div id='current-channel'></div>
        </div>

        <div class='grid-container'>
            <div class='grid' id='channel-grid'></div>
            <div id='load-more-btn' class='load-more' onclick='loadMore()' style='display:none;'>Daha Fazla Yükle</div>
        </div>
    </div>

    <script>
        var allChannels = {jsonChannels};
        var filteredChannels = allChannels;
        var currentPage = 1;
        var pageSize = 50;

        document.getElementById('total-text').innerText = 'Toplam: ' + allChannels.length;

        function renderGrid(append) {{
            var grid = document.getElementById('channel-grid');
            if (!append) {{ grid.innerHTML = ''; currentPage = 1; }}
            
            var start = (currentPage - 1) * pageSize;
            var end = Math.min(currentPage * pageSize, filteredChannels.length);
            
            for (var i = start; i < end; i++) {{
                var ch = filteredChannels[i];
                var div = document.createElement('div');
                div.className = 'channel-card';
                div.onclick = (function(c) {{ return function() {{ playChannel(c); }} }})(ch);
                div.innerHTML = ""<img src='"" + ch.logo + ""' onerror=\""this.onerror=null;this.src='data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSIxNTAiIGhlaWdodD0iODAiPjxwYXRoIGQ9Ik0wIDBoMTUwdjgwaC0xNTB6IiBmaWxsPSIjMzMzIi8+PHRleHQgeD0iNzUiIHk9IjQ1IiBmaWxsPSIjOTk5IiBmb250LWZhbWlseT0ic2Fucy1zZXJpZiIgZm9udC1zaXplPSIxMiIgdGV4dC1hbmNob3I9Im1pZGRsZSI+TmV0U3RyZWFtPC90ZXh0Pjwvc3ZnPg==';\"">"" +
                                ""<h4>"" + ch.name + ""</h4>"" +
                                ""<p>"" + ch.group + ""</p>"" +
                                ""<span class='badge'>"" + (ch.cat || 'Diğer') + ""</span> "" +
                                ""<span class='badge' style='background:#0ea5e9;'>"" + (ch.srcType || 'M3U') + ""</span>"";
                grid.appendChild(div);
            }}
            document.getElementById('load-more-btn').style.display = (end < filteredChannels.length) ? 'block' : 'none';
        }}

        function loadMore() {{
            currentPage++;
            renderGrid(true);
        }}

        function filterSearch() {{
            var q = document.getElementById('search-box').value.toLowerCase();
            if (!q) {{
                filteredChannels = allChannels;
            }} else {{
                filteredChannels = allChannels.filter(function(c) {{
                    return c.name.toLowerCase().includes(q) || c.group.toLowerCase().includes(q);
                }});
            }}
            document.getElementById('total-text').innerText = 'Bulunan: ' + filteredChannels.length;
            renderGrid(false);
        }}

        window.onload = function() {{ renderGrid(false); }};

        var video = document.getElementById('video');
        var container = document.getElementById('player-container');
        var channelTitle = document.getElementById('current-channel');
        var hls = null;

        function playChannel(ch) {{
            container.style.display = 'flex';
            channelTitle.innerText = ch.name + ' - Yükleniyor...';
            
            video.style.display = 'block';
            if(hls) {{ hls.destroy(); hls = null; }}
            video.pause();
            video.src = '';
            
            var streamUrl = '/stream?id=' + ch.id;
            
            if (ch.srcType === 'ACESTREAM' || ch.srcType === 'YOUTUBE') {{
                fallbackNative(streamUrl, ch.name);
            }} else {{
                playNativeOrHls(streamUrl, ch.name);
            }}
            
            window.scrollTo(0, 0);
        }}

        function playNativeOrHls(streamUrl, name) {{
            video.style.display = 'block';
            if (Hls.isSupported()) {{
                hls = new Hls();
                hls.loadSource(streamUrl);
                hls.attachMedia(video);
                hls.on(Hls.Events.MANIFEST_PARSED, function() {{
                    channelTitle.innerText = name;
                    video.play().catch(e => console.log('Oynatma hatası:', e));
                }});
                hls.on(Hls.Events.ERROR, function(event, data) {{
                    if (data.fatal) {{
                        console.log('HLS.js fallback');
                        fallbackNative(streamUrl, name);
                    }}
                }});
            }} else if (video.canPlayType('application/vnd.apple.mpegurl')) {{
                fallbackNative(streamUrl, name);
            }} else {{
                fallbackNative(streamUrl, name);
            }}
        }}

        function fallbackNative(url, name) {{
            if(hls) {{ hls.destroy(); }}
            video.src = url;
            video.play().then(() => {{
                channelTitle.innerText = name;
            }}).catch(e => {{
                channelTitle.innerText = name + ' (Oynatılamadı/Cors)';
                console.log('Native playback error:', e);
            }});
        }}
    </script>
</body>
</html>";

            byte[] buffer = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            CloseResponse(response);
        }

        private void CloseResponse(HttpListenerResponse response)
        {
            try { response.OutputStream.Close(); } catch { }
        }
    }
}
