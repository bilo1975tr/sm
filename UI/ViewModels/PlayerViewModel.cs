using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using StreamMesh.Models;
using StreamMesh.Core.Media;
using StreamMesh.Core.Network;
using StreamMesh.Core.Database;
using StreamMesh.Core.Utils;

namespace StreamMesh.UI.ViewModels
{
    public class PlayerViewModel : INotifyPropertyChanged
    {
        private readonly YoutubeEngine _yt = new YoutubeEngine();
        private readonly AceEngine _ace = new AceEngine();
        private readonly DatabaseEngine _db = new DatabaseEngine();
        private readonly EpgService _epgService = new EpgService();

        private Channel? _currentChannel;
        public Channel? CurrentChannel
        {
            get => _currentChannel;
            set { _currentChannel = value; OnPropertyChanged(); }
        }

        private List<EpgProgram> _epgList = new();
        public List<EpgProgram> EpgList
        {
            get => _epgList;
            set { _epgList = value; OnPropertyChanged(); }
        }

        private bool _isAudioNormEnabled;
        public bool IsAudioNormEnabled
        {
            get => _isAudioNormEnabled;
            set { _isAudioNormEnabled = value; OnPropertyChanged(); }
        }

        private bool _isVideoEnhanceEnabled;
        public bool IsVideoEnhanceEnabled
        {
            get => _isVideoEnhanceEnabled;
            set { _isVideoEnhanceEnabled = value; OnPropertyChanged(); }
        }

        public PlayerViewModel()
        {
            _isAudioNormEnabled = _db.GetSetting("AudioNormEnabled", "true") == "true";
            _isVideoEnhanceEnabled = _db.GetSetting("VideoEnhanceEnabled", "false") == "true";
        }

        public async Task<List<string>> GetSmartOrderedCandidatesAsync(Channel channel, CancellationToken token, Action<string>? onStatusUpdate = null)
        {
            var rawUrls = channel.GetOrderedUrlList();
            if (rawUrls.Count == 0 && !string.IsNullOrWhiteSpace(channel.Url))
            {
                rawUrls = new List<string> { channel.Url.Trim() };
            }

            if (rawUrls.Count <= 1)
            {
                return rawUrls;
            }

            // 1. If user explicitly specified a preferred URL index (PreferredUrlIndex > 0), respect it immediately without probe delay
            if (channel.PreferredUrlIndex > 0 && channel.PreferredUrlIndex < rawUrls.Count)
            {
                LogService.LogInfo($"[SmartRouter] Channel='{channel.Name}', user selected preferred source at index {channel.PreferredUrlIndex}. Using default source first.");
                return rawUrls; // rawUrls has PreferredUrlIndex at index 0 by GetOrderedUrlList()
            }

            // 2. Default is not an explicit user override or index 0 with multiple candidates: Run fast parallel probe
            LogService.LogInfo($"[SmartRouter] Channel='{channel.Name}', probing {rawUrls.Count} candidate URLs in parallel...");
            onStatusUpdate?.Invoke("En iyi yayın kaynağı seçiliyor...");

            try
            {
                using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                probeCts.CancelAfter(2500); // 2.5s maximum probe window

                var probeTasks = rawUrls.Select(async url =>
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    bool isValid = false;
                    long latency = 99999;

                    try
                    {
                        if (_ace.IsAceStreamUrl(url) || url.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase))
                        {
                            isValid = true;
                            latency = 500;
                        }
                        else if (url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
                        {
                            isValid = true;
                            latency = 300;
                        }
                        else
                        {
                            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
                            if (!string.IsNullOrWhiteSpace(channel.HttpUserAgent))
                            {
                                req.Headers.UserAgent.ParseAdd(channel.HttpUserAgent);
                            }

                            using var resp = await MediaHttpClient.Client.SendAsync(req, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, probeCts.Token).ConfigureAwait(false);
                            sw.Stop();
                            latency = sw.ElapsedMilliseconds;

                            if (resp.IsSuccessStatusCode)
                            {
                                bool isHls = url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) || 
                                             (resp.Content.Headers.ContentType?.MediaType?.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) ?? false);

                                if (isHls)
                                {
                                    using var stream = await resp.Content.ReadAsStreamAsync(probeCts.Token).ConfigureAwait(false);
                                    byte[] buffer = new byte[512];
                                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, probeCts.Token).ConfigureAwait(false);
                                    string headerText = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);

                                    if (!headerText.Contains("<html", StringComparison.OrdinalIgnoreCase) &&
                                        !headerText.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
                                    {
                                        isValid = true;
                                    }
                                }
                                else
                                {
                                    isValid = true;
                                }
                            }
                        }
                    }
                    catch
                    {
                        isValid = false;
                    }

