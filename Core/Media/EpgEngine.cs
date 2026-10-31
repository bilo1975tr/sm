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
using StreamMesh.Core.Network;

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
            if (string.IsNullOrWhiteSpace(url)) return;
            _db.UpdateEpgSourceStats(url, "İndiriliyor", 0, 0, 0, "");

            try
            {
                string? tempFile = null;
                Stream? epgStream = null;
                bool downloaded = false;

                var urlsToTry = new List<string> { url };
                if (url.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    urlsToTry.Add(url.Substring(0, url.Length - 3));
                }
                else
                {
                    urlsToTry.Add(url + ".gz");
                }

                string successfulUrl = url;
                foreach (var tryUrl in urlsToTry)
                {
                    try
                    {
                        if (tryUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            progressCallback?.Invoke($"EPG İndiriliyor: {MediaHttpClient.GetShortUrl(tryUrl)}", 0);

                            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(120));
                            tempFile = Path.Combine(Path.GetTempPath(), $"streammesh_epg_{Guid.NewGuid():N}.tmp");
                            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                            {
                                await MediaHttpClient.DownloadToStreamWithProgressAsync(tryUrl, fs, progressCallback, "EPG İndiriliyor", 120, cts.Token);
                            }

                            epgStream = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                            successfulUrl = tryUrl;
                            downloaded = true;
                            break;
                        }
                        else if (File.Exists(tryUrl))
                        {
                            epgStream = new FileStream(tryUrl, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                            successfulUrl = tryUrl;
                            downloaded = true;
                            break;
                        }
                    }
                    catch
                    {
                        if (tempFile != null && File.Exists(tempFile))
                        {
                            try { File.Delete(tempFile); } catch { }
                            tempFile = null;
                        }
                    }
                }

                if (!downloaded || epgStream == null)
                {
                    throw new Exception("EPG kaynağına ulaşılamadı veya indirilemedi.");
                }

                try
                {
                    await _db.ClearEpgSourceDataAsync(url);

                    progressCallback?.Invoke("EPG Rehberi İşleniyor...", 80);

                    bool isGzip = false;
                    byte[] magic = new byte[2];
                    int readBytes = epgStream.Read(magic, 0, 2);
                    epgStream.Seek(0, SeekOrigin.Begin);
                    if (readBytes == 2 && magic[0] == 0x1F && magic[1] == 0x8B)
                    {
                        isGzip = true;
                    }
                    else if (successfulUrl.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                    {
                        isGzip = true;
                    }

                    Stream readStream = epgStream;
                    if (isGzip)
                    {
                        try
                        {
                            readStream = new GZipStream(epgStream, CompressionMode.Decompress);
                        }
                        catch
                        {
                            epgStream.Seek(0, SeekOrigin.Begin);
                            readStream = epgStream;
                        }
                    }

                    int totalSaved = 0;
                    int channelCount = 0;
                    var batch = new List<EpgProgram>();
                    var channelBatch = new List<(string epgId, string name, string logo, string url)>();
                    var channelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    using (var reader = new StreamReader(readStream))
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

                                        var dispNames = el.Elements().Where(e => e.Name.LocalName == "display-name").Select(e => e.Value.Trim()).Distinct().ToList();
                                        string combinedName = string.Join(", ", dispNames);
                                        string logo = el.Elements().FirstOrDefault(e => e.Name.LocalName == "icon")?.Attribute("src")?.Value ?? "";

                                        if (!string.IsNullOrWhiteSpace(chId))
                                        {
                                            if (string.IsNullOrWhiteSpace(combinedName)) combinedName = chId;
                                            channelMap[chId] = combinedName;
                                            channelBatch.Add((chId, combinedName, logo, url));
                                            channelCount++;
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

                    _db.UpdateEpgSourceStats(url, "Başarılı", channelCount, totalSaved, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "");
                    progressCallback?.Invoke($"EPG Tamamlandı: Toplam {totalSaved} yayın akışı eklendi.", 100);
                }
                finally
                {
                    epgStream?.Dispose();
                    if (!string.IsNullOrEmpty(tempFile) && File.Exists(tempFile))
                    {
                        try { File.Delete(tempFile); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                _db.UpdateEpgSourceStats(url, "Hata: " + ex.Message, 0, 0, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ex.Message);
                progressCallback?.Invoke($"EPG Hatası: {ex.Message}", 0);
            }
        }

        private string GetShortUrl(string url) => MediaHttpClient.GetShortUrl(url);

        private bool TryParseXmlTime(string time, string sourceUrl, out DateTime result)
        {
            result = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(time)) return false;

            try
            {
                string cleanTime = time.Trim();
                int timeShift = _db.GetEpgSourceTimeShift(sourceUrl);

                if (cleanTime.Length >= 14 && char.IsDigit(cleanTime[0]))
                {
                    string datePart = cleanTime.Substring(0, 14);
                    if (DateTime.TryParseExact(datePart, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                    {
                        if (cleanTime.Length > 14)
                        {
                            string offsetPart = cleanTime.Substring(14).Trim();
                            var match = Regex.Match(offsetPart, @"^([+-])(\d{2}):?(\d{2})$");
                            if (match.Success)
                            {
                                int hours = int.Parse(match.Groups[2].Value);
                                int mins = int.Parse(match.Groups[3].Value);
                                TimeSpan offset = new TimeSpan(hours, mins, 0);
                                if (match.Groups[1].Value == "-") offset = offset.Negate();

                                var dto = new DateTimeOffset(dt, offset);
                                result = dto.LocalDateTime.AddHours(timeShift);
                                return true;
                            }
                        }

                        result = DateTime.SpecifyKind(dt, DateTimeKind.Local).AddHours(timeShift);
                        return true;
                    }
                }

                if (DateTimeOffset.TryParse(cleanTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset dtoFallback))
                {
                    result = dtoFallback.LocalDateTime.AddHours(timeShift);
                    return true;
                }
            }
            catch { }

            return DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }
    }
}
