using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using StreamMesh.Models;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public interface IStreamResolver
    {
        bool CanHandle(string url, Channel channel);
        Task<string> ResolveAsync(string url, Channel channel, CancellationToken token, Action<string>? onStatusUpdate = null);
    }

    public class AceStreamResolver : IStreamResolver
    {
        private readonly AceEngine _ace = AceEngine.Instance;

        public bool CanHandle(string url, Channel channel)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return _ace.IsAceStreamUrl(url) || 
                   string.Equals(channel?.SourceType, "ACESTREAM", StringComparison.OrdinalIgnoreCase) || 
                   (url.Length == 40 && Regex.IsMatch(url, @"^[a-fA-F0-9]{40}$"));
        }

        public async Task<string> ResolveAsync(string url, Channel channel, CancellationToken token, Action<string>? onStatusUpdate = null)
        {
            if (url.StartsWith("http://127.0.0.1:6878/ace/", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("http://localhost:6878/ace/", StringComparison.OrdinalIgnoreCase))
            {
                LogService.LogInfo($"[AceStreamResolver] Direct stream -> {url}");
                return url;
            }

            onStatusUpdate?.Invoke("AceStream: Motor Hazırlanıyor...");
            await _ace.StartEngineIfNotRunningAsync().ConfigureAwait(false);
            string hash = _ace.ExtractAceId(url);
            if (string.IsNullOrEmpty(hash)) hash = url;

            bool isChannelId = url.Contains("channel_id=") || (long.TryParse(hash, out _) && hash.Length < 15);
            if (isChannelId)
            {
                onStatusUpdate?.Invoke("AceStream: Kanal Kimliği Çözümleniyor...");
                try
                {
                    string resolved = await _ace.ResolveChannelIdToInfohashAsync(hash).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(resolved) && resolved.Length == 40)
                    {
                        hash = resolved;
                    }
                }
                catch { }
            }

            if (hash.Length == 40 && Regex.IsMatch(hash, "^[a-fA-F0-9]{40}$"))
            {
                onStatusUpdate?.Invoke("AceStream: Akış Başlatılıyor...");
                string pid = _ace.ExtractPid(url);
                string pidParam = !string.IsNullOrWhiteSpace(pid) ? $"&pid={pid}" : "";
                string resolvedUrl = $"http://127.0.0.1:6878/ace/getstream?id={hash}{pidParam}";
                LogService.LogInfo($"[AceStreamResolver] Resolved -> {resolvedUrl}");
                return resolvedUrl;
            }

            LogService.LogWarning($"[AceStreamResolver] Geçersiz AceStream hash ('{hash}').");
            onStatusUpdate?.Invoke("Hata: Geçersiz AceStream kimliği");
            return "";
        }
    }

    public class YouTubeResolver : IStreamResolver
    {
        private readonly YoutubeEngine _yt = new YoutubeEngine();

        public bool CanHandle(string url, Channel channel)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(channel?.SourceType, "YOUTUBE", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<string> ResolveAsync(string url, Channel channel, CancellationToken token, Action<string>? onStatusUpdate = null)
        {
            onStatusUpdate?.Invoke("YouTube: Adres Çözülüyor...");
            string? resolved = await _yt.GetStreamUrlAsync(url).ConfigureAwait(false);
            LogService.LogInfo($"[YouTubeResolver] Resolved -> {resolved ?? url}");
            return resolved ?? url;
        }
    }

    public class HlsTimeshiftResolver : IStreamResolver
    {
        public bool CanHandle(string url, Channel channel)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            bool isVod = channel != null && (
                string.Equals(channel.Category, "VOD", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(channel.Category, "Filmler", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(channel.Category, "Diziler", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".ts", StringComparison.OrdinalIgnoreCase));

            return !isVod && (url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) || url.Contains("extension=ts", StringComparison.OrdinalIgnoreCase));
        }

        public async Task<string> ResolveAsync(string url, Channel channel, CancellationToken token, Action<string>? onStatusUpdate = null)
        {
            try
            {
                HlsProxyEngine.Instance.Start();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(6000);
                var session = await HlsProxyEngine.Instance.InspectAndPrepareHlsAsync(
                    url,
                    channel?.HttpUserAgent,
                    channel?.HttpReferer,
                    channel?.HttpCookie,
                    channel?.HttpOrigin,
                    channel?.CustomHeaders).WaitAsync(cts.Token).ConfigureAwait(false);

                if (session != null && session.Segments.Count > 0)
                {
                    string proxyUrl = HlsProxyEngine.Instance.GetProxyPlaybackUrl(url);
                    LogService.LogInfo($"[HlsTimeshiftResolver] Proxy activated -> {proxyUrl}");
                    return proxyUrl;
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[HlsTimeshiftResolver] Timeshift/Proxy fallback error: {ex.Message}");
            }

            LogService.LogInfo($"[HlsTimeshiftResolver] Direct stream used -> {url}");
            return url;
        }
    }

    public class DirectHttpResolver : IStreamResolver
    {
        public bool CanHandle(string url, Channel channel) => true;

        public Task<string> ResolveAsync(string url, Channel channel, CancellationToken token, Action<string>? onStatusUpdate = null)
        {
            LogService.LogInfo($"[DirectHttpResolver] Direct stream -> {url}");
            return Task.FromResult(url);
        }
    }

    public class StreamResolverRegistry
    {
        private static readonly Lazy<StreamResolverRegistry> _lazy = new(() => new StreamResolverRegistry());
        public static StreamResolverRegistry Instance => _lazy.Value;

        private readonly List<IStreamResolver> _resolvers = new()
        {
            new AceStreamResolver(),
            new YouTubeResolver(),
            new HlsTimeshiftResolver(),
            new DirectHttpResolver()
        };

        private StreamResolverRegistry() { }

        public async Task<string> ResolveStreamAsync(string url, Channel channel, CancellationToken token, Action<string>? onStatusUpdate = null)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            url = url.Trim();

            foreach (var resolver in _resolvers)
            {
                if (resolver.CanHandle(url, channel))
                {
                    try
                    {
                        return await resolver.ResolveAsync(url, channel, token, onStatusUpdate).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LogService.LogWarning($"[StreamResolverRegistry] Resolver {resolver.GetType().Name} failed: {ex.Message}");
                    }
                }
            }

            return url;
        }
    }
}
