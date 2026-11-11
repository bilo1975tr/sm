using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class DiscoveredSource
    {
        public string Url { get; set; } = "";
        public string Category { get; set; } = "TV";
        public string RepoName { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public bool IsSeedSource { get; set; } = false;
    }

    public class GitHubRepoTarget
    {
        public string Owner { get; set; } = "";
        public string Repo { get; set; } = "";
        public string Branch { get; set; } = "main";
        public string DefaultCategory { get; set; } = "TV";
        public string Key => $"{Owner}/{Repo}".ToLowerInvariant();
    }

    /// <summary>
    /// GitHub depolarını özyinelemeli (recursive Git Trees API) tarayarak
    /// projedeki tüm M3U, M3U8 ve video (MP4/MKV) dosyalarını tespit eden derin tarayıcı.
    /// </summary>
    public class GitHubRepoCrawler
    {
        private static readonly HttpClient _httpClient;
        private static readonly ConcurrentDictionary<string, List<DiscoveredSource>> _repoCache = new();
        private static readonly ConcurrentDictionary<string, DateTime> _cacheTimestamps = new();
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(4);

        static GitHubRepoCrawler()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                AllowAutoRedirect = true
            };
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) StreamMesh/2.0 GitHubSyncEngine/2.0");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github.v3+json, application/json, */*");
        }

        public static bool TryParseGitHubUrl(string url, out string owner, out string repo, out string branch, out string relativePath)
        {
            owner = "";
            repo = "";
            branch = "main";
            relativePath = "";

            if (string.IsNullOrWhiteSpace(url)) return false;
            string clean = url.Trim();

            // 1. Format: raw.githubusercontent.com/{owner}/{repo}/{branch_or_refs}/{path}
            // Examples:
            // https://raw.githubusercontent.com/UzunMuhalefet/Legal-IPTV/refs/heads/main/lists/video/...
            // https://raw.githubusercontent.com/hayatiptv/iptv/master/index.m3u
            // https://raw.githubusercontent.com/don24crk/Don24crk-Repository/HEAD/GermanTV/ARD.m3u8
            var rawMatch = Regex.Match(clean, @"raw\.githubusercontent\.com/([^/]+)/([^/]+)/(?:refs/heads/)?([^/]+)/(.*)", RegexOptions.IgnoreCase);
            if (rawMatch.Success)
            {
                owner = rawMatch.Groups[1].Value.Trim();
                repo = rawMatch.Groups[2].Value.Trim();
                branch = rawMatch.Groups[3].Value.Trim();
                relativePath = rawMatch.Groups[4].Value.Trim();
                return true;
            }

            // 2. Format: github.com/{owner}/{repo}/(blob|raw)/{branch}/{path}
            var webMatch = Regex.Match(clean, @"github\.com/([^/]+)/([^/]+)/(?:blob|raw)/(?:refs/heads/)?([^/]+)/(.*)", RegexOptions.IgnoreCase);
            if (webMatch.Success)
            {
                owner = webMatch.Groups[1].Value.Trim();
                repo = webMatch.Groups[2].Value.Trim();
                branch = webMatch.Groups[3].Value.Trim();
                relativePath = webMatch.Groups[4].Value.Trim();
                return true;
            }

            // 3. Format: iptv-org.github.io/iptv/... or similar GitHub Pages
            if (clean.Contains("iptv-org.github.io/iptv", StringComparison.OrdinalIgnoreCase))
            {
                owner = "iptv-org";
                repo = "iptv";
                branch = "master";
                relativePath = clean.Substring(clean.IndexOf("iptv/", StringComparison.OrdinalIgnoreCase) + 5);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tohum URL listesindeki GitHub depolarını tespit edip,
        /// tüm depolardaki .m3u, .m3u8 ve video dosyalarını derinlemesine tarar.
        /// </summary>
        public async Task<List<DiscoveredSource>> CrawlAllAsync(
            Dictionary<string, List<string>> categorizedSeedUrls,
            Action<string, int>? progressCallback = null)
        {
            var results = new List<DiscoveredSource>();
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Önce tohum kaynakları orijinal kategorileriyle ekle
            foreach (var kvp in categorizedSeedUrls)
            {
                string cat = kvp.Key;
                foreach (var rawUrl in kvp.Value)
                {
                    if (string.IsNullOrWhiteSpace(rawUrl)) continue;
                    string u = rawUrl.Trim();
                    if (seenUrls.Add(u))
                    {
                        results.Add(new DiscoveredSource
                        {
                            Url = u,
                            Category = cat,
                            RepoName = TryParseGitHubUrl(u, out var o, out var r, out _, out var rp) ? $"{o}/{r}" : "Harici",
                            RelativePath = rp,
                            IsSeedSource = true
                        });
                    }
                }
            }

            // 2. Tohum kaynaklar içinden GitHub depolarını grupla
            var repoTargets = new Dictionary<string, GitHubRepoTarget>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in results)
            {
                if (TryParseGitHubUrl(s.Url, out var owner, out var repo, out var branch, out _))
                {
                    string key = $"{owner}/{repo}".ToLowerInvariant();
                    if (!repoTargets.ContainsKey(key))
                    {
                        repoTargets[key] = new GitHubRepoTarget
                        {
                            Owner = owner,
                            Repo = repo,
                            Branch = string.Equals(branch, "HEAD", StringComparison.OrdinalIgnoreCase) ? "master" : branch,
                            DefaultCategory = s.Category
                        };
                    }
                }
            }

            LogService.LogInfo($"[GitHubRepoCrawler] Toplam {repoTargets.Count} adet GitHub deposu tespit edildi. Derin ağaç taraması başlatılıyor...");
            progressCallback?.Invoke($"GitHub depoları taranıyor ({repoTargets.Count} depo)...", 5);

            int targetIndex = 0;
            foreach (var target in repoTargets.Values)
            {
                targetIndex++;
                int pct = 5 + (int)((double)targetIndex / Math.Max(1, repoTargets.Count) * 15.0);
                progressCallback?.Invoke($"Depo taranıyor [{targetIndex}/{repoTargets.Count}]: {target.Owner}/{target.Repo}...", pct);

                try
                {
                    var discoveredFromRepo = await ScanSingleRepoAsync(target);
                    int addedCount = 0;

                    foreach (var item in discoveredFromRepo)
                    {
                        if (seenUrls.Add(item.Url))
                        {
                            results.Add(item);
                            addedCount++;
                        }
                    }

                    LogService.LogInfo($"[GitHubRepoCrawler] {target.Owner}/{target.Repo}: {addedCount} yeni kaynak kütüphaneye dahil edildi.");
                }
                catch (Exception ex)
                {
                    LogService.LogWarning($"[GitHubRepoCrawler] {target.Owner}/{target.Repo} tarama uyarısı: {ex.Message}");
                }
            }

            LogService.LogInfo($"[GitHubRepoCrawler] Derin tarama tamamlandı: Toplam {results.Count} kaynak hazırlandı.");
            return results;
        }

        private async Task<List<DiscoveredSource>> ScanSingleRepoAsync(GitHubRepoTarget target)
        {
            string cacheKey = target.Key;
            if (_repoCache.TryGetValue(cacheKey, out var cached) &&
                _cacheTimestamps.TryGetValue(cacheKey, out var time) &&
                (DateTime.UtcNow - time) < CacheDuration)
            {
                return cached;
            }

            var list = new List<DiscoveredSource>();
            string[] candidateBranches = new[] { target.Branch, "main", "master" }.Distinct().ToArray();
            JObject? treeJson = null;
            string workingBranch = target.Branch;

            foreach (var br in candidateBranches)
            {
                string apiUrl = $"https://api.github.com/repos/{target.Owner}/{target.Repo}/git/trees/{br}?recursive=1";
                try
                {
                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(12));
                    var response = await _httpClient.GetAsync(apiUrl, cts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        string body = await response.Content.ReadAsStringAsync(cts.Token);
                        treeJson = JObject.Parse(body);
                        workingBranch = br;
                        break;
                    }
                    else if ((int)response.StatusCode == 403 || (int)response.StatusCode == 429)
                    {
                        LogService.LogWarning($"[GitHubRepoCrawler] {target.Owner}/{target.Repo} için GitHub API rate limit aşıldı, tohum adreslerle devam ediliyor.");
                        return list;
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogWarning($"[GitHubRepoCrawler] Dal '{br}' sorgulanamadı ({target.Owner}/{target.Repo}): {ex.Message}");
                }
            }

            if (treeJson == null || treeJson["tree"] is not JArray treeArray)
            {
                return list;
            }

            foreach (var node in treeArray)
            {
                string type = node["type"]?.ToString() ?? "";
                if (type != "blob") continue;

                string path = node["path"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(path)) continue;

                string lowerPath = path.ToLowerInvariant();

                // Yalnızca oynatılabilir M3U, M3U8 ve Video dosyalarını seç
                bool isM3u = lowerPath.EndsWith(".m3u") || lowerPath.EndsWith(".m3u8");
                bool isVideo = lowerPath.EndsWith(".mp4") || lowerPath.EndsWith(".mkv");

                if (!isM3u && !isVideo) continue;

                // Hariç tutulacak şablonlar (örnek / template / test / gitignore / build dosyaları)
                if (lowerPath.Contains("/test/") || lowerPath.Contains("/tests/") ||
                    lowerPath.Contains(".github/") || lowerPath.Contains("readme"))
                {
                    continue;
                }

                // Otomatik kategori tespiti
                string cat = InferCategory(path, target.DefaultCategory);

                // Ham GitHub URL'si inşa et
                string rawUrl = $"https://raw.githubusercontent.com/{target.Owner}/{target.Repo}/{workingBranch}/{path.TrimStart('/')}";

                list.Add(new DiscoveredSource
                {
                    Url = rawUrl,
                    Category = cat,
                    RepoName = $"{target.Owner}/{target.Repo}",
                    RelativePath = path,
                    IsSeedSource = false
                });
            }

            _repoCache[cacheKey] = list;
            _cacheTimestamps[cacheKey] = DateTime.UtcNow;
            return list;
        }

        public static string InferCategory(string filePath, string fallbackCategory = "TV")
        {
            if (string.IsNullOrWhiteSpace(filePath)) return fallbackCategory;
            string p = filePath.ToLowerInvariant();

            // Film & Sinema
            if (p.Contains("/film/") || p.Contains("/filmler/") || p.Contains("/movie/") || p.Contains("/movies/") ||
                p.Contains("/sinema/") || p.Contains("filmando") || p.Contains("yesilcam") || p.Contains("yeşilçam") ||
                p.Contains("kemalsunal") || p.Contains("movie") || p.Contains("evde-sinema"))
            {
                return "Film";
            }

            // Dizi & Program & Belgesel
            if (p.Contains("/dizi/") || p.Contains("/diziler/") || p.Contains("/series/") || p.Contains("/programlar/") ||
                p.Contains("/belgeseller/") || p.Contains("/arsiv-diziler/") || p.Contains("/arsiv-programlar/") ||
                p.Contains("/dizi-arsivi/") || p.Contains("kurtlar-vadisi") || p.Contains("ezel") || p.Contains("vatanim-sensin") ||
                p.Contains("muhtesem-yuzyil") || p.Contains("jet-sosyete"))
            {
                return "Dizi";
            }

            // Radyo
            if (p.Contains("/radyo/") || p.Contains("/radio/") || p.Contains("radyo-") || p.Contains("global_radio"))
            {
                return "Radyo";
            }

            // TV & Canlı Yayın
            if (p.Contains("/tv/") || p.Contains("/streams/") || p.Contains("/live/") || p.Contains("channels") || p.Contains("playlist"))
            {
                return "TV";
            }

            return string.IsNullOrWhiteSpace(fallbackCategory) ? "TV" : fallbackCategory;
        }
    }
}
