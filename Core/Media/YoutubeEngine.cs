using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using StreamMesh.Models;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class YoutubeEngine
    {
        private readonly YoutubeClient _yt = new YoutubeClient();

        public async Task<List<Channel>> GetChannelsFromUrlAsync(string url)
        {
            var list = new List<Channel>();
            try
            {
                if (url.Contains("list="))
                {
                    string listId = url.Split("list=")[1].Split('&')[0];
                    var playlist = await _yt.Playlists.GetAsync(listId);
                    await foreach (var video in _yt.Playlists.GetVideosAsync(playlist.Id))
                    {
                        list.Add(new Channel {
                            Id = video.Id,
                            Name = video.Title,
                            Url = video.Url,
                            LogoUrl = video.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url ?? "",
                            SourceType = "YOUTUBE",
                            Category = "TV"
                        });
                    }
                }
                else
                {
                    var video = await _yt.Videos.GetAsync(url);
                    list.Add(new Channel {
                        Id = video.Id,
                        Name = video.Title,
                        Url = video.Url,
                        LogoUrl = video.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url ?? "",
                        SourceType = "YOUTUBE",
                        Category = "TV"
                    });
                }
            }
            catch (Exception ex) { LogService.LogError("YoutubeEngine: Metadata error", ex); }
            return list;
        }

        public async Task<string?> GetStreamUrlAsync(string videoUrl)
        {
            try
            {
                var manifest = await _yt.Videos.Streams.GetManifestAsync(videoUrl);

                // Adaptive streams (Best quality 1080p+)
                var videoStream = manifest.GetVideoOnlyStreams().OrderByDescending(s => s.VideoQuality.MaxHeight).FirstOrDefault();
                var audioStream = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                if (videoStream != null && audioStream != null)
                {
                    // For VLC to handle adaptive streams, we need to pass them specially or just fallback to muxed
                    // In hybrid, we stick to muxed for simplicity unless we implement the YTCUSTOM logic fully in PlayerView
                    var muxed = manifest.GetMuxedStreams().GetWithHighestVideoQuality();
                    return muxed?.Url;
                }

                return manifest.GetMuxedStreams().GetWithHighestVideoQuality()?.Url;
            }
            catch { return null; }
        }

        public async Task<int> GetActiveLiveViewersAsync(string videoUrl)
        {
            try
            {
                var video = await _yt.Videos.GetAsync(videoUrl);
                // Video engagement metrics if available
                return (int)(video.Engagement.ViewCount);
            }
            catch
            {
                return 0;
            }
        }
    }
}
