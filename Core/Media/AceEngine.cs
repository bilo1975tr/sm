using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class AceResult
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string Peers { get; set; } = "0";
        public string SourceName { get; set; } = "AceStream";
    }

    public class AceEngine
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        private const int ACESTREAM_PORT = 6878;

        public async Task<List<AceResult>> SearchAsync(string query)
        {
            var list = new List<AceResult>();
            if (string.IsNullOrWhiteSpace(query)) return list;

            // 1. Search Local AceStream Engine WebUI API
            try
            {
                if (await IsEngineRunningAsync())
                {
                    string url = $"http://127.0.0.1:{ACESTREAM_PORT}/webui/api/service?method=search&query={Uri.EscapeDataString(query)}";
                    var response = await _httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        dynamic? data = JsonConvert.DeserializeObject(json);
                        if (data?.result != null)
                        {
                            foreach (var item in data.result)
                            {
                                string name = item.name ?? query;
                                string cid = item.content_id ?? item.infohash ?? "";
                                string peers = item.availability?.ToString() ?? item.peers?.ToString() ?? "0";
                                if (!string.IsNullOrEmpty(cid))
                                {
                                    list.Add(new AceResult
                                    {
                                        Name = name,
                                        Url = cid.StartsWith("acestream://") ? cid : $"acestream://{cid}",
                                        Peers = peers,
                                        SourceName = "AceStream Local Engine"
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // 2. Search search-ace.stream Online P2P Index
            try
            {
                string searchUrl = $"https://search-ace.stream/?q={Uri.EscapeDataString(query)}";
                var html = await _httpClient.GetStringAsync(searchUrl);

                // Scrape acestream links and titles from search-ace.stream
                MatchCollection matches = Regex.Matches(html, @"href=""(acestream://[a-f0-9]{40})""[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (matches.Count == 0)
                {
                    matches = Regex.Matches(html, @"([a-f0-9]{40}).*?class=""title""[^>]*>(.*?)<", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                }

                foreach (Match m in matches)
                {
                    string rawUrl = m.Groups[1].Value;
                    string rawTitle = m.Groups[2].Value;
                    string title = Regex.Replace(rawTitle, "<.*?>", "").Trim();
                    if (string.IsNullOrWhiteSpace(title)) title = query;
                    string finalUrl = rawUrl.StartsWith("acestream://") ? rawUrl : $"acestream://{rawUrl}";

                    if (!list.Any(x => x.Url.Equals(finalUrl, StringComparison.OrdinalIgnoreCase)))
                    {
                        list.Add(new AceResult
                        {
                            Name = title,
                            Url = finalUrl,
                            Peers = "Active P2P",
                            SourceName = "search-ace.stream"
                        });
                    }
                }
            }
            catch { }

            return list;
        }

        public async Task<bool> IsEngineRunningAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"http://127.0.0.1:{ACESTREAM_PORT}/webui/api/service?method=get_version");
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public static string GetEngineExecutablePath()
        {
            string[] paths = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"ACEStream\engine\ace_engine.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"ACEStream\engine\ace_engine.exe"),
                @"C:\ACEStream\engine\ace_engine.exe"
            };

            foreach (var p in paths)
            {
                if (File.Exists(p)) return p;
            }
            return "";
        }

        public bool IsInstalled()
        {
            return !string.IsNullOrEmpty(GetEngineExecutablePath());
        }

        public async Task<bool> DownloadAndExtractEngineAsync(Action<int>? progressCallback = null)
        {
            try
            {
                string downloadUrl = "https://github.com/bilo1975tr/sm/releases/download/v1.0/AceStream.zip";
                string targetFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ACEStream");
                Directory.CreateDirectory(targetFolder);

                string tempZip = Path.Combine(Path.GetTempPath(), "AceStream_setup.zip");

                using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    long totalBytes = response.Content.Headers.ContentLength ?? -1;

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer = new byte[8192];
                        long totalRead = 0;
                        int read;

                        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read);
                            totalRead += read;
                            if (totalBytes > 0)
                            {
                                int prog = (int)((totalRead * 100) / totalBytes);
                                progressCallback?.Invoke(prog);
                            }
                        }
                    }
                }

                // Extract Zip
                if (File.Exists(tempZip))
                {
                    System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, targetFolder, overwriteFiles: true);
                    File.Delete(tempZip);
                    return IsInstalled();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("AceEngine Auto-Download Exception", ex);
            }
            return false;
        }

        public async Task StartEngineAsync()
        {
            if (await IsEngineRunningAsync()) return;

            string found = GetEngineExecutablePath();

            if (!string.IsNullOrEmpty(found))
            {
                LogService.LogInfo($"AceEngine: Başlatılıyor -> {found}");
                Process.Start(new ProcessStartInfo { FileName = found, WindowStyle = ProcessWindowStyle.Hidden });
                await Task.Delay(4000);
            }
            else
            {
                LogService.LogInfo("AceEngine: Motor dosyası bulunamadı.");
            }
        }

        public string GetHttpUrl(string contentId)
        {
            string cid = contentId.Trim();
            if (cid.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase)) cid = cid.Substring(12);
            return $"http://127.0.0.1:{ACESTREAM_PORT}/ace/getstream?id={cid}";
        }
    }
}
