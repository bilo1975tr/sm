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
                MaxConnectionsPerServer = 10
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

        public static async Task<HttpResponseMessage> GetAsync(string url, HttpCompletionOption option = HttpCompletionOption.ResponseContentRead, int timeoutSec = 15, CancellationToken ct = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
            return await _client.GetAsync(url, option, cts.Token).ConfigureAwait(false);
        }
    }
}
