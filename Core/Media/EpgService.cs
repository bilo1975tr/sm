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

                // V1.8.9: Multi-index structure for faster lookups
                var lookupByChannel = new Dictionary<string, List<EpgProgram>>(StringComparer.OrdinalIgnoreCase);
                var lookupByNormKey = new Dictionary<string, List<EpgProgram>>(StringComparer.OrdinalIgnoreCase);

                foreach (var p in programs)
                {
                    if (string.IsNullOrWhiteSpace(p.ChannelName)) continue;

                    // Handle comma separated channel names (e.g. "RTL.de, RTL")
                    var ids = p.ChannelName.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim());
                    foreach (var epgId in ids)
                    {
                        if (!lookupByChannel.TryGetValue(epgId, out var listByCh))
                        {
                            listByCh = new List<EpgProgram>();
                            lookupByChannel[epgId] = listByCh;
                        }
                        listByCh.Add(p);
                    }

                    string normKey = ChannelUtils.ToNormalizedKey(p.ChannelName);
                    if (!string.IsNullOrEmpty(normKey))
                    {
                        if (!lookupByNormKey.TryGetValue(normKey, out var listByNorm))
                        {
                            listByNorm = new List<EpgProgram>();
                            lookupByNormKey[normKey] = listByNorm;
                        }
                        listByNorm.Add(p);
                    }
                }

                foreach (var ch in channels)
                {
                    EpgProgram? bestMatch = null;
                    var chEpgIds = ch.GetEpgIdList();
                    var chEpgUrls = (ch.EpgUrl ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(u => u.Trim()).ToList();

                    // 1. Try Matching by EpgId (Multiple IDs supported)
                    foreach (var epgId in chEpgIds)
                    {
                        if (lookupByChannel.TryGetValue(epgId, out var candidates))
                        {
                            bestMatch = FindBestProgram(candidates, chEpgUrls, now);
                            if (bestMatch != null) break;
                        }
                    }

                    // 2. Try Exact Name Match
                    if (bestMatch == null)
                    {
                        if (lookupByChannel.TryGetValue(ch.Name, out var candidates))
                        {
                            bestMatch = FindBestProgram(candidates, chEpgUrls, now);
                        }
                    }

                    // 3. Try Normalized Key Match
                    if (bestMatch == null)
                    {
                        string chNormKey = ChannelUtils.ToNormalizedKey(ch.Name);
                        if (!string.IsNullOrEmpty(chNormKey) && lookupByNormKey.TryGetValue(chNormKey, out var candidates))
                        {
                            bestMatch = FindBestProgram(candidates, chEpgUrls, now);
                        }
                    }

                    // 4. Try Clean Name Match
                    if (bestMatch == null)
                    {
                        string cleanName = ChannelUtils.GetCleanName(ch.Name);
                        if (!string.IsNullOrEmpty(cleanName) && lookupByChannel.TryGetValue(cleanName, out var candidates))
                        {
                            bestMatch = FindBestProgram(candidates, chEpgUrls, now);
                        }
                    }

                    if (bestMatch != null) dict[ch.Id] = bestMatch;
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("EpgService.GetCurrentEpgsAsync failed", ex);
            }
            return dict;
        }

        private EpgProgram? FindBestProgram(List<EpgProgram> candidates, List<string> preferredUrls, DateTime now)
        {
            var active = candidates.Where(p => now >= p.StartTime && now <= p.EndTime).ToList();
            if (active.Count == 0) return null;

            // Prioritize by source URL if specified
            if (preferredUrls.Count > 0)
            {
                var preferred = active.FirstOrDefault(p => preferredUrls.Any(u => (p.SourceUrl ?? "").Contains(u)));
                if (preferred != null) return preferred;
            }

            return active.FirstOrDefault();
        }

        public async Task<EpgProgram?> GetNextEpgAsync(Channel channel)
        {
            if (channel == null) return null;
            try
            {
                var namesToTry = new List<string>();
                namesToTry.AddRange(channel.GetEpgIdList());
                namesToTry.Add(channel.Name);
                string clean = ChannelUtils.GetCleanName(channel.Name);
                if (!string.IsNullOrWhiteSpace(clean)) namesToTry.Add(clean);

                var programs = await _db.GetEpgForChannelsAsync(namesToTry.Distinct().ToList());
                var now = DateTime.Now;
                var chEpgUrls = (channel.EpgUrl ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(u => u.Trim()).ToList();

                var future = programs.Where(p => p.StartTime > now).OrderBy(p => p.StartTime).ToList();
                if (future.Count == 0) return null;

                if (chEpgUrls.Count > 0)
                {
                    var pref = future.FirstOrDefault(p => chEpgUrls.Any(u => (p.SourceUrl ?? "").Contains(u)));
                    if (pref != null) return pref;
                }

                return future.FirstOrDefault();
            }
            catch { return null; }
        }

        public async Task<List<EpgProgram>> GetChannelEpgHistoryAsync(Channel channel)
        {
            var list = new List<EpgProgram>();
            if (channel == null) return list;

            try
            {
                var namesToTry = new List<string>();
                namesToTry.AddRange(channel.GetEpgIdList());
                if (!string.IsNullOrWhiteSpace(channel.Name)) namesToTry.Add(channel.Name);
                if (!string.IsNullOrWhiteSpace(channel.PrimaryName)) namesToTry.Add(channel.PrimaryName);
                string clean = ChannelUtils.GetCleanName(channel.Name);
                if (!string.IsNullOrWhiteSpace(clean)) namesToTry.Add(clean);

                var programs = await _db.GetEpgForChannelsAsync(namesToTry.Distinct().ToList());
                return programs.OrderBy(p => p.StartTime).ToList();
            }
            catch (Exception ex)
            {
                LogService.LogError("EpgService.GetChannelEpgHistoryAsync failed", ex);
                return list;
            }
        }
    }
}
