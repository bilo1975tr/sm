using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using StreamMesh.Models;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class M3uService
    {
        private readonly HttpClient _httpClient = new HttpClient();

        public async Task<List<Channel>> ParseM3uAsync(string url)
        {
            var channels = new List<Channel>();
            try
            {
                var content = await _httpClient.GetStringAsync(url);
                var lines = content.Split('\n');
                Channel? current = null;

                foreach (var line in lines)
                {
                    if (line.StartsWith("#EXTINF:"))
                    {
                        current = new Channel { SourceType = "M3U" };
                        int nameIdx = line.LastIndexOf(',');
                        if (nameIdx > 0) current.Name = line.Substring(nameIdx + 1).Trim();
                    }
                    else if (!line.StartsWith("#") && current != null && !string.IsNullOrWhiteSpace(line))
                    {
                        current.Url = line.Trim();
                        channels.Add(current);
                        current = null;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"M3u Parse Error: {ex.Message}");
            }
            return channels;
        }
    }
}
