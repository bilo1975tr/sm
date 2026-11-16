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

            string groupTitle = (channel.GroupTitle ?? "").Trim();
            string groupUpper = groupTitle.ToUpperInvariant();
            string catUpper = (channel.Category ?? "").ToUpperInvariant();
            string name = (channel.Name ?? "").Trim();
            string nameUpper = name.ToUpperInvariant();
            string urlLower = (channel.Url ?? "").ToLowerInvariant();

            // 1. Dil Normalizasyonu (Öncelikle kanalın dili und ise veya boşsa isim ve gruptan çıkar)
            if (string.IsNullOrWhiteSpace(channel.Language) || channel.Language.Equals("und", StringComparison.OrdinalIgnoreCase))
            {
                string detectedLang = Channel.ExtractLanguageFromText(groupTitle);
                if (detectedLang == "und") detectedLang = Channel.ExtractLanguageFromText(name);
                if (detectedLang != "und") channel.Language = detectedLang;
                else channel.Language = Channel.NormalizeLanguage(channel.Language);
            }
            else
            {
                channel.Language = Channel.NormalizeLanguage(channel.Language);
            }

            // V1.8.8: Standart kategori koruması
            if (channel.Notes == "FORCE_CAT")
            {
                channel.Notes = ""; // Clear marker
                return;
            }

            // 2. KESİN CANLI YAYIN ve VOD FİLM/DİZİ TESPİTİ
            bool hasMovieExt = urlLower.EndsWith(".mkv") || urlLower.EndsWith(".mp4") || urlLower.EndsWith(".avi") ||
                               urlLower.Contains(".mkv?") || urlLower.Contains(".mp4?") || urlLower.Contains(".avi?") ||
                               urlLower.Contains(".mkv,") || urlLower.Contains(".mp4,");
            bool hasMoviePath = urlLower.Contains("/movie/") || urlLower.Contains("/movies/") || urlLower.Contains("/vod/");
            bool hasSeriesPath = urlLower.Contains("/series/");
            bool isExplicitLiveUrl = urlLower.Contains("/live/") || urlLower.EndsWith(".ts") || urlLower.Contains(".ts?") ||
                                     urlLower.EndsWith(".m3u8") || urlLower.Contains(".m3u8?") || urlLower.StartsWith("acestream://") ||
                                     string.Equals(channel.SourceType, "ACESTREAM", StringComparison.OrdinalIgnoreCase);

            bool hasStrongVodGroup = groupUpper.Contains("VOD") || groupUpper.Contains("FILMLER") || groupUpper.Contains("MOVIES") ||
                                     groupUpper.Contains("YABANCI FILM") || groupUpper.Contains("YERLI FILM") ||
                                     catUpper == "FILM" || catUpper == "FILMLER" || catUpper == "MOVIE" || catUpper == "MOVIES" || catUpper == "VOD";

            bool hasYearInName = System.Text.RegularExpressions.Regex.IsMatch(name, @"\((19\d{2}|20\d{2})\)");

            // 3. DİZİ TESPİTİ (Bölüm/Sezon kalıbı, /series/ yolu veya VOD uzantılı dizi grubu)
            bool hasEpisodePattern = channel.SeasonNumber > 0 || channel.EpisodeNumber > 0 ||
                                     System.Text.RegularExpressions.Regex.IsMatch(nameUpper, @"(?i)\bs\d+\s*e\d+\b|\b\d+x\d+\b|(?:bölüm|bolum|\bsezon\b|\bep\b|\bpart\b)\s*\d+");
            bool hasSeriesGroup = groupUpper.Contains("DIZILER") || groupUpper.Contains("DİZİLER") || groupUpper.Contains("SERIES") ||
                                  catUpper == "DIZI" || catUpper == "DİZİ" || catUpper == "DIZILER" || catUpper == "SERIES";
            bool isSeriesPattern = hasSeriesPath ||
                                   (!isExplicitLiveUrl && (hasEpisodePattern || (hasSeriesGroup && string.IsNullOrWhiteSpace(channel.EpgId))));

            // 4. RADYO TESPİTİ
            bool isRadio = groupUpper.Contains("RADYO") || groupUpper.Contains("RADIO") ||
                           catUpper.Contains("RADYO") || catUpper.Contains("RADIO") ||
                           nameUpper.Contains(" RADYO") || nameUpper.Contains(" RADIO") || nameUpper.StartsWith("RADYO ") || nameUpper.StartsWith("RADIO ");

            if (isRadio)
            {
                channel.Category = "Radyo";
            }
            else if (isSeriesPattern)
            {
                channel.Category = "Dizi";
            }
            else if (hasMoviePath || hasMovieExt || (!isExplicitLiveUrl && string.IsNullOrWhiteSpace(channel.EpgId) && (hasStrongVodGroup || (hasYearInName && !groupUpper.Contains("CANLI") && !groupUpper.Contains("LIVE")))))
            {
                // Gerçek VOD film sinyalleri varsa Film yap (Action/Drama/Cinema gibi canlı TV kanalları TV kalır)
                channel.Category = "Film";
            }
            else
            {
                // Canlı TV (Ulusal, Haber, Spor, Belgesel, Sinema kanalları vb.)
                channel.Category = "TV";
            }

            if (string.IsNullOrWhiteSpace(channel.Category)) channel.Category = "TV";
        }
    }
}
