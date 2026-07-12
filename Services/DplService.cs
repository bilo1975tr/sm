using StreamMesh.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace StreamMesh.Services
{
    public class DplService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public async Task<List<Channel>> ParseDplAsync(string dplContentOrUrl, string categoryHint = null)
        {
            var channels = new List<Channel>();
            string content = dplContentOrUrl;
            string playlistUrl = "";
            string fileName = "";

            if (dplContentOrUrl.StartsWith("http://") || dplContentOrUrl.StartsWith("https://"))
            {
                try
                {
                    LogService.Log($"Downloading DPL from URL: {dplContentOrUrl}");
                    content = await _httpClient.GetStringAsync(dplContentOrUrl);
                    playlistUrl = dplContentOrUrl;
                    fileName = Path.GetFileName(new Uri(dplContentOrUrl).AbsolutePath);
                }
                catch (Exception ex)
                {
                    LogService.LogError("Error downloading DPL", ex);
                    return channels;
                }
            }
            else if (File.Exists(dplContentOrUrl))
            {
                try
                {
                    LogService.Log($"Reading DPL from local file: {dplContentOrUrl}");
                    content = await File.ReadAllTextAsync(dplContentOrUrl);
                    playlistUrl = dplContentOrUrl;
                    fileName = Path.GetFileName(dplContentOrUrl);
                }
                catch (Exception ex)
                {
                    LogService.LogError("Error reading DPL file", ex);
                    return channels;
                }
            }
            else
            {
                return channels;
            }

            // Kategori tahmini
            string autoCategory = null;
            if (string.IsNullOrEmpty(categoryHint) || categoryHint == "Otomatik")
            {
                string fnLow = fileName.ToLower();
                if (fnLow.Contains("film") || fnLow.Contains("movie") || fnLow.Contains("sinema") || fnLow.Contains("vod")) autoCategory = "Film";
                else if (fnLow.Contains("dizi") || fnLow.Contains("series")) autoCategory = "Dizi";
                else if (fnLow.Contains("tv") || fnLow.Contains("kanal")) autoCategory = "TV";
                else autoCategory = "DPL Listesi";
            }
            else
            {
                autoCategory = categoryHint;
            }

            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var channelDict = new Dictionary<string, Channel>();

            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"^(\d+)\*(file|title|played)\*(.+)$");
                if (match.Success)
                {
                    string id = match.Groups[1].Value;
                    string key = match.Groups[2].Value;
                    string value = match.Groups[3].Value;

                    if (!channelDict.ContainsKey(id))
                    {
                        channelDict[id] = new Channel
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            PlaylistUrl = playlistUrl,
                            SourceType = "DPL",
                            Category = autoCategory,
                            LogoUrl = "",
                            Language = "Bilinmiyor"
                        };
                    }

                    if (key == "file")
                    {
                        channelDict[id].Url = value.Trim();
                        if (value.StartsWith("acestream://"))
                        {
                            channelDict[id].SourceType = "ACESTREAM";
                        }
                    }
                    else if (key == "title")
                    {
                        channelDict[id].Name = value.Trim();
                    }
                }
            }

            foreach (var kvp in channelDict)
            {
                var ch = kvp.Value;
                if (!string.IsNullOrEmpty(ch.Url) && !string.IsNullOrEmpty(ch.Name))
                {
                    channels.Add(ch);
                }
            }

            LogService.Log($"Successfully parsed {channels.Count} channels from DPL.");
            return channels;
        }
    }
}
