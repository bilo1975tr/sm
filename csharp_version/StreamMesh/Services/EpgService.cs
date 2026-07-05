using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using System.IO;
using System.IO.Compression;
using StreamMesh.Models;
using System.Linq;

namespace StreamMesh.Services
{
    public class EpgService
    {
        private DatabaseService _db;

        public EpgService()
        {
            _db = new DatabaseService();
        }

        public async Task<bool> ParseEpgUrlAsync(string url)
        {
            LogService.Log($"[EpgService] ParseEpgUrlAsync başlatıldı. URL: '{url}'");
            try
            {
                var handler = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate };
                using (var client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromMinutes(15);
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                    string savedHash = _db.GetSetting($"last_epg_hash_{url}", "");
                    int currentProgCount = _db.GetEpgSourceProgramCount(url);

                    var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    if (!response.IsSuccessStatusCode) return false;

                    byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                    string newHash = "";
                    using (var md5 = System.Security.Cryptography.MD5.Create())
                    {
                        newHash = BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                    }

                    if (newHash == savedHash && currentProgCount > 0)
                    {
                        LogService.Log($"[EpgService] EPG değişmemiş (Hash eşleşti): {url}");
                        return true;
                    }

                    using (var ms = new MemoryStream(bytes))
                    {
                        Stream xmlStream = ms;
                        // Akıllı GZip Tespiti (Sihirli Numara: 1F 8B)
                        if (bytes.Length > 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
                        {
                            LogService.Log("[EpgService] GZip içeriği algılandı. Dekompresyon yapılıyor.");
                            xmlStream = new GZipStream(ms, CompressionMode.Decompress);
                        }

                        var programs = new List<EpgProgram>();
                        var epgChannels = new List<Tuple<string, string, string, string>>();
                        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreWhitespace = true, Async = true };

                        using (var reader = XmlReader.Create(xmlStream, settings))
                        {
                            string currentChannelId = null;
                            EpgProgram currentProg = null;
                            string currentElement = "";

                            while (await reader.ReadAsync())
                            {
                                if (reader.NodeType == XmlNodeType.Element)
                                {
                                    currentElement = reader.Name;
                                    if (reader.Name == "channel")
                                    {
                                        currentChannelId = reader.GetAttribute("id");
                                    }
                                    else if (reader.Name == "icon" && currentChannelId != null)
                                    {
                                        var logo = reader.GetAttribute("src");
                                        // Mevcut kanala logo ekle (Basitlik için sonradan eklenebilir veya listede tutulur)
                                    }
                                    else if (reader.Name == "programme")
                                    {
                                        currentProg = new EpgProgram
                                        {
                                            StartTime = ParseXmltvDate(reader.GetAttribute("start")),
                                            EndTime = ParseXmltvDate(reader.GetAttribute("stop")),
                                            SourceUrl = url,
                                            ChannelName = reader.GetAttribute("channel")
                                        };
                                    }
                                }
                                else if (reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.CDATA)
                                {
                                    if (currentElement == "display-name" && currentChannelId != null)
                                    {
                                        epgChannels.Add(new Tuple<string, string, string, string>(currentChannelId, reader.Value, "", url));
                                    }
                                    else if (currentProg != null)
                                    {
                                        if (currentElement == "title") currentProg.Title = reader.Value;
                                        else if (currentElement == "desc") currentProg.Description = reader.Value;
                                    }
                                }
                                else if (reader.NodeType == XmlNodeType.EndElement)
                                {
                                    if (reader.Name == "programme" && currentProg != null)
                                    {
                                        if (!string.IsNullOrEmpty(currentProg.Title)) programs.Add(currentProg);
                                        currentProg = null;
                                    }
                                    else if (reader.Name == "channel") currentChannelId = null;
                                    currentElement = "";
                                }
                            }
                        }

                        _db.ClearEpgByUrl(url);
                        if (epgChannels.Count > 0) _db.SaveEpgChannels(epgChannels);
                        if (programs.Count > 0) _db.SaveEpgPrograms(programs);

                        _db.CleanupOldEpgPrograms();

                        _db.AddEpgSource(url);
                        _db.SetSetting($"last_epg_hash_{url}", newHash);
                        _db.SetSetting($"epg_updated_{url}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

                        LogService.Log($"[EpgService] EPG Başarılı: {programs.Count} program, {epgChannels.Count} kanal yüklendi.");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"[EpgService] Kritik Hata (URL: {url})", ex);
                return false;
            }
        }

        private DateTime ParseXmltvDate(string dateStr)
        {
            if (string.IsNullOrEmpty(dateStr)) return DateTime.Now;
            try
            {
                string cleanDate = dateStr.Split(' ')[0];
                if (cleanDate.Length >= 14)
                {
                    int y = int.Parse(cleanDate.Substring(0, 4));
                    int m = int.Parse(cleanDate.Substring(4, 2));
                    int d = int.Parse(cleanDate.Substring(6, 2));
                    int h = int.Parse(cleanDate.Substring(8, 2));
                    int min = int.Parse(cleanDate.Substring(10, 2));
                    int s = int.Parse(cleanDate.Substring(12, 2));
                    return new DateTime(y, m, d, h, min, s, DateTimeKind.Utc).ToLocalTime();
                }
            } catch { }
            return DateTime.Now;
        }

        public EpgProgram GetCurrentEpgForChannel(Channel channel) => _db.GetCurrentEpgForChannel(channel);
        public List<string> GetUniqueEpgChannelNames() => _db.GetUniqueEpgChannelNames();
        public Dictionary<string, EpgProgram> GetCurrentEpgsForChannels(List<Channel> channels) => _db.GetCurrentEpgsForChannels(channels);
        public EpgProgram GetNextEpgForChannel(Channel channel) => _db.GetNextEpgForChannel(channel);

        public async Task StartAutoUpdateTimerAsync()
        {
            while (true)
            {
                try
                {
                    var sources = _db.GetEpgSources();
                    foreach (var url in sources)
                    {
                        string lastUpdatedStr = _db.GetSetting($"epg_updated_{url}", "");
                        bool shouldUpdate = false;
                        if (string.IsNullOrEmpty(lastUpdatedStr)) shouldUpdate = true;
                        else if (DateTime.TryParseExact(lastUpdatedStr, "yyyy-MM-dd HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime lastUpdated))
                        {
                            if ((DateTime.Now - lastUpdated).TotalHours >= 24) shouldUpdate = true;
                        }
                        else shouldUpdate = true;

                        if (shouldUpdate)
                        {
                            Console.WriteLine($"[EPG Auto-Update] {url} güncelleniyor...");
                            await ParseEpgUrlAsync(url);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[EPG Auto-Update] Hata: " + ex.Message);
                }
                await Task.Delay(TimeSpan.FromHours(4));
            }
        }
    }
}
