using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StreamMesh.Models;
using StreamMesh.Core.Database;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class EpgService
    {
        private readonly DatabaseEngine _db = new DatabaseEngine();

        public async Task<Dictionary<string, EpgProgram>> GetCurrentEpgsAsync(List<Channel> channels)
        {
            var dict = new Dictionary<string, EpgProgram>();
            if (channels == null || channels.Count == 0) return dict;

            try
            {
                var programs = await _db.GetCurrentEpgProgramsAsync();
                var now = DateTime.Now;

                var activePrograms = programs.Where(p => now >= p.StartTime && now <= p.EndTime).ToList();
                if (activePrograms.Count == 0) activePrograms = programs;

                var exactLookup = new Dictionary<string, EpgProgram>(StringComparer.OrdinalIgnoreCase);
                var normLookup = new Dictionary<string, EpgProgram>(StringComparer.OrdinalIgnoreCase);

                foreach (var p in activePrograms)
                {
                    if (string.IsNullOrWhiteSpace(p.ChannelName)) continue;

                    if (!exactLookup.ContainsKey(p.ChannelName)) exactLookup[p.ChannelName] = p;

                    string normKey = ChannelUtils.ToNormalizedKey(p.ChannelName);
                    if (!string.IsNullOrEmpty(normKey) && !normLookup.ContainsKey(normKey))
                    {
                        normLookup[normKey] = p;
                    }
                }

                foreach (var ch in channels)
                {
                    EpgProgram? current = null;

                    // 1. Match by EpgId
                    if (!string.IsNullOrWhiteSpace(ch.EpgId) && exactLookup.TryGetValue(ch.EpgId, out current))
                    {
                        dict[ch.Id] = current;
                        continue;
                    }

                    // 2. Match by exact Name
                    if (!string.IsNullOrWhiteSpace(ch.Name) && exactLookup.TryGetValue(ch.Name, out current))
                    {
                        dict[ch.Id] = current;
                        continue;
                    }

                    // 3. Match by Normalized Key
                    string chNormKey = ChannelUtils.ToNormalizedKey(ch.Name);
                    if (!string.IsNullOrEmpty(chNormKey) && normLookup.TryGetValue(chNormKey, out current))
                    {
                        dict[ch.Id] = current;
                        continue;
                    }

                    // 4. Match by Clean Name
                    string cleanName = ChannelUtils.GetCleanName(ch.Name);
                    if (!string.IsNullOrEmpty(cleanName) && exactLookup.TryGetValue(cleanName, out current))
                    {
                        dict[ch.Id] = current;
                        continue;
                    }

                // 5. Fast Fuzzy / Contains Match (V1.8.8 Optimized)
                // Remove heavy O(N*M) loop that causes hang on large databases.
                // Priority is given to exact and normalized matches above.
                if (current == null && !string.IsNullOrEmpty(chNormKey) && chNormKey.Length >= 4)
                {
                    // Only try lookup if not found in dictionary, but skip full scan for speed.
                    if (normLookup.TryGetValue(chNormKey, out current))
                    {
                        dict[ch.Id] = current;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.LogError("EpgService.GetCurrentEpgsAsync failed", ex);
        }
        return dict;
    }

        public async Task<EpgProgram?> GetNextEpgAsync(Channel channel)
        {
            if (channel == null) return null;
            try
            {
                var namesToTry = new List<string>();
                if (!string.IsNullOrWhiteSpace(channel.EpgId)) namesToTry.Add(channel.EpgId);
                namesToTry.Add(channel.Name);
                string clean = ChannelUtils.GetCleanName(channel.Name);
                if (!string.IsNullOrWhiteSpace(clean)) namesToTry.Add(clean);

                var programs = await _db.GetEpgForChannelsAsync(namesToTry.Distinct().ToList());
                var now = DateTime.Now;
                return programs.Where(p => p.StartTime > now)
                               .OrderBy(p => p.StartTime)
                               .FirstOrDefault();
            }
            catch { return null; }
        }
    }
}
