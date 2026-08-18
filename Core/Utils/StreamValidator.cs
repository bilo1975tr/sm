using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using StreamMesh.Models;
using System.Linq;
using System.IO;

namespace StreamMesh.Core.Utils
{
    public enum ValidationLevel
    {
        Fast,
        Detailed,
        Full
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
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        public StreamValidator()
        {
            FlyleafHelper.SafeStart();
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

            try
            {
                logger?.Report($"[{channel.PrimaryName}] Hızlı kontrol yapılıyor: {url}");
                using var getResponse = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                result.IsOnline = getResponse.IsSuccessStatusCode;
                result.Status = result.IsOnline ? "Erişilebilir" : $"Hata: {getResponse.StatusCode}";
            }
            catch (Exception ex)
            {
                result.IsOnline = false;
                result.Status = "Erişilemedi";
                result.Error = ex.Message;
            }

            if (level == ValidationLevel.Fast || !result.IsOnline)
            {
                return result;
            }

            try
            {
                logger?.Report($"[{channel.PrimaryName}] Flyleaf ile analiz yapılıyor...");

                using var player = new Player();
                player.Open(url);

                bool started = false;
                for (int t = 0; t < 10; t++)
                {
                    await Task.Delay(1000);
                    if (player.Status == Status.Playing) { started = true; break; }
                }

                if (started)
                {
                    result.Status = "Oynatılabilir";
                    if (level == ValidationLevel.Full)
                    {
                        result.Resolution = $"{player.Video.Width}x{player.Video.Height}";
                        result.VideoCodec = player.Video.Codec;
                        result.Status = $"Analiz Tamam: {result.Resolution}";
                    }
                }
                else
                {
                    result.IsOnline = false;
                    result.Status = "Yayın Başlatılamadı (Timeout)";
                }
                player.Stop();
            }
            catch (Exception ex) { result.Error = $"Flyleaf Hatası: {ex.Message}"; }

            return result;
        }

        public void Dispose() { }
    }
}
