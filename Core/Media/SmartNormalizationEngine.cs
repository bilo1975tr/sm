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

            // 2. KESİN VOD FİLM TESPİTİ (URL Deseni, dosya uzantısı, yıl bilgisi ve uluslararası anahtar kelimeler)
            bool hasMovieExt = urlLower.EndsWith(".mkv") || urlLower.EndsWith(".mp4") || urlLower.EndsWith(".avi") ||
                               urlLower.Contains(".mkv?") || urlLower.Contains(".mp4?") || urlLower.Contains(".avi?") ||
                               urlLower.Contains(".mkv,") || urlLower.Contains(".mp4,");
            bool hasMoviePath = urlLower.Contains("/movie/") || urlLower.Contains("/movies/") || urlLower.Contains("/vod/");
            bool hasMovieKeywords = groupUpper.Contains("FILM") || groupUpper.Contains("MOVIE") || groupUpper.Contains("SINEMA") || groupUpper.Contains("VOD") ||
                                    groupUpper.Contains("AKSIYON") || groupUpper.Contains("ACTION") || groupUpper.Contains("KOMEDI") || groupUpper.Contains("COMEDY") ||
                                    groupUpper.Contains("KORKU") || groupUpper.Contains("HORROR") || groupUpper.Contains("THRILLER") || groupUpper.Contains("GERILIM") ||
                                    groupUpper.Contains("DRAMA") || groupUpper.Contains("ABENTEUER") || groupUpper.Contains("ADVENTURE") || groupUpper.Contains("ROMANCE") ||
                                    groupUpper.Contains("NETFLIX") || groupUpper.Contains("CINEMA") || groupUpper.Contains("KINO") ||
                                    catUpper.Contains("FILM") || catUpper.Contains("MOVIE") || catUpper.Contains("SINEMA") || catUpper.Contains("VOD");

            bool hasYearInName = System.Text.RegularExpressions.Regex.IsMatch(name, @"\((19\d{2}|20\d{2})\)");

            // 3. DİZİ TESPİTİ (Sezon, Bölüm, Series anahtar kelimeleri)
            bool isSeriesPattern = channel.SeasonNumber > 0 || channel.EpisodeNumber > 0 ||
                                   System.Text.RegularExpressions.Regex.IsMatch(nameUpper, @"(?i)\bs\d+\s*e\d+|\b\d+x\d+|(?:bölüm|bolum|\bep\b|\be\b|\bpart\b)\s*\d+") ||
                                   urlLower.Contains("/series/") ||
                                   groupUpper.Contains("DIZI") || groupUpper.Contains("SERIES") || groupUpper.Contains("SEZON") || groupUpper.Contains("EPISODE") ||
                                   catUpper.Contains("DIZI") || catUpper.Contains("SERIES");

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
            else if (hasMoviePath || hasMovieExt || (hasYearInName && (hasMovieKeywords || !groupUpper.Contains("CANLI") && !groupUpper.Contains("LIVE"))) || hasMovieKeywords)
            {
                // Canlı TV kanalı adı değilse ve Film işaretleri varsa FİLM yap
                channel.Category = "Film";
            }
            else
            {
                // Canlı TV (Ulusal, Haber, Spor, Belgesel, Çocuk, Müzik vb. tüm canlı yayınlar)
                channel.Category = "TV";
            }

            if (string.IsNullOrWhiteSpace(channel.Category)) channel.Category = "TV";
        }
    }
}
