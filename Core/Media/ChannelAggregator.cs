using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StreamMesh.Models;

namespace StreamMesh.Core.Media
{
    public class ChannelAggregator
    {
        private static readonly ChannelAggregator _instance = new ChannelAggregator();
        public static ChannelAggregator Instance => _instance;

        private ChannelAggregator() { }

        /// <summary>
        /// Normalizes channel title to a clean canonical key for duplicate matching.
        /// E.g., "TRT 1 HD", "TRT 1 FHD", "TRT 1 (TR)" -> "trt1"
        /// </summary>
        public string GetNormalizedChannelKey(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return "";

            string name = rawName.ToLowerInvariant();

            // Strip common quality and region indicators
            name = Regex.Replace(name, @"\b(hd|fhd|sd|4k|uhd|hevc|raw|1080p|720p|hq|tr|en|de|fr|ru|ar)\b", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\[.*?\]|\(.*?\)", ""); // Remove brackets and parentheses
            name = Regex.Replace(name, @"[^a-z0-9]", ""); // Keep only alphanumeric

            return name.Trim();
        }

        /// <summary>
        /// Aggregates a list of raw channels by merging duplicates with alternative URLs, Logos, EPG IDs, and Names.
        /// </summary>
        public List<Channel> AggregateChannels(IEnumerable<Channel> incomingChannels)
        {
            if (incomingChannels == null) return new List<Channel>();

            var aggregated = new List<Channel>();
            var urlMap = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);
            var keyMap = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);

            foreach (var ch in incomingChannels)
            {
                if (ch == null) continue;

                Channel? matched = null;

                // 1. Check for matching Stream URL first
                var urls = ch.GetUrlList();
                foreach (var u in urls)
                {
                    if (urlMap.TryGetValue(u, out var foundByUrl))
                    {
                        matched = foundByUrl;
                        break;
                    }
                }

                // 2. Check for matching Normalized Channel Key + Category
                if (matched == null)
                {
                    string primaryName = ch.PrimaryName;
                    string nameKey = GetNormalizedChannelKey(primaryName);
                    if (!string.IsNullOrEmpty(nameKey))
                    {
                        string compositeKey = $"{ch.Category?.ToLowerInvariant() ?? "tv"}:{nameKey}";
                        if (keyMap.TryGetValue(compositeKey, out var foundByKey))
                        {
                            matched = foundByKey;
                        }
                    }
                }

                if (matched != null)
                {
                    // Merge into existing matched channel
                    matched.MergeWith(ch);

                    // Register any newly added URLs to the urlMap
                    foreach (var u in matched.GetUrlList())
                    {
                        if (!urlMap.ContainsKey(u)) urlMap[u] = matched;
                    }
                }
                else
                {
                    // Create a clone or new aggregated channel entry
                    var newAggregated = ch;
                    aggregated.Add(newAggregated);

                    foreach (var u in newAggregated.GetUrlList())
                    {
                        if (!urlMap.ContainsKey(u)) urlMap[u] = newAggregated;
                    }

                    string primaryName = newAggregated.PrimaryName;
                    string nameKey = GetNormalizedChannelKey(primaryName);
                    if (!string.IsNullOrEmpty(nameKey))
                    {
                        string compositeKey = $"{newAggregated.Category?.ToLowerInvariant() ?? "tv"}:{nameKey}";
                        if (!keyMap.ContainsKey(compositeKey)) keyMap[compositeKey] = newAggregated;
                    }
                }
            }

            return aggregated;
        }
    }
}
