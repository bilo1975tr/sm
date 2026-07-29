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
            var epgMap = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);

            foreach (var ch in incomingChannels)
            {
                if (ch == null) continue;

                Channel? matched = null;
                var urls = ch.GetUrlList();
                var epgs = ch.GetEpgIdList();

                // 1. Try matching by URL
                foreach (var u in urls)
                {
                    if (urlMap.TryGetValue(u, out var foundByUrl))
                    {
                        matched = foundByUrl;
                        break;
                    }
                }

                // 2. Try matching by EPG ID if no URL match
                if (matched == null)
                {
                    foreach (var e in epgs)
                    {
                        if (string.IsNullOrEmpty(e)) continue;
                        if (epgMap.TryGetValue(e, out var foundByEpg))
                        {
                            matched = foundByEpg;
                            break;
                        }
                    }
                }

                if (matched != null)
                {
                    // Merge Metadata: URL or EPG matched, combine everything
                    matched.MergeWith(ch);

                    // Re-index to ensure all alternate URLs/EPGs point to the same merged card
                    foreach (var u in matched.GetUrlList()) urlMap[u] = matched;
                    foreach (var e in matched.GetEpgIdList()) epgMap[e] = matched;
                }
                else
                {
                    aggregated.Add(ch);
                    foreach (var u in urls) urlMap[u] = ch;
                    foreach (var e in epgs) { if (!string.IsNullOrEmpty(e)) epgMap[e] = ch; }
                }
            }

            return aggregated;
        }
    }
}
