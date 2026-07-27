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
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        private readonly DatabaseEngine _db = new DatabaseEngine();

        public async Task LoadEpgAsync(string url)
        {
            try
            {
                byte[] data;
                if (url.StartsWith("http")) data = await _httpClient.GetByteArrayAsync(url);
                else data = await File.ReadAllBytesAsync(url);

                if (data == null || data.Length == 0) return;

                Stream stream = new MemoryStream(data);
                if (url.EndsWith(".gz")) stream = new GZipStream(stream, CompressionMode.Decompress);

                using var reader = new StreamReader(stream);
                string xml = await reader.ReadToEndAsync();
                var doc = XDocument.Parse(xml);

                var programs = new List<EpgProgram>();
                foreach (var el in doc.Root!.Elements("programme"))
                {
                    var prog = new EpgProgram
                    {
                        ChannelName = el.Attribute("channel")?.Value ?? "",
                        Title = el.Element("title")?.Value ?? "",
                        Description = el.Element("desc")?.Value ?? "",
                        SourceUrl = url
                    };

                    string start = el.Attribute("start")?.Value ?? "";
                    string stop = el.Attribute("stop")?.Value ?? "";

                    if (TryParseXmlTime(start, out DateTime st)) prog.StartTime = st;
                    if (TryParseXmlTime(stop, out DateTime et)) prog.EndTime = et;

                    programs.Add(prog);
                }

                if (programs.Count > 0)
                {
                    await _db.SaveEpgProgramsAsync(programs);
                }
            }
            catch { }
        }

        private bool TryParseXmlTime(string time, out DateTime result)
        {
            // Format: 20260726000000 +0300
            result = DateTime.MinValue;
            if (string.IsNullOrEmpty(time) || time.Length < 14) return false;
            try
            {
                string s = time.Substring(0, 14);
                result = DateTime.ParseExact(s, "yyyyMMddHHmmss", null);
                return true;
            }
            catch { return false; }
        }
    }
}