                    return new { Url = url, IsValid = isValid, Latency = latency };
                }).ToList();

                var results = await Task.WhenAll(probeTasks).ConfigureAwait(false);

                // Sort: Valid reachable ones by lowest latency first, then unreachable/backup ones
                var orderedCandidates = results
                    .OrderByDescending(r => r.IsValid)
                    .ThenBy(r => r.Latency)
                    .Select(r => r.Url)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var best = results.FirstOrDefault(r => r.IsValid);
                if (best != null)
                {
                    LogService.LogInfo($"[SmartRouter] Probe completed: Best candidate '{best.Url}' (Latency: {best.Latency}ms). Valid: {results.Count(r => r.IsValid)}/{rawUrls.Count}");
                }
                else
                {
                    LogService.LogWarning($"[SmartRouter] Probe completed: No candidates responded in probe window. Keeping original order for fallback attempts.");
                }

                return orderedCandidates;
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[SmartRouter] Probe error ({ex.Message}), falling back to original URL order.");
                return rawUrls;
            }
        }

        public async Task<string> PrepareSingleStreamUrlAsync(string tryUrl, Channel channel, CancellationToken token, Action<string>? onStatusUpdate = null)
        {
            if (string.IsNullOrWhiteSpace(tryUrl)) return "";
            tryUrl = tryUrl.Trim();

            try
            {
                if (_ace.IsAceStreamUrl(tryUrl) || channel.SourceType == "ACESTREAM")
                {
                    onStatusUpdate?.Invoke("AceStream: Motor Hazırlanıyor...");
                    await _ace.StartEngineAsync().ConfigureAwait(false);
                    string hash = _ace.ExtractHash(tryUrl);
                    await _ace.OpenStreamAsync(hash).ConfigureAwait(false);
                    var aceUrls = await _ace.GetHttpUrlsWithTokenAsync(tryUrl).ConfigureAwait(false);
                    if (aceUrls != null && aceUrls.Count > 0)
                    {
                        tryUrl = aceUrls[0];
                        onStatusUpdate?.Invoke("AceStream: Bağlanılıyor...");
                        bool ready = await _ace.WaitForStreamReadyAsync(tryUrl, 5).ConfigureAwait(false);
                        if (!ready && aceUrls.Count > 1) tryUrl = aceUrls[1];
                    }
                    LogService.LogInfo($"[PLAYBACK] AceStream prepared -> {tryUrl}");
                }
                else if (tryUrl.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || tryUrl.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
                {
                    onStatusUpdate?.Invoke("YouTube: Adres Çözülüyor...");
                    tryUrl = await _yt.GetStreamUrlAsync(tryUrl).ConfigureAwait(false) ?? tryUrl;
                    LogService.LogInfo($"[PLAYBACK] YouTube resolved -> {tryUrl}");
                }
                else if (!tryUrl.Contains(":6878/ace/") && !IsVod(channel))
                {
                    tryUrl = await PrepareTimeshiftAsync(tryUrl, channel, token).ConfigureAwait(false);
                    bool isProxy = tryUrl.Contains("127.0.0.1") || tryUrl.Contains("localhost");
                    if (isProxy)
                    {
                        LogService.LogInfo($"[PLAYBACK] HLS Proxy activated -> {tryUrl}");
                    }
                    else
                    {
                        LogService.LogInfo($"[PLAYBACK] Direct stream used (HLS Proxy not attached) -> {tryUrl}");
                    }
                }

                return tryUrl;
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[PLAYBACK] Error preparing candidate URL '{tryUrl}': {ex.Message}");
                return "";
            }
        }

        public async Task<string> PrepareStreamAsync(Channel channel, CancellationToken token, Action<string> onStatusUpdate)
        {
            var candidates = await GetSmartOrderedCandidatesAsync(channel, token, onStatusUpdate).ConfigureAwait(false);
            if (candidates.Count > 0)
            {
                return await PrepareSingleStreamUrlAsync(candidates[0], channel, token, onStatusUpdate).ConfigureAwait(false);
            }
            return "";
        }

        private async Task<string> PrepareTimeshiftAsync(string url, Channel? channel, CancellationToken token)
        {
            if (url.Contains(".m3u8") || url.Contains("extension=ts"))
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
                        return HlsProxyEngine.Instance.GetProxyPlaybackUrl(url);
                    }
                    else
                    {
                        LogService.LogWarning($"[PLAYBACK] HlsProxyEngine returned null or 0 segments for {url}, falling back to direct stream.");
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // User navigated away or switched channel
                    return "";
                }
                catch (Exception ex)
                {
                    LogService.LogWarning($"[PLAYBACK] HLS Timeshift preparation exception ({ex.Message}), falling back to direct stream: {url}");
                }
            }
            return url;
        }

        public async Task RefreshEpgAsync(Channel channel)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                EpgList = await _epgService.GetChannelEpgHistoryAsync(channel).WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch { EpgList = new List<EpgProgram>(); }
        }

        public bool IsVod(Channel? channel)
        {
            if (channel == null) return false;
            string cat = (channel.Category ?? "").ToLowerInvariant();
            if (cat.Contains("film") || cat.Contains("dizi") || cat.Contains("movie") || cat.Contains("series") || cat.Contains("vod")) return true;
            string url = (channel.Url ?? "").ToLowerInvariant();
            return url.EndsWith(".mp4") || url.EndsWith(".mkv") || url.Contains("/movie/") || url.Contains("/series/");
        }

        public void SaveVodPosition(Channel channel, long positionMs)
        {
            if (channel != null && IsVod(channel))
            {
                channel.LastPositionMs = positionMs;
                _db.SaveChannelSync(channel);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
