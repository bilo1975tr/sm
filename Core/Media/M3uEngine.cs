using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using StreamMesh.Models;
using StreamMesh.Core.Network;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class M3uEngine
    {
        public async Task<List<Channel>> ParseM3uAsync(string urlOrPath, string categoryHint = "TV", bool forceCategory = false, Action<string, double>? progressCallback = null)
        {
            var channels = new List<Channel>();
            string content = "";

            try
            {
                if (urlOrPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    progressCallback?.Invoke($"Bağlanılıyor: {MediaHttpClient.GetShortUrl(urlOrPath)}", 0);

                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(90));
                    using var ms = new MemoryStream();
                    await MediaHttpClient.DownloadToStreamWithProgressAsync(urlOrPath, ms, progressCallback, "İndiriliyor", 25, cts.Token);
                    byte[] rawBytes = ms.ToArray();
                    content = DecodeM3uContent(rawBytes);
                }
                else if (File.Exists(urlOrPath))
                {
                    progressCallback?.Invoke("Yerel dosya okunuyor...", 50);
                    byte[] rawBytes = await File.ReadAllBytesAsync(urlOrPath);
                    content = DecodeM3uContent(rawBytes);
                }

                if (string.IsNullOrEmpty(content)) return channels;

                // 1. Check if the content is HTML, JS or script response instead of M3U
                string trimmedStart = content.TrimStart();
                if (trimmedStart.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
                    trimmedStart.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
                    trimmedStart.StartsWith("<script", StringComparison.OrdinalIgnoreCase) ||
                    trimmedStart.StartsWith("(function", StringComparison.OrdinalIgnoreCase) ||
                    trimmedStart.StartsWith("var ", StringComparison.OrdinalIgnoreCase) ||
                    trimmedStart.StartsWith("function", StringComparison.OrdinalIgnoreCase))
                {
                    progressCallback?.Invoke("Hata: Geçersiz M3U içeriği (Web sayfası veya script algılandı)", 0);
                    return channels;
                }

                progressCallback?.Invoke("Ayrıştırılıyor...", 80);

                var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                Channel? current = null;
                var discoveredAccounts = new Dictionary<string, IptvAccountInfo>(StringComparer.OrdinalIgnoreCase);
                int currentLineNumber = 0;
                int extinfLineNumber = 0;
                string rawExtinf = "";
                var extraDirectives = new List<string>();
                var db = new StreamMesh.Core.Database.DatabaseEngine();

                foreach (var rawLine in lines)
                {
                    currentLineNumber++;
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    if (line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
                    {
                        // Check for url-tvg, x-tvg-url or tvg-url header attributes (quoted or unquoted, comma/semicolon/space separated)
                        var matches = System.Text.RegularExpressions.Regex.Matches(line, @"(?:url-tvg|x-tvg-url|tvg-url)=(?:[""']([^""']+)[""']|([^\s,""']+))", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        foreach (System.Text.RegularExpressions.Match match in matches)
                        {
                            string epgUrlsRaw = !string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : match.Groups[2].Value;
                            var epgUrls = epgUrlsRaw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var epgUrl in epgUrls)
                            {
                                string trimmedEpg = epgUrl.Trim();
                                if (!string.IsNullOrEmpty(trimmedEpg) && trimmedEpg.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                {
                                    db.AddEpgSource(trimmedEpg);
                                }
                            }
                        }
                        continue;
                    }

                    if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
                    {
                        extinfLineNumber = currentLineNumber;
                        rawExtinf = rawLine;
                        extraDirectives.Clear();

                        current = new Channel { Category = categoryHint, PlaylistUrl = urlOrPath, M3uLineNumber = extinfLineNumber };
                        if (forceCategory) current.Notes = "FORCE_CAT";

                        // Logo
                        var logoMatch = System.Text.RegularExpressions.Regex.Match(line, @"tvg-logo=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (logoMatch.Success) current.LogoUrl = logoMatch.Groups[1].Value;

                        // EPG ID
                        var epgMatch = System.Text.RegularExpressions.Regex.Match(line, @"tvg-id=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (epgMatch.Success) current.EpgId = epgMatch.Groups[1].Value;

                        // tvg-name
                        string explicitTvgName = "";
                        var tvgNameMatch = System.Text.RegularExpressions.Regex.Match(line, @"tvg-name=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (tvgNameMatch.Success)
                        {
                            string tvgName = tvgNameMatch.Groups[1].Value.Trim();
                            if (!string.IsNullOrEmpty(tvgName))
                            {
                                explicitTvgName = tvgName;
                                current.AddAlternativeName(tvgName);
                            }
                        }

                        // tvg-language
                        var langMatch = System.Text.RegularExpressions.Regex.Match(line, @"(?:tvg-language|tvg-lang|language)=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (langMatch.Success) current.Language = langMatch.Groups[1].Value.Trim();

                        // Group Title
                        var groupMatch = System.Text.RegularExpressions.Regex.Match(line, @"group-title=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (groupMatch.Success)
                        {
                            current.GroupTitle = groupMatch.Groups[1].Value;
                            if (!forceCategory) current.Category = groupMatch.Groups[1].Value;
                        }

                        // HTTP Headers in EXTINF attributes
                        var uaMatch = System.Text.RegularExpressions.Regex.Match(line, @"(?:http-user-agent|user-agent)=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (uaMatch.Success) current.HttpUserAgent = uaMatch.Groups[1].Value.Trim();

                        var refMatch = System.Text.RegularExpressions.Regex.Match(line, @"(?:http-referrer|http-referer|referer|referrer)=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (refMatch.Success) current.HttpReferer = refMatch.Groups[1].Value.Trim();

                        var cookieMatch = System.Text.RegularExpressions.Regex.Match(line, @"(?:http-cookie|cookie)=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (cookieMatch.Success) current.HttpCookie = cookieMatch.Groups[1].Value.Trim();

                        var originMatch = System.Text.RegularExpressions.Regex.Match(line, @"(?:http-origin|origin)=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (originMatch.Success) current.HttpOrigin = originMatch.Groups[1].Value.Trim();

                        int nameIdx = line.LastIndexOf(',');
                        if (nameIdx != -1)
                        {
                            string rawExtinfName = line.Substring(nameIdx + 1).Trim();
                            // Temizlik: Kanal adının içine sızmış tırnak veya tvg- niteliklerini soy
                            if (rawExtinfName.Contains("tvg-") || rawExtinfName.Contains("group-title="))
                            {
                                rawExtinfName = System.Text.RegularExpressions.Regex.Replace(rawExtinfName, @"[a-zA-Z0-9_\-]+=[""'][^""']*[""']", "").Trim();
                            }
                            rawExtinfName = rawExtinfName.Trim(' ', '"', '\'', ',');
                            current.Name = rawExtinfName;
                            if (string.IsNullOrEmpty(current.Name)) current.Name = "İsimsiz Kanal";
                        }

                        // Eğer satır sonundaki isim grup adı ile aynıysa (örn: TR-SINEMA) veya generic ise,
                        // ancak tvg-name özgül bir kanal adı içeriyorsa (örn: TR: beIN Movies Action), tvg-name'i birincil isim yap
                        if (!string.IsNullOrEmpty(explicitTvgName))
                        {
                            if (string.IsNullOrWhiteSpace(current.Name) ||
                                current.Name.Equals(current.GroupTitle, StringComparison.OrdinalIgnoreCase) ||
                                current.Name.Equals("İsimsiz Kanal", StringComparison.OrdinalIgnoreCase) ||
                                (explicitTvgName.Length > current.Name.Length && explicitTvgName.Contains(current.Name, StringComparison.OrdinalIgnoreCase)))
                            {
                                if (!string.IsNullOrWhiteSpace(current.Name)) current.AddAlternativeName(current.Name);
                                current.Name = explicitTvgName;
                            }
                        }
                    }
                    else if (line.StartsWith("#EXTVLCOPT:", StringComparison.OrdinalIgnoreCase))
                    {
                        extraDirectives.Add(rawLine);
                        if (current == null) current = new Channel { Category = categoryHint, PlaylistUrl = urlOrPath, M3uLineNumber = currentLineNumber };
                        string opt = line.Substring(11).Trim();
                        int eq = opt.IndexOf('=');
                        if (eq > 0)
                        {
                            string optKey = opt.Substring(0, eq).Trim().ToLowerInvariant();
                            string optVal = opt.Substring(eq + 1).Trim('"', '\'', ' ');

                            if (optKey == "http-user-agent" || optKey == "user-agent")
                                current.HttpUserAgent = optVal;
                            else if (optKey == "http-referrer" || optKey == "http-referer" || optKey == "referer" || optKey == "referrer")
                                current.HttpReferer = optVal;
                            else if (optKey == "http-cookie" || optKey == "cookie")
                                current.HttpCookie = optVal;
                            else if (optKey == "http-origin" || optKey == "origin")
                                current.HttpOrigin = optVal;
                            else
                                current.CustomHeaders[optKey] = optVal;
                        }
                    }
                    else if (line.StartsWith("#EXTHTTP:", StringComparison.OrdinalIgnoreCase))
                    {
                        extraDirectives.Add(rawLine);
                        if (current == null) current = new Channel { Category = categoryHint, PlaylistUrl = urlOrPath, M3uLineNumber = currentLineNumber };
                        string jsonPart = line.Substring(9).Trim();
                        try
                        {
                            var headers = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(jsonPart);
                            if (headers != null)
                            {
                                foreach (var kv in headers)
                                {
                                    if (kv.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase) || kv.Key.Equals("http-user-agent", StringComparison.OrdinalIgnoreCase))
                                        current.HttpUserAgent = kv.Value;
                                    else if (kv.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase) || kv.Key.Equals("http-referrer", StringComparison.OrdinalIgnoreCase) || kv.Key.Equals("http-referer", StringComparison.OrdinalIgnoreCase))
                                        current.HttpReferer = kv.Value;
                                    else if (kv.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) || kv.Key.Equals("http-cookie", StringComparison.OrdinalIgnoreCase))
                                        current.HttpCookie = kv.Value;
                                    else if (kv.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase) || kv.Key.Equals("http-origin", StringComparison.OrdinalIgnoreCase))
                                        current.HttpOrigin = kv.Value;
                                    else
                                        current.CustomHeaders[kv.Key] = kv.Value;
                                }
                            }
                        }
                        catch { }
                    }
                    else if (!line.StartsWith("#"))
                    {
                        string rawUrl = line;
                        if (!IsValidStreamUrl(rawUrl))
                        {
                            current = null;
                            rawExtinf = "";
                            extraDirectives.Clear();
                            continue;
                        }

                        if (current == null)
                        {
                            // Single line format without #EXTINF
                            string baseName = Path.GetFileNameWithoutExtension(rawUrl);
                            if (string.IsNullOrWhiteSpace(baseName)) baseName = "Yayın";
                            current = new Channel { Category = categoryHint, PlaylistUrl = urlOrPath, Name = baseName, M3uLineNumber = currentLineNumber };
                        }

                        // Assemble RawM3uBlock
                        var rawSb = new System.Text.StringBuilder();
                        if (!string.IsNullOrEmpty(rawExtinf)) rawSb.AppendLine(rawExtinf);
                        foreach (var ed in extraDirectives) rawSb.AppendLine(ed);
                        rawSb.Append(rawLine);
                        current.RawM3uBlock = rawSb.ToString();
                        if (current.M3uLineNumber <= 0)
                        {
                            current.M3uLineNumber = extinfLineNumber > 0 ? extinfLineNumber : currentLineNumber;
                        }

                        // Check for pipe syntax in URL (e.g. url|User-Agent=...&Referer=...)
                        if (rawUrl.Contains('|'))
                        {
                            int pipeIdx = rawUrl.IndexOf('|');
                            string headerPart = rawUrl.Substring(pipeIdx + 1).Trim();
                            var parts = headerPart.Split(new[] { '&', '|' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var part in parts)
                            {
                                int eqIdx = part.IndexOf('=');
                                if (eqIdx > 0)
                                {
                                    string key = part.Substring(0, eqIdx).Trim();
                                    string val = part.Substring(eqIdx + 1).Trim('"', '\'', ' ');

                                    if (key.Equals("http-user-agent", StringComparison.OrdinalIgnoreCase) || key.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (string.IsNullOrWhiteSpace(current.HttpUserAgent)) current.HttpUserAgent = val;
                                    }
                                    else if (key.Equals("http-referrer", StringComparison.OrdinalIgnoreCase) || key.Equals("http-referer", StringComparison.OrdinalIgnoreCase) || key.Equals("referer", StringComparison.OrdinalIgnoreCase) || key.Equals("referrer", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (string.IsNullOrWhiteSpace(current.HttpReferer)) current.HttpReferer = val;
                                    }
                                    else if (key.Equals("http-cookie", StringComparison.OrdinalIgnoreCase) || key.Equals("cookie", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (string.IsNullOrWhiteSpace(current.HttpCookie)) current.HttpCookie = val;
                                    }
                                    else if (key.Equals("http-origin", StringComparison.OrdinalIgnoreCase) || key.Equals("origin", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (string.IsNullOrWhiteSpace(current.HttpOrigin)) current.HttpOrigin = val;
                                    }
                                    else
                                    {
                                        if (!current.CustomHeaders.ContainsKey(key)) current.CustomHeaders[key] = val;
                                    }
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(rawUrl))
                        {
                            current.Url = rawUrl;

                            // URL Temizliği ve Kimliklendirme: URL'deki port :80 veya geçici session/token parametrelerini soyup tekilleştir
                            string cleanUrlForHash = CleanStreamUrlForIdentity(rawUrl);

                            byte[] hash = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(cleanUrlForHash));
                            current.Id = Convert.ToHexString(hash).ToLowerInvariant();

                            // Dizi / Program / Arşiv isim ve grup tamamlama
                            EnrichSeriesInfo(current, urlOrPath, categoryHint);

                            SmartNormalizationEngine.Instance.NormalizeChannel(current);
                            channels.Add(current);

                            // Otomatik IPTV / Xtream hesap keşfi
                            if (!string.IsNullOrWhiteSpace(rawUrl))
                            {
                                var acc = IptvAccountHelper.ParseAccountFromUrl(rawUrl);
                                if (acc != null && !string.IsNullOrWhiteSpace(acc.Username) && !discoveredAccounts.ContainsKey(acc.SignatureKey))
                                {
                                    discoveredAccounts[acc.SignatureKey] = acc;
                                }
                            }
                        }
                        current = null;
                        rawExtinf = "";
                        extraDirectives.Clear();
                    }
                }

                progressCallback?.Invoke($"Ayrıştırma tamamlandı: {channels.Count} kanal bulundu.", 100);

                // Keşfedilen IPTV hesaplarının orijinal tam M3U ve VOD film/dizi indekslerini arka planda çek (Bulut senkronizasyonu çalışmıyorsa)
                if (discoveredAccounts.Count > 0 && !urlOrPath.Contains("get.php?") && !GitHubSyncEngine.IsRunning)
                {
                    var targets = discoveredAccounts.Values
                        .GroupBy(a => IptvAccountHelper.ExtractHostAndPort(a.HostWithPort).ToLowerInvariant())
                        .Select(g => g.First())
                        .ToList();

                    LogService.LogInfo($"[M3uEngine] {targets.Count} benzersiz sağlayıcı host adresi keşfedildi. Sağlayıcılar doğrulanıp tam paketleri sorgulanıyor...");
                    _ = Task.Run(async () =>
                    {
                        foreach (var acc in targets)
                        {
                            try
                            {
                                bool isRealHost = await IptvAccountHelper.FastCheckHostIsIptvAsync(acc).ConfigureAwait(false);
                                if (!isRealHost) continue;

                                bool isVerified = await IptvAccountHelper.FastVerifyAccountAsync(acc).ConfigureAwait(false);
                                if (isVerified)
                                {
                                    await IptvAccountHelper.FetchAndImportFullAccountAsync(acc).ConfigureAwait(false);
                                }
                            }
                            catch (Exception ex)
                            {
                                LogService.LogWarning($"[AutoIptv] Otomatik paket çekme atlandı ({acc.DisplaySummary}): {ex.Message}");
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                progressCallback?.Invoke($"Ayrıştırma hatası: {ex.Message}", 0);
            }

            return ChannelAggregator.Instance.AggregateChannels(channels);
        }

        private string DecodeM3uContent(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;

            try
            {
                // Try UTF8 first
                string utf8Str = System.Text.Encoding.UTF8.GetString(bytes);

                // If no replacement characters, check if valid UTF8
                if (!utf8Str.Contains('\uFFFD'))
                {
                    return utf8Str;
                }

                // If UTF8 produced replacement characters (broken encoding), fallback to Windows-1254 (Turkish) / ISO-8859-9
                try
                {
                    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                    var win1254 = System.Text.Encoding.GetEncoding("windows-1254");
                    return win1254.GetString(bytes);
                }
                catch
                {
                    return utf8Str;
                }
            }
            catch
            {
                return System.Text.Encoding.Default.GetString(bytes);
            }
        }

        private bool IsValidStreamUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            url = url.Trim();

            // Must be at least 5 characters
            if (url.Length < 5) return false;

            // Reject common code/html snippets
            if (url.Contains("<") || url.Contains(">") || url.Contains(";") || url.Contains("{") || url.Contains("}") || url.Contains("var ") || url.Contains("function"))
                return false;

            // Check if valid URL or supported scheme
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("mms://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("udp://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                (url.Length == 40 && System.Text.RegularExpressions.Regex.IsMatch(url, "^[a-fA-F0-9]{40}$"))) // Raw AceStream hash
            {
                return true;
            }

            // Local file paths
            if (File.Exists(url)) return true;

            return false;
        }

        public static string CleanStreamUrlForIdentity(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            try
            {
                // AceStream hash
                if (url.Length == 40 && System.Text.RegularExpressions.Regex.IsMatch(url, "^[a-fA-F0-9]{40}$"))
                    return url.ToLowerInvariant();

                if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = new Uri(url);
                    // Host + Path (Port :80 ve :443 standart olduğundan yoksay, query param'ları token vb. temizle)
                    string host = uri.Host.ToLowerInvariant();
                    string path = uri.AbsolutePath.TrimEnd('/');
                    return $"{host}{path}";
                }
            }
            catch { }
            return url.Trim().ToLowerInvariant();
        }

        private string GetShortUrl(string url) => MediaHttpClient.GetShortUrl(url);

        private void EnrichSeriesInfo(Channel channel, string sourceUrlOrPath, string categoryHint)
        {
            if (channel == null) return;

            string cat = (channel.Category ?? categoryHint ?? "").ToLowerInvariant();
            string origGroup = (channel.GroupTitle ?? "").Trim();
            string currentName = (channel.Name ?? "").Trim();

            bool isExplicitEpisode = System.Text.RegularExpressions.Regex.IsMatch(currentName, @"^(?i)(?:sezon\s*\d+\s*)?(?:bölüm|bolum|\bep\b|\be\b|\bpart\b)?\s*\d+\.?$") ||
                                     System.Text.RegularExpressions.Regex.IsMatch(currentName, @"^(?i)s\d+\s*e\d+");

            bool isSeriesCategory = cat.Contains("dizi") || cat.Contains("series") ||
                                    (!string.IsNullOrEmpty(origGroup) && (origGroup.IndexOf("dizi", StringComparison.OrdinalIgnoreCase) >= 0 || origGroup.IndexOf("series", StringComparison.OrdinalIgnoreCase) >= 0));

            // Sadece açıkça dizi olan veya bölüm formatına uyan içeriklerde dizi zenginleştirmesi yap
            if (!isSeriesCategory && !isExplicitEpisode)
            {
                return;
            }

            // TV kanalı, Film veya Radyo ise ezme
            if (!isExplicitEpisode && (cat == "tv" || cat == "film" || cat == "movie" || cat == "radyo" || cat == "radio"))
            {
                return;
            }

            string[] genericGroups = { "dizi", "genel", "tv", "series", "1 bölüm / parça", "1 bolum / parca", "filmler", "movies" };
            string seriesName = "";

            if (!string.IsNullOrEmpty(origGroup) && !genericGroups.Contains(origGroup.ToLowerInvariant()))
            {
                seriesName = origGroup;
            }
            else
            {
                string seriesNameFromSource = ExtractSeriesNameFromSource(sourceUrlOrPath);
                if (!string.IsNullOrEmpty(seriesNameFromSource))
                {
                    seriesName = seriesNameFromSource;
                }
            }

            if (!string.IsNullOrEmpty(seriesName))
            {
                channel.Category = "Dizi";

                if (isExplicitEpisode && !genericGroups.Contains(seriesName.ToLowerInvariant()))
                {
                    channel.Name = $"{seriesName} - {currentName}";
                }

                if (string.IsNullOrWhiteSpace(channel.GroupTitle) || genericGroups.Contains(channel.GroupTitle.ToLowerInvariant()))
                {
                    channel.GroupTitle = seriesName;
                }
            }
        }

        private string ExtractSeriesNameFromSource(string sourceUrlOrPath)
        {
            if (string.IsNullOrWhiteSpace(sourceUrlOrPath)) return "";
            try
            {
                string path = sourceUrlOrPath.Split('?')[0].Trim();
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(fileName)) return "";

                // Standart karma veya tekil olmayan m3u dosya adlarını atla
                string lower = fileName.ToLowerInvariant();
                if (lower == "index" || lower == "playlist" || lower == "all" || lower == "tv" || lower == "channels" ||
                    lower == "tr" || lower == "de" || lower == "streams" || lower == "live")
                {
                    return "";
                }

                // Tire, alt çizgi ve nokta karakterlerini boşluğa dönüştür
                string clean = fileName.Replace("-", " ").Replace("_", " ").Replace(".", " ");
                clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+", " ").Trim();

                // İlk harfleri büyük yap (Title Case)
                var culture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
                var textInfo = culture.TextInfo;
                return textInfo.ToTitleCase(clean.ToLower(culture));
            }
            catch
            {
                return "";
            }
        }
    }
}
