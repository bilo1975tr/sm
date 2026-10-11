using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using StreamMesh.Core.Media;

namespace StreamMesh.Core.Utils
{
    public class UpdateService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private const string REMOTE_VERSION_URL = "https://raw.githubusercontent.com/bilo1975tr/sm/refs/heads/main/version.txt";

        public static event Action<string>? OnVersionUpdated;

        public static string GetCurrentVersion()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string vPath = Path.Combine(baseDir, "version.txt");
                if (!File.Exists(vPath))
                {
                    vPath = Path.Combine(baseDir, "VERSION");
                }
                if (!File.Exists(vPath))
                {
                    vPath = "version.txt";
                }

                if (File.Exists(vPath))
                {
                    string content = File.ReadAllText(vPath).Trim();
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        // Extract first line if multi-line
                        string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Length > 0 && !string.IsNullOrWhiteSpace(lines[0]))
                        {
                            return lines[0].Trim();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("UpdateService: GetCurrentVersion error", ex);
            }

            return "0.0.1";
        }

        public async Task<(bool HasUpdate, string RemoteVersion)> CheckForUpdateAsync()
        {
            try
            {
                string currentVersionStr = GetCurrentVersion();
                string remoteVersionStr = "";

                try
                {
                    var response = await _httpClient.GetAsync(REMOTE_VERSION_URL);
                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Length > 0) remoteVersionStr = lines[0].Trim();
                    }
                }
                catch { }

                if (string.IsNullOrWhiteSpace(remoteVersionStr))
                {
                    return (false, currentVersionStr);
                }

                // Clean version strings (e.g. "v1.0.1" -> "1.0.1")
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

        public async Task<bool> PerformUpdateAsync(Action<int, string>? onProgress = null)
        {
            try
            {
                onProgress?.Invoke(10, "GitHub senkronizasyonu ve kanal listeleri indiriliyor...");

                var gitSync = new GitHubSyncEngine();
                gitSync.OnProgress += (percent, msg) => {
                    onProgress?.Invoke(percent, msg);
                };

                await gitSync.PullFromGitHubAsync();

                // Download latest version.txt from remote if available
                onProgress?.Invoke(90, "Sürüm dosyası güncelleniyor...");
                try
                {
                    var response = await _httpClient.GetAsync(REMOTE_VERSION_URL);
                    if (response.IsSuccessStatusCode)
                    {
                        string remoteVerContent = await response.Content.ReadAsStringAsync();
                        if (!string.IsNullOrWhiteSpace(remoteVerContent))
                        {
                            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                            string localPath = Path.Combine(baseDir, "version.txt");
                            await File.WriteAllTextAsync(localPath, remoteVerContent.Trim());

                            try
                            {
                                string verAltPath = Path.Combine(baseDir, "VERSION");
                                await File.WriteAllTextAsync(verAltPath, remoteVerContent.Trim());
                            }
                            catch { }
                        }
                    }
                }
                catch { }

                string updatedVersion = GetCurrentVersion();
                OnVersionUpdated?.Invoke(updatedVersion);

                onProgress?.Invoke(100, "Güncelleme başarıyla tamamlandı!");
                return true;
            }
            catch (Exception ex)
            {
                LogService.LogError("UpdateService: PerformUpdateAsync error", ex);
                return false;
            }
        }
    }
}
