using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using StreamMesh.Core.Media;

namespace StreamMesh.Core.Utils
{
    public class UpdateService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private const string REMOTE_VERSION_URL = "https://raw.githubusercontent.com/bilo1975tr/sm/refs/heads/main/version.txt";
        private const string GITHUB_REPO_API = "https://api.github.com/repos/bilo1975tr/sm/releases/latest";

        public static event Action<string>? OnVersionUpdated;

        public static string GetCurrentVersion()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string vPath = Path.Combine(baseDir, "version.txt");

                if (!File.Exists(vPath))
                {
                    vPath = "version.txt";
                }

                if (File.Exists(vPath))
                {
                    string content = File.ReadAllText(vPath).Trim();
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Length > 0 && !string.IsNullOrWhiteSpace(lines[0]))
                        {
                            return lines[0].Trim().TrimStart('v', 'V');
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("UpdateService: GetCurrentVersion error", ex);
            }

            return "0.1.0";
        }

        public async Task<(bool HasUpdate, string RemoteVersion)> CheckForUpdateAsync()
        {
            try
            {
                string currentVersionStr = GetCurrentVersion();
                string remoteVersionStr = "";

                try
                {
                    var response = await _httpClient.GetAsync(REMOTE_VERSION_URL).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Length > 0) remoteVersionStr = lines[0].Trim();
                    }
                }
                catch { }

                if (string.IsNullOrWhiteSpace(remoteVersionStr))
                {
                    return (false, currentVersionStr);
                }

                string cleanLocal = currentVersionStr.TrimStart('v', 'V');
                string cleanRemote = remoteVersionStr.TrimStart('v', 'V');

                if (Version.TryParse(cleanLocal, out Version? vLocal) && Version.TryParse(cleanRemote, out Version? vRemote))
                {
                    if (vRemote > vLocal)
                    {
                        return (true, remoteVersionStr);
                    }
                }
                else if (!string.Equals(cleanLocal, cleanRemote, StringComparison.OrdinalIgnoreCase))
                {
                    return (true, remoteVersionStr);
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("UpdateService: CheckForUpdateAsync error", ex);
            }

            return (false, GetCurrentVersion());
        }

        public async Task<(string? downloadUrl, string? fileName)> GetLatestReleaseAssetAsync(string targetVersion)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, GITHUB_REPO_API);
                request.Headers.Add("User-Agent", "StreamMesh-Updater");
                var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                    {
                        string? installerUrl = null;
                        string? installerName = null;
                        string? zipUrl = null;
                        string? zipName = null;

                        foreach (var asset in assets.EnumerateArray())
                        {
                            string name = asset.GetProperty("name").GetString() ?? "";
                            string url = asset.GetProperty("browser_download_url").GetString() ?? "";

                            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                            {
                                installerUrl = url;
                                installerName = name;
                                break;
                            }
                            else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                zipUrl = url;
                                zipName = name;
                            }
                        }

                        if (!string.IsNullOrEmpty(installerUrl)) return (installerUrl, installerName);
                        if (!string.IsNullOrEmpty(zipUrl)) return (zipUrl, zipName);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"UpdateService: GitHub Releases API check failed: {ex.Message}");
            }

            // Fallback direct URL schema
            string cleanVer = targetVersion.TrimStart('v', 'V');
            string fallbackExe = $"https://github.com/bilo1975tr/sm/releases/download/v{cleanVer}/StreamMesh-Setup-v{cleanVer}.exe";
            return (fallbackExe, $"StreamMesh-Setup-v{cleanVer}.exe");
        }

        public async Task<bool> DownloadAndInstallUpdateAsync(string targetVersion, Action<int, string, string?> onProgress)
        {
            try
            {
                onProgress(5, "Son yayın paketi aranıyor...", "GitHub Release bilgileri taranıyor");

                var (downloadUrl, fileName) = await GetLatestReleaseAssetAsync(targetVersion).ConfigureAwait(false);
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    onProgress(0, "İndirme bağlantısı bulunamadı.", null);
                    return false;
                }

                string tempDir = Path.GetTempPath();
                string localInstallerPath = Path.Combine(tempDir, fileName ?? "StreamMesh-Setup.exe");

                onProgress(15, "İndirme başlatılıyor...", $"Dosya: {fileName}");

                // Stream Download with Real-Time Progress
                using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        // If direct release exe is not yet generated or accessible, perform in-place content sync fallback
                        LogService.LogWarning($"UpdateService: Release file not accessible ({response.StatusCode}), falling back to content sync.");
                        return await FallbackContentUpdateAsync(onProgress).ConfigureAwait(false);
                    }

                    long totalBytes = response.Content.Headers.ContentLength ?? -1;

                    using (var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var fileStream = new FileStream(localInstallerPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        var buffer = new byte[81920];
                        long totalRead = 0;
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
                            totalRead += bytesRead;

                            if (totalBytes > 0)
                            {
                                int percent = 15 + (int)((totalRead * 75.0) / totalBytes);
                                double mbDownloaded = totalRead / (1024.0 * 1024.0);
                                double mbTotal = totalBytes / (1024.0 * 1024.0);
                                onProgress(percent, "Yeni sürüm indiriliyor...", $"{mbDownloaded:0.0} MB / {mbTotal:0.0} MB");
                            }
                            else
                            {
                                double mbDownloaded = totalRead / (1024.0 * 1024.0);
                                onProgress(50, "Yeni sürüm indiriliyor...", $"{mbDownloaded:0.0} MB indirildi");
                            }
                        }
                    }
                }

                onProgress(95, "Kurulum başlatılıyor...", "Mevcut uygulama kapatılıp yeni sürüm kurulacak");
                await Task.Delay(1000).ConfigureAwait(false);

                // Execute Inno Setup Installer in background and terminate current process safely
                if (File.Exists(localInstallerPath))
                {
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = localInstallerPath,
                            Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                            UseShellExecute = true
                        };
                        Process.Start(psi);

                        // Safely shut down the current application instance so the installer can replace files without file-lock issues
                        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            System.Windows.Application.Current?.Shutdown();
                        });

                        return true;
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError("UpdateService: Installer launch failed", ex);
                        return false;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                LogService.LogError("UpdateService: DownloadAndInstallUpdateAsync error", ex);
                return await FallbackContentUpdateAsync(onProgress).ConfigureAwait(false);
            }
        }

        private async Task<bool> FallbackContentUpdateAsync(Action<int, string, string?> onProgress)
        {
            try
            {
                onProgress(40, "Kanal listeleri ve sistem güncelleniyor...", "GitHub Sync");
                var gitSync = new GitHubSyncEngine();
                gitSync.OnProgress += (percent, msg) => {
                    onProgress(40 + (int)(percent * 0.4), msg, null);
                };
                await gitSync.PullFromGitHubAsync().ConfigureAwait(false);

                onProgress(90, "Sürüm bilgisi eşitleniyor...", null);
                try
                {
                    var response = await _httpClient.GetAsync(REMOTE_VERSION_URL).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        string remoteVerContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(remoteVerContent))
                        {
                            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                            string localPath = Path.Combine(baseDir, "version.txt");
                            await File.WriteAllTextAsync(localPath, remoteVerContent.Trim()).ConfigureAwait(false);
                        }
                    }
                }
                catch { }

                string updatedVersion = GetCurrentVersion();
                OnVersionUpdated?.Invoke(updatedVersion);

                onProgress(100, "Güncelleme başarıyla tamamlandı!", "Sürüm: v" + updatedVersion);
                return true;
            }
            catch (Exception ex)
            {
                LogService.LogError("UpdateService: FallbackContentUpdateAsync error", ex);
                return false;
            }
        }
    }
}
