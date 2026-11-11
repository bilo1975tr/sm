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
                logger?.Report($"[{channel.PrimaryName}] Doğrulama yapılıyor: {url}");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                int timeoutMs = (level == ValidationLevel.Fast) ? 3500 : 5000;
                cts.CancelAfter(timeoutMs);

                // Asla sadece HEAD isteğine güvenme! Cloudflare/Worker'lar sahte 200 döner.
                // Gerçek GET ile manifest veya medya akışının başını doğrula.
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                
                // Bazı IPTV/Cloudflare sunucuları tarayıcı yerine medya oynatıcı User-Agent bekler
                request.Headers.TryAddWithoutValidation("User-Agent", "VLC/3.0.20 LibVLC/3.0.20");
                request.Headers.TryAddWithoutValidation("Accept", "*/*");

                using var getResponse = await MediaHttpClient.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);

                if (!getResponse.IsSuccessStatusCode)
                {
                    result.IsOnline = false;
                    result.Status = $"Sunucu Hatası: {(int)getResponse.StatusCode} {getResponse.StatusCode}";
                    return result;
                }

                // İlk 2048 baytı oku ve doğrula
                using var stream = await getResponse.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
                byte[] buffer = new byte[2048];
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    result.IsOnline = false;
                    result.Status = "Boş Yanıt (0 Bayt)";
                    return result;
                }

                string headerText = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);

                // 1. HTML HATA SAYFASI / BOT ENGELİ KONTROLÜ (Örn: Cloudflare, 404 sayfaları, JS engelleri)
                if (headerText.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
                    headerText.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                    headerText.Contains("<head", StringComparison.OrdinalIgnoreCase) ||
                    headerText.Contains("<body", StringComparison.OrdinalIgnoreCase) ||
                    headerText.Contains("Google Analytics", StringComparison.OrdinalIgnoreCase) ||
                    headerText.Contains("cf-browser-verification", StringComparison.OrdinalIgnoreCase) ||
                    headerText.Contains("Cloudflare", StringComparison.OrdinalIgnoreCase) && headerText.Contains("Error", StringComparison.OrdinalIgnoreCase))
                {
                    result.IsOnline = false;
                    result.Status = "Geçersiz Akış (HTML Web Sayfası)";
                    return result;
                }

                // 2. HLS / M3U8 AKIŞLARI İÇİN SIKI MANIFEST KONTROLÜ
                bool isHls = url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                             (getResponse.Content.Headers.ContentType?.MediaType?.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) ?? false);

                if (isHls)
                {
                    // Geçerli bir HLS manifesti MUTLAKA #EXTM3U ile başlamalı veya içermelidir
                    if (headerText.Contains("#EXTM3U", StringComparison.OrdinalIgnoreCase))
                    {
                        // Alt segment veya akış etiketlerini de ara (#EXTINF, #EXT-X-STREAM-INF, #EXT-X-TARGETDURATION vb.)
                        if (headerText.Contains("#EXTINF", StringComparison.OrdinalIgnoreCase) ||
                            headerText.Contains("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase) ||
                            headerText.Contains("#EXT-X-TARGETDURATION", StringComparison.OrdinalIgnoreCase) ||
                            headerText.Contains("#EXT-X-MEDIA", StringComparison.OrdinalIgnoreCase))
                        {
                            result.IsOnline = true;
                            result.Status = "Aktif (HLS Canlı)";
                            return result;
                        }
                        else
                        {
                            // Sadece #EXTM3U var ama segment yoksa veya boşsa
                            result.IsOnline = false;
                            result.Status = "Boş HLS Çalma Listesi";
                            return result;
                        }
                    }
                    else
                    {
                        // .m3u8 uzantılı ama #EXTM3U içermiyorsa bu bozuk/ölü bir akıştır
                        result.IsOnline = false;
                        result.Status = "Geçersiz HLS Manifesti";
                        return result;
                    }
                }

                // 3. DOĞRUDAN BİNARY VİDEO / TS / MP4 / RADYO AKIŞLARI
                var contentType = getResponse.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
                if (contentType.StartsWith("video/") || contentType.StartsWith("audio/") || contentType == "application/octet-stream")
                {
                    result.IsOnline = true;
                    result.Status = "Aktif (Medya Akışı)";
                    return result;
                }

                // TS akışı veya binary akış (MPEG-TS sync baytı 0x47 kontrolü)
                if (buffer[0] == 0x47 || (bytesRead > 188 && buffer[188] == 0x47))
                {
                    result.IsOnline = true;
                    result.Status = "Aktif (MPEG-TS Yayını)";
                    return result;
                }

                // Diğer durumlar
                if (bytesRead > 64 && !headerText.Contains("<") && !headerText.Contains("error", StringComparison.OrdinalIgnoreCase))
                {
                    result.IsOnline = true;
                    result.Status = "Aktif";
                }
                else
                {
                    result.IsOnline = false;
                    result.Status = "Bilinmeyen / Geçersiz Medya Biçimi";
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
                // 100ms aralıklarla en fazla 5 saniye bekle; başladığı anda çık
                for (int t = 0; t < 50; t++)
                {
                    if (ct.IsCancellationRequested) break;
                    await Task.Delay(100, ct).ConfigureAwait(false);

                    if (player.Status == Status.Playing)
                    {
                        started = true;
                        if (level == ValidationLevel.Full && player.Video != null && player.Video.Width > 0)
                        {
                            break;
                        }
                        else if (level == ValidationLevel.Detailed)
                        {
                            break;
                        }
                    }
                    else if (player.Status == Status.Failed || player.Status == Status.Ended)
                    {
                        break;
                    }
                }

                if (started)
                {
                    result.Status = "Oynatılabilir";
                    if (level == ValidationLevel.Full && player.Video != null)
                    {
                        result.Resolution = $"{player.Video.Width}x{player.Video.Height}";
                        result.VideoCodec = player.Video.Codec ?? "";
                        result.AudioCodec = player.Audio?.Codec ?? "";
                        result.Status = (string.IsNullOrWhiteSpace(result.Resolution) || result.Resolution == "0x0")
                            ? "Oynatılabilir"
                            : $"Analiz Tamam: {result.Resolution}";
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
