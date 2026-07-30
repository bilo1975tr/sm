using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using StreamMesh.Models;
using StreamMesh.Core.Database;

namespace StreamMesh.Core.Media
{
    public class EpgEngine
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        private readonly DatabaseEngine _db = new DatabaseEngine();

        static EpgEngine()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) StreamMesh/1.0");
        }

        public async Task LoadEpgAsync(string url, Action<string, double>? progressCallback = null)
        {
            try
            {
                byte[] data;
                if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    progressCallback?.Invoke($"EPG İndiriliyor: {GetShortUrl(url)}", 0);

                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                    using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        progressCallback?.Invoke($"EPG İndirme Hatası ({response.StatusCode}): {GetShortUrl(url)}", 0);
                        return;
                    }

                    long totalBytes = response.Content.Headers.ContentLength ?? -1;
                    using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                    using var ms = new MemoryStream();

                    byte[] buffer = new byte[81920];
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
                            progressCallback?.Invoke($"EPG İndiriliyor: {currentMB:F2} / {totalMB:F2} MB (%{percent:F0}) - {speedMBs:F2} MB/s", percent);
                        }
                        else
                        {
                            progressCallback?.Invoke($"EPG İndiriliyor: {currentMB:F2} MB - {speedMBs:F2} MB/s", 50);
                        }
                    }

                    data = ms.ToArray();
                }
                else
                {
                    data = await File.ReadAllBytesAsync(url);
                }

                if (data == null || data.Length == 0) return;

                progressCallback?.Invoke("EPG Rehberi İşleniyor...", 80);

                Stream epgStream = new MemoryStream(data);
                if (url.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    epgStream = new GZipStream(epgStream, CompressionMode.Decompress);
                }

                int totalSaved = 0;
                var batch = new List<EpgProgram>();

                using (var reader = new StreamReader(epgStream))
                {
                    using (var xmlReader = System.Xml.XmlReader.Create(reader, new System.Xml.XmlReaderSettings { IgnoreWhitespace = true, DtdProcessing = System.Xml.DtdProcessing.Ignore }))
                    {
                        while (xmlReader.Read())
                        {
                            if (xmlReader.NodeType == System.Xml.XmlNodeType.Element && xmlReader.Name.Equals("programme", StringComparison.OrdinalIgnoreCase))
                            {
                                string channelAttr = xmlReader.GetAttribute("channel") ?? "";
                                string startAttr = xmlReader.GetAttribute("start") ?? "";
                                string stopAttr = xmlReader.GetAttribute("stop") ?? "";

                                using var subTree = xmlReader.ReadSubtree();
                                XElement el = XElement.Load(subTree);

                                var prog = new EpgProgram
                                {
                                    ChannelName = channelAttr,
                                    Title = el.Element("title")?.Value ?? "",
                                    Description = el.Element("desc")?.Value ?? "",
                                    SourceUrl = url
                                };

                                if (TryParseXmlTime(startAttr, out DateTime st)) prog.StartTime = st;
                                if (TryParseXmlTime(stopAttr, out DateTime et)) prog.EndTime = et;

                                batch.Add(prog);

                                if (batch.Count >= 2000)
                                {
                                    await _db.SaveEpgProgramsAsync(batch);
                                    totalSaved += batch.Count;
                                    progressCallback?.Invoke($"EPG İşleniyor: {totalSaved} yayın akışı eklendi...", 85);
                                    batch.Clear();
                                }
                            }
                        }
                    }
                }

                if (batch.Count > 0)
                {
                    await _db.SaveEpgProgramsAsync(batch);
                    totalSaved += batch.Count;
                    batch.Clear();
                }

                progressCallback?.Invoke($"EPG Tamamlandı: Toplam {totalSaved} yayın akışı eklendi.", 100);
            }
            catch (Exception ex)
            {
                progressCallback?.Invoke($"EPG Hatası: {ex.Message}", 0);
            }
        }

        private string GetShortUrl(string url)
        {
            try { return new Uri(url).Host + new Uri(url).AbsolutePath; }
            catch { return url; }
        }

        private bool TryParseXmlTime(string time, out DateTime result)
        {
            result = DateTime.MinValue;
            if (string.IsNullOrEmpty(time)) return false;
            try
            {
                // XMLTV format: yyyyMMddHHmmss [+-]HHmm
                string cleanTime = time.Trim();
                if (cleanTime.Length >= 14)
                {
                    string datePart = cleanTime.Substring(0, 14);
                    DateTime dt = DateTime.ParseExact(datePart, "yyyyMMddHHmmss", null);

                    if (cleanTime.Length > 15)
                    {
                        string offsetPart = cleanTime.Substring(14).Trim();
                        if (offsetPart.Length >= 5 && (offsetPart.StartsWith("+") || offsetPart.StartsWith("-")))
                        {
                            int hours = int.Parse(offsetPart.Substring(1, 2));
                            int mins = int.Parse(offsetPart.Substring(3, 2));
                            TimeSpan offset = new TimeSpan(hours, mins, 0);
                            if (offsetPart.StartsWith("-")) offset = offset.Negate();

                            result = new DateTimeOffset(dt, offset).LocalDateTime;
                            return true;
                        }
                    }
                    result = dt;
                    return true;
                }
            }
            catch { }
            return DateTime.TryParse(time, out result);
        }
    }
}
