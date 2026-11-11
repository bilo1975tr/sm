using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using StreamMesh.Core.Network;
using StreamMesh.Models;

namespace StreamMesh.Core.Media.Iptv
{
    /// <summary>
    /// Performs fast network verification of IPTV endpoints and account credentials.
    /// </summary>
    public static class IptvAccountVerifier
    {
        public static async Task<bool> FastCheckHostIsIptvAsync(IptvAccountInfo? account, int timeoutMs = 1500)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.HostWithPort))
                return false;

            string host = IptvUrlParser.ExtractHostAndPort(account.HostWithPort).ToLowerInvariant();
            if (IptvHostFilter.IsIgnoredHost(host)) return false;

            string scheme = account.HostWithPort.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || 
                            account.HostWithPort.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "" : "http://";
            string hostTrimmed = account.HostWithPort.TrimEnd('/');

            // 1. player_api.php testi
            try
            {
                string apiUrl = $"{scheme}{hostTrimmed}/player_api.php?username={Uri.EscapeDataString(account.Username)}&password={Uri.EscapeDataString(account.Password)}";
                using var cts = new CancellationTokenSource(timeoutMs);
                using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                req.Headers.TryAddWithoutValidation("User-Agent", "IPTVSmartersPro/1.0.0 (Linux;Android 11) StreamMesh/1.0");

                using var resp = await MediaHttpClient.Client.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        string trimmed = body.TrimStart();
                        if (trimmed.StartsWith("{") && (body.Contains("\"user_info\"") || body.Contains("\"auth\"")))
                        {
                            return true;
                        }
                        if (trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
                            body.Contains("<body", StringComparison.OrdinalIgnoreCase))
                        {
                            IptvHostFilter.MarkAccountFailed(account, markHostFailed: true);
                            return false;
                        }
                    }
                }
            }
            catch
            {
                IptvHostFilter.MarkAccountFailed(account, markHostFailed: true);
                return false;
            }

            // 2. get.php testi (M3U başlığı kontrolü)
            try
            {
                string m3uUrl = $"{scheme}{hostTrimmed}/get.php?username={Uri.EscapeDataString(account.Username)}&password={Uri.EscapeDataString(account.Password)}&type=m3u_plus";
                using var cts = new CancellationTokenSource(timeoutMs);
                using var req = new HttpRequestMessage(HttpMethod.Get, m3uUrl);
                req.Headers.TryAddWithoutValidation("User-Agent", "IPTVSmartersPro/1.0.0 (Linux;Android 11) StreamMesh/1.0");

                using var resp = await MediaHttpClient.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    byte[] buf = new byte[512];
                    int read = await stream.ReadAsync(buf, 0, buf.Length, cts.Token).ConfigureAwait(false);
                    if (read > 0)
                    {
                        string snippet = System.Text.Encoding.UTF8.GetString(buf, 0, read);
                        if (snippet.Contains("#EXTM3U") || snippet.Contains("#EXTINF"))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }

            IptvHostFilter.MarkAccountFailed(account, markHostFailed: true);
            return false;
        }

        public static async Task<bool> FastVerifyAccountAsync(IptvAccountInfo? account, int timeoutMs = 1500)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.Username) || string.IsNullOrWhiteSpace(account.Password))
                return false;

            if (IptvHostFilter.IsFailedAccount(account)) return false;

            string scheme = account.HostWithPort.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || 
                            account.HostWithPort.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "" : "http://";
            string host = account.HostWithPort.TrimEnd('/');

            bool hostUnreachable = false;
            bool isNotIptv = false;

            try
            {
                string apiUrl = $"{scheme}{host}/player_api.php?username={Uri.EscapeDataString(account.Username)}&password={Uri.EscapeDataString(account.Password)}";
                using var cts = new CancellationTokenSource(timeoutMs);
                using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                req.Headers.TryAddWithoutValidation("User-Agent", "IPTVSmartersPro/1.0.0 (Linux;Android 11) StreamMesh/1.0");

                using var resp = await MediaHttpClient.Client.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        string trimmed = body.TrimStart();
                        if (trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
                        {
                            isNotIptv = true;
                        }
                        else if (trimmed.StartsWith("{") && body.Contains("\"user_info\""))
                        {
                            if (body.Contains("\"auth\":1") || body.Contains("\"auth\": 1") || body.Contains("\"status\":\"Active\""))
                            {
                                return true;
                            }
                            else
                            {
                                IptvHostFilter.MarkAccountFailed(account, markHostFailed: false);
                                return false;
                            }
                        }
                    }
                }
                else if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    isNotIptv = true;
                }
            }
            catch (OperationCanceledException)
            {
                hostUnreachable = true;
            }
            catch (HttpRequestException)
            {
                hostUnreachable = true;
            }
            catch { }

            if (!hostUnreachable && !isNotIptv)
            {
                try
                {
                    string m3uUrl = $"{scheme}{host}/get.php?username={Uri.EscapeDataString(account.Username)}&password={Uri.EscapeDataString(account.Password)}&type=m3u_plus";
                    using var cts = new CancellationTokenSource(timeoutMs);
                    using var req = new HttpRequestMessage(HttpMethod.Get, m3uUrl);
                    req.Headers.TryAddWithoutValidation("User-Agent", "IPTVSmartersPro/1.0.0 (Linux;Android 11) StreamMesh/1.0");

                    using var resp = await MediaHttpClient.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                        byte[] buf = new byte[512];
                        int read = await stream.ReadAsync(buf, 0, buf.Length, cts.Token).ConfigureAwait(false);
                        if (read > 0)
                        {
                            string snippet = System.Text.Encoding.UTF8.GetString(buf, 0, read);
                            if (snippet.Contains("#EXTM3U") || snippet.Contains("#EXTINF"))
                            {
                                return true;
                            }
                        }
                    }
                    else if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        isNotIptv = true;
                    }
                }
                catch { }
            }

            IptvHostFilter.MarkAccountFailed(account, markHostFailed: hostUnreachable || isNotIptv);
            return false;
        }
    }
}
