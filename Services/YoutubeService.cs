using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using StreamMesh.Models;

namespace StreamMesh.Services
{
    public class YoutubeService
    {
        private readonly YoutubeClient _youtubeClient;

        public YoutubeService()
        {
            _youtubeClient = new YoutubeClient();
        }

        public async Task<string> GetDirectStreamUrlAsync(string videoUrl)
        {
            try
            {
                var manifest = await _youtubeClient.Videos.Streams.GetManifestAsync(videoUrl);
                
                // Get the best audio stream
                var audioStream = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();
                if (audioStream == null) return null;

                // Get all video streams (adaptive, no audio) and group by resolution
                var videoStreams = manifest.GetVideoOnlyStreams()
                                           .Where(s => s.Container.Name == "mp4" || s.Container.Name == "webm")
                                           .OrderByDescending(s => s.VideoQuality.MaxHeight)
                                           .ToList();

                if (!videoStreams.Any())
                {
                    // Fallback to muxed
                    return manifest.GetMuxedStreams().GetWithHighestVideoQuality()?.Url;
                }

                // Forced 1080p search
                var best1080 = videoStreams.FirstOrDefault(s => s.VideoQuality.MaxHeight == 1080);
                
                // Format: YTCUSTOM::::[AudioUrl]::::[Res1]|||[Url1]::::[Res2]|||[Url2]...
                var parts = new List<string> { "YTCUSTOM", audioStream.Url };

                if (best1080 != null)
                {
                    parts.Add($"{best1080.VideoQuality.MaxHeight}p|||{best1080.Url}");
                    // Also add others but skip 1080 if already added as first choice
                }

                var handledHeights = new HashSet<int>();
                if (best1080 != null) handledHeights.Add(1080);

                foreach (var stn in videoStreams)
                {
                    if (!handledHeights.Contains(stn.VideoQuality.MaxHeight))
                    {
                        parts.Add($"{stn.VideoQuality.MaxHeight}p|||{stn.Url}");
                        handledHeights.Add(stn.VideoQuality.MaxHeight);
                    }
                }

                return string.Join("::::", parts);
            }
            catch (Exception ex)
            {
                LogService.LogError($"YouTube oynatma URL'si alınırken hata: {ex.Message}", ex);
                return null;
            }
        }

        public async Task<string> GetSingleMuxedStreamUrlAsync(string videoUrl)
        {
            try
            {
                var manifest = await _youtubeClient.Videos.Streams.GetManifestAsync(videoUrl);
                var muxedStream = manifest.GetMuxedStreams().GetWithHighestVideoQuality();
                return muxedStream?.Url;
            }
            catch (Exception ex)
            {
                LogService.LogError($"YouTube oynatma URL'si alınırken hata (Muxed): {ex.Message}", ex);
                return null;
            }
        }

        public async Task<List<Channel>> GetChannelsFromUrlAsync(string url)
        {
            var channels = new List<Channel>();
            
            try
            {
                if (!url.StartsWith("http"))
                {
                    if (url.StartsWith("www.")) url = "https://" + url;
                    else url = "https://www." + url.TrimStart('/');
                }

                bool isPlaylist = url.Contains("playlist?list=") || url.Contains("&list=") || url.Contains("?list=");

                if (isPlaylist)
                {
                    // Extract list ID to be sure
                    string listId = "";
                    if (url.Contains("list="))
                    {
                        var parts = url.Split(new[] { "list=" }, StringSplitOptions.None);
                        if (parts.Length > 1)
                        {
                            listId = parts[1].Split('&')[0];
                        }
                    }

                    if (!string.IsNullOrEmpty(listId))
                    {
                        await foreach (var video in _youtubeClient.Playlists.GetVideosAsync(listId))
                        {
                            var ch = new Channel
                            {
                                Id = Guid.NewGuid().ToString("N"),
                                Name = video.Title,
                                Url = video.Url,
                                LogoUrl = video.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url ?? "",
                                GroupTitle = "YouTube Playlist",
                                Category = "TV",
                                Language = "Bilinmiyor",
                                SourceType = "YOUTUBE",
                                PlaylistUrl = url
                            };
                            channels.Add(ch);
                        }
                    }
                }
                else
                {
                    var video = await _youtubeClient.Videos.GetAsync(url);
                    if (video != null)
                    {
                        var ch = new Channel
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Name = video.Title,
                            Url = video.Url,
                            LogoUrl = video.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url ?? "",
                            GroupTitle = "YouTube Video",
                            Category = "TV",
                            Language = "Bilinmiyor",
                            SourceType = "YOUTUBE",
                            PlaylistUrl = url
                        };
                        channels.Add(ch);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"[YouTube] Metadata Hatası: {ex.Message}", ex);
            }

            return channels;
        }
    }
}
