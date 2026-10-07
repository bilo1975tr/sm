using System;
using System.Collections.Generic;
using System.Linq;
using StreamMesh.Models;

namespace StreamMesh.Core.Media
{
    public class ChannelAggregator
    {
        private static readonly ChannelAggregator _instance = new ChannelAggregator();
        public static ChannelAggregator Instance => _instance;

        private ChannelAggregator() { }

        public List<Channel> AggregateChannels(IEnumerable<Channel> incomingChannels)
        {
            if (incomingChannels == null) return new List<Channel>();

            var aggregated = new List<Channel>();
            var urlMap = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);
            var aceMap = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);
            var aceEngine = new AceEngine();

            foreach (var ch in incomingChannels)
            {
                if (ch == null) continue;

                Channel? matched = null;
                var urls = ch.GetUrlList();

                foreach (var u in urls)
                {
                    string hash = aceEngine.ExtractHash(u);
                    if (!string.IsNullOrEmpty(hash))
                    {
                        if (aceMap.TryGetValue(hash, out matched)) break;
                    }
                }

                if (matched == null)
                {
                    foreach (var u in urls)
                    {
                        if (urlMap.TryGetValue(u, out matched)) break;
                        string cleanId = M3uEngine.CleanStreamUrlForIdentity(u);
                        if (!string.IsNullOrEmpty(cleanId) && urlMap.TryGetValue(cleanId, out matched)) break;
                    }
                }

                if (matched != null)
                {
                    bool isSeriesMismatch = (ch.SeasonNumber > 0 && matched.SeasonNumber > 0 && (ch.SeasonNumber != matched.SeasonNumber || ch.EpisodeNumber != matched.EpisodeNumber)) ||
                                            (ch.EpisodeNumber > 0 && matched.EpisodeNumber > 0 && ch.EpisodeNumber != matched.EpisodeNumber);

                    if (isSeriesMismatch)
                    {
                        matched = null;
                    }
                }

                if (matched != null)
                {
                    matched.MergeWith(ch);
                }
                else
                {
                    matched = ch;
                    aggregated.Add(matched);
                }

                foreach (var u in matched.GetUrlList())
                {
                    urlMap[u] = matched;
                    string cleanId = M3uEngine.CleanStreamUrlForIdentity(u);
                    if (!string.IsNullOrEmpty(cleanId)) urlMap[cleanId] = matched;

                    string h = aceEngine.ExtractHash(u);
                    if (!string.IsNullOrEmpty(h)) aceMap[h] = matched;
                }
            }

            return aggregated;
        }
    }
}
