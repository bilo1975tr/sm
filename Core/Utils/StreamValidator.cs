using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using StreamMesh.Models;
using System.Linq;

namespace StreamMesh.Core.Utils
{
    public enum ValidationLevel
    {
        Fast,       // HTTP HEAD/GET Check
        Detailed,   // LibVLC Start Check
        Full        // LibVLC Media Track Analysis (Codec/Resolution)
    }

    public class ValidationResult
    {
        public bool IsOnline { get; set; }
        public string Status { get; set; } = "Unknown";
        public string Resolution { get; set; } = "";
        public string VideoCodec { get; set; } = "";
        public string AudioCodec { get; set; } = "";
        public string Error { get; set; } = "";
    }

    public class StreamValidator : IDisposable
    {
        private readonly LibVLC? _libVLC;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        public StreamValidator()
        {
            try
            {
                // Simple init, assuming core is already initialized by PlayerView or Maintenance
                _libVLC = new LibVLC("--no-audio", "--no-video", "--no-osd");
            }
            catch (Exception ex)
            {
                LogService.LogError("StreamValidator: LibVLC init failed", ex);
            }
        }

        public async Task<ValidationResult> ValidateAsync(Channel channel, ValidationLevel level, IProgress<string>? logger = null)
        {
            var result = new ValidationResult();
            string url = channel.GetUrlList().FirstOrDefault() ?? "";

            if (string.IsNullOrEmpty(url))
            {
                result.IsOnline = false;
                result.Status = "URL Yok";
                return result;
            }

            // --- Fast Test ---
            try
            {
                logger?.Report($"[{channel.PrimaryName}] Hızlı kontrol yapılıyor: {url}");
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    result.IsOnline = true;
                    result.Status = "Erişilebilir";
                }
                else
                {
                    // Fallback to GET just in case HEAD is not allowed
                    using var getResponse = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    result.IsOnline = getResponse.IsSuccessStatusCode;
                    result.Status = result.IsOnline ? "Erişilebilir (GET)" : $"Hata: {getResponse.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                result.IsOnline = false;
                result.Status = "Erişilemedi";
                result.Error = ex.Message;
            }

            if (level == ValidationLevel.Fast || !result.IsOnline || _libVLC == null)
            {
                return result;
            }

            // --- Detailed & Full Test ---
            try
            {
                logger?.Report($"[{channel.PrimaryName}] {(level == ValidationLevel.Full ? "Tam" : "Detaylı")} analiz yapılıyor...");
                using var media = new LibVLCSharp.Shared.Media(_libVLC, new Uri(url));
                using var mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);

                mediaPlayer.Play(media);

                // Wait for playback to start or fail
                bool started = false;
                int timeoutMs = level == ValidationLevel.Full ? 8000 : 5000;
                int checkInterval = 500;

                for (int t = 0; t < timeoutMs; t += checkInterval)
                {
                    await Task.Delay(checkInterval);
                    if (mediaPlayer.IsPlaying)
                    {
                        started = true;
                        break;
                    }
                    if (mediaPlayer.State == VLCState.Error) break;
                }

                if (started)
                {
                    result.Status = "Oynatılabilir";

                    if (level == ValidationLevel.Full)
                    {
                        // Wait a bit more for metadata to populate
                        await Task.Delay(2000);

                        var tracks = media.Tracks;
                        var vTracks = tracks.Where(t => t.TrackType == TrackType.Video).ToList();
                        var aTracks = tracks.Where(t => t.TrackType == TrackType.Audio).ToList();

                        if (vTracks.Count > 0)
                        {
                            var videoTrack = vTracks[0];
                            result.VideoCodec = GetCodecName(videoTrack.Codec);
                            result.Resolution = $"{videoTrack.Data.Video.Width}x{videoTrack.Data.Video.Height}";
                        }

                        if (aTracks.Count > 0)
                        {
                            var audioTrack = aTracks[0];
                            result.AudioCodec = GetCodecName(audioTrack.Codec);
                        }

                        result.Status = $"Analiz Tamam: {result.Resolution} {result.VideoCodec}";
                    }
                }
                else
                {
                    result.IsOnline = false;
                    result.Status = "Yayın Başlatılamadı (Timeout/Error)";
                }

                mediaPlayer.Stop();
            }
            catch (Exception ex)
            {
                result.Error = $"VLC Hatası: {ex.Message}";
            }

            return result;
        }

        private string GetCodecName(uint fourcc)
        {
            byte[] bytes = BitConverter.GetBytes(fourcc);
            // VLC uses Little Endian for FourCC usually, or we can just convert to string
            char[] chars = new char[4];
            for (int i = 0; i < 4; i++)
            {
                chars[i] = (char)bytes[i];
            }
            return new string(chars).Trim();
        }

        public void Dispose()
        {
            _libVLC?.Dispose();
        }
    }
}
