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

            // Standart 4 ana kategoriye eşleme [TV, Film, Dizi, Radyo]
            if (groupTitle == "DIZI" || groupTitle.Contains("DIZI") || groupTitle.Contains("SERIES") || groupTitle.Contains("SEZON") || groupTitle.Contains("EPISODE") ||
                cat == "DIZI" || cat.Contains("DIZI") || cat.Contains("SERIES"))
            {
                channel.Category = "Dizi";
            }
            else if (groupTitle == "FILM" || groupTitle.Contains("FILM") || groupTitle.Contains("MOVIE") || groupTitle.Contains("SINEMA") || groupTitle.Contains("VOD") ||
                     groupTitle.Contains("AKSIYON") || groupTitle.Contains("KOMEDI") || groupTitle.Contains("KORKU") || groupTitle.Contains("GERILIM") || groupTitle.Contains("YESILCAM") ||
                     cat == "FILM" || cat.Contains("FILM") || cat.Contains("MOVIE") || cat.Contains("SINEMA") || cat.Contains("VOD"))
            {
                channel.Category = "Film";
            }
            else if (groupTitle == "RADYO" || groupTitle.Contains("RADYO") || groupTitle.Contains("RADIO") ||
                     cat == "RADYO" || cat.Contains("RADYO") || cat.Contains("RADIO"))
            {
                channel.Category = "Radyo";
            }
            else
            {
                // Canlı TV (Ulusal, Haber, Spor, Belgesel, Çocuk, Müzik vb. tüm canlı yayınlar)
                channel.Category = "TV";
            }

            // Dizi fallback (bölüm veya sezon formatı varsa kesin Dizi'dir)
            if (channel.SeasonNumber > 0 || channel.EpisodeNumber > 0 || System.Text.RegularExpressions.Regex.IsMatch(name, @"(?i)\bs\d+\s?e\d+|\d+x\d+"))
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
