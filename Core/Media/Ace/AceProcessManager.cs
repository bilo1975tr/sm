using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media.Ace
{
    /// <summary>
    /// Manages the local AceStream background engine process and health monitoring.
    /// </summary>
    public static class AceProcessManager
    {
        public const string AceStreamBaseUrl = "http://127.0.0.1:6878";
        private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(4) };
        private static readonly SemaphoreSlim _startLock = new(1, 1);
        private static int _isProcessSpawning = 0;

        public static string GetEngineExecutablePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string standardPath = Path.Combine(appData, "ACEStream", "engine", "ace_engine.exe");
            if (File.Exists(standardPath)) return standardPath;

            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string progPath = Path.Combine(progFiles, "ACEStream", "engine", "ace_engine.exe");
            if (File.Exists(progPath)) return progPath;

            string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string progX86Path = Path.Combine(progFilesX86, "ACEStream", "engine", "ace_engine.exe");
            if (File.Exists(progX86Path)) return progX86Path;

            return standardPath;
        }

        public static bool IsAnyAceProcessRunning()
        {
            try
            {
                var procs = Process.GetProcesses();
                foreach (var p in procs)
                {
                    try
                    {
                        string name = p.ProcessName.ToLowerInvariant();
                        if (name.Contains("ace_engine") || name.Contains("acestream") || name.Contains("ace_player") || name.Contains("ace_web"))
                        {
                            return true;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return false;
        }

        public static int GetAceProcessCount()
        {
            int count = 0;
            try
            {
                var procs = Process.GetProcesses();
                foreach (var p in procs)
                {
                    try
                    {
                        string name = p.ProcessName.ToLowerInvariant();
                        if (name.Contains("ace_engine") || name.Contains("acestream") || name.Contains("ace_player") || name.Contains("ace_web"))
                        {
                            count++;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return count;
        }

        public static async Task<bool> IsEngineHttpAliveAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(400);
                var resp = await HttpClient.GetAsync($"{AceStreamBaseUrl}/server/api?method=get_version", cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { }

            try
            {
                using var cts = new CancellationTokenSource(400);
                var resp = await HttpClient.GetAsync($"{AceStreamBaseUrl}/webui/api/service?method=get_version", cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { }

            try
            {
                using var cts = new CancellationTokenSource(400);
                var resp = await HttpClient.GetAsync($"{AceStreamBaseUrl}/", cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { }

            return false;
        }

        public static async Task<bool> IsEngineRunningAsync()
        {
            if (await IsEngineHttpAliveAsync().ConfigureAwait(false)) return true;
            return IsAnyAceProcessRunning();
        }

        public static async Task<bool> StartEngineIfNotRunningAsync()
        {
            if (await IsEngineHttpAliveAsync().ConfigureAwait(false)) return true;

            if (IsAnyAceProcessRunning())
            {
                LogService.LogInfo("[ACEENGINE] AceStream süreci zaten aktif. Yeni kopya başlatılmayacak, API yanıtı bekleniyor...");
                for (int i = 0; i < 5; i++)
                {
                    await Task.Delay(300).ConfigureAwait(false);
                    if (await IsEngineHttpAliveAsync().ConfigureAwait(false)) return true;
                }
                return true;
            }

            await _startLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (await IsEngineHttpAliveAsync().ConfigureAwait(false)) return true;
                if (IsAnyAceProcessRunning()) return true;

                if (Interlocked.CompareExchange(ref _isProcessSpawning, 1, 0) != 0)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        await Task.Delay(300).ConfigureAwait(false);
                        if (await IsEngineHttpAliveAsync().ConfigureAwait(false)) return true;
                    }
                    return IsAnyAceProcessRunning();
                }

                try
                {
                    string exePath = GetEngineExecutablePath();
                    if (File.Exists(exePath))
                    {
                        LogService.LogInfo($"[ACEENGINE] Starting AceStream engine executable once: {exePath}");
                        var psi = new ProcessStartInfo
                        {
                            FileName = exePath,
                            UseShellExecute = true,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        Process.Start(psi);

                        for (int i = 0; i < 6; i++)
                        {
                            await Task.Delay(400).ConfigureAwait(false);
                            if (await IsEngineHttpAliveAsync().ConfigureAwait(false))
                            {
                                LogService.LogInfo("[ACEENGINE] AceStream engine successfully started and ready.");
                                return true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("[ACEENGINE] Failed to start ace_engine.exe", ex);
                }
                finally
                {
                    Interlocked.Exchange(ref _isProcessSpawning, 0);
                }

                return IsAnyAceProcessRunning();
            }
            finally
            {
                _startLock.Release();
            }
        }

        public static void ShutdownEngine()
        {
            try
            {
                try
                {
                    using var cts = new CancellationTokenSource(1000);
                    _ = HttpClient.GetAsync($"{AceStreamBaseUrl}/webui/api/service?method=stop_all", cts.Token);
                }
                catch { }

                var processes = Process.GetProcessesByName("ace_engine");
                foreach (var proc in processes)
                {
                    try
                    {
                        proc.Kill();
                        proc.Dispose();
                    }
                    catch { }
                }
                LogService.LogInfo("[ACEENGINE] AceStream engine process terminated on application exit.");
            }
            catch (Exception ex)
            {
                LogService.LogError("[ACEENGINE] Error shutting down AceStream engine", ex);
            }
        }
    }
}
