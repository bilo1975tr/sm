using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using StreamMesh.Models;
using StreamMesh.Core.Database;

namespace StreamMesh.Core.Media
{
    public class EpgEngine
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
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

                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(60));
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

                // V1.9.0: Clear old data for THIS source before loading new ones to prevent bloat
                await _db.ClearEpgSourceDataAsync(url);

                progressCallback?.Invoke("EPG Rehberi İşleniyor...", 80);

                Stream epgStream = new MemoryStream(data);
                if (url.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    epgStream = new GZipStream(epgStream, CompressionMode.Decompress);
                }

                int totalSaved = 0;
                var batch = new List<EpgProgram>();
                var channelBatch = new List<(string epgId, string name, string logo, string url)>();
                var channelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                using (var reader = new StreamReader(epgStream))
                {
                    using (var xmlReader = System.Xml.XmlReader.Create(reader, new System.Xml.XmlReaderSettings { IgnoreWhitespace = true, DtdProcessing = System.Xml.DtdProcessing.Ignore }))
                    {
                        while (xmlReader.Read())
                        {
                            if (xmlReader.NodeType == System.Xml.XmlNodeType.Element)
                            {
                                string ln = xmlReader.LocalName;
                                if (ln.Equals("channel", StringComparison.OrdinalIgnoreCase))
                                {
                                    string chId = xmlReader.GetAttribute("id") ?? "";
                                    using var subTree = xmlReader.ReadSubtree();
                                    XElement el = XElement.Load(subTree);

                                    // Namespace agnostic element access - capture ALL display-names
                                    var dispNames = el.Elements().Where(e => e.Name.LocalName == "display-name").Select(e => e.Value.Trim()).Distinct().ToList();
                                    string combinedName = string.Join(", ", dispNames);
                                    string logo = el.Elements().FirstOrDefault(e => e.Name.LocalName == "icon")?.Attribute("src")?.Value ?? "";

                                    if (!string.IsNullOrWhiteSpace(chId))
                                    {
                                        if (string.IsNullOrWhiteSpace(combinedName)) combinedName = chId;
                                        channelMap[chId] = combinedName;
                                        channelBatch.Add((chId, combinedName, logo, url));
                                    }

                                    if (channelBatch.Count >= 500)
                                    {
                                        await _db.SaveEpgChannelsBatchAsync(channelBatch);
                                        channelBatch.Clear();
                                    }
                                }
                                else if (ln.Equals("programme", StringComparison.OrdinalIgnoreCase))
                                {
                                    string channelAttr = xmlReader.GetAttribute("channel") ?? "";
                                    string startAttr = xmlReader.GetAttribute("start") ?? "";
                                    string stopAttr = xmlReader.GetAttribute("stop") ?? "";

                                    using var subTree = xmlReader.ReadSubtree();
                                    XElement el = XElement.Load(subTree);

                                    string combinedChannelName = channelAttr;
                                    if (channelMap.TryGetValue(channelAttr, out string? dName) && !string.IsNullOrWhiteSpace(dName) && !dName.Equals(channelAttr, StringComparison.OrdinalIgnoreCase))
                                    {
                                        combinedChannelName = $"{channelAttr}, {dName}";
                                    }

                                    var prog = new EpgProgram
                                    {
                                        ChannelName = combinedChannelName,
                                        Title = el.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value ?? "",
                                        Description = el.Elements().FirstOrDefault(e => e.Name.LocalName == "desc")?.Value ?? "",
                                        SourceUrl = url
                                    };

                                    if (TryParseXmlTime(startAttr, url, out DateTime st)) prog.StartTime = st;
                                    if (TryParseXmlTime(stopAttr, url, out DateTime et)) prog.EndTime = et;

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
                }

                if (batch.Count > 0)
                {
                    await _db.SaveEpgProgramsAsync(batch);
                    totalSaved += batch.Count;
                    batch.Clear();
                }

                if (channelBatch.Count > 0)
                {
                    await _db.SaveEpgChannelsBatchAsync(channelBatch);
                    channelBatch.Clear();
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

        private bool TryParseXmlTime(string time, string sourceUrl, out DateTime result)
        {
            result = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(time)) return false;

            try
            {
                string cleanTime = time.Trim();

                // 1. Try ISO 8601 and standard .NET formats first (Handles Z, +03:00, etc.)
                if (DateTimeOffset.TryParse(cleanTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset dto))
                {
                    result = dto.LocalDateTime;
                    return true;
                }

                // 2. Handle XMLTV standard format: yyyyMMddHHmmss [+-]HHmm
                // Example: 20231024153000 +0300
                if (cleanTime.Length >= 14 && char.IsDigit(cleanTime[0]))
                {
                    string datePart = cleanTime.Substring(0, 14);
                    if (DateTime.TryParseExact(datePart, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                    {
                        if (cleanTime.Length > 14)
                        {
                            string offsetPart = cleanTime.Substring(14).Trim();
                            // Handle both +HHmm and +HH:mm
                            if (DateTimeOffset.TryParseExact(datePart + " " + offsetPart,
                                new[] { "yyyyMMddHHmmss zzz", "yyyyMMddHHmmss zz", "yyyyMMddHHmmss z" },
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset dtoXml))
                            {
                                result = dtoXml.LocalDateTime;
                                return true;
                            }

                            // Manual parsing for cases like +0300 (standard XMLTV)
                            var match = Regex.Match(offsetPart, @"^([+-])(\d{2}):?(\d{2})$");
                            if (match.Success)
                            {
                                int hours = int.Parse(match.Groups[2].Value);
                                int mins = int.Parse(match.Groups[3].Value);
                                TimeSpan offset = new TimeSpan(hours, mins, 0);
                                if (match.Groups[1].Value == "-") offset = offset.Negate();

                                result = new DateTimeOffset(dt, offset).LocalDateTime;
                                return true;
                            }
                        }

                        // Fallback: If no valid offset found but source is known to be local
                        if (sourceUrl.Contains("iptv-epg.org", StringComparison.OrdinalIgnoreCase) ||
                            sourceUrl.Contains("turk", StringComparison.OrdinalIgnoreCase))
                        {
                            result = DateTime.SpecifyKind(dt, DateTimeKind.Local);
                            return true;
                        }

                        // Default to the parsed DateTime as is
                        result = dt;
                        return true;
                    }
                }
            }
            catch { }

            // Final fallback to generic parser
            return DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }
    }
}
