using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using StreamMesh.Models;
using System.Linq;
using System.IO;
using StreamMesh.Core.Network;

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
        public StreamValidator()
        {
            FlyleafHelper.SafeStart();
        }

        public async Task<ValidationResult> ValidateAsync(Channel channel, ValidationLevel level, IProgress<string>? logger = null, CancellationToken ct = default)
        {
            var result = new ValidationResult();
            // Test the preferred/default URL first
            string url = channel.GetOrderedUrlList().FirstOrDefault() ?? channel.GetUrlList().FirstOrDefault() ?? "";

            if (string.IsNullOrEmpty(url))
            {
                result.IsOnline = false;
                result.Status = "URL Yok";
                return result;
            }

            try
            {
                logger?.Report($"[{channel.PrimaryName}] Hızlı kontrol yapılıyor: {url}");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(6));

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var getResponse = await MediaHttpClient.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                
                if (getResponse.IsSuccessStatusCode)
                {
                    // Check HLS content if it's an m3u8 or video stream
                    bool isHls = url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) || 
                                 (getResponse.Content.Headers.ContentType?.MediaType?.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) ?? false);

                    if (isHls)
                    {
                        using var stream = await getResponse.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
                        byte[] buffer = new byte[1024];
                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false);
                        string headerText = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);

                        // If it's returning HTML error page with 200 OK (e.g. <!DOCTYPE html> or <html>)
                        if (headerText.Contains("<html", StringComparison.OrdinalIgnoreCase) || headerText.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
                        {
                            result.IsOnline = false;
                            result.Status = "Geçersiz Yanıt (HTML Hata Sayfası)";
                            return result;
                        }

                        // Must contain valid HLS tag
                        if (headerText.Contains("#EXTM3U", StringComparison.OrdinalIgnoreCase))
                        {
                            result.IsOnline = true;
                            result.Status = "Erişilebilir (HLS Canlı)";
                        }
                        else
                        {
                            // Some non-standard streams might still be media
                            result.IsOnline = true;
                            result.Status = "Erişilebilir";
                        }
                    }
                    else
                    {
                        result.IsOnline = true;
                        result.Status = "Erişilebilir";
                    }
                }
                else
                {
                    result.IsOnline = false;
                    result.Status = $"Hata: {(int)getResponse.StatusCode} {getResponse.StatusCode}";
                }
            }
            catch (OperationCanceledException)
            {
                result.IsOnline = false;
                result.Status = ct.IsCancellationRequested ? "İptal Edildi" : "Zaman Aşımı (Timeout)";
                result.Error = "İstek zaman aşımına uğradı.";
            }
            catch (Exception ex)
            {
                result.IsOnline = false;
                result.Status = "Erişilemedi";
                result.Error = ex.Message;
            }

            if (level == ValidationLevel.Fast || !result.IsOnline || ct.IsCancellationRequested)
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
                    if (ct.IsCancellationRequested) break;
                    await Task.Delay(1000, ct).ConfigureAwait(false);
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
