using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StreamMesh.Models;

namespace StreamMesh.Core.Media
{
    /// <summary>
    /// Enriches series, season, and episode metadata from filenames and M3U playlist groups.
    /// </summary>
    public static class SeriesMetadataEnricher
    {
        public static void EnrichSeriesInfo(Channel channel, string sourceUrlOrPath, string categoryHint)
        {
            if (channel == null) return;

            string cat = (channel.Category ?? categoryHint ?? "").ToLowerInvariant();
            string origGroup = (channel.GroupTitle ?? "").Trim();
            string currentName = (channel.Name ?? "").Trim();

            bool isExplicitEpisode = Regex.IsMatch(currentName, @"^(?i)(?:sezon\s*\d+\s*)?(?:bölüm|bolum|\bep\b|\be\b|\bpart\b)?\s*\d+\.?$") ||
                                     Regex.IsMatch(currentName, @"^(?i)s\d+\s*e\d+");

            bool isSeriesCategory = cat.Contains("dizi") || cat.Contains("series") ||
                                    (!string.IsNullOrEmpty(origGroup) && (origGroup.IndexOf("dizi", StringComparison.OrdinalIgnoreCase) >= 0 || origGroup.IndexOf("series", StringComparison.OrdinalIgnoreCase) >= 0));

            // Sadece açıkça dizi olan veya bölüm formatına uyan içeriklerde dizi zenginleştirmesi yap
            if (!isSeriesCategory && !isExplicitEpisode)
            {
                return;
            }

            // TV kanalı, Film veya Radyo ise ezme
            if (!isExplicitEpisode && (cat == "tv" || cat == "film" || cat == "movie" || cat == "radyo" || cat == "radio"))
            {
                return;
            }

            string[] genericGroups = { "dizi", "genel", "tv", "series", "1 bölüm / parça", "1 bolum / parca", "filmler", "movies" };
            string seriesName = "";

            if (!string.IsNullOrEmpty(origGroup) && !genericGroups.Contains(origGroup.ToLowerInvariant()))
            {
                seriesName = origGroup;
            }
            else
            {
                string seriesNameFromSource = ExtractSeriesNameFromSource(sourceUrlOrPath);
                if (!string.IsNullOrEmpty(seriesNameFromSource))
                {
                    seriesName = seriesNameFromSource;
                }
            }

            if (!string.IsNullOrEmpty(seriesName))
            {
                channel.Category = "Dizi";

                if (isExplicitEpisode && !genericGroups.Contains(seriesName.ToLowerInvariant()))
                {
                    channel.Name = $"{seriesName} - {currentName}";
                }

                if (string.IsNullOrWhiteSpace(channel.GroupTitle) || genericGroups.Contains(channel.GroupTitle.ToLowerInvariant()))
                {
                    channel.GroupTitle = seriesName;
                }
            }
        }

        public static string ExtractSeriesNameFromSource(string sourceUrlOrPath)
        {
            if (string.IsNullOrWhiteSpace(sourceUrlOrPath)) return "";
            try
            {
                string path = sourceUrlOrPath.Split('?')[0].Trim();
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(fileName)) return "";

                // Standart karma veya tekil olmayan m3u dosya adlarını atla
                string lower = fileName.ToLowerInvariant();
                if (lower == "index" || lower == "playlist" || lower == "all" || lower == "tv" || lower == "channels" ||
                    lower == "tr" || lower == "de" || lower == "streams" || lower == "live")
                {
                    return "";
                }

                // Tire, alt çizgi ve nokta karakterlerini boşluğa dönüştür
                string clean = fileName.Replace("-", " ").Replace("_", " ").Replace(".", " ");
                clean = Regex.Replace(clean, @"\s+", " ").Trim();

                // İlk harfleri büyük yap (Title Case)
                var culture = CultureInfo.GetCultureInfo("tr-TR");
                var textInfo = culture.TextInfo;
                return textInfo.ToTitleCase(clean.ToLower(culture));
            }
            catch
            {
                return "";
            }
        }
    }
}
