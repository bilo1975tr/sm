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
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://*:{Port}/");
                _listener.Start();
                _isRunning = true;

                _serverThread = new Thread(Listen);
                _serverThread.IsBackground = true;
                _serverThread.Start();

                LogService.Log($"Server started successfully at http://*:{Port}/");
                OnStatusChanged?.Invoke(true, LocalIp, Port.ToString());
            }
            catch (Exception ex)
            {
                LogService.LogError($"Server start error (wildcard failed). Trying localhost...", ex);
                try
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://localhost:{Port}/");
                    _listener.Start();
                    _isRunning = true;

                    _serverThread = new Thread(Listen);
                    _serverThread.IsBackground = true;
                    _serverThread.Start();

                    LogService.Log($"Server started on localhost only.");
                    OnStatusChanged?.Invoke(true, "localhost", Port.ToString());
                }
                catch (Exception e2)
                {
                    LogService.LogError("Server fallback failed completely.", e2);
                    _isRunning = false;
                    OnStatusChanged?.Invoke(false, "", "");
                }
            }
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
                        var directUrl = await _youtubeService.GetDirectStreamUrlAsync(url);
                        if (!string.IsNullOrEmpty(directUrl)) url = directUrl;
                    }
                    else if (channel.SourceType == "ACESTREAM")
                    {
                        await _aceStreamService.StartEngineAsync();
                        url = _aceStreamService.GetHttpUrl(url);
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
            
            string html = $@"
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset='utf-8'>
                <meta name='viewport' content='width=device-width, initial-scale=1'>
                <style>
                    body {{ font-family: sans-serif; background: #0f172a; color: white; padding: 20px; }}
                    a {{ color: #38bdf8; text-decoration: none; }}
                    .btn {{ display: inline-block; background: #22c55e; color: white; padding: 10px 20px; border-radius: 8px; text-decoration: none; margin-bottom: 20px; font-weight: bold; }}
                </style>
            </head>
            <body>
                <h2>StreamMesh C# Medya Sunucusu</h2>
                <a href='/playlist.m3u' class='btn'>📂 M3U Listesini İndir / Oynat</a>
                <p>Toplam Kanal: {channels.Count}</p>
                <p>Cihazınızın ağı üzerinden <b>http://{LocalIp}:{Port}/playlist.m3u</b> adresini Smart TVs veya Apple TV uygulamalarınızda kullanabilirsiniz.</p>
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
