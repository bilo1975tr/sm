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
                LogService.LogInfo($"[AceStreamResolver] Doğrudan getstream adresi kullanılacak -> {url}");
                onStatusUpdate?.Invoke("AceStream: P2P Akışı Başlatılıyor...");
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
                onStatusUpdate?.Invoke("AceStream: P2P Eşleri Aranıyor & Akış Başlatılıyor...");
                string pid = _ace.ExtractPid(url);
                string pidParam = !string.IsNullOrWhiteSpace(pid) ? $"&pid={pid}" : "";
                string resolvedUrl = $"http://127.0.0.1:6878/ace/getstream?infohash={hash.ToLowerInvariant()}{pidParam}";
                LogService.LogInfo($"[AceStreamResolver] Çözümlendi -> {resolvedUrl}");
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
            // Sadece halihazırda yerel Timeshift/Proxy oturumu olan veya açıkça proxy talep eden akışları ele al
            return url.Contains("127.0.0.1:48931") || url.Contains("session=ace_");
        }

        public Task<string> ResolveAsync(string url, Channel channel, CancellationToken token, Action<string>? onStatusUpdate = null)
        {
            LogService.LogInfo($"[HlsTimeshiftResolver] Yerel proxy adresi -> {url}");
            return Task.FromResult(url);
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
