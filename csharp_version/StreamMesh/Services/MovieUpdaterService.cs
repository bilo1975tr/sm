using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StreamMesh.Models;
using StreamMesh.Services.P2P;

namespace StreamMesh.Services
{
    public class MovieUpdaterService
    {
        private static readonly string[] MovieAndSeriesSources = new[]
        {
            "https://tinyurl.com/power-cinema", // Power Cinema
            "https://raw.githubusercontent.com/Zerk1903/zerkfilm/refs/heads/main/Filmler.m3u" // Zerk Film
        };

        private static readonly string[] BackupLiveSources = new[]
        {
            "https://raw.githubusercontent.com/smartwebos/cdn/refs/heads/main/viziTV.m3u",
            "https://streams.uzunmuhalefet.com/lists/tr.m3u",
            "https://link.testworkery0.workers.dev/patron.m3u",
            "https://raw.githubusercontent.com/hayatiptv/iptv/master/index.m3u",
            "https://raw.githubusercontent.com/iptv-org/iptv/refs/heads/master/streams/tr.m3u",
            "https://raw.githubusercontent.com/yasarfalkan/m3u-dosyam/refs/heads/main/YMBK.m3u8",
            "https://www.dropbox.com/scl/fi/p58t5o980tah2hz3234a5/SmartGO.m3u?rlkey=w44w0ycaa83uyn21uph77pp6v&st=mj0n6byr&raw=1",
            "https://raw.githubusercontent.com/hydrokin/M3U/e4e9ba44d54d360ff3de6388220a4dc1019bf34e/tvando.m3u",
            "https://iptv-org.github.io/iptv/countries/tr.m3u"
        };

        private readonly DatabaseService _databaseService = new DatabaseService();
        private readonly M3uService _m3uService = new M3uService();
        private readonly StreamCheckerService _streamChecker = new StreamCheckerService();

        public bool IsRunning { get; private set; }

        public static MovieUpdaterService Instance { get; } = new MovieUpdaterService();

        private MovieUpdaterService() { }

        public async Task RunWeeklyUpdateIfNeededAsync()
        {
            var profile = UserService.GetProfile();
            if (profile == null || !profile.WeeklyMovieAndChannelUpdateEnabled)
                return;

            // Check if 7 days have passed
            if ((DateTime.Now - profile.LastMovieAndChannelUpdateTime).TotalDays >= 7)
            {
                LogService.Log("Weekly scheduled update triggered automatically.");
                try
                {
                    await UpdateResourcesAsync(null);
                }
                catch (Exception ex)
                {
                    LogService.LogError("Weekly auto update failed", ex);
                }
            }
        }

