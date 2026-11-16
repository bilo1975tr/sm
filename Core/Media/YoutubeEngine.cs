using System.Linq;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace StreamMesh.Core.Media
{
    public class YoutubeEngine
    {
        private readonly YoutubeClient _yt = new YoutubeClient();

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
