using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using StreamMesh.Models;

namespace StreamMesh.Core.Media
{
    public class M3uEngine
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        static M3uEngine()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) StreamMesh/1.0");
        }

        public async Task<List<Channel>> ParseM3uAsync(string urlOrPath, string categoryHint = "TV", Action<string, double>? progressCallback = null)
        {
            var channels = new List<Channel>();
            string content = "";

            try
            {
                if (urlOrPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    progressCallback?.Invoke($"Bağlanılıyor: {GetShortUrl(urlOrPath)}", 0);

                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                    using var response = await _httpClient.GetAsync(urlOrPath, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        progressCallback?.Invoke($"Hata ({response.StatusCode}): {GetShortUrl(urlOrPath)}", 0);
                        return channels;
                    }

                    long totalBytes = response.Content.Headers.ContentLength ?? -1;
                    using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                    using var ms = new MemoryStream();

                    byte[] buffer = new byte[81920]; // 80KB buffer
                    long totalRead = 0;
                    int bytesRead = 0;
                    var startTime = DateTime.Now;

                    while (true)
                    {
                        using var readCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                        bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, readCts.Token);
                        if (bytesRead <= 0) break;

                        await ms.WriteAsync(buffer, 0, bytesRead, cts.Token);
                        totalRead += bytesRead;

                        double elapsed = (DateTime.Now - startTime).TotalSeconds;
                        double speedMBs = elapsed > 0 ? (totalRead / 1024.0 / 1024.0) / elapsed : 0;
                        double totalMB = totalBytes > 0 ? totalBytes / 1024.0 / 1024.0 : 0;
                        double currentMB = totalRead / 1024.0 / 1024.0;

                        if (totalBytes > 0)
                        {
                            double percent = Math.Min(100.0, (double)totalRead / totalBytes * 100.0);
                            progressCallback?.Invoke($"İndiriliyor: {currentMB:F2} MB / {totalMB:F2} MB (%{percent:F0}) - {speedMBs:F2} MB/s", percent);
                        }
                        else
                        {
                            progressCallback?.Invoke($"İndiriliyor: {currentMB:F2} MB - {speedMBs:F2} MB/s", 50);
                        }
                    }

                    ms.Position = 0;
                    using var reader = new StreamReader(ms);
                    content = await reader.ReadToEndAsync();
                }
                else if (File.Exists(urlOrPath))
                {
                    progressCallback?.Invoke("Yerel dosya okunuyor...", 50);
                    content = await File.ReadAllTextAsync(urlOrPath);
                }

                if (string.IsNullOrEmpty(content)) return channels;

                progressCallback?.Invoke("Ayrıştırılıyor...", 80);

                var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                Channel? current = null;

                foreach (var line in lines)
                {
                    if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                    {
                        current = new Channel { Category = categoryHint, PlaylistUrl = urlOrPath };

                        int logoIdx = line.IndexOf("tvg-logo=\"", StringComparison.OrdinalIgnoreCase);
                        if (logoIdx != -1)
                        {
                            int start = logoIdx + 10;
                            int end = line.IndexOf("\"", start);
                            if (end != -1) current.LogoUrl = line.Substring(start, end - start);
                        }

                        int epgIdx = line.IndexOf("tvg-id=\"", StringComparison.OrdinalIgnoreCase);
                        if (epgIdx != -1)
                        {
                            int start = epgIdx + 8;
                            int end = line.IndexOf("\"", start);
                            if (end != -1) current.EpgId = line.Substring(start, end - start);
                        }

                        int groupIdx = line.IndexOf("group-title=\"", StringComparison.OrdinalIgnoreCase);
                        if (groupIdx != -1)
                        {
                            int start = groupIdx + 13;
                            int end = line.IndexOf("\"", start);
                            if (end != -1) current.GroupTitle = line.Substring(start, end - start);
                        }

                        int nameIdx = line.LastIndexOf(',');
                        if (nameIdx != -1)
                        {
                            current.Name = line.Substring(nameIdx + 1).Trim();
                            if (string.IsNullOrEmpty(current.Name)) current.Name = "İsimsiz Kanal";
                        }
                    }
                    else if (!line.StartsWith("#") && current != null)
                    {
                        string url = line.Trim();
                        if (!string.IsNullOrEmpty(url))
                        {
                            current.Url = url;
                            current.Id = Guid.NewGuid().ToString("N");

                            SmartNormalizationEngine.Instance.NormalizeChannel(current);
                            channels.Add(current);
                        }
                        current = null;
                    }
                }

                progressCallback?.Invoke($"Ayrıştırma tamamlandı: {channels.Count} kanal bulundu.", 100);
            }
            catch (Exception ex)
            {
                progressCallback?.Invoke($"Ayrıştırma hatası: {ex.Message}", 0);
            }

            return ChannelAggregator.Instance.AggregateChannels(channels);
        }

        private string GetShortUrl(string url)
        {
            try { return new Uri(url).Host + new Uri(url).AbsolutePath; }
            catch { return url; }
        }
    }
}
