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
        private static ServerService _instance;
        public static ServerService Instance => _instance ?? (_instance = new ServerService());

        private Thread _serverThread;
        private bool _isRunning = false;
        public bool IsRunning => _isRunning;
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
                var listener = new TcpListener(IPAddress.Any, Port);
                listener.Start();
                _isRunning = true;

                _serverThread = new Thread(() => Listen(listener));
                _serverThread.IsBackground = true;
                _serverThread.Start();

                LogService.Log($"Server started successfully on port {Port} (Bypass Mode)");
                OnStatusChanged?.Invoke(true, LocalIp, Port.ToString());
            }
            catch (Exception ex)
            {
                LogService.LogError($"Server start error.", ex);
                _isRunning = false;
                OnStatusChanged?.Invoke(false, "", "");
            }
        }

        public void StopServer()
        {
            if (!_isRunning) return;

            _isRunning = false;
            OnStatusChanged?.Invoke(false, "", "");
        }

        private async void Listen(TcpListener listener)
        {
            while (_isRunning)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => ProcessClientAsync(client));
                }
                catch (Exception ex)
                {
                    if (_isRunning) Console.WriteLine("Listen error: " + ex.Message);
                }
            }
            listener.Stop();
        }

        private async Task ProcessClientAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    using (var reader = new System.IO.StreamReader(stream, Encoding.UTF8, true, 4096, true))
                    {
                        string requestLine = await reader.ReadLineAsync();
                        if (string.IsNullOrEmpty(requestLine)) return;

                        string[] parts = requestLine.Split(' ');
                        if (parts.Length < 2) return;

                        string method = parts[0];
                        string fullUrl = parts[1];

                        string path = fullUrl;
                        string idStr = null;

                        if (path.Contains("?"))
                        {
                            var partsUrl = path.Split('?');
                            path = partsUrl[0];
                            var query = partsUrl[1];
                            foreach (var p in query.Split('&'))
                            {
                                if (p.StartsWith("id=")) idStr = p.Substring(3);
                            }
                        }

                        // Read headers until empty line
                        while (true)
                        {
                            string headerLine = await reader.ReadLineAsync();
                            if (string.IsNullOrEmpty(headerLine)) break;
                        }

                        if (path == "/playlist.m3u")
                        {
                            await HandlePlaylistAsync(stream);
                        }
                        else if (path == "/stream")
                        {
                            await HandleStreamAsync(stream, idStr);
                        }
                        else if (path == "/")
                        {
                            await HandleHomeAsync(stream);
                        }
                        else
                        {
                            await WriteErrorAsync(stream, 404, "Not Found");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ProcessClient error: " + ex.Message);
                }
            }
        }

        private async Task WriteHeadersAsync(NetworkStream stream, int statusCode, string statusText, string contentType, Dictionary<string, string> extraHeaders = null)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"HTTP/1.1 {statusCode} {statusText}");
            sb.AppendLine($"Content-Type: {contentType}");
            sb.AppendLine("Connection: close");
            sb.AppendLine("Access-Control-Allow-Origin: *");
            if (extraHeaders != null)
            {
                foreach (var h in extraHeaders)
                    sb.AppendLine($"{h.Key}: {h.Value}");
            }
            sb.AppendLine();
            byte[] headerBytes = Encoding.UTF8.GetBytes(sb.ToString());
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
        }

        private async Task WriteErrorAsync(NetworkStream stream, int statusCode, string statusText)
        {
            await WriteHeadersAsync(stream, statusCode, statusText, "text/plain");
            byte[] body = Encoding.UTF8.GetBytes(statusText);
            await stream.WriteAsync(body, 0, body.Length);
        }

        private async Task HandlePlaylistAsync(NetworkStream stream)
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

            var headers = new Dictionary<string, string>
            {
                { "Content-Length", buffer.Length.ToString() },
                { "Cache-Control", "no-cache, no-store, must-revalidate" }
            };

            await WriteHeadersAsync(stream, 200, "OK", "audio/mpegurl", headers);
            await stream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task HandleStreamAsync(NetworkStream stream, string idStr)
        {
            if (!string.IsNullOrEmpty(idStr))
            {
                var channel = _databaseService.GetChannelById(idStr);
                if (channel != null && !string.IsNullOrEmpty(channel.Url))
                {
                    string url = channel.Url;

                    bool isYoutube = url.Contains("youtube.com") || url.Contains("youtu.be");
                    bool isAceStream = url.StartsWith("acestream://");

                    if (channel.SourceType == "YOUTUBE" || isYoutube)
                    {
                        var directUrl = await _youtubeService.GetSingleMuxedStreamUrlAsync(url);
                        if (!string.IsNullOrEmpty(directUrl)) url = directUrl;
                    }
                    else if (channel.SourceType == "ACESTREAM" || isAceStream)
                    {
                        await _aceStreamService.StartEngineAsync();
                        string contentId = url;
                        if (contentId.StartsWith("acestream://"))
                        {
                            contentId = contentId.Substring("acestream://".Length);
                        }

                        string ffmpegPath = StreamMesh.Services.InventoryService.FFmpegPath;
                        if (System.IO.File.Exists(ffmpegPath))
                        {
                            string inputUrl = $"http://127.0.0.1:6878/ace/getstream?id={contentId}";
                            
                            var responseHeaders = new Dictionary<string, string>
                            {
                                { "Cache-Control", "no-cache, no-store, must-revalidate" },
                                { "Pragma", "no-cache" },
                                { "Expires", "0" },
                                { "Access-Control-Allow-Origin", "*" }
                            };
                            await WriteHeadersAsync(stream, 200, "OK", "video/mp2t", responseHeaders);

                            var startInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = ffmpegPath,
                                Arguments = $"-i \"{inputUrl}\" -c:v copy -c:a aac -y -f mpegts -",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            };

                            using (System.Diagnostics.Process ffmpeg = new System.Diagnostics.Process { StartInfo = startInfo })
                            {
                                ffmpeg.Start();

                                byte[] buffer = new byte[16384];
                                var stdout = ffmpeg.StandardOutput.BaseStream;
                                int bytesRead;

                                while ((bytesRead = await stdout.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                {
                                    try
                                    {
                                        await stream.WriteAsync(buffer, 0, bytesRead);
                                        await stream.FlushAsync();
                                    }
                                    catch
                                    {
                                        break;
                                    }
                                }

                                try { ffmpeg.Kill(); } catch {}
                            }
                            return;
                        }
                        else
                        {
                            using (var httpClient = new System.Net.Http.HttpClient())
                            {
                                string inputUrl = $"http://127.0.0.1:6878/ace/getstream?id={contentId}";
                                try
                                {
                                    var response = await httpClient.GetAsync(inputUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                                    if (response.IsSuccessStatusCode)
                                    {
                                        var responseHeaders = new Dictionary<string, string>
                                        {
                                            { "Cache-Control", "no-cache, no-store, must-revalidate" },
                                            { "Pragma", "no-cache" },
                                            { "Expires", "0" },
                                            { "Access-Control-Allow-Origin", "*" }
                                        };
                                        await WriteHeadersAsync(stream, 200, "OK", "video/mp2t", responseHeaders);
                                        using (var srcStream = await response.Content.ReadAsStreamAsync())
                                        {
                                            byte[] buffer = new byte[16384];
                                            int bytesRead;
                                            while ((bytesRead = await srcStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                            {
                                                try
                                                {
                                                    await stream.WriteAsync(buffer, 0, bytesRead);
                                                    await stream.FlushAsync();
                                                }
                                                catch
                                                {
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                    else
                                        await WriteErrorAsync(stream, 502, "Bad Gateway");
                                }
                                catch
                                {
                                    await WriteErrorAsync(stream, 502, "Bad Gateway Connection Error");
                                }
                            }
                            return;
                        }
                    }

                    // HTTP 302 Redirect
                    var redirectHeaders = new Dictionary<string, string>
                    {
                        { "Location", url }
                    };
                    await WriteHeadersAsync(stream, 302, "Found", "text/plain", redirectHeaders);
                    return;
                }
            }

            await WriteErrorAsync(stream, 404, "Not Found");
        }

        private async Task HandleHomeAsync(NetworkStream stream)
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
    <script src='https://cdn.jsdelivr.net/npm/mpegts.js@latest/dist/mpegts.js'></script>
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
        header h2 {{ margin: 0; color: var(--primary); font-size: 20px; cursor: pointer; text-decoration: none; }}
        #search-box {{ padding: 8px 12px; border-radius: 6px; border: 1px solid #334155; background: #0f172a; color: white; width: 250px; outline: none; }}
        #search-box:focus {{ border-color: var(--primary); }}
        .btn {{ background: #22c55e; color: white; padding: 10px 15px; border-radius: 6px; text-decoration: none; font-weight: bold; font-size: 14px; transition: background 0.2s; white-space: nowrap; cursor: pointer; border: none; }}
        .btn:hover {{ background: #16a34a; }}
        .filter-btn {{ background: #334155; color: var(--text); padding: 8px 12px; border-radius: 6px; font-size: 13px; font-weight: 600; cursor: pointer; border: 1px solid transparent; transition: all 0.2s; margin-right: 5px; }}
        .filter-btn:hover {{ background: #475569; }}
        .filter-btn.active {{ background: var(--primary); color: #0f172a; }}
        .filters {{ display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 20px; }}
        
        .main-content {{ display: flex; flex-direction: column; flex-grow: 1; overflow: hidden; }}
        
        .player-container {{ width: 100%; background: #000; display: flex; flex-direction: column; align-items: center; padding: 20px 0; border-bottom: 1px solid #334155; flex-shrink: 0; }}
        video {{ max-width: 100%; width: 640px; height: 360px; background: #111; border-radius: 8px; box-shadow: 0 4px 6px -1px rgb(0 0 0 / 0.1); border: none; }}
        .player-controls {{ margin-top: 15px; display: flex; gap: 10px; align-items: center; justify-content: center; }}
        #current-channel {{ font-weight: bold; color: var(--primary); font-size: 18px; }}
        .retry-btn {{ display: none; background: #ef4444; color: white; border: none; padding: 8px 15px; border-radius: 6px; cursor: pointer; font-weight: bold; }}
        .retry-btn:hover {{ background: #dc2626; }}

        .grid-container {{ padding: 20px; flex-grow: 1; overflow-y: auto; }}
        .grid {{ display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 15px; }}
        
        .channel-card {{ position: relative; background: var(--card-bg); border-radius: 8px; padding: 15px; text-align: center; cursor: pointer; transition: transform 0.2s, box-shadow 0.2s; border: 1px solid transparent; }}
        .channel-card:hover {{ transform: scale(1.03); border-color: var(--primary); box-shadow: 0 10px 15px -3px rgb(0 0 0 / 0.1); }}
        .channel-card img {{ max-width: 100%; height: 80px; object-fit: contain; margin-bottom: 10px; border-radius: 4px; }}
        .channel-card h4 {{ margin: 0 0 5px 0; font-size: 16px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }}
        .channel-card p {{ margin: 0 0 10px 0; font-size: 12px; color: var(--text-muted); }}
        .badge {{ background: #334155; color: white; padding: 3px 8px; border-radius: 12px; font-size: 10px; text-transform: uppercase; font-weight: bold; }}
        .fav-star {{ position: absolute; top: 10px; right: 10px; font-size: 20px; color: #64748b; background: transparent; border: none; cursor: pointer; padding: 0; line-height: 1; outline: none; transition: transform 0.2s; z-index: 2; }}
        .fav-star:hover {{ transform: scale(1.2); }}
        .fav-star.active {{ color: #eab308; text-shadow: 0 0 5px rgba(234, 179, 8, 0.5); }}
        .load-more {{ background: #334155; color: white; padding: 12px; text-align: center; border-radius: 8px; margin-top: 20px; cursor: pointer; font-weight: bold; transition: background 0.2s; }}
        .load-more:hover {{ background: #475569; }}
    </style>
</head>
<body>
    <header>
        <h2 onclick='closePlayer(); setCategory("""");'>StreamMesh Web Oynatıcı</h2>
        <input type='text' id='search-box' placeholder='Kanal ara...' onkeyup='filterSearch()'>
        <div>
            <span style='margin-right:15px; color:var(--text-muted); font-size:14px;' id='total-text'>Toplam: 0</span>
            <a href='/playlist.m3u' class='btn'>📂 M3U İndir</a>
        </div>
    </header>

    <div class='main-content'>
        <div class='player-container' id='player-container' style='display: none;'>
            <video id='video' controls autoplay style='display: none;'></video>
            <div class='player-controls'>
                <div id='current-channel'></div>
                <button id='retry-btn' class='retry-btn' onclick='retryCurrent()'>Tekrar Dene</button>
            </div>
        </div>

        <div class='grid-container'>
            <div class='filters'>
                <button class='filter-btn active' id='filter-all' onclick='setCategory("""")'>Tümü</button>
                <button class='filter-btn' id='filter-fav' onclick='setCategory(""Fav"")'>Favoriler ⭐</button>
                <button class='filter-btn' id='filter-tv' onclick='setCategory(""TV"")'>TV</button>
                <button class='filter-btn' id='filter-film' onclick='setCategory(""Film"")'>Film</button>
                <button class='filter-btn' id='filter-dizi' onclick='setCategory(""Dizi"")'>Dizi</button>
            </div>
            <div class='grid' id='channel-grid'></div>
            <div id='load-more-btn' class='load-more' onclick='loadMore()' style='display:none;'>Daha Fazla Yükle</div>
        </div>
    </div>

    <script>
        var allChannels = {jsonChannels};
        var filteredChannels = allChannels;
        var currentPage = 1;
        var pageSize = 50;
        var currentCategory = """";
        var favorites = JSON.parse(localStorage.getItem('sm_favorites') || '[]');

        function toggleFavorite(event, id) {{
            event.stopPropagation();
            var index = favorites.indexOf(id);
            if(index > -1) {{ favorites.splice(index, 1); }}
            else {{ favorites.push(id); }}
            localStorage.setItem('sm_favorites', JSON.stringify(favorites));
            renderGrid(false);
        }}

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
                
                var isFav = favorites.includes(ch.id);
                var starClass = isFav ? 'fav-star active' : 'fav-star';
                
                div.innerHTML = ""<button class='"" + starClass + ""' onclick='toggleFavorite(event, \"""" + ch.id + ""\"")' title='Favorilere Ekle/Çıkar'>★</button>"" +
                                ""<img src='"" + ch.logo + ""' onerror=\""this.onerror=null;this.src='data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSIxNTAiIGhlaWdodD0iODAiPjxwYXRoIGQ9Ik0wIDBoMTUwdjgwaC0xNTB6IiBmaWxsPSIjMzMzIi8+PHRleHQgeD0iNzUiIHk9IjQ1IiBmaWxsPSIjOTk5IiBmb250LWZhbWlseT0ic2Fucy1zZXJpZiIgZm9udC1zaXplPSIxMiIgdGV4dC1hbmNob3I9Im1pZGRsZSI+TmV0U3RyZWFtPC90ZXh0Pjwvc3ZnPg==';\"">"" +
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

        function applyFilters() {{
            var q = document.getElementById('search-box').value.toLowerCase();
            filteredChannels = allChannels.filter(function(c) {{
                var matchesSearch = !q || c.name.toLowerCase().includes(q) || c.group.toLowerCase().includes(q);
                var matchesCat = true;
                if (currentCategory === 'Fav') {{
                    matchesCat = favorites.includes(c.id);
                }} else if (currentCategory !== """") {{
                    matchesCat = (c.cat || """").toUpperCase() === currentCategory.toUpperCase();
                }}
                return matchesSearch && matchesCat;
            }});
            document.getElementById('total-text').innerText = 'Bulunan: ' + filteredChannels.length;
            renderGrid(false);
        }}

        function filterSearch() {{ applyFilters(); }}

        function setCategory(cat) {{
            currentCategory = cat;
            document.querySelectorAll('.filter-btn').forEach(b => b.classList.remove('active'));
            if(cat === """") document.getElementById('filter-all').classList.add('active');
            else if(cat === ""Fav"") document.getElementById('filter-fav').classList.add('active');
            else if(cat === ""TV"") document.getElementById('filter-tv').classList.add('active');
            else if(cat === ""Film"") document.getElementById('filter-film').classList.add('active');
            else if(cat === ""Dizi"") document.getElementById('filter-dizi').classList.add('active');
            applyFilters();
        }}
        
        function closePlayer() {{
            video.pause();
            video.src = '';
            if(hls) {{ hls.destroy(); hls = null; }}
            if(mpegtsPlayer) {{ mpegtsPlayer.destroy(); mpegtsPlayer = null; }}
            container.style.display = 'none';
            document.getElementById('retry-btn').style.display = 'none';
            channelTitle.innerText = '';
        }}

        window.onload = function() {{ renderGrid(false); }};

        var video = document.getElementById('video');
        var container = document.getElementById('player-container');
        var channelTitle = document.getElementById('current-channel');
        var retryBtn = document.getElementById('retry-btn');
        var hls = null;
        var mpegtsPlayer = null;
        var currentPlayedChannel = null;

        function retryCurrent() {{
            if (currentPlayedChannel) playChannel(currentPlayedChannel);
        }}

        function playChannel(ch) {{
            currentPlayedChannel = ch;
            container.style.display = 'flex';
            channelTitle.innerText = ch.name + ' - Yükleniyor...';
            retryBtn.style.display = 'none';
            
            video.style.display = 'block';
            if(hls) {{ hls.destroy(); hls = null; }}
            if(mpegtsPlayer) {{ mpegtsPlayer.destroy(); mpegtsPlayer = null; }}
            video.pause();
            video.src = '';
            
            var streamUrl = window.location.origin + '/stream?id=' + ch.id;
            
            if (ch.srcType === 'ACESTREAM') {{
                playMpegTs(streamUrl, ch.name, ch.srcType);
            }} else if (ch.srcType === 'YOUTUBE') {{
                fallbackNative(streamUrl, ch.name, ch.srcType);
            }} else {{
                var lowerUrl = (ch.url || '').toLowerCase();
                if (lowerUrl.includes('.ts') || lowerUrl.includes('mpegts') || ch.srcType === 'TS') {{
                    playMpegTs(streamUrl, ch.name, ch.srcType);
                }} else {{
                    playNativeOrHls(streamUrl, ch.name, ch.srcType);
                }}
            }}
            
            window.scrollTo(0, 0);
        }}

        function playMpegTs(streamUrl, name, srcType) {{
            video.style.display = 'block';
            if (mpegts.getFeatureList().mseLivePlayback) {{
                mpegtsPlayer = mpegts.createPlayer({{
                    type: 'mpegts',
                    isLive: true,
                    url: streamUrl
                }}, {{
                    enableWorker: true,
                    lazyLoadMaxKeepAliveDuration: 10,
                    seekType: 'range'
                }});
                mpegtsPlayer.attachMediaElement(video);
                mpegtsPlayer.load();
                
                var playPromise = mpegtsPlayer.play();
                if (playPromise !== undefined) {{
                    playPromise.then(() => {{
                        channelTitle.innerText = name;
                    }}).catch(e => {{
                        console.log('mpegts.js play error:', e);
                        handleMpegTsError(streamUrl, name, srcType, e);
                    }});
                }}
                
                mpegtsPlayer.on(mpegts.Events.ERROR, function(type, detail, info) {{
                    console.log('mpegts.js error event:', type, detail, info);
                    handleMpegTsError(streamUrl, name, srcType, detail);
                }});
            }} else {{
                fallbackNative(streamUrl, name, srcType);
            }}
        }}

        function handleMpegTsError(streamUrl, name, srcType, err) {{
            if (srcType === 'ACESTREAM') {{
                retryBtn.style.display = 'block';
                channelTitle.innerText = name + ' - AceStream Motoru Başlıyor...';
                setTimeout(() => {{ if (currentPlayedChannel && currentPlayedChannel.name === name) retryCurrent(); }}, 3500);
            }} else {{
                retryBtn.style.display = 'block';
                channelTitle.innerText = name + ' (Oynatılamıyor)';
            }}
        }}

        function playNativeOrHls(streamUrl, name, srcType) {{
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
                        fallbackNative(streamUrl, name, srcType);
                    }}
                }});
            }} else if (video.canPlayType('application/vnd.apple.mpegurl')) {{
                fallbackNative(streamUrl, name, srcType);
            }} else {{
                fallbackNative(streamUrl, name, srcType);
            }}
        }}

        function fallbackNative(url, name, srcType) {{
            if(hls) {{ hls.destroy(); }}
            if(mpegtsPlayer) {{ mpegtsPlayer.destroy(); mpegtsPlayer = null; }}
            video.src = url;
            
            var playPromise = video.play();
            if (playPromise !== undefined) {{
                playPromise.then(() => {{
                    channelTitle.innerText = name;
                }}).catch(e => {{
                    channelTitle.innerText = name + ' (Bekleniyor...)';
                    if (srcType === 'ACESTREAM') {{
                         retryBtn.style.display = 'block';
                         channelTitle.innerText += ' - AceStream Motoru Başlıyor...';
                         setTimeout(() => {{ if (currentPlayedChannel && currentPlayedChannel.name === name) retryCurrent(); }}, 3500);
                    }} else {{
                         retryBtn.style.display = 'block';
                         console.log('Native playback error:', e);
                    }}
                }});
            }}
        }}

        video.addEventListener('error', function(e) {{
            if (currentPlayedChannel) {{
                 retryBtn.style.display = 'block';
                 channelTitle.innerText = currentPlayedChannel.name + ' (Yayın Hatası)';
                 console.log('Video Element Error', e);
            }}
        }});
    </script>
</body>
</html>";

            byte[] buffer = Encoding.UTF8.GetBytes(html);
            var headers = new Dictionary<string, string>
            {
                { "Content-Length", buffer.Length.ToString() }
            };
            await WriteHeadersAsync(stream, 200, "OK", "text/html; charset=utf-8", headers);
            await stream.WriteAsync(buffer, 0, buffer.Length);
        }
    }
}