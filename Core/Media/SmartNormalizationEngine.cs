using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using StreamMesh.Models;
using StreamMesh.Core.Database;

namespace StreamMesh.Core.Media
{
    public class SmartNormalizationEngine
    {
        private static readonly SmartNormalizationEngine _instance = new SmartNormalizationEngine();
        public static SmartNormalizationEngine Instance => _instance;

        private SmartNormalizationEngine() { }

        public void NormalizeChannel(Channel channel)
        {
            if (channel == null) return;
            channel.Language = Channel.NormalizeLanguage(channel.Language);

            // V1.8.8: Standardize categories to [TV, Film, Dizi, Radyo]
            if (channel.Notes == "FORCE_CAT")
            {
                channel.Notes = ""; // Clear marker
                return;
            }

            string groupTitle = (channel.GroupTitle ?? "").ToUpperInvariant();
            string cat = (channel.Category ?? "").ToUpperInvariant();
            string name = (channel.Name ?? "").ToUpperInvariant();

            // Check GroupTitle or Category
            if (groupTitle == "DIZI" || groupTitle.Contains("DIZI") || groupTitle.Contains("SERIES") || groupTitle.Contains("SEZON") || groupTitle.Contains("EPISODE") ||
                cat == "DIZI" || cat.Contains("DIZI") || cat.Contains("SERIES"))
            {
                channel.Category = "Dizi";
            }
            else if (groupTitle == "FILM" || groupTitle.Contains("FILM") || groupTitle.Contains("MOVIE") || groupTitle.Contains("SINEMA") || groupTitle.Contains("VOD") ||
                     cat == "FILM" || cat.Contains("FILM") || cat.Contains("MOVIE") || cat.Contains("SINEMA"))
            {
                channel.Category = "Film";
            }
            else if (groupTitle == "RADYO" || groupTitle.Contains("RADYO") || groupTitle.Contains("RADIO") ||
                     cat == "RADYO" || cat.Contains("RADYO") || cat.Contains("RADIO"))
            {
                channel.Category = "Radyo";
            }
            else if (groupTitle == "TV" || groupTitle.Contains("TV") || groupTitle.Contains("CANLI") || groupTitle.Contains("LIVE") ||
                     cat == "TV" || cat.Contains("TV") || cat.Contains("CANLI") || cat.Contains("LIVE"))
            {
                channel.Category = "TV";
            }
            else if (!string.IsNullOrEmpty(groupTitle))
            {
                // If groupTitle is set to something custom, preserve it or fallback
                channel.Category = groupTitle;
            }

            // Series detection fallback based on name patterns S01E01, etc.
            if (channel.SeasonNumber > 0 || channel.EpisodeNumber > 0 || System.Text.RegularExpressions.Regex.IsMatch(name, @"(?i)s\d+\s?e\d+|\d+x\d+"))
            {
                channel.Category = "Dizi";
            }
            else if (name.Contains(" RADYO") || name.Contains(" RADIO") || name.StartsWith("RADYO ") || name.StartsWith("RADIO "))
            {
                channel.Category = "Radyo";
            }

            if (string.IsNullOrWhiteSpace(channel.Category)) channel.Category = "TV";
        }
    }
}
