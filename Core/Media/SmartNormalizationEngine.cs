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

            // V1.8.5: Removed name-based category guessing.
            // Only using the category explicitly set from auto_update.json or M3U source.
            if (string.IsNullOrEmpty(channel.Category)) channel.Category = "TV";
        }
    }
}
