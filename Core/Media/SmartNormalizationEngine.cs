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
            string cat = (channel.Category ?? "").ToUpperInvariant();
            string name = (channel.Name ?? "").ToUpperInvariant();

            if (cat.Contains("TV") || cat.Contains("CANLI") || cat.Contains("LIVE")) channel.Category = "TV";
            else if (cat.Contains("FILM") || cat.Contains("MOVIE") || cat.Contains("SINEMA")) channel.Category = "Film";
            else if (cat.Contains("DIZI") || cat.Contains("SERIES") || cat.Contains("SERI")) channel.Category = "Dizi";
            else if (cat.Contains("RADYO") || cat.Contains("RADIO")) channel.Category = "Radyo";

            // Name-based fallback if category is generic or empty
            if (channel.Category == "TV" || string.IsNullOrEmpty(channel.Category))
            {
                if (name.Contains(" RADYO") || name.Contains(" RADIO")) channel.Category = "Radyo";
            }

            if (string.IsNullOrEmpty(channel.Category)) channel.Category = "TV";
        }
    }
}
