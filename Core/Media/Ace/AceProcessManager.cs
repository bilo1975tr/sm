using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media.Ace
{
    /// <summary>
    /// Manages the local AceStream background engine process(es) and health monitoring.
    /// Supports both the primary engine (port 6878) and isolated auxiliary console engines
    /// (ports 6879+) for simultaneous Multi-View AceStream playback without external tools.
    /// </summary>
    public static class AceProcessManager
    {
        public const int BaseHttpPort = 6878;
        public const int BaseP2pPort = 8621;
        public const string AceStreamBaseUrl = "http://127.0.0.1:6878";

        private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(4) };
        private static readonly SemaphoreSlim _startLock = new(1, 1);
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _slotLocks = new();
        private static readonly ConcurrentDictionary<int, Process> _slotProcesses = new();
        private static int _isProcessSpawning = 0;

        public static int GetSlotHttpPort(int slotIndex)
        {
            if (slotIndex <= 0) return BaseHttpPort;
            // Port 6879 and 8622 can be occupied internally by primary ace_engine.exe (HTTPS/alt P2P),
            // so auxiliary console instances start at HTTP 6881+ and P2P 8631+.
            return 6880 + Math.Clamp(slotIndex, 1, 9);
        }

        public static int GetSlotP2pPort(int slotIndex)
        {
            if (slotIndex <= 0) return BaseP2pPort;
            return 8630 + Math.Clamp(slotIndex, 1, 9);
        }

        public static string GetBaseUrlForPort(int httpPort) => $"http://127.0.0.1:{httpPort}";

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

        public static string? GetConsoleExecutablePath()
        {
            string engineDir = Path.GetDirectoryName(GetEngineExecutablePath()) ?? "";
            if (!string.IsNullOrEmpty(engineDir))
            {
                string sameDir = Path.Combine(engineDir, "ace_console.exe");
                if (File.Exists(sameDir)) return sameDir;
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string standardPath = Path.Combine(appData, "ACEStream", "engine", "ace_console.exe");
            if (File.Exists(standardPath)) return standardPath;

            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string progPath = Path.Combine(progFiles, "ACEStream", "engine", "ace_console.exe");
            if (File.Exists(progPath)) return progPath;

            string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string progX86Path = Path.Combine(progFilesX86, "ACEStream", "engine", "ace_console.exe");
            if (File.Exists(progX86Path)) return progX86Path;

            return null;
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
                        if (name.Contains("ace_engine") || name.Contains("ace_console") || name.Contains("acestream") || name.Contains("ace_player") || name.Contains("ace_web"))
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
                        if (name.Contains("ace_engine") || name.Contains("ace_console") || name.Contains("acestream") || name.Contains("ace_player") || name.Contains("ace_web"))
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

        public static Task<bool> IsEngineHttpAliveAsync() => IsEngineHttpAliveAsync(BaseHttpPort);

        public static async Task<bool> IsEngineHttpAliveAsync(int httpPort, CancellationToken token = default)
        {
            string baseUrl = GetBaseUrlForPort(httpPort);
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(350);
                var resp = await HttpClient.GetAsync($"{baseUrl}/server/api?method=get_version", cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { }

            if (token.IsCancellationRequested) return false;

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(350);
                var resp = await HttpClient.GetAsync($"{baseUrl}/webui/api/service?method=get_version", cts.Token).ConfigureAwait(false);
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

        /// <summary>
        /// Ensures an isolated AceStream engine is running for the given Multi-View slot.
        /// Slot 0 uses the primary engine (port 6878). Slots 1..3 launch isolated ace_console.exe
        /// instances on ports 6881..6883 with dedicated P2P ports (8631..8633), state dirs, and cache dirs.
        /// Falls back to port 6878 (multi-PID mode) if ace_console.exe is unavailable or fails to bind.
        /// </summary>
        public static async Task<int> StartSlotEngineAsync(int slotIndex, CancellationToken token = default)
        {
            if (slotIndex <= 0)
            {
                await StartEngineIfNotRunningAsync().ConfigureAwait(false);
                return BaseHttpPort;
            }

            int httpPort = GetSlotHttpPort(slotIndex);
            int p2pPort = GetSlotP2pPort(slotIndex);

            if (await IsEngineHttpAliveAsync(httpPort, token).ConfigureAwait(false))
                return httpPort;

            var slotLock = _slotLocks.GetOrAdd(slotIndex, _ => new SemaphoreSlim(1, 1));
            await slotLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (await IsEngineHttpAliveAsync(httpPort, token).ConfigureAwait(false))
                    return httpPort;

                string? consoleExe = GetConsoleExecutablePath();
                if (string.IsNullOrEmpty(consoleExe) || !File.Exists(consoleExe))
                {
                    LogService.LogWarning($"[ACEENGINE] ace_console.exe bulunamadı; Slot {slotIndex + 1} ana motor (port {BaseHttpPort}) üzerinden farklı PID ile denenecek.");
                    await StartEngineIfNotRunningAsync().ConfigureAwait(false);
                    return BaseHttpPort;
                }

                if (_slotProcesses.TryRemove(slotIndex, out var oldProc))
                {
                    try
                    {
                        if (!oldProc.HasExited) oldProc.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        try { oldProc.Kill(); } catch { }
                    }
                    finally
                    {
                        try { oldProc.Dispose(); } catch { }
                    }
                }

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string instanceRoot = Path.Combine(localAppData, "StreamMesh", "AceInstances", $"slot_{slotIndex}");
                string stateDir = Path.Combine(instanceRoot, "state");
                string cacheDir = Path.Combine(instanceRoot, "cache");
                Directory.CreateDirectory(stateDir);
                Directory.CreateDirectory(cacheDir);

                string args = $"--client-console --http-port {httpPort} --port {p2pPort} --state-dir \"{stateDir}\" --cache-dir \"{cacheDir}\"";
                LogService.LogInfo($"[ACEENGINE] Slot {slotIndex + 1} için izole AceStream başlatılıyor (HTTP:{httpPort}, P2P:{p2pPort}): {consoleExe}");

                var psi = new ProcessStartInfo
                {
                    FileName = consoleExe,
                    Arguments = args,
                    WorkingDirectory = Path.GetDirectoryName(consoleExe) ?? instanceRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                var proc = Process.Start(psi);
                if (proc == null)
                {
                    LogService.LogWarning($"[ACEENGINE] Slot {slotIndex + 1} için ace_console.exe başlatılamadı; ana motora düşülüyor.");
                    await StartEngineIfNotRunningAsync().ConfigureAwait(false);
                    return BaseHttpPort;
                }

                _slotProcesses[slotIndex] = proc;

                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 9000 && !token.IsCancellationRequested)
                {
                    if (proc.HasExited)
                    {
                        LogService.LogWarning($"[ACEENGINE] Slot {slotIndex + 1} ace_console.exe erken kapandı (ExitCode={proc.ExitCode}); ana motora (PID modu) düşülüyor.");
                        break;
                    }

                    await Task.Delay(350, token).ConfigureAwait(false);
                    if (await IsEngineHttpAliveAsync(httpPort, token).ConfigureAwait(false))
                    {
                        LogService.LogInfo($"[ACEENGINE] Slot {slotIndex + 1} izole motor hazır (http://127.0.0.1:{httpPort}, {sw.ElapsedMilliseconds}ms).");
                        return httpPort;
                    }
                }

                if (!token.IsCancellationRequested)
                    LogService.LogWarning($"[ACEENGINE] Slot {slotIndex + 1} izole motor HTTP:{httpPort} üzerinde zaman aşımına uğradı; ana motora (PID modu) düşülüyor.");

                // Cleanup failed or timed-out auxiliary process before fallback
                if (_slotProcesses.TryRemove(slotIndex, out var failedProc))
                {
                    try { if (!failedProc.HasExited) failedProc.Kill(entireProcessTree: true); } catch { }
                    try { failedProc.Dispose(); } catch { }
                }

                await StartEngineIfNotRunningAsync().ConfigureAwait(false);
                return BaseHttpPort;
            }
            catch (OperationCanceledException)
            {
                if (_slotProcesses.TryRemove(slotIndex, out var canceledProc))
                {
                    try { if (!canceledProc.HasExited) canceledProc.Kill(entireProcessTree: true); } catch { }
                    try { canceledProc.Dispose(); } catch { }
                }
                throw;
            }
            catch (Exception ex)
            {
                LogService.LogError($"[ACEENGINE] Slot {slotIndex + 1} başlatma hatası", ex);
                await StartEngineIfNotRunningAsync().ConfigureAwait(false);
                return BaseHttpPort;
            }
            finally
            {
                slotLock.Release();
            }
        }

        public static async Task StopSlotEngineAsync(int slotIndex)
        {
            if (slotIndex <= 0)
            {
                await AceStreamTokenResolver.StopAllStreamsOnPortAsync(BaseHttpPort).ConfigureAwait(false);
                return;
            }

            int httpPort = GetSlotHttpPort(slotIndex);
            try
            {
                await AceStreamTokenResolver.StopAllStreamsOnPortAsync(httpPort).ConfigureAwait(false);
            }
            catch { }

            if (_slotProcesses.TryRemove(slotIndex, out var proc))
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        try { proc.Kill(entireProcessTree: true); }
                        catch { proc.Kill(); }
                    }
                    LogService.LogInfo($"[ACEENGINE] Slot {slotIndex + 1} izole motoru durduruldu (Port {httpPort}).");
                }
                catch { }
                finally
                {
                    try { proc.Dispose(); } catch { }
                }
            }
        }

        public static async Task StopAllAuxiliaryEnginesAsync()
        {
            var slotKeys = _slotProcesses.Keys.Where(k => k > 0).ToList();
            foreach (var key in slotKeys)
            {
                await StopSlotEngineAsync(key).ConfigureAwait(false);
            }
        }

        public static void ShutdownEngine()
        {
            try
            {
                foreach (var kv in _slotProcesses.ToArray())
                {
                    if (_slotProcesses.TryRemove(kv.Key, out var auxProc))
                    {
                        try
                        {
                            using var ctsAux = new CancellationTokenSource(500);
                            _ = HttpClient.GetAsync($"{GetBaseUrlForPort(GetSlotHttpPort(kv.Key))}/webui/api/service?method=stop_all", ctsAux.Token);
                        }
                        catch { }

                        try
                        {
                            if (!auxProc.HasExited)
                            {
                                try { auxProc.Kill(entireProcessTree: true); }
                                catch { auxProc.Kill(); }
                            }
                            auxProc.Dispose();
                        }
                        catch { }
                    }
                }

                try
                {
                    using var cts = new CancellationTokenSource(1000);
                    _ = HttpClient.GetAsync($"{AceStreamBaseUrl}/webui/api/service?method=stop_all", cts.Token);
                }
                catch { }

                foreach (var proc in Process.GetProcessesByName("ace_engine"))
                {
                    try
                    {
                        proc.Kill();
                        proc.Dispose();
                    }
                    catch { }
                }

                foreach (var proc in Process.GetProcessesByName("ace_console"))
                {
                    try
                    {
                        proc.Kill();
                        proc.Dispose();
                    }
                    catch { }
                }

                LogService.LogInfo("[ACEENGINE] AceStream engine process(es) terminated on application exit.");
            }
            catch (Exception ex)
            {
                LogService.LogError("[ACEENGINE] Error shutting down AceStream engine", ex);
            }
        }
    }
}
