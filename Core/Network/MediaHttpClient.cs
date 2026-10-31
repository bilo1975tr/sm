using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Network
{
    public static class MediaHttpClient
    {
        private static readonly HttpClient _client;

        static MediaHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                MaxConnectionsPerServer = 50
            };

            _client = new HttpClient(handler);
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            _client.Timeout = TimeSpan.FromSeconds(15);
        }

        public static HttpClient Client => _client;

        public static async Task<string> GetStringAsync(string url, int timeoutSec = 15, CancellationToken ct = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
                return await _client.GetStringAsync(url, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"MediaHttpClient: GetString failed for {url}: {ex.Message}");
                throw;
            }
        }

        public static async Task<byte[]> GetByteArrayAsync(string url, int timeoutSec = 20, CancellationToken ct = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
                return await _client.GetByteArrayAsync(url, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"MediaHttpClient: GetByteArray failed for {url}: {ex.Message}");
                throw;
            }
        }

        public static async Task<HttpResponseMessage> GetAsync(string url, HttpCompletionOption option = HttpCompletionOption.ResponseContentRead, int timeoutSec = 15, CancellationToken ct = default, string? customUserAgent = null)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

            var req = new HttpRequestMessage(HttpMethod.Get, url);
            string ua = customUserAgent ?? (url.Contains("get.php") || url.Contains("player_api.php") || url.Contains("type=m3u")
                ? "IPTVSmartersPro/1.0.0 (Linux;Android 11) StreamMesh/1.0"
                : "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 StreamMesh/1.0");
            req.Headers.TryAddWithoutValidation("User-Agent", ua);
            return await _client.SendAsync(req, option, cts.Token).ConfigureAwait(false);
        }

        public static string GetShortUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            try
            {
                var uri = new Uri(url);
                string fn = System.IO.Path.GetFileName(uri.AbsolutePath);
                if (!string.IsNullOrEmpty(fn)) return fn;
                return uri.Host + uri.AbsolutePath;
            }
            catch
            {
                return url.Length > 45 ? "..." + url.Substring(url.Length - 45) : url;
            }
        }

        public static async Task DownloadToStreamWithProgressAsync(
            string url,
            System.IO.Stream targetStream,
            Action<string, double>? progressCallback,
            string operationLabel = "İndiriliyor",
            int timeoutSeconds = 90,
            CancellationToken ct = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            using var response = await GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeoutSeconds, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                progressCallback?.Invoke($"Hata ({response.StatusCode}): {GetShortUrl(url)}", 0);
                throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            long totalBytes = response.Content.Headers.ContentLength ?? -1;
            using var netStream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);

            byte[] buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            var startTime = DateTime.Now;

            while (true)
            {
                using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, readCts.Token);
                bytesRead = await netStream.ReadAsync(buffer, 0, buffer.Length, linkedCts.Token).ConfigureAwait(false);
                if (bytesRead <= 0) break;

                await targetStream.WriteAsync(buffer, 0, bytesRead, cts.Token).ConfigureAwait(false);
                totalRead += bytesRead;

                double elapsed = (DateTime.Now - startTime).TotalSeconds;
                double speedMBs = elapsed > 0 ? (totalRead / 1024.0 / 1024.0) / elapsed : 0;
                double totalMB = totalBytes > 0 ? totalBytes / 1024.0 / 1024.0 : 0;
                double currentMB = totalRead / 1024.0 / 1024.0;

                if (totalBytes > 0)
                {
                    double percent = Math.Min(100.0, (double)totalRead / totalBytes * 100.0);
                    progressCallback?.Invoke($"{operationLabel}: {currentMB:F2} MB / {totalMB:F2} MB (%{percent:F0}) - {speedMBs:F2} MB/s", percent);
                }
                else
                {
                    progressCallback?.Invoke($"{operationLabel}: {currentMB:F2} MB - {speedMBs:F2} MB/s", 50);
                }
            }
        }
    }
}
