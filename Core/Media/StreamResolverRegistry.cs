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

        public Task<string> ResolveAsync(string url, Channel channel, CancellationToken token, Action<string>? onStatusUpdate = null) =>
            ResolveForSlotAsync(url, channel, 0, token, onStatusUpdate);

        public async Task<string> ResolveForSlotAsync(string url, Channel channel, int aceSlotIndex, CancellationToken token, Action<string>? onStatusUpdate = null)
        {
            onStatusUpdate?.Invoke(aceSlotIndex > 0
                ? $"AceStream (Ekran {aceSlotIndex + 1}): İzole Motor Hazırlanıyor..."
                : "AceStream: Motor Hazırlanıyor...");

            int targetPort = await _ace.StartSlotEngineAsync(aceSlotIndex, token).ConfigureAwait(false);
            if (token.IsCancellationRequested) return "";

            if (_ace.IsLocalAceHttpUrl(url))
            {
                string rewritten = Regex.Replace(
                    url,
                    @"^(https?://)(?:127\.0\.0\.1|localhost):\d+",
                    m => $"{m.Groups[1].Value}127.0.0.1:{targetPort}",
                    RegexOptions.IgnoreCase);

                if (aceSlotIndex > 0 && !Regex.IsMatch(rewritten, @"[?&]pid=", RegexOptions.IgnoreCase))
                {
                    rewritten += (rewritten.Contains('?') ? "&" : "?") + $"pid=mv_slot{aceSlotIndex}";
                }

                LogService.LogInfo($"[AceStreamResolver] Slot {aceSlotIndex + 1} doğrudan getstream adresi -> {rewritten}");
                onStatusUpdate?.Invoke("AceStream: P2P Akışı Başlatılıyor...");
                return rewritten;
            }

            string hash = _ace.ExtractAceId(url);
            if (string.IsNullOrEmpty(hash)) hash = url;

            bool isChannelId = url.Contains("channel_id=") || (long.TryParse(hash, out _) && hash.Length < 15);
            if (isChannelId)
            {
                onStatusUpdate?.Invoke("AceStream: Kanal Kimliği Çözümleniyor...");
                try
                {
                    string resolved = await _ace.ResolveChannelIdToInfohashAsync(hash, targetPort).ConfigureAwait(false);
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
                if (string.IsNullOrWhiteSpace(pid) && aceSlotIndex > 0)
                    pid = $"mv_slot{aceSlotIndex}";
                string pidParam = !string.IsNullOrWhiteSpace(pid) ? $"&pid={pid}" : "";
                string resolvedUrl = $"http://127.0.0.1:{targetPort}/ace/getstream?infohash={hash.ToLowerInvariant()}{pidParam}";
                LogService.LogInfo($"[AceStreamResolver] Slot {aceSlotIndex + 1} çözümlendi -> {resolvedUrl}");
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

    public class StalkerStreamResolver : IStreamResolver
    {
        public bool CanHandle(string url, Channel channel)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return url.StartsWith("stalker://", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(channel?.SourceType, "STALKER", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<string> ResolveAsync(string url, Channel channel, CancellationToken token, Action<string>? onStatusUpdate = null)
        {
            onStatusUpdate?.Invoke("Stalker Portal: Yayın bağlantısı alınıyor...");
            string resolved = await StalkerPortalService.ResolveStreamLinkAsync(url, token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                LogService.LogInfo($"[StalkerStreamResolver] Resolved -> {resolved}");
                return resolved;
            }
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
            new StalkerStreamResolver(),
            new DirectHttpResolver()
        };

        private StreamResolverRegistry() { }

        private static bool IsVodOrDirectBinary(string url, Channel? channel)
        {
            string cat = (channel?.Category ?? "").ToLowerInvariant();
            if (cat.Contains("film") || cat.Contains("dizi") || cat.Contains("movie") ||
                cat.Contains("series") || cat.Contains("vod") || cat == "catchup")
                return true;

            string lower = url.ToLowerInvariant();
            if (lower.Contains("127.0.0.1:48931") || lower.Contains("localhost:48931") ||
                AceEngine.Instance.IsLocalAceHttpUrl(url) || lower.Contains("extension=ts") ||
                lower.Contains("/movie/") || lower.Contains("/series/") || lower.Contains("/timeshift/"))
                return true;

            int qIdx = lower.IndexOf('?');
            string pathOnly = qIdx >= 0 ? lower.Substring(0, qIdx) : lower;
            return pathOnly.EndsWith(".mp4") || pathOnly.EndsWith(".mkv") ||
                   pathOnly.EndsWith(".avi") || pathOnly.EndsWith(".mov") || pathOnly.EndsWith(".ts");
        }

        private static bool LooksLikeHlsUrl(string url)
        {
            string lower = url.ToLowerInvariant();
            return lower.Contains(".m3u8") ||
                   lower.Contains("extension=m3u8") ||
                   lower.Contains("/hls/") ||
                   lower.Contains("format=m3u8") ||
                   lower.Contains("type=m3u8");
        }

        public async Task<string> ResolveStreamAsync(string url, Channel channel, CancellationToken token, Action<string>? onStatusUpdate = null, int aceSlotIndex = 0)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            url = url.Trim();

            string resolved = url;
            foreach (var resolver in _resolvers)
            {
                if (resolver.CanHandle(url, channel))
                {
                    try
                    {
                        resolved = resolver is AceStreamResolver aceResolver
                            ? await aceResolver.ResolveForSlotAsync(url, channel, aceSlotIndex, token, onStatusUpdate).ConfigureAwait(false)
                            : await resolver.ResolveAsync(url, channel, token, onStatusUpdate).ConfigureAwait(false);
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogService.LogWarning($"[StreamResolverRegistry] Resolver {resolver.GetType().Name} failed: {ex.Message}");
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(resolved) &&
                resolved.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                !IsVodOrDirectBinary(resolved, channel) &&
                LooksLikeHlsUrl(resolved))
            {
                try
                {
                    var session = await HlsProxyEngine.Instance.InspectAndPrepareHlsAsync(
                        resolved,
                        channel?.HttpUserAgent,
                        channel?.HttpReferer,
                        channel?.HttpCookie,
                        channel?.HttpOrigin,
                        channel?.CustomHeaders).ConfigureAwait(false);

                    if (session != null && session.IsLive && session.Segments.Count > 0)
                    {
                        string proxyUrl = HlsProxyEngine.Instance.GetProxyPlaybackUrl(resolved, -1);
                        LogService.LogInfo($"[StreamResolverRegistry] Live HLS Timeshift proxy active -> {proxyUrl}");
                        return proxyUrl;
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogWarning($"[StreamResolverRegistry] HLS proxy inspect skipped: {ex.Message}");
                }
            }

            return resolved;
        }
    }
}
