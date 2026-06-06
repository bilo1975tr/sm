using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using System.IO;
using System.IO.Compression;
using StreamMesh.Models;

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
            try
            {
                var handler = new HttpClientHandler
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
                };

                using (var client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromMinutes(15);
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                    using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"EPG Bağlantı Hatası: {response.StatusCode} URL: {url}");
                            return false;
                        }

                        using (var stream = await response.Content.ReadAsStreamAsync())
                        {
                            Stream xmlStream = stream;
                            // Manuel GZip desteği (URL bazlı veya content-type bazlı değilse)
                            if (url.ToLower().EndsWith(".gz") || url.ToLower().Contains(".xml.gz"))
                            {
                                xmlStream = new GZipStream(stream, CompressionMode.Decompress);
                            }

                            var programs = new List<EpgProgram>();
                            var channelsMap = new Dictionary<string, string>();

                            var settings = new XmlReaderSettings
                            {
                                DtdProcessing = DtdProcessing.Ignore,
                                IgnoreWhitespace = true,
                                CheckCharacters = false,
                                Async = true
                            };

                            using (var reader = XmlReader.Create(xmlStream, settings))
                            {
                                string currentChannelId = null;
                                string currentElemName = null;
                                EpgProgram currentProg = null;

                                while (await reader.ReadAsync())
                                {
                                    if (reader.NodeType == XmlNodeType.Element)
                                    {
                                        currentElemName = reader.Name;
                                        if (currentElemName == "channel")
                                        {
                                            currentChannelId = reader.GetAttribute("id");
                                            if (!string.IsNullOrEmpty(currentChannelId) && !channelsMap.ContainsKey(currentChannelId))
                                                channelsMap[currentChannelId] = currentChannelId;
                                        }
                                        else if (currentElemName == "programme")
                                        {
                                            currentProg = new EpgProgram
                                            {
                                                StartTime = ParseXmltvDate(reader.GetAttribute("start")),
                                                EndTime = ParseXmltvDate(reader.GetAttribute("stop")),
                                                SourceUrl = url,
                                                ChannelName = reader.GetAttribute("channel") // Geçici olarak teknik ID'yi ata
                                            };
                                        }
                                    }
                                    else if (reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.CDATA)
                                    {
                                        string value = reader.Value;
                                        if (currentElemName == "display-name" && currentChannelId != null)
                                        {
                                            if (!string.IsNullOrEmpty(value))
                                                channelsMap[currentChannelId] = value;
                                        }
                                        else if (currentProg != null)
                                        {
                                            if (currentElemName == "title") currentProg.Title = value;
                                            else if (currentElemName == "desc") currentProg.Description = value;
                                        }
                                    }
                                    else if (reader.NodeType == XmlNodeType.EndElement)
                                    {
                                        if (reader.Name == "programme" && currentProg != null)
                                        {
                                            if (!string.IsNullOrEmpty(currentProg.Title))
                                                programs.Add(currentProg);
                                            currentProg = null;
                                        }
                                        else if (reader.Name == "channel")
                                        {
                                            currentChannelId = null;
                                        }
                                        currentElemName = null;
                                    }
                                }
                            }

                            // Gecikmeli Eşleştirme: Program isimlerini gerçek kanal isimleriyle güncelle
                            foreach (var prog in programs)
                            {
                                string techId = prog.ChannelName; // Yukarıda atadığımız ID
                                if (techId != null && channelsMap.TryGetValue(techId, out string mappedName))
                                {
                                    prog.ChannelName = mappedName;
                                }
                            }

                            if (programs.Count > 0 || channelsMap.Count > 0)
                            {
                                _db.ClearEpgByUrl(url);
                                _db.SaveEpgPrograms(programs);
                                _db.AddEpgSource(url);
                                Console.WriteLine($"EPG Başarılı: {programs.Count} program, {channelsMap.Count} kanal yüklendi.");
                                return true;
                            }
                            else
                            {
                                Console.WriteLine("EPG Hatası: Ayrıştırma bitti ancak veri bulunamadı.");
                                return false;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EPG Kritik Ayrıştırma Hatası: " + ex.Message);
                return false;
            }
        }

        private DateTime ParseXmltvDate(string dateStr)
        {
            if (string.IsNullOrEmpty(dateStr)) return DateTime.Now;
            // Format: 20240428163400 +0300
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
            }
            catch { }
            return DateTime.Now;
        }

        public EpgProgram GetCurrentEpgForChannel(Channel channel)
        {
            return _db.GetCurrentEpgForChannel(channel);
        }

        public List<string> GetUniqueEpgChannelNames()
        {
            return _db.GetUniqueEpgChannelNames();
        }

        public Dictionary<string, EpgProgram> GetCurrentEpgsForChannels(List<Channel> channels)
        {
            return _db.GetCurrentEpgsForChannels(channels);
        }

        public EpgProgram GetNextEpgForChannel(Channel channel)
        {
            return _db.GetNextEpgForChannel(channel);
        }

        public async Task StartAutoUpdateTimerAsync()
        {
            // Bu metod uygulama boyunca arka planda çalışarak EPG sürelerini kontrol eder.
            // 24 saatten eski olan veya hiç güncellenmemiş EPG kaynaklarını arka plandan asenkron olarak otomatik günceller.
            while (true)
            {
                try
                {
                    var sources = _db.GetEpgSources();
                    foreach (var url in sources)
                    {
                        string lastUpdatedStr = _db.GetSetting($"epg_updated_{url}", "");
                        bool shouldUpdate = false;
                        if (string.IsNullOrEmpty(lastUpdatedStr))
                        {
                            shouldUpdate = true;
                        }
                        else if (DateTime.TryParseExact(lastUpdatedStr, "yyyy-MM-dd HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime lastUpdated))
                        {
                            if ((DateTime.Now - lastUpdated).TotalHours >= 24)
                            {
                                shouldUpdate = true;
                            }
                        }
                        else
                        {
                            shouldUpdate = true;
                        }

                        if (shouldUpdate)
                        {
                            Console.WriteLine($"[EPG Auto-Update] {url} güncelleniyor (24 saati geçti)...");
                            bool success = await ParseEpgUrlAsync(url);
                            if (success)
                            {
                                _db.SetSetting($"epg_updated_{url}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                                Console.WriteLine($"[EPG Auto-Update] {url} başarıyla güncellendi.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[EPG Auto-Update] Hata: " + ex.Message);
                }

                // 4 saat aralıkla kontrol et
                await Task.Delay(TimeSpan.FromHours(4));
            }
        }
    }
}
