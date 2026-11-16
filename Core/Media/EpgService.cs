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
        private readonly DatabaseEngine _db = DatabaseEngine.Instance;

        private sealed class ActiveEpgSnapshot
        {
            public DateTime FetchedUtc { get; init; }
            public Dictionary<string, List<EpgProgram>> ByChannel { get; init; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, List<EpgProgram>> ByNormKey { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class EpgChannelIndexSnapshot
        {
            public DateTime FetchedUtc { get; init; }
            public Dictionary<string, List<(string EpgId, string Name)>> ByExactId { get; init; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, List<(string EpgId, string Name)>> ByCleanName { get; init; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, List<(string EpgId, string Name)>> ByNormKey { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private static readonly System.Threading.SemaphoreSlim _snapshotLock = new(1, 1);
        private static readonly System.Threading.SemaphoreSlim _channelIndexLock = new(1, 1);
        private static ActiveEpgSnapshot? _activeEpgSnapshot;
        private static EpgChannelIndexSnapshot? _channelIndexSnapshot;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _unmatchedChannelCache = new(StringComparer.OrdinalIgnoreCase);

        public static void InvalidateCache()
        {
            _activeEpgSnapshot = null;
            _channelIndexSnapshot = null;
            _unmatchedChannelCache.Clear();
        }

        private async Task<ActiveEpgSnapshot> GetActiveEpgSnapshotAsync()
        {
            var cached = _activeEpgSnapshot;
            if (cached != null && (DateTime.UtcNow - cached.FetchedUtc).TotalMinutes < 2.0)
                return cached;

            await _snapshotLock.WaitAsync().ConfigureAwait(false);
            try
            {
                cached = _activeEpgSnapshot;
                if (cached != null && (DateTime.UtcNow - cached.FetchedUtc).TotalMinutes < 2.0)
                    return cached;

                var programs = await _db.GetCurrentEpgProgramsAsync().ConfigureAwait(false);
                var byChannel = new Dictionary<string, List<EpgProgram>>(StringComparer.OrdinalIgnoreCase);
                var byNormKey = new Dictionary<string, List<EpgProgram>>(StringComparer.OrdinalIgnoreCase);

                foreach (var p in programs)
                {
                    if (string.IsNullOrWhiteSpace(p.ChannelName)) continue;
                    var ids = p.ChannelName.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var rawId in ids)
                    {
                        string epgId = rawId.Trim();
                        if (string.IsNullOrEmpty(epgId)) continue;
                        if (!byChannel.TryGetValue(epgId, out var listByCh))
                        {
                            listByCh = new List<EpgProgram>();
                            byChannel[epgId] = listByCh;
                        }
                        listByCh.Add(p);

                        string norm = ChannelUtils.ToNormalizedKey(epgId);
                        if (!string.IsNullOrEmpty(norm))
                        {
                            if (!byNormKey.TryGetValue(norm, out var listByNorm))
                            {
                                listByNorm = new List<EpgProgram>();
                                byNormKey[norm] = listByNorm;
                            }
                            listByNorm.Add(p);
                        }
                    }
                }

                var fresh = new ActiveEpgSnapshot
                {
                    FetchedUtc = DateTime.UtcNow,
                    ByChannel = byChannel,
                    ByNormKey = byNormKey
                };
                _activeEpgSnapshot = fresh;
                return fresh;
            }
            finally
            {
                _snapshotLock.Release();
            }
        }

        private async Task<EpgChannelIndexSnapshot> GetChannelIndexSnapshotAsync()
        {
            var cached = _channelIndexSnapshot;
            if (cached != null && (DateTime.UtcNow - cached.FetchedUtc).TotalMinutes < 5.0)
                return cached;

            await _channelIndexLock.WaitAsync().ConfigureAwait(false);
            try
            {
                cached = _channelIndexSnapshot;
                if (cached != null && (DateTime.UtcNow - cached.FetchedUtc).TotalMinutes < 5.0)
                    return cached;

                var rows = await _db.GetAllEpgChannelIndexAsync().ConfigureAwait(false);
                var byId = new Dictionary<string, List<(string EpgId, string Name)>>(StringComparer.OrdinalIgnoreCase);
                var byClean = new Dictionary<string, List<(string EpgId, string Name)>>(StringComparer.OrdinalIgnoreCase);
                var byNorm = new Dictionary<string, List<(string EpgId, string Name)>>(StringComparer.OrdinalIgnoreCase);

                void AddEntry(Dictionary<string, List<(string EpgId, string Name)>> dict, string key, (string EpgId, string Name) entry)
                {
                    if (string.IsNullOrWhiteSpace(key)) return;
                    if (!dict.TryGetValue(key, out var list))
                    {
                        list = new List<(string EpgId, string Name)>(2);
                        dict[key] = list;
                    }
                    if (!list.Any(x => string.Equals(x.EpgId, entry.EpgId, StringComparison.OrdinalIgnoreCase)))
                        list.Add(entry);
                }

                foreach (var (epgId, name) in rows)
                {
                    if (string.IsNullOrWhiteSpace(epgId)) continue;
                    var entry = (epgId.Trim(), string.IsNullOrWhiteSpace(name) ? epgId.Trim() : name.Trim());
                    AddEntry(byId, entry.Item1, entry);
                    AddEntry(byNorm, ChannelUtils.ToNormalizedKey(entry.Item1), entry);

                    foreach (var part in entry.Item2.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string trimmed = part.Trim();
                        if (string.IsNullOrEmpty(trimmed)) continue;
                        AddEntry(byId, trimmed, entry);
                        string clean = ChannelUtils.GetCleanName(trimmed);
                        if (!string.IsNullOrEmpty(clean)) AddEntry(byClean, clean, entry);
                        string norm = ChannelUtils.ToNormalizedKey(trimmed);
                        if (!string.IsNullOrEmpty(norm)) AddEntry(byNorm, norm, entry);
                    }
                }

                var fresh = new EpgChannelIndexSnapshot
                {
                    FetchedUtc = DateTime.UtcNow,
                    ByExactId = byId,
                    ByCleanName = byClean,
                    ByNormKey = byNorm
                };
                _channelIndexSnapshot = fresh;
                return fresh;
            }
            finally
            {
                _channelIndexLock.Release();
            }
        }

        public async Task<Dictionary<string, EpgProgram>> GetCurrentEpgsAsync(List<Channel> channels)
        {
            var dict = new Dictionary<string, EpgProgram>();
            if (channels == null || channels.Count == 0) return dict;

            try
            {
                var snapshot = await GetActiveEpgSnapshotAsync().ConfigureAwait(false);
                var lookupByChannel = snapshot.ByChannel;
                var lookupByNormKey = snapshot.ByNormKey;
                var now = DateTime.Now;

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
                    if (bestMatch == null && lookupByChannel.TryGetValue(ch.Name, out var exactCandidates))
                    {
                        bestMatch = FindBestProgram(exactCandidates, chEpgUrls, now);
                    }

                    // 3. Try Comprehensive Candidate Names Match (country tag stripped, etc.)
                    if (bestMatch == null)
                    {
                        var searchCandidates = ChannelUtils.GetEpgSearchCandidateNames(ch.Name, ch.EpgId);
                        foreach (var cand in searchCandidates)
                        {
                            if (lookupByChannel.TryGetValue(cand, out var candidates))
                            {
                                bestMatch = FindBestProgram(candidates, chEpgUrls, now);
                                if (bestMatch != null) break;
                            }
                            string norm = ChannelUtils.ToNormalizedKey(cand);
                            if (!string.IsNullOrEmpty(norm) && lookupByNormKey.TryGetValue(norm, out var normCandidates))
                            {
                                bestMatch = FindBestProgram(normCandidates, chEpgUrls, now);
                                if (bestMatch != null) break;
                            }
                        }
                    }

                    // 4. Try Normalized Key Match
                    if (bestMatch == null)
                    {
                        string chNormKey = ChannelUtils.ToNormalizedKey(ch.Name);
                        if (!string.IsNullOrEmpty(chNormKey) && lookupByNormKey.TryGetValue(chNormKey, out var candidates))
                        {
                            bestMatch = FindBestProgram(candidates, chEpgUrls, now);
                        }
                    }

                    // 5. Try Clean Name Match
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
                var channelsToAutoMatch = new List<Channel>();
                foreach (var ch in channels)
                {
                    if (string.IsNullOrEmpty(ch.EpgId) && !ch.IsEpgLocked &&
                        !string.IsNullOrEmpty(ch.Id) && !_unmatchedChannelCache.ContainsKey(ch.Id))
                    {
                        channelsToAutoMatch.Add(ch);
                    }
                }

                // Perform fast in-memory Smart Auto-Matching for eligible channels
                if (channelsToAutoMatch.Count > 0)
                {
                    await PerformSmartEpgMatchAsync(channelsToAutoMatch).ConfigureAwait(false);
                }

                var snapshot = await GetActiveEpgSnapshotAsync().ConfigureAwait(false);
                var lookup = snapshot.ByChannel;
                var normLookup = snapshot.ByNormKey;
                var now = DateTime.Now;

                foreach (var ch in channels)
                {
                    EpgProgram? bestMatch = null;
                    var chKeys = ChannelUtils.GetEpgSearchCandidateNames(ch.Name, ch.EpgId);
                    if (!string.IsNullOrWhiteSpace(ch.PrimaryName) && !string.Equals(ch.PrimaryName, ch.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        chKeys.AddRange(ChannelUtils.GetEpgSearchCandidateNames(ch.PrimaryName));
                    }

                    foreach (var key in chKeys.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (lookup.TryGetValue(key, out var candidates))
                        {
                            bestMatch = FindBestProgram(candidates, Array.Empty<string>(), now);
                            if (bestMatch != null) break;
                        }

                        string norm = ChannelUtils.ToNormalizedKey(key);
                        if (!string.IsNullOrEmpty(norm) && normLookup.TryGetValue(norm, out var normCandidates))
                        {
                            bestMatch = FindBestProgram(normCandidates, Array.Empty<string>(), now);
                            if (bestMatch != null) break;
                        }
                    }

                    if (bestMatch != null)
                    {
                        string newTitle = bestMatch.Title ?? "";
                        string newTime = $"{bestMatch.StartTime:HH:mm} - {bestMatch.EndTime:HH:mm}";
                        if (ch.CurrentEpgTitle != newTitle) ch.CurrentEpgTitle = newTitle;
                        if (ch.CurrentEpgTime != newTime) ch.CurrentEpgTime = newTime;
                    }
                    else
                    {
                        if (ch.CurrentEpgTitle != "Yayın akışı bilgisi yok") ch.CurrentEpgTitle = "Yayın akışı bilgisi yok";
                        if (ch.CurrentEpgTime != "--:--") ch.CurrentEpgTime = "--:--";
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("EpgService.EnrichBatchEpgAsync failed", ex);
            }
        }

        private EpgProgram? FindBestProgram(List<EpgProgram> candidates, IReadOnlyList<string> preferredUrls, DateTime now)
        {
            EpgProgram? firstActive = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                var p = candidates[i];
                if (now >= p.StartTime && now <= p.EndTime)
                {
                    firstActive ??= p;
                    if (preferredUrls.Count == 0)
                        return p;

                    string src = p.SourceUrl ?? "";
                    for (int u = 0; u < preferredUrls.Count; u++)
                    {
                        if (src.Contains(preferredUrls[u], StringComparison.OrdinalIgnoreCase))
                            return p;
                    }
                }
            }
            return firstActive;
        }

        public async Task<List<EpgProgram>> GetChannelEpgHistoryAsync(Channel channel)
        {
            var list = new List<EpgProgram>();
            if (channel == null) return list;

            try
            {
                var namesToTry = ChannelUtils.GetEpgSearchCandidateNames(channel.Name, channel.EpgId);
                if (!string.IsNullOrWhiteSpace(channel.PrimaryName))
                {
                    namesToTry.AddRange(ChannelUtils.GetEpgSearchCandidateNames(channel.PrimaryName));
                }

                var programs = await _db.GetEpgForChannelsAsync(namesToTry.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
                if (programs != null && programs.Count > 0)
                {
                    return programs.OrderBy(p => p.StartTime).ToList();
                }

                // Fast in-memory Smart EPG Fallback first, then targeted DB query only if needed
                var index = await GetChannelIndexSnapshotAsync().ConfigureAwait(false);
                var matchedId = FindBestChannelIndexMatch(index, channel, namesToTry);
                if (!string.IsNullOrEmpty(matchedId))
                {
                    channel.EpgId = matchedId;
                    try { await _db.SaveChannelAsync(channel, notifyUpdated: false).ConfigureAwait(false); } catch { }
                    LogService.LogInfo($"[SmartEPG] Dynamic auto-matched '{channel.Name}' to EPG ID '{matchedId}'");

                    var matchedPrograms = await _db.GetEpgForChannelsAsync(new List<string> { matchedId });
                    if (matchedPrograms != null && matchedPrograms.Count > 0)
                    {
                        return matchedPrograms.OrderBy(p => p.StartTime).ToList();
                    }
                }

                foreach (var searchKey in namesToTry)
                {
                    var searchResults = await _db.SearchEpgChannelsAsync(searchKey, false, includeCurrentProgram: false);
                    if (searchResults != null && searchResults.Count > 0)
                    {
                        var best = searchResults.FirstOrDefault(r =>
                        {
                            bool nameMatch = string.Equals(ChannelUtils.GetCleanName(r.ChannelName), searchKey, StringComparison.OrdinalIgnoreCase) ||
                                             string.Equals(r.EpgId, searchKey, StringComparison.OrdinalIgnoreCase) ||
                                             namesToTry.Any(k => string.Equals(r.EpgId, k, StringComparison.OrdinalIgnoreCase) || string.Equals(ChannelUtils.GetCleanName(r.ChannelName), k, StringComparison.OrdinalIgnoreCase)) ||
                                             ChannelUtils.ToNormalizedKey(r.ChannelName) == ChannelUtils.ToNormalizedKey(searchKey);

                            if (!nameMatch) return false;
                            if (!string.IsNullOrEmpty(channel.Language) && channel.Language != "und")
                                return ChannelUtils.MatchesLanguageFilter(r.ChannelName, channel.Language);
                            return true;
                        }) ?? searchResults.FirstOrDefault();

                        if (best != null && !string.IsNullOrEmpty(best.EpgId))
                        {
                            channel.EpgId = best.EpgId;
                            try { await _db.SaveChannelAsync(channel, notifyUpdated: false).ConfigureAwait(false); } catch { }
                            LogService.LogInfo($"[SmartEPG] Dynamic auto-matched '{channel.Name}' to EPG ID '{best.EpgId}'");

                            var matchedPrograms = await _db.GetEpgForChannelsAsync(new List<string> { best.EpgId });
                            if (matchedPrograms != null && matchedPrograms.Count > 0)
                                return matchedPrograms.OrderBy(p => p.StartTime).ToList();
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

        private static string? FindBestChannelIndexMatch(EpgChannelIndexSnapshot index, Channel ch, List<string> candidateKeys)
        {
            bool hasLangFilter = !string.IsNullOrEmpty(ch.Language) && ch.Language != "und";

            string? PickMatching(List<(string EpgId, string Name)> candidates)
            {
                foreach (var entry in candidates)
                {
                    if (!hasLangFilter || ChannelUtils.MatchesLanguageFilter(entry.Name, ch.Language))
                        return entry.EpgId;
                }
                return null;
            }

            foreach (var key in candidateKeys)
            {
                if (index.ByExactId.TryGetValue(key, out var exactList))
                {
                    var picked = PickMatching(exactList);
                    if (picked != null) return picked;
                }
                if (index.ByCleanName.TryGetValue(key, out var cleanList))
                {
                    var picked = PickMatching(cleanList);
                    if (picked != null) return picked;
                }
            }

            foreach (var key in candidateKeys)
            {
                string norm = ChannelUtils.ToNormalizedKey(key);
                if (!string.IsNullOrEmpty(norm) && index.ByNormKey.TryGetValue(norm, out var normList))
                {
                    var picked = PickMatching(normList);
                    if (picked != null) return picked;
                }
            }

            return null;
        }

        private async Task PerformSmartEpgMatchAsync(List<Channel> channels)
        {
            if (channels == null || channels.Count == 0) return;

            var index = await GetChannelIndexSnapshotAsync().ConfigureAwait(false);
            if (index.ByExactId.Count == 0 && index.ByCleanName.Count == 0 && index.ByNormKey.Count == 0)
                return;

            var updatedChannels = new List<Channel>();
            foreach (var ch in channels)
            {
                if (!string.IsNullOrEmpty(ch.EpgId) || ch.IsEpgLocked) continue;

                var candidateKeys = ChannelUtils.GetEpgSearchCandidateNames(ch.Name, ch.EpgId);
                if (!string.IsNullOrWhiteSpace(ch.PrimaryName) && !string.Equals(ch.PrimaryName, ch.Name, StringComparison.OrdinalIgnoreCase))
                    candidateKeys.AddRange(ChannelUtils.GetEpgSearchCandidateNames(ch.PrimaryName));

                var matchedId = FindBestChannelIndexMatch(index, ch, candidateKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
                if (!string.IsNullOrEmpty(matchedId))
                {
                    ch.EpgId = matchedId;
                    updatedChannels.Add(ch);
                    LogService.LogInfo($"[SmartEPG] Auto-linked '{ch.Name}' to EPG ID '{matchedId}'");
                }
                else if (!string.IsNullOrEmpty(ch.Id))
                {
                    _unmatchedChannelCache.TryAdd(ch.Id, 0);
                }
            }

            if (updatedChannels.Count > 0)
            {
                // Silently save back to DB to "cement" the auto-link without triggering UI refresh loop
                await _db.SaveChannelsBatchAsync(updatedChannels, clearFirst: false, notifyUpdated: false).ConfigureAwait(false);
            }
        }
    }
}
