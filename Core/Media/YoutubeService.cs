using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using StreamMesh.Models;

namespace StreamMesh.Core.Media
{
    public class YoutubeService
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
    }
}
