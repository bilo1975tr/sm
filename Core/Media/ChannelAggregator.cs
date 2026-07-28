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

            foreach (var ch in incomingChannels)
            {
                if (ch == null) continue;

                Channel? matched = null;
                var urls = ch.GetUrlList();

                foreach (var u in urls)
                {
                    if (urlMap.TryGetValue(u, out var foundByUrl))
                    {
                        matched = foundByUrl;
                        break;
                    }
                }

                if (matched != null)
                {
                    // Merge Metadata: URL is identical, so we just append new Names, Logos, EPGs
                    matched.MergeWith(ch);
                }
                else
                {
                    aggregated.Add(ch);
                    foreach (var u in urls) urlMap[u] = ch;
                }
            }

            return aggregated;
        }
    }
}
