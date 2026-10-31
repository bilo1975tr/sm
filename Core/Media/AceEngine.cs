using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using StreamMesh.Core.Network;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class AceEngine
    {
        private const string AceStreamBaseUrl = "http://127.0.0.1:6878";
        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        private static readonly SemaphoreSlim _startLock = new SemaphoreSlim(1, 1);
        private static int _isProcessSpawning = 0;

        public static AceEngine Instance { get; } = new AceEngine();

        public static string GetEngineExecutablePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string standardPath = Path.Combine(appData, "ACEStream", "engine", "ace_engine.exe");
            if (File.Exists(standardPath)) return standardPath;

            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string progPath = Path.Combine(progFiles, "ACEStream", "engine", "ace_engine.exe");
            if (File.Exists(progPath)) return progPath;

            string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string progX86Path = Path.Combine(progFilesX86, "ACEStream", "engine", "ace_engine.exe");
            if (File.Exists(progX86Path)) return progX86Path;

            return standardPath;
        }

        public static bool IsAnyAceProcessRunning()
        {
            try
            {
                var procs = Process.GetProcesses();
                foreach (var p in procs)
                {
                    try
                    {
                        string name = p.ProcessName.ToLowerInvariant();
                        if (name.Contains("ace_engine") || name.Contains("acestream") || name.Contains("ace_player") || name.Contains("ace_web"))
                        {
                            return true;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return false;
        }

        public static int GetAceProcessCount()
        {
            int count = 0;
            try
            {
                var procs = Process.GetProcesses();
                foreach (var p in procs)
                {
                    try
                    {
                        string name = p.ProcessName.ToLowerInvariant();
                        if (name.Contains("ace_engine") || name.Contains("acestream") || name.Contains("ace_player") || name.Contains("ace_web"))
                        {
                            count++;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return count;
        }

        public async Task<bool> IsEngineHttpAliveAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(1200);
                var resp = await HttpClient.GetAsync($"{AceStreamBaseUrl}/server/api?method=get_version", cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { }

            try
            {
                using var cts = new CancellationTokenSource(1200);
                var resp = await HttpClient.GetAsync($"{AceStreamBaseUrl}/webui/api/service?method=get_version", cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { }

            try
            {
                using var cts = new CancellationTokenSource(1200);
                var resp = await HttpClient.GetAsync($"{AceStreamBaseUrl}/", cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { }

            return false;
        }

        public async Task<bool> IsEngineRunningAsync()
        {
            if (await IsEngineHttpAliveAsync().ConfigureAwait(false)) return true;
            return IsAnyAceProcessRunning();
        }

        public async Task<bool> StartEngineIfNotRunningAsync()
        {
            // 1. HTTP API halihazırda yanıt veriyorsa hiçbir işlem yapma
            if (await IsEngineHttpAliveAsync().ConfigureAwait(false)) return true;

            // 2. Hafızada çalışan herhangi bir ace_engine süreci varsa ASLA yeni kopya başlatma
            if (IsAnyAceProcessRunning())
            {
                LogService.LogInfo("[ACEENGINE] AceStream süreci zaten aktif. Yeni kopya başlatılmayacak, API yanıtı bekleniyor...");
                for (int i = 0; i < 10; i++)
                {
                    await Task.Delay(500).ConfigureAwait(false);
                    if (await IsEngineHttpAliveAsync().ConfigureAwait(false)) return true;
                }
                return true;
            }

            await _startLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Kilit altında tekrar kontrol
                if (await IsEngineHttpAliveAsync().ConfigureAwait(false)) return true;
                if (IsAnyAceProcessRunning()) return true;

                // Atomik başlatma bayrağı kontrolü (Mükerrer Process.Start çağrılarını tamamen engeller)
                if (Interlocked.CompareExchange(ref _isProcessSpawning, 1, 0) != 0)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        await Task.Delay(500).ConfigureAwait(false);
                        if (await IsEngineHttpAliveAsync().ConfigureAwait(false)) return true;
                    }
                    return IsAnyAceProcessRunning();
                }

                try
                {
                    string exePath = GetEngineExecutablePath();
                    if (File.Exists(exePath))
                    {
                        LogService.LogInfo($"[ACEENGINE] Starting AceStream engine executable once: {exePath}");
                        var psi = new ProcessStartInfo
                        {
                            FileName = exePath,
                            UseShellExecute = true,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        Process.Start(psi);

                        for (int i = 0; i < 12; i++)
                        {
                            await Task.Delay(600).ConfigureAwait(false);
                            if (await IsEngineHttpAliveAsync().ConfigureAwait(false))
                            {
                                LogService.LogInfo("[ACEENGINE] AceStream engine successfully started and ready.");
                                return true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("[ACEENGINE] Failed to start ace_engine.exe", ex);
                }
                finally
                {
                    Interlocked.Exchange(ref _isProcessSpawning, 0);
                }

                return IsAnyAceProcessRunning();
            }
            finally
            {
                _startLock.Release();
            }
        }

        public async Task<bool> StartEngineAsync()
        {
            return await StartEngineIfNotRunningAsync().ConfigureAwait(false);
        }

        public async Task<bool> StartAceEngineAsync()
        {
            return await StartEngineIfNotRunningAsync().ConfigureAwait(false);
        }

        public bool IsAceStreamUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            string trimmed = url.Trim();
            if (trimmed.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.Contains(":6878/ace/") || trimmed.Contains(":6878/content_id/")) return true;
            if (trimmed.Contains("channel_id=")) return true;
            if (trimmed.Length == 40 && Regex.IsMatch(trimmed, "^[a-fA-F0-9]{40}$")) return true;
            return false;
        }

        public bool IsAceStream(string? url)
        {
            return IsAceStreamUrl(url);
        }

        public async Task<string> ResolveChannelIdToInfohashAsync(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId)) return "";
            try
            {
                await StartEngineIfNotRunningAsync().ConfigureAwait(false);
                using var cts = new CancellationTokenSource(3500);
                string query = $"{AceStreamBaseUrl}/server/api?method=get_content_ids&channel_id={channelId}";
                string json = await HttpClient.GetStringAsync(query, cts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(json) && json.StartsWith("{"))
                {
                    var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(json);
                    var resultToken = data?["result"];
                    if (resultToken != null)
                    {
                        if (resultToken.Type == Newtonsoft.Json.Linq.JTokenType.Array && resultToken.HasValues)
                        {
                            string? hash = resultToken.First?.ToString();
                            if (!string.IsNullOrWhiteSpace(hash)) return hash.Trim();
                        }
                        else if (resultToken.Type == Newtonsoft.Json.Linq.JTokenType.String)
                        {
                            string? hash = resultToken.ToString();
                            if (!string.IsNullOrWhiteSpace(hash)) return hash.Trim();
                        }
                        else if (resultToken.Type == Newtonsoft.Json.Linq.JTokenType.Object)
                        {
                            // Bazı API sürümlerinde {"result": {"24785": "9b12..."}} döner
                            var firstProp = resultToken.Children<Newtonsoft.Json.Linq.JProperty>().FirstOrDefault();
                            if (firstProp != null)
                            {
                                string? hash = firstProp.Value?.ToString();
                                if (!string.IsNullOrWhiteSpace(hash)) return hash.Trim();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[ACEENGINE] ResolveChannelIdToInfohash error for {channelId}: {ex.Message}");
            }
            return channelId; // Fallback to original if API lookup fails
        }

        public string ExtractPid(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            var match = Regex.Match(url, @"[?&]pid=([^&]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        public string ExtractAceId(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            string trimmed = url.Trim();

            // 1. Any 40-hex hash in id=, content_id=, infohash=
            var match40 = Regex.Match(trimmed, @"[?&](?:id|content_id|infohash)=([a-fA-F0-9]{40})", RegexOptions.IgnoreCase);
            if (match40.Success) return match40.Groups[1].Value.ToLowerInvariant();

            // 2. Channel ID in query (Sadece AceStream URL veya :6878 portu ise)
            if (trimmed.Contains(":6878") || trimmed.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase))
            {
                var matchChannel = Regex.Match(trimmed, @"[?&](?:channel_id|channel)=(\d+)", RegexOptions.IgnoreCase);
                if (matchChannel.Success) return matchChannel.Groups[1].Value;
            }

            // 3. acestream:// scheme
            if (trimmed.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase))
            {
                string afterScheme = trimmed.Substring("acestream://".Length).Trim();
                if (afterScheme.StartsWith("?channel_id=", StringComparison.OrdinalIgnoreCase))
                {
                    return afterScheme.Substring("?channel_id=".Length);
                }
                var m = Regex.Match(afterScheme, @"^([a-fA-F0-9]{40})");
                if (m.Success) return m.Groups[1].Value.ToLowerInvariant();
                if (afterScheme.Length == 40 && Regex.IsMatch(afterScheme, "^[a-fA-F0-9]{40}$"))
                    return afterScheme.ToLowerInvariant();
            }

            // 4. Standalone 40-character hex hash (Tek başına hash girilmişse)
            if (trimmed.Length == 40 && Regex.IsMatch(trimmed, @"^[a-fA-F0-9]{40}$"))
            {
                return trimmed.ToLowerInvariant();
            }

            // AceStream adresi veya kimliği değilse boş dön (Asla tüm URL'yi veya rastgele ?id= parametresini hash sayma!)
            return "";
        }

        public string ExtractHash(string url)
        {
            return ExtractAceId(url);
        }

        public async Task<List<string>> GetHttpUrlsWithTokenAsync(string aceUrl)
        {
            if (string.IsNullOrWhiteSpace(aceUrl)) return new List<string>();

            // 1. Eğer zaten tam bir AceStream getstream URL'si ise ASLA tekrar sarmalama!
            if (aceUrl.StartsWith("http://127.0.0.1:6878/ace/", StringComparison.OrdinalIgnoreCase) ||
                aceUrl.StartsWith("http://localhost:6878/ace/", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { aceUrl };
            }

            string id = ExtractAceId(aceUrl);
            if (string.IsNullOrWhiteSpace(id)) return new List<string>();

            // Ensure engine is alive
            await StartEngineIfNotRunningAsync().ConfigureAwait(false);

            // Eğer sayısal channel_id ise öncelikle gerçek 40 haneli Content ID'ye çözümle
            if (aceUrl.Contains("channel_id=") || (long.TryParse(id, out _) && id.Length < 15))
            {
                string resolved = await ResolveChannelIdToInfohashAsync(id).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(resolved) && resolved.Length == 40)
                {
                    id = resolved;
                }
            }

            // DİKKAT: id hala 40 karakterli bir hex hash değilse (örneğin 24785 gibi sayısal kaldıysa),
            // AceStream motoru /ace/getstream?id=24785 isteğinde 500 hatası verir.
            if (id.Length != 40 || !Regex.IsMatch(id, "^[a-fA-F0-9]{40}$"))
            {
                LogService.LogWarning($"[ACEENGINE] '{id}' geçerli bir 40 haneli Content ID değil. Motor 500 hatası vermemesi için getstream çağrısı engellendi.");
                return new List<string>();
            }

            // Orijinal URL'de pid varsa koru; yoksa ekleme (yayın pid olmadan da başlıyor)
            string pid = ExtractPid(aceUrl);
            string pidParam = !string.IsNullOrWhiteSpace(pid) ? $"&pid={pid}" : "";

            // AceStream motoru için doğru ve uyumlu format: id={id}
            var urls = new List<string>
            {
                $"{AceStreamBaseUrl}/ace/getstream?id={id}{pidParam}",
                $"{AceStreamBaseUrl}/ace/getstream?infohash={id}{pidParam}"
            };

            return urls;
        }

        public async Task<string?> GetApiAccessTokenAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(2500);
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{AceStreamBaseUrl}/server/api?method=get_api_access_token");
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
                                var obj = Newtonsoft.Json.Linq.JObject.Parse(trimmed);
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

        public async Task<bool> WaitForStreamReadyAsync(string httpUrl, int maxWaitSeconds = 6)
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
                        LogService.LogInfo($"[ACEENGINE] Stream data arrived ({read} bytes buffered). Stream is active and ready for player.");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[ACEENGINE] WaitForStreamReadyAsync: {ex.Message}");
            }
            return false;
        }

        public async Task<int> GetStreamPeersAsync(string streamUrl)
        {
            string id = ExtractAceId(streamUrl);
            if (string.IsNullOrWhiteSpace(id)) return 0;

            try
            {
                using var cts = new CancellationTokenSource(1500);
                string query = $"{AceStreamBaseUrl}/webui/api/service?method=get_stream_peers&id={id}";
                var resp = await HttpClient.GetStringAsync(query, cts.Token).ConfigureAwait(false);
                if (int.TryParse(resp, out int p)) return p;
            }
            catch { }
            return 0;
        }

        public async Task StopAllStreamsAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(1500);
                var t1 = HttpClient.GetAsync($"{AceStreamBaseUrl}/webui/api/service?method=stop_all", cts.Token);
                var t2 = HttpClient.GetAsync($"{AceStreamBaseUrl}/server/api?method=stop_all", cts.Token);
                await Task.WhenAll(t1, t2).ConfigureAwait(false);
            }
            catch { }
        }

        public void ShutdownEngine()
        {
            try
            {
                try
                {
                    using var cts = new CancellationTokenSource(1000);
                    _ = HttpClient.GetAsync($"{AceStreamBaseUrl}/webui/api/service?method=stop_all", cts.Token);
                }
                catch { }

                var processes = Process.GetProcessesByName("ace_engine");
                foreach (var proc in processes)
                {
                    try
                    {
                        proc.Kill();
                        proc.Dispose();
                    }
                    catch { }
                }
                LogService.LogInfo("[ACEENGINE] AceStream engine process terminated on application exit.");
            }
            catch (Exception ex)
            {
                LogService.LogError("[ACEENGINE] Error shutting down AceStream engine", ex);
            }
        }

        public static string Find40CharContentId(Newtonsoft.Json.Linq.JToken? token)
        {
            if (token == null) return "";
            string[] keys = { "content_id", "contentId", "infohash", "info_hash", "id", "torrent_hash", "hash", "url" };
            foreach (var k in keys)
            {
                string val = token[k]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(val))
                {
                    var match = Regex.Match(val, @"\b([a-fA-F0-9]{40})\b");
                    if (match.Success) return match.Groups[1].Value.ToLowerInvariant();
                }
            }

            if (token is Newtonsoft.Json.Linq.JObject obj)
            {
                foreach (var prop in obj.Properties())
                {
                    string v = prop.Value?.ToString() ?? "";
                    if (v.Length == 40 && Regex.IsMatch(v, "^[a-fA-F0-9]{40}$"))
                    {
                        return v.ToLowerInvariant();
                    }
                }
            }
            return "";
        }

        public async Task<List<SearchResultItem>> SearchAceStreamP2pAsync(string query)
        {
            var results = new List<SearchResultItem>();
            string clean = (query ?? "").Trim();
            if (string.IsNullOrWhiteSpace(clean)) return results;

            // 1. Direct AceStream Hash or URL (Sadece 40 haneli gerçek ID ise)
            if (IsAceStreamUrl(clean))
            {
                string id = ExtractAceId(clean);
                if (id.Length == 40 && Regex.IsMatch(id, "^[a-fA-F0-9]{40}$"))
                {
                    int peers = await GetStreamPeersAsync(clean);
                    results.Add(new SearchResultItem
                    {
                        Name = $"AceStream P2P Yayını ({id.Substring(0, Math.Min(8, id.Length))}...)",
                        Url = $"acestream://{id}",
                        Source = "AceStream P2P",
                        Category = "Canlı TV",
                        GroupTitle = "AceStream P2P",
                        PeersOrDetails = peers > 0 ? $"{peers} Peer / Eş" : "Yerel AceEngine P2P"
                    });
                    return results;
                }
            }

            string cleanTag = clean.Trim('[', ']', '(', ')', '{', '}').Trim();
            var searchTerms = new List<string> { clean };
            if (!string.IsNullOrEmpty(cleanTag) && !cleanTag.Equals(clean, StringComparison.OrdinalIgnoreCase))
            {
                searchTerms.Add(cleanTag);
            }

            // 2. Query Local AceStream Engine Search API (AceEngine v3.1.8.0) with pagination
            try
            {
                if (await IsEngineHttpAliveAsync().ConfigureAwait(false) || IsAnyAceProcessRunning())
                {
                    string? token = await GetApiAccessTokenAsync().ConfigureAwait(false);

                    foreach (var sTerm in searchTerms)
                    {
                        string encoded = Uri.EscapeDataString(sTerm);

                        // Try pages 0, 1, 2, 3 to gather complete results
                        for (int page = 0; page < 4; page++)
                        {
                            using var cts = new CancellationTokenSource(3000);
                            int beforeCount = results.Count;

                            var engineUrls = new List<string>();
                            if (!string.IsNullOrWhiteSpace(token))
                            {
                                engineUrls.Add($"{AceStreamBaseUrl}/server/api?method=search&token={token}&query={encoded}&api_version=4&page_size=200&page={page}");
                                engineUrls.Add($"{AceStreamBaseUrl}/server/api?method=search&token={token}&q={encoded}&api_version=4&page_size=200&page={page}");
                            }
                            engineUrls.Add($"{AceStreamBaseUrl}/server/api?method=search&query={encoded}&api_version=4&page_size=200&page={page}");
                            engineUrls.Add($"{AceStreamBaseUrl}/search?query={encoded}&page_size=200&page={page}");

                            foreach (var url in engineUrls)
                            {
                                try
                                {
                                    var json = await HttpClient.GetStringAsync(url, cts.Token).ConfigureAwait(false);
                                    if (!string.IsNullOrWhiteSpace(json) && json.StartsWith("{"))
                                    {
                                        var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(json);
                                        var resultsToken = data?["result"]?["results"] ?? data?["results"] ?? data?["result"];
                                        
                                        if (resultsToken != null)
                                        {
                                            if (resultsToken.Type == Newtonsoft.Json.Linq.JTokenType.Array)
                                            {
                                                foreach (var chItem in resultsToken)
                                                {
                                                    ParseLocalChannelItem(chItem, results);
                                                }
                                            }
                                            else if (resultsToken.Type == Newtonsoft.Json.Linq.JTokenType.Object)
                                            {
                                                ParseLocalChannelItem(resultsToken, results);
                                            }
                                        }
                                    }
                                    if (results.Count > beforeCount) break; // This engine URL worked for this page
                                }
                                catch { }
                            }

                            // If no new items found on this page, stop paging for this term
                            if (results.Count == beforeCount || results.Count >= 400)
                            {
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"[ACEENGINE] P2P Search Error: {ex.Message}");
            }

            // 3. Complement with AceStream Web P2P Index (api.acestream.me & search.acestream.net) to guarantee full catalog
            try
            {
                var webItems = await SearchAceStreamWebAsync(clean);
                foreach (var itm in webItems)
                {
                    results.Add(itm);
                }

                // If bracketed search had few results, also fetch for cleanTag
                if (searchTerms.Count > 1 && results.Count < 50)
                {
                    var webItemsTag = await SearchAceStreamWebAsync(cleanTag);
                    foreach (var itm in webItemsTag)
                    {
                        results.Add(itm);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[ACEENGINE] AceStream P2P Web Complement error: {ex.Message}");
            }

            return results.GroupBy(r => r.Url).Select(g => g.First()).ToList();
        }

        private static void ParseLocalChannelItem(Newtonsoft.Json.Linq.JToken chItem, List<SearchResultItem> resList)
        {
            if (chItem == null) return;
            string chName = (string?)chItem["name"] ?? (string?)chItem["title"] ?? "AceStream P2P Yayın";
            string chIcon = (string?)chItem["icon"] ?? (string?)chItem["logo"] ?? "";
            string chCategory = (string?)chItem["category"] ?? "Canlı TV";
            string epgTitle = (string?)chItem["epg"]?["name"] ?? "";

            var itemsToken = chItem["items"] ?? chItem["streams"];
            if (itemsToken != null && itemsToken.Type == Newtonsoft.Json.Linq.JTokenType.Array && itemsToken.Any())
            {
                foreach (var stream in itemsToken)
                {
                    string infohash = Find40CharContentId(stream);
                    if (string.IsNullOrEmpty(infohash))
                    {
                        infohash = Find40CharContentId(chItem);
                    }

                    // SADECE 40 haneli gerçek Content ID varsa listeye ekle!
                    if (!string.IsNullOrWhiteSpace(infohash) && infohash.Length == 40 && Regex.IsMatch(infohash, "^[a-fA-F0-9]{40}$"))
                    {
                        int peers = (int?)stream["availability"] ?? (int?)stream["peers"] ?? (int?)chItem["peers"] ?? 0;
                        string details = peers > 0 ? $"{peers} Peer / Eş" : (!string.IsNullOrWhiteSpace(epgTitle) ? $"Yayın: {epgTitle}" : "127.0.0.1 AceEngine");
                        resList.Add(new SearchResultItem
                        {
                            Name = chName,
                            Url = $"acestream://{infohash}",
                            Source = "AceStream P2P",
                            Category = chCategory,
                            GroupTitle = "AceStream P2P (Yerel)",
                            LogoUrl = chIcon,
                            PeersOrDetails = details
                        });
                    }
                }
            }
            else
            {
                string infohash = Find40CharContentId(chItem);
                if (!string.IsNullOrWhiteSpace(infohash) && infohash.Length == 40 && Regex.IsMatch(infohash, "^[a-fA-F0-9]{40}$"))
                {
                    int peers = (int?)chItem["availability"] ?? (int?)chItem["peers"] ?? 0;
                    string details = peers > 0 ? $"{peers} Peer / Eş" : (!string.IsNullOrWhiteSpace(epgTitle) ? $"Yayın: {epgTitle}" : "127.0.0.1 AceEngine");
                    resList.Add(new SearchResultItem
                    {
                        Name = chName,
                        Url = $"acestream://{infohash}",
                        Source = "AceStream P2P",
                        Category = chCategory,
                        GroupTitle = "AceStream P2P (Yerel)",
                        LogoUrl = chIcon,
                        PeersOrDetails = details
                    });
                }
            }
        }

        public async Task<List<SearchResultItem>> SearchAceStreamWebAsync(string query, string domain = "all")
        {
            var results = new List<SearchResultItem>();
            string clean = (query ?? "").Trim();
            if (string.IsNullOrWhiteSpace(clean)) return results;

            string encoded = Uri.EscapeDataString(clean);
            var targets = new List<(string url, string sourceName)>();

            string d = domain.ToLowerInvariant();
            if (d.Contains("me") && !d.Contains("tüm") && !d.Contains("all"))
            {
                targets.Add(($"https://api.acestream.me/search?q={encoded}", "AceStream (me)"));
            }
            else if (d.Contains("org"))
            {
                // api.acestream.org sunucusu kapalı olduğundan çalışan aktif resmi web aynasından (api.acestream.me & search.acestream.net) çekilir
                targets.Add(($"https://api.acestream.me/search?q={encoded}", "AceStream (org)"));
                targets.Add(($"https://search.acestream.net/search?q={encoded}", "AceStream (org)"));
                targets.Add(($"https://api.acestream.org/search?q={encoded}", "AceStream (org)"));
            }
            else if (d.Contains("net"))
            {
                targets.Add(($"https://search.acestream.net/search?q={encoded}", "AceStream (net)"));
                targets.Add(($"https://api.acestream.me/search?q={encoded}", "AceStream (net)"));
            }
            else
            {
                targets.Add(($"https://api.acestream.me/search?q={encoded}", "AceStream (me)"));
                targets.Add(($"https://api.acestream.me/search?q={encoded}", "AceStream (org)"));
                targets.Add(($"https://search.acestream.net/search?q={encoded}", "AceStream (net)"));
            }

            foreach (var (endpoint, srcName) in targets)
            {
                for (int page = 1; page <= 4; page++)
                {
                    int beforeCount = results.Count;
                    string pageUrl = endpoint.Contains("?") ? $"{endpoint}&page={page}&page_size=100" : $"{endpoint}?page={page}&page_size=100";

                    try
                    {
                        using var cts = new CancellationTokenSource(3500);
                        using var req = new HttpRequestMessage(HttpMethod.Get, pageUrl);
                        req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 StreamMesh/2.1");
                        var resp = await HttpClient.SendAsync(req, cts.Token).ConfigureAwait(false);
                        if (resp.IsSuccessStatusCode)
                        {
                            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(body) && (body.StartsWith("{") || body.StartsWith("[")))
                            {
                                dynamic? data = Newtonsoft.Json.JsonConvert.DeserializeObject(body);
                                var itemsToken = data?.results ?? data;
                                if (itemsToken != null)
                                {
                                    foreach (var item in itemsToken)
                                    {
                                        string name = (string?)item.name ?? (string?)item.title ?? "";
                                        string icon = (string?)item.icon ?? "";
                                        string category = (string?)item.category ?? "Canlı TV";
                                        string epgTitle = (string?)item.epg?.name ?? "";

                                        // SADECE ve SADECE 40 haneli gerçek Content ID kabul edilir
                                        string cid = Find40CharContentId(item);

                                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(cid) && cid.Length == 40 && Regex.IsMatch(cid, "^[a-fA-F0-9]{40}$"))
                                        {
                                            string playUrl = $"acestream://{cid}";
                                            string details = !string.IsNullOrWhiteSpace(epgTitle)
                                                ? $"Yayın: {epgTitle}"
                                                : $"{srcName} İndeksi";

                                            results.Add(new SearchResultItem
                                            {
                                                Name = name,
                                                Url = playUrl,
                                                Source = srcName,
                                                Category = category,
                                                GroupTitle = srcName,
                                                LogoUrl = icon,
                                                PeersOrDetails = details
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.LogWarning($"[ACEENGINE] AceStream Web endpoint {endpoint} (sayfa {page}) hatası: {ex.Message}");
                        break;
                    }

                    // If no new items found on this page, or we've reached enough items, stop paging this endpoint
                    if (results.Count == beforeCount || results.Count >= 400)
                    {
                        break;
                    }
                }
            }

            return results.GroupBy(r => r.Url).Select(g => g.First()).ToList();
        }

        public async Task<List<SearchResultItem>> SearchAceStreamsAsync(string query)
        {
            var p2pTask = SearchAceStreamP2pAsync(query);
            var webTask = SearchAceStreamWebAsync(query);

            await Task.WhenAll(p2pTask, webTask).ConfigureAwait(false);

            var results = new List<SearchResultItem>();
            results.AddRange(await p2pTask);
            results.AddRange(await webTask);

            return results.GroupBy(r => r.Url).Select(g => g.First()).ToList();
        }
    }
}