        public async Task<int> UpdateResourcesAsync(Action<string> progressCallback)
        {
            if (IsRunning)
                throw new InvalidOperationException("Güncelleme işlemi zaten devam ediyor.");

            IsRunning = true;
            int totalAddedOrMerged = 0;

            try
            {
                progressCallback?.Invoke("Kanal listesi veritabanından yükleniyor...");
                var existingChannels = _databaseService.GetAllChannels();

                // Create dictionaries for fast lookup
                var normalizedExisting = existingChannels
                    .GroupBy(c => NormalizeName(c.Name))
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                // 1. Fetch film sources
                var rawAdaylar = new List<Channel>();

                foreach (var srcUrl in MovieAndSeriesSources)
                {
                    try
                    {
                        progressCallback?.Invoke($"{srcUrl} indirilip ayrıştırılıyor...");
                        var parsed = await _m3uService.ParseM3uAsync(srcUrl, "Film");
                        foreach (var ch in parsed)
                        {
                            ch.Category = "Film"; // default for movies
                            ch.PlaylistUrl = srcUrl;
                            rawAdaylar.Add(ch);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError($"Film kaynağı indirilemedi: {srcUrl}", ex);
                    }
                }

                // 2. Fetch live & backup sources
                foreach (var srcUrl in BackupLiveSources)
                {
                    try
                    {
                        progressCallback?.Invoke($"{srcUrl} indirilip ayrıştırılıyor...");
                        var parsed = await _m3uService.ParseM3uAsync(srcUrl, "TV");
                        foreach (var ch in parsed)
                        {
                            ch.PlaylistUrl = srcUrl;
                            rawAdaylar.Add(ch);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError($"Yedek canlı kaynağı indirilemedi: {srcUrl}", ex);
                    }
                }

                // Deduplicate candidates to load faster
                var uniqueCandidateChannels = new List<Channel>();
                var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var ch in rawAdaylar)
                {
                    if (string.IsNullOrWhiteSpace(ch.Url)) continue;
                    if (!seenUrls.Contains(ch.Url.Trim()))
                    {
                        seenUrls.Add(ch.Url.Trim());
                        uniqueCandidateChannels.Add(ch);
                    }
                }

                progressCallback?.Invoke($"Bulunan {uniqueCandidateChannels.Count} aday kanal doğrulanmaya başlıyor...");

                int verifiedCounter = 0;
                int totalCount = uniqueCandidateChannels.Count;

                // Process canditates sequentially or via small parallel chunks to keep UI highly responsive
                // Let's use parallel degrees of 10 for speed
                int batchSize = 10;
                for (int i = 0; i < totalCount; i += batchSize)
                {
                    var batch = uniqueCandidateChannels.Skip(i).Take(batchSize).ToList();
                    var checkTasks = batch.Select(async candidate =>
                    {
                        bool isTvando = candidate.PlaylistUrl != null && candidate.PlaylistUrl.Contains("tvando.m3u", StringComparison.OrdinalIgnoreCase);
                        
                        // Tvando bypass - direct verify
                        bool isAlive = isTvando;
                        if (!isAlive)
                        {
                            // Ordinary test
                            isAlive = await _streamChecker.CheckStreamAsync(candidate.Url);
                        }

                        return new { Candidate = candidate, IsAlive = isAlive };
                    }).ToList();

                    var results = await Task.WhenAll(checkTasks);

                    foreach (var res in results)
                    {
                        if (res.IsAlive)
                        {
                            var candidate = res.Candidate;
                            string normName = NormalizeName(candidate.Name);

                            if (normalizedExisting.TryGetValue(normName, out var existing))
                            {
                                // Match and merge URLs
                                var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                if (!string.IsNullOrEmpty(existing.Url))
                                {
                                    foreach (var u in existing.Url.Split(','))
                                    {
                                        if (!string.IsNullOrWhiteSpace(u.Trim())) urls.Add(u.Trim());
                                    }
                                }

                                if (!urls.Contains(candidate.Url.Trim()))
                                {
                                    urls.Add(candidate.Url.Trim());
                                    existing.Url = string.Join(",", urls);
                                    existing.IsVerified = true;
                                    _databaseService.SaveChannel(existing);
                                    totalAddedOrMerged++;
                                }
                            }
                            else
                            {
                                // Create new channel
                                candidate.Id = Guid.NewGuid().ToString("N");
                                candidate.IsVerified = true;
                                _databaseService.SaveChannel(candidate);
                                normalizedExisting[normName] = candidate;
                                totalAddedOrMerged++;
                            }
                        }
                    }

                    verifiedCounter += batch.Count;
                    progressCallback?.Invoke($"Tarama ve Birleştirme: %{(verifiedCounter * 100 / totalCount)} ({verifiedCounter}/{totalCount} işlendi, {totalAddedOrMerged} aktif edildi)");
                }

                // Save update metadata in profile
                var profile = UserService.GetProfile();
                if (profile != null)
                {
                    profile.LastMovieAndChannelUpdateTime = DateTime.Now;
                    UserService.SaveProfile(profile);
                }

                progressCallback?.Invoke($"Tamamlandı! {totalAddedOrMerged} yeni/yedek yayın veritabanı ile senkronize edildi.");
            }
            finally
            {
                IsRunning = false;
            }

            return totalAddedOrMerged;
        }

        private string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            string clean = name.Trim().ToLowerInvariant();
            
            // Remove common quality tags
            clean = Regex.Replace(clean, @"\b(hd|fhd|sd|uhd|4k|raw|plus|hevc)\b", "", RegexOptions.IgnoreCase);
            // Remove non-alphanumeric chars
            clean = Regex.Replace(clean, @"[^a-z0-9]", "");
            return clean;
        }
    }
}
