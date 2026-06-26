using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StreamMesh.Models;

namespace StreamMesh.Services
{
    public class M3uService
    {
        private readonly HttpClient _httpClient = new HttpClient();

        public async Task<List<Channel>> ParseM3uAsync(string m3uContentOrUrl, string categoryHint = null)
        {
            var channels = new List<Channel>();
            string content = m3uContentOrUrl;
            string playlistUrl = "";
            string fileName = "";

            // Eğer bir URL ise içeriği indir
            if (m3uContentOrUrl.StartsWith("http://") || m3uContentOrUrl.StartsWith("https://"))
            {
                try
                {
                    LogService.Log($"Downloading/Analyzing URL: {m3uContentOrUrl}");
                    var response = await _httpClient.GetAsync(m3uContentOrUrl);
                    response.EnsureSuccessStatusCode();
                    
                    // Try to read as bytes to handle encoding better
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                    content = System.Text.Encoding.UTF8.GetString(bytes);

                    // If it looks like junk or doesn't have M3U signature, try default encoding
                    if (!content.Contains("#EXTINF") && !content.Contains("#EXTM3U"))
                    {
                        content = System.Text.Encoding.Default.GetString(bytes);
                    }

                    // Akıllı Kontrol: Daha esnek kontrol
                    string trimmedStart = content.TrimStart();
                    if (!trimmedStart.StartsWith("#EXTM3U") && !content.Contains("#EXTINF") && !content.Contains("http"))
                    {
                         LogService.Log("Content does not look like an M3U playlist. Handing back.");
                         return channels; 
                    }

                    // HLS Chunklist / Master Playlist kontrolü (Canlı yayın linkini IPTV listesi sanmasını engellemek için)
                    if (content.Contains("#EXT-X-TARGETDURATION") || content.Contains("#EXT-X-STREAM-INF") || content.Contains("#EXT-X-MEDIA-SEQUENCE"))
                    {
                        LogService.Log("Content is an HLS stream chunklist, not an IPTV channel list. Falling back to direct link mode.");
                        return channels; 
                    }

                    playlistUrl = m3uContentOrUrl;
                    fileName = Path.GetFileName(new Uri(m3uContentOrUrl).AbsolutePath);
                }
                catch (Exception ex)
                {
                    LogService.LogError($"M3U URL download error: {m3uContentOrUrl}", ex);
                    return channels;
                }
            }
            else if (File.Exists(m3uContentOrUrl))
            {
                try
                {
                    LogService.Log($"Reading M3U from local file: {m3uContentOrUrl}");
                    content = await File.ReadAllTextAsync(m3uContentOrUrl);
                    playlistUrl = m3uContentOrUrl;
                    fileName = Path.GetFileName(m3uContentOrUrl);
                }
                catch (Exception ex)
                {
                    LogService.LogError($"M3U local file read error: {m3uContentOrUrl}", ex);
                    return channels;
                }
            }

            // Dosya adına göre kategori tahmini (eğer hint Otomatik ise)
            string autoCategory = null;
            if (string.IsNullOrEmpty(categoryHint) || categoryHint == "Otomatik")
            {
                string fnLow = fileName.ToLower();
                if (fnLow.Contains("film") || fnLow.Contains("movie") || fnLow.Contains("sinema") || fnLow.Contains("vod")) autoCategory = "Film";
                else if (fnLow.Contains("dizi") || fnLow.Contains("series") || fnLow.Contains("tv-show")) autoCategory = "Dizi";
                else if (fnLow.Contains("tv") || fnLow.Contains("kanal") || fnLow.Contains("live")) autoCategory = "TV";
            }
            else
            {
                autoCategory = categoryHint;
            }

            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Channel currentChannel = null;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("#EXTINF:"))
                {
                    currentChannel = new Channel();
                    currentChannel.Id = Guid.NewGuid().ToString("N");
                    currentChannel.PlaylistUrl = playlistUrl;
                    
                    // Logo parse
                    int logoStart = trimmedLine.IndexOf("tvg-logo=\"");
                    if (logoStart != -1)
                    {
                        logoStart += 10;
                        int logoEnd = trimmedLine.IndexOf("\"", logoStart);
                        if (logoEnd != -1)
                        {
                            currentChannel.LogoUrl = trimmedLine.Substring(logoStart, logoEnd - logoStart);
                        }
                    }

                    // Grup parse
                    int groupStart = trimmedLine.IndexOf("group-title=\"");
                    if (groupStart != -1)
                    {
                        groupStart += 13;
                        int groupEnd = trimmedLine.IndexOf("\"", groupStart);
                        if (groupEnd != -1)
                        {
                            currentChannel.GroupTitle = trimmedLine.Substring(groupStart, groupEnd - groupStart);
                        }
                    }

                    // İsim parse
                    var nameIndex = trimmedLine.LastIndexOf(',');
                    if (nameIndex != -1 && nameIndex + 1 < trimmedLine.Length)
                    {
                        currentChannel.Name = trimmedLine.Substring(nameIndex + 1).Trim();
                    }
                    else
                    {
                        currentChannel.Name = "Bilinmeyen Kanal";
                    }

                    // Basit kategori ve dil tespiti için küçük harf kopyaları
                    string groupLow = currentChannel.GroupTitle?.ToLower() ?? "";
                    string nameLow = currentChannel.Name?.ToLower() ?? "";

                    // Kategori Belirleme
                    if (!string.IsNullOrEmpty(autoCategory))
                    {
                        currentChannel.Category = autoCategory;
                    }
                    else
                    {
                        if (groupLow.Contains("film") || groupLow.Contains("movie") || groupLow.Contains("vod") || nameLow.Contains("vod") || groupLow.Contains("sinema"))
                            currentChannel.Category = "Film";
                        else if (groupLow.Contains("dizi") || groupLow.Contains("series"))
                            currentChannel.Category = "Dizi";
                        else
                            currentChannel.Category = "TV";
                    }

                    // Dil Belirleme (tvg-language, language öznitelikleri veya grup başlığı ile)
                    string parsedLang = null;
                    int langStart = trimmedLine.IndexOf("tvg-language=\"");
                    if (langStart != -1)
                    {
                        langStart += 14;
                        int langEnd = trimmedLine.IndexOf("\"", langStart);
                        if (langEnd != -1)
                        {
                            parsedLang = trimmedLine.Substring(langStart, langEnd - langStart);
                        }
                    }

                    if (string.IsNullOrEmpty(parsedLang))
                    {
                        int langAttrStart = trimmedLine.IndexOf("language=\"");
                        if (langAttrStart != -1)
                        {
                            langAttrStart += 10;
                            int langAttrEnd = trimmedLine.IndexOf("\"", langAttrStart);
                            if (langAttrEnd != -1)
                            {
                                parsedLang = trimmedLine.Substring(langAttrStart, langAttrEnd - langAttrStart);
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(parsedLang))
                    {
                        currentChannel.Language = Channel.NormalizeLanguage(parsedLang);
                    }
                    else
                    {
                        if (groupLow.Contains("tr") || groupLow.Contains("türk") || groupLow.Contains("turk"))
                        {
                            currentChannel.Language = "Türkçe";
                        }
                        else if (groupLow.Contains("en") || groupLow.Contains("uk") || groupLow.Contains("us") || groupLow.Contains("english"))
                        {
                            currentChannel.Language = "İngilizce";
                        }
                        else if (groupLow.Contains("de") || groupLow.Contains("germ") || groupLow.Contains("deutsch"))
                        {
                            currentChannel.Language = "Almanca";
                        }
                        else if (groupLow.Contains("fr") || groupLow.Contains("french") || groupLow.Contains("français") || groupLow.Contains("fransizca"))
                        {
                            currentChannel.Language = "Fransızca";
                        }
                        else if (groupLow.Contains("es") || groupLow.Contains("spanish") || groupLow.Contains("español"))
                        {
                            currentChannel.Language = "İspanyolca";
                        }
                        else if (groupLow.Contains("it") || groupLow.Contains("italian") || groupLow.Contains("italiano"))
                        {
                            currentChannel.Language = "İtalyanca";
                        }
                        else if (groupLow.Contains("ru") || groupLow.Contains("russian") || groupLow.Contains("русский") || groupLow.Contains("rusca"))
                        {
                            currentChannel.Language = "Rusça";
                        }
                        else if (groupLow.Contains("ar") || groupLow.Contains("arabic") || groupLow.Contains("arapca") || groupLow.Contains("arap"))
                        {
                            currentChannel.Language = "Arapça";
                        }
                        else
                        {
                            currentChannel.Language = "Bilinmiyor";
                        }
                    }

                    // Her durumda son bir kez normalize et
                    currentChannel.Language = Channel.NormalizeLanguage(currentChannel.Language);
                }
                else if (!trimmedLine.StartsWith("#") && currentChannel != null && !string.IsNullOrWhiteSpace(trimmedLine))
                {
                    currentChannel.Url = trimmedLine;
                    currentChannel.SourceType = DetermineSourceType(trimmedLine);
                    
                    if (currentChannel.Category != null && currentChannel.Category.Equals("Dizi", StringComparison.OrdinalIgnoreCase))
                    {
                        var seriesDetails = Channel.ParseSeriesDetails(currentChannel.Name, currentChannel.Url);
                        if (seriesDetails.IsParsed)
                        {
                            currentChannel.Name = $"{seriesDetails.SeriesName} - S{seriesDetails.Season:D2}E{seriesDetails.Episode:D2}";
                        }
                    }

                    channels.Add(currentChannel);
                    currentChannel = null;
                }
            }

            return channels;
        }

        private string DetermineSourceType(string url)
        {
            url = url.Trim();
            if (url.Contains("youtube.com") || url.Contains("youtu.be"))
                return "YOUTUBE";
            if (url.StartsWith("acestream://"))
                return "ACESTREAM";
            
            // if it looks like a 40 character hex string, it is likely an AceStream Content ID
            if (url.Length == 40 && Regex.IsMatch(url, @"^[a-fA-F0-9]+$"))
                return "ACESTREAM";

            return "M3U";
        }
    }
}
