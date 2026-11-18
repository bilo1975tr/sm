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
        private readonly AceEngine _ace = AceEngine.Instance;
        private readonly DatabaseEngine _db = DatabaseEngine.Instance;
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

        private string _preferredAudioLanguage = "";
        public string PreferredAudioLanguage
        {
            get => _preferredAudioLanguage;
            set { _preferredAudioLanguage = value; OnPropertyChanged(); }
        }

        public PlayerViewModel()
        {
            string audioNormVal = _db.GetSetting("AudioNormEnabled", _db.GetSetting("FlyleafAudioNorm", "false"));
            string videoEnhanceVal = _db.GetSetting("VideoEnhanceEnabled", _db.GetSetting("FlyleafSharpen", "false"));
            _isAudioNormEnabled = audioNormVal == "true";
            _isVideoEnhanceEnabled = videoEnhanceVal == "true";
            _preferredAudioLanguage = _db.GetSetting("PreferredAudioLanguage", "");
        }

        public string GetPreferredLanguageForChannel(string channelId)
        {
            if (string.IsNullOrEmpty(channelId)) return _preferredAudioLanguage;
            string channelLang = _db.GetSetting($"ChannelAudioLang_{channelId}", "");
            return !string.IsNullOrEmpty(channelLang) ? channelLang : _preferredAudioLanguage;
        }

        public void SavePreferredLanguageForChannel(string channelId, string langCode)
        {
            if (string.IsNullOrEmpty(langCode)) return;
            _preferredAudioLanguage = langCode;
            _db.SetSetting("PreferredAudioLanguage", langCode);
            if (!string.IsNullOrEmpty(channelId))
            {
                _db.SetSetting($"ChannelAudioLang_{channelId}", langCode);
            }
        }

        public async Task<List<string>> GetSmartOrderedCandidatesAsync(Channel channel, CancellationToken token, Action<string>? onStatusUpdate = null)
        {
            var rawUrls = channel.GetOrderedUrlList();
            if (rawUrls.Count == 0 && !string.IsNullOrWhiteSpace(channel.Url))
            {
                rawUrls = new List<string> { channel.Url.Trim() };
            }

            // ÖZEL ACESTREAM YÖNETİMİ:
            // Eğer kanal bir AceStream yayını ise (SourceType == ACESTREAM veya URL'lerden biri acestream:// içeriyorsa),
            // asla alternatif veya yapay URL türetilmez, paralel HTTP probu yapılmaz.
            // Sadece tek ve deterministik AceStream getstream URL'si döndürülür!
            bool isAceChannel = string.Equals(channel.SourceType, "ACESTREAM", StringComparison.OrdinalIgnoreCase) ||
                                rawUrls.Any(u => _ace.IsAceStreamUrl(u));

            if (isAceChannel)
            {
                var aceCandidates = new List<string>();
                foreach (var u in rawUrls)
                {
                    if (string.IsNullOrWhiteSpace(u)) continue;
                    string trimmed = u.Trim();

                    // Zaten tam getstream adresi ise doğrudan kullan
                    if (trimmed.StartsWith("http://127.0.0.1:6878/ace/", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("http://localhost:6878/ace/", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!aceCandidates.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                            aceCandidates.Add(trimmed);
                        continue;
                    }

                    string hash = _ace.ExtractAceId(trimmed);
                    if (string.IsNullOrEmpty(hash)) continue;

                    // Sayısal channel_id ise çöz
                    if (trimmed.Contains("channel_id=") || (long.TryParse(hash, out _) && hash.Length < 15))
                    {
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

                    if (hash.Length == 40 && System.Text.RegularExpressions.Regex.IsMatch(hash, "^[a-fA-F0-9]{40}$"))
                    {
                        string pid = _ace.ExtractPid(trimmed);
                        string pidParam = !string.IsNullOrWhiteSpace(pid) ? $"&pid={pid}" : "";
                        string canonicalUrl = $"http://127.0.0.1:6878/ace/getstream?infohash={hash.ToLowerInvariant()}{pidParam}";
                        if (!aceCandidates.Contains(canonicalUrl, StringComparer.OrdinalIgnoreCase))
                        {
                            aceCandidates.Add(canonicalUrl);
                        }
                    }
                }

                if (aceCandidates.Count > 0)
                {
                    LogService.LogInfo($"[SmartRouter] AceStream channel '{channel.Name}' resolved directly to canonical URL: {aceCandidates[0]}");
                    return aceCandidates;
                }
            }

            // IPTV / Xtream canlı yayınları ve VOD akışları için sıralı aday listesi (Tek bağlantı koruması)
            var expandedUrls = new List<string>();
            foreach (var u in rawUrls)
            {
                if (string.IsNullOrWhiteSpace(u)) continue;
                string trimmed = u.Trim();
                if (!expandedUrls.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    expandedUrls.Add(trimmed);
                }

                if (VodSmartResolver.IsVodCdnUrl(trimmed))
                {
                    var vodCandidates = VodSmartResolver.GenerateVodCandidates(trimmed);
                    foreach (var vc in vodCandidates)
                    {
                        if (!expandedUrls.Contains(vc, StringComparer.OrdinalIgnoreCase))
                        {
                            expandedUrls.Add(vc);
                        }
                    }
                }
            }
            rawUrls = expandedUrls;

            // DİKKAT: IPTV sağlayıcıları çoğunlukla tek eşzamanlı bağlantı (max_connections = 1) kuralına sahiptir.
            // Arka planda aynı anda birden fazla paralel HTTP GET/HEAD prob isteği göndermek sağlayıcı sunucusunda
            // hesabı anında kilitlemekte veya HTTP 403 / 429 döndürmektedir.
            // Bu sebeple paralel prob tamamen devre dışı bırakılmış olup, kanaldaki öncelikli/orijinal URL sırasıyla
            // doğrudan oynatıcıya sunulur (Sequential direct connection).
            LogService.LogInfo($"[SmartRouter] Channel='{channel.Name}', {rawUrls.Count} kaynak hazırlandı (Tek bağlantı koruması aktif, prob yok).");
            return rawUrls;
        }

        public async Task<string> PrepareSingleStreamUrlAsync(string tryUrl, Channel channel, CancellationToken token, Action<string>? onStatusUpdate = null, int aceSlotIndex = 0)
        {
            if (string.IsNullOrWhiteSpace(tryUrl)) return "";
            try
            {
                return await StreamResolverRegistry.Instance.ResolveStreamAsync(tryUrl, channel, token, onStatusUpdate, aceSlotIndex).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[PLAYBACK] Error preparing candidate URL '{tryUrl}': {ex.Message}");
                return "";
            }
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
            if (cat.Contains("film") || cat.Contains("dizi") || cat.Contains("movie") || cat.Contains("series") || cat.Contains("vod") || cat == "catchup") return true;
            string url = (channel.Url ?? "").ToLowerInvariant();
            return url.EndsWith(".mp4") || url.EndsWith(".mkv") || url.Contains("/movie/") || url.Contains("/series/");
        }

        public void SaveVodPosition(Channel channel, long positionMs)
        {
            if (channel != null && IsVod(channel) && !(channel.Id?.StartsWith("catchup_", StringComparison.Ordinal) ?? false))
            {
                channel.LastPositionMs = positionMs;
                _ = _db.SaveChannelLogoAndPosAsync(channel);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
