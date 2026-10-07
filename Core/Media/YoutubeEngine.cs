using System;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace StreamMesh.Core.Media
{
    public class YoutubeEngine
    {
        private readonly YoutubeClient _client = new YoutubeClient();

        public async Task<string?> GetStreamUrlAsync(string videoUrl)
        {
            try
            {
                var manifest = await _client.Videos.Streams.GetManifestAsync(videoUrl);
                var streamInfo = manifest.GetMuxedStreams().GetWithHighestVideoQuality();
                return streamInfo?.Url;
            }
            catch { return null; }
        }

        public async Task<int> GetActiveLiveViewersAsync(string videoUrl)
        {
            try
            {
                var manifest = await _client.Videos.Streams.GetManifestAsync(videoUrl);
                return 0;
            }
            catch { return 0; }
        }
    }
}
