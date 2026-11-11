using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media.Ace
{
    /// <summary>
    /// Handles stream URL resolution, content ID hashing, access tokens, and peer metrics.
    /// </summary>
    public static class AceStreamTokenResolver
    {
        private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(6) };
        private static readonly ConcurrentDictionary<string, string> _channelHashCache = new();

        public static async Task<string> ResolveChannelIdToInfohashAsync(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId)) return "";
            if (_channelHashCache.TryGetValue(channelId, out string? cached) && !string.IsNullOrEmpty(cached))
            {
                return cached;
            }
            try
            {
                await AceProcessManager.StartEngineIfNotRunningAsync().ConfigureAwait(false);
                using var cts = new CancellationTokenSource(5000);
                string query = $"{AceProcessManager.AceStreamBaseUrl}/server/api?method=get_content_ids&channel_id={channelId}";
                string json = await HttpClient.GetStringAsync(query, cts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(json) && json.StartsWith("{"))
                {
                    var data = Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(json);
                    var resultToken = data?["result"];
                    if (resultToken != null)
                    {
                        string? resolvedHash = null;
                        if (resultToken.Type == JTokenType.Array && resultToken.HasValues)
                        {
                            resolvedHash = resultToken.First?.ToString();
                        }
                        else if (resultToken.Type == JTokenType.String)
                        {
                            resolvedHash = resultToken.ToString();
                        }
                        else if (resultToken.Type == JTokenType.Object)
                        {
                            var firstProp = resultToken.Children<JProperty>().FirstOrDefault();
                            if (firstProp != null)
                            {
                                resolvedHash = firstProp.Value?.ToString();
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(resolvedHash))
                        {
                            string finalHash = resolvedHash.Trim();
                            if (finalHash.Length == 40)
                            {
                                _channelHashCache[channelId] = finalHash;
                                return finalHash;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[AceStreamResolver] ResolveChannelIdToInfohash error for {channelId}: {ex.Message}");
            }
            return channelId;
        }

        public static async Task<List<string>> GetHttpUrlsWithTokenAsync(string aceUrl)
        {
            if (string.IsNullOrWhiteSpace(aceUrl)) return new List<string>();

            if (aceUrl.StartsWith("http://127.0.0.1:6878/ace/", StringComparison.OrdinalIgnoreCase) ||
                aceUrl.StartsWith("http://localhost:6878/ace/", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { aceUrl };
            }

            string id = AceIdExtractor.ExtractAceId(aceUrl);
            if (string.IsNullOrWhiteSpace(id)) return new List<string>();

            await AceProcessManager.StartEngineIfNotRunningAsync().ConfigureAwait(false);

            if (aceUrl.Contains("channel_id=") || (long.TryParse(id, out _) && id.Length < 15))
            {
                string resolved = await ResolveChannelIdToInfohashAsync(id).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(resolved) && resolved.Length == 40)
                {
                    id = resolved;
                }
            }

            if (id.Length != 40 || !Regex.IsMatch(id, "^[a-fA-F0-9]{40}$"))
            {
                LogService.LogWarning($"[AceStreamResolver] '{id}' geçerli bir 40 haneli Content ID değil. getstream çağrısı engellendi.");
                return new List<string>();
            }

            string pid = AceIdExtractor.ExtractPid(aceUrl);
            string pidParam = !string.IsNullOrWhiteSpace(pid) ? $"&pid={pid}" : "";

            return new List<string>
            {
                $"{AceProcessManager.AceStreamBaseUrl}/ace/getstream?infohash={id.ToLowerInvariant()}{pidParam}"
            };
        }

        public static async Task<string?> GetApiAccessTokenAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(2500);
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{AceProcessManager.AceStreamBaseUrl}/server/api?method=get_api_access_token");
                req.Headers.TryAddWithoutValidation("User-Agent", "StreamMesh/2.1");
                var resp = await HttpClient.SendAsync(req, cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        string trimmed = text.Trim();
                        if (trimmed.StartsWith("{"))
                        {
                            try
                            {
                                var obj = JObject.Parse(trimmed);
                                string? token = (string?)obj["result"]?["token"]
                                    ?? (string?)obj["result"]
                                    ?? (string?)obj["token"]
                                    ?? (string?)obj["data"]?["token"];
                                if (!string.IsNullOrWhiteSpace(token))
                                {
                                    return token.Trim().Trim('"');
                                }
                            }
                            catch { }
                        }
                        return trimmed.Trim().Trim('"');
                    }
                }
            }
            catch { }
            return null;
        }

        public static async Task<bool> WaitForStreamReadyAsync(string httpUrl, int maxWaitSeconds = 6)
        {
            if (string.IsNullOrWhiteSpace(httpUrl)) return false;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(maxWaitSeconds));
                using var req = new HttpRequestMessage(HttpMethod.Get, httpUrl);
                using var resp = await HttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    using var stream = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
                    byte[] buffer = new byte[16 * 1024];
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false);
                    if (read > 0)
                    {
                        LogService.LogInfo($"[AceStreamResolver] Stream data arrived ({read} bytes buffered). Stream is active and ready.");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[AceStreamResolver] WaitForStreamReadyAsync: {ex.Message}");
            }
            return false;
        }

        public static async Task<int> GetStreamPeersAsync(string streamUrl)
        {
            string id = AceIdExtractor.ExtractAceId(streamUrl);
            if (string.IsNullOrWhiteSpace(id)) return 0;

            try
            {
                using var cts = new CancellationTokenSource(1500);
                string query = $"{AceProcessManager.AceStreamBaseUrl}/webui/api/service?method=get_stream_peers&id={id}";
                var resp = await HttpClient.GetStringAsync(query, cts.Token).ConfigureAwait(false);
                if (int.TryParse(resp, out int p)) return p;
            }
            catch { }
            return 0;
        }

        public static async Task StopAllStreamsAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(1500);
                var t1 = HttpClient.GetAsync($"{AceProcessManager.AceStreamBaseUrl}/webui/api/service?method=stop_all", cts.Token);
                var t2 = HttpClient.GetAsync($"{AceProcessManager.AceStreamBaseUrl}/server/api?method=stop_all", cts.Token);
                await Task.WhenAll(t1, t2).ConfigureAwait(false);
            }
            catch { }
        }
    }
}
