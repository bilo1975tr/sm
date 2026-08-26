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
            var aceMap = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);
            var nameMap = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);

            var aceEngine = new AceEngine();

            foreach (var ch in incomingChannels)
            {
                if (ch == null) continue;

                Channel? matched = null;
                var urls = ch.GetUrlList();
                var epgs = ch.GetEpgIdList();
                var names = ch.GetNamesList();

                // 1. Try matching by AceStream Hash (Strongest match for P2P)
                foreach (var u in urls)
                {
                    string hash = aceEngine.ExtractHash(u);
                    if (!string.IsNullOrEmpty(hash))
                    {
                        if (aceMap.TryGetValue(hash, out matched)) break;
                    }
                }

                // 2. Try matching by URL
                if (matched == null)
                {
                    foreach (var u in urls)
                    {
                        if (urlMap.TryGetValue(u, out matched)) break;
                    }
                }

                // 3. Try matching by EPG ID
                if (matched == null)
                {
                    foreach (var e in epgs)
                    {
                        if (string.IsNullOrEmpty(e)) continue;
                        if (epgMap.TryGetValue(e, out matched)) break;
                    }
                }

                // 4. Try matching by Normalized Name (if name is meaningful)
                if (matched == null)
                {
                    foreach (var n in names)
                    {
                        string normKey = ChannelUtils.ToNormalizedKey(n);
                        if (!string.IsNullOrEmpty(normKey) && normKey.Length >= 3)
                        {
                            if (nameMap.TryGetValue(normKey, out matched)) break;
                        }
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

                // Re-index
                foreach (var u in matched.GetUrlList())
                {
                    urlMap[u] = matched;
                    string h = aceEngine.ExtractHash(u);
                    if (!string.IsNullOrEmpty(h)) aceMap[h] = matched;
                }
                foreach (var e in matched.GetEpgIdList())
                {
                    if (!string.IsNullOrEmpty(e)) epgMap[e] = matched;
                }
                foreach (var n in matched.GetNamesList())
                {
                    string nk = ChannelUtils.ToNormalizedKey(n);
                    if (!string.IsNullOrEmpty(nk) && nk.Length >= 3)
                    {
                        nameMap[nk] = matched;
                    }
                }
            }

            return aggregated;
        }
    }
}
