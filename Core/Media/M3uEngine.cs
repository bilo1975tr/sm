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
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        public async Task<List<Channel>> ParseM3uAsync(string urlOrPath, string categoryHint = "TV")
        {
            var channels = new List<Channel>();
            string content = "";

            try
            {
                if (urlOrPath.StartsWith("http")) content = await _httpClient.GetStringAsync(urlOrPath);
                else if (File.Exists(urlOrPath)) content = await File.ReadAllTextAsync(urlOrPath);

                if (string.IsNullOrEmpty(content)) return channels;

                var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                Channel? current = null;

                foreach (var line in lines)
                {
                    if (line.StartsWith("#EXTINF:"))
                    {
                        current = new Channel { Category = categoryHint, PlaylistUrl = urlOrPath };

                        int logoIdx = line.IndexOf("tvg-logo=\"");
                        if (logoIdx != -1)
                        {
                            int start = logoIdx + 10;
                            int end = line.IndexOf("\"", start);
                            if (end != -1) current.LogoUrl = line.Substring(start, end - start);
                        }

                        int groupIdx = line.IndexOf("group-title=\"");
                        if (groupIdx != -1)
                        {
                            int start = groupIdx + 13;
                            int end = line.IndexOf("\"", start);
                            if (end != -1) current.GroupTitle = line.Substring(start, end - start);
                        }

                        int nameIdx = line.LastIndexOf(',');
                        if (nameIdx != -1) current.Name = line.Substring(nameIdx + 1).Trim();
                    }
                    else if (!line.StartsWith("#") && current != null)
                    {
                        current.Url = line.Trim();
                        current.Id = Guid.NewGuid().ToString("N");

                        SmartNormalizationEngine.Instance.NormalizeChannel(current);
                        channels.Add(current);
                        current = null;
                    }
                }
            }
            catch { }
            return channels;
        }
    }
}
