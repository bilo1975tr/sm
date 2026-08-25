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

        public async Task EnrichBatchEpgAsync(List<Channel> channels)
        {
            if (channels == null || channels.Count == 0) return;

            try
            {
                var namesToTry = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var channelsToAutoMatch = new List<Channel>();

                foreach (var ch in channels)
                {
                    // If no EPG ID and not locked, mark for auto-matching
                    if (string.IsNullOrEmpty(ch.EpgId) && !ch.IsEpgLocked)
                    {
                        channelsToAutoMatch.Add(ch);
                    }

                    namesToTry.Add(ch.Name);
                    string clean = ChannelUtils.GetCleanName(ch.Name);
                    if (!string.IsNullOrWhiteSpace(clean)) namesToTry.Add(clean);
                    foreach (var id in ch.GetEpgIdList()) namesToTry.Add(id);
                }

                // Perform Smart Auto-Matching for eligible channels
                if (channelsToAutoMatch.Count > 0)
                {
                    await PerformSmartEpgMatchAsync(channelsToAutoMatch);
                    // Re-add potentially new EPG IDs to namesToTry
                    foreach (var ch in channelsToAutoMatch)
                    {
                        if (!string.IsNullOrEmpty(ch.EpgId))
                        {
                            foreach (var id in ch.GetEpgIdList()) namesToTry.Add(id);
                        }
                    }
                }

                var programs = await _db.GetEpgForChannelsAsync(namesToTry.ToList());
                var now = DateTime.Now;

                // Group programs by channel name/id for quick matching
                var lookup = new Dictionary<string, List<EpgProgram>>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in programs)
                {
                    if (string.IsNullOrWhiteSpace(p.ChannelName)) continue;
                    var ids = p.ChannelName.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim());
                    foreach (var id in ids)
                    {
                        if (!lookup.TryGetValue(id, out var list))
                        {
                            list = new List<EpgProgram>();
                            lookup[id] = list;
                        }
                        list.Add(p);
                    }
                }

                foreach (var ch in channels)
                {
                    EpgProgram? bestMatch = null;
                    var chKeys = new List<string> { ch.Name };
                    string clean = ChannelUtils.GetCleanName(ch.Name);
                    if (!string.IsNullOrWhiteSpace(clean)) chKeys.Add(clean);
                    chKeys.AddRange(ch.GetEpgIdList());

                    foreach (var key in chKeys.Distinct())
                    {
                        if (lookup.TryGetValue(key, out var candidates))
                        {
                            bestMatch = FindBestProgram(candidates, new List<string>(), now);
                            if (bestMatch != null) break;
                        }
                    }

                    if (bestMatch != null)
                    {
                        ch.CurrentEpgTitle = bestMatch.Title;
                        ch.CurrentEpgTime = $"{bestMatch.StartTime:HH:mm} - {bestMatch.EndTime:HH:mm}";
                    }
                    else
                    {
                        ch.CurrentEpgTitle = "Yayın akışı bilgisi yok";
                        ch.CurrentEpgTime = "--:--";
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("EpgService.EnrichBatchEpgAsync failed", ex);
            }
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
                string cleanPrimary = ChannelUtils.GetCleanName(channel.PrimaryName);
                if (!string.IsNullOrWhiteSpace(cleanPrimary)) namesToTry.Add(cleanPrimary);

                var programs = await _db.GetEpgForChannelsAsync(namesToTry.Distinct().ToList());
                if (programs != null && programs.Count > 0)
                {
                    return programs.OrderBy(p => p.StartTime).ToList();
                }

                // Dynamic Smart EPG Fallback: Search EpgChannels for best matching clean name
                string searchKey = !string.IsNullOrWhiteSpace(clean) ? clean : (!string.IsNullOrWhiteSpace(cleanPrimary) ? cleanPrimary : channel.Name);
                if (!string.IsNullOrWhiteSpace(searchKey))
                {
                    var searchResults = await _db.SearchEpgChannelsAsync(searchKey, false);
                    if (searchResults != null && searchResults.Count > 0)
                    {
                        var best = searchResults.FirstOrDefault(r => {
                            bool nameMatch = string.Equals(ChannelUtils.GetCleanName(r.ChannelName), searchKey, StringComparison.OrdinalIgnoreCase) ||
                                             string.Equals(r.EpgId, searchKey, StringComparison.OrdinalIgnoreCase) ||
                                             ChannelUtils.ToNormalizedKey(r.ChannelName) == ChannelUtils.ToNormalizedKey(searchKey);

                            if (!nameMatch) return false;

                            if (!string.IsNullOrEmpty(channel.Language) && channel.Language != "und")
                            {
                                return ChannelUtils.MatchesLanguageFilter(r.ChannelName, channel.Language);
                            }
                            return true;
                        }) ?? searchResults.FirstOrDefault();

                        if (best != null && !string.IsNullOrEmpty(best.EpgId))
                        {
                            channel.EpgId = best.EpgId;
                            try { _db.SaveChannelSync(channel); } catch { }
                            LogService.LogInfo($"[SmartEPG] Dynamic auto-matched '{channel.Name}' to EPG ID '{best.EpgId}'");

                            var matchedPrograms = await _db.GetEpgForChannelsAsync(new List<string> { best.EpgId });
                            if (matchedPrograms != null && matchedPrograms.Count > 0)
                            {
                                return matchedPrograms.OrderBy(p => p.StartTime).ToList();
                            }
                        }
                    }
                }

                return list;
            }
            catch (Exception ex)
            {
                LogService.LogError("EpgService.GetChannelEpgHistoryAsync failed", ex);
                return list;
            }
        }

        private async Task PerformSmartEpgMatchAsync(List<Channel> channels)
        {
            if (channels == null || channels.Count == 0) return;

            var updatedChannels = new List<Channel>();
            foreach (var ch in channels)
            {
                if (!string.IsNullOrEmpty(ch.EpgId) || ch.IsEpgLocked) continue;

                string cleanName = ChannelUtils.GetCleanName(ch.Name);
                if (string.IsNullOrEmpty(cleanName)) continue;

                // Search for 100% clean name match in EpgChannels table
                var results = await _db.SearchEpgChannelsAsync(cleanName, true);
                var bestMatch = results.FirstOrDefault(r => {
                    bool nameMatch = string.Equals(ChannelUtils.GetCleanName(r.ChannelName), cleanName, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(r.EpgId, cleanName, StringComparison.OrdinalIgnoreCase);

                    if (!nameMatch) return false;

                    // V1.9.9: Added Language check to avoid cross-language mismatches (e.g. Discovery TR vs DE)
                    // If target channel has a defined language, ensure the EPG channel matches it.
                    if (!string.IsNullOrEmpty(ch.Language) && ch.Language != "und")
                    {
                        return ChannelUtils.MatchesLanguageFilter(r.ChannelName, ch.Language);
                    }
                    return true;
                });

                if (bestMatch != null)
                {
                    ch.EpgId = bestMatch.EpgId;
                    updatedChannels.Add(ch);
                    LogService.LogInfo($"[SmartEPG] Auto-linked '{ch.Name}' to EPG ID '{bestMatch.EpgId}'");
                }
            }

            if (updatedChannels.Count > 0)
            {
                // Silently save back to DB to "cement" the auto-link
                await _db.SaveChannelsBatchAsync(updatedChannels);
            }
        }
    }
}
