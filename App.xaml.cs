using System;
using System.Windows;
using StreamMesh.Core;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media;
using StreamMesh.Core.Utils;
using StreamMesh.Core.Network;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;

namespace StreamMesh
{
    public partial class App : System.Windows.Application
    {
        private static Mutex? _appMutex;
        private const string MUTEX_NAME = "Global\\StreamMesh_SingleInstance_Mutex_99218274";

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        public static MediaServer? Server { get; private set; }
        public static SsdpService? Ssdp { get; private set; }

        protected override async void OnStartup(System.Windows.StartupEventArgs e)
        {
            if (!EnsureSingleInstance()) return;

            SetupExceptionHandling();

            LogService.ClearLogs();
            LogService.LogInfo($"App: Baslatiliyor. Process: {Process.GetCurrentProcess().MainModule?.FileName}");

            await InitializeServicesAsync();

            base.OnStartup(e);
        }

        private bool EnsureSingleInstance()
        {
            _appMutex = new Mutex(true, MUTEX_NAME, out bool createdNew);
            if (!createdNew)
            {
                bool broughtToFront = BringExistingInstanceToForeground();
                if (!broughtToFront)
                {
                    KillGhostInstances();
                    _appMutex = new Mutex(true, MUTEX_NAME, out createdNew);
                }
                else
                {
                    System.Windows.MessageBox.Show("StreamMesh zaten çalışıyor! Uygulama penceresi ön plana getirildi.", "StreamMesh", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    Shutdown();
                    return false;
                }
            }
            return true;
        }

        private void SetupExceptionHandling()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
                LogService.LogError("AppDomain UnhandledException", ev.ExceptionObject as Exception);

            TaskScheduler.UnobservedTaskException += (s, ev) => {
                LogService.LogError("UnobservedTaskException (Background Task)", ev.Exception);
                ev.SetObserved();
            };

            this.DispatcherUnhandledException += (s, ev) => {
                LogService.LogError("DispatcherUnhandledException (UI Thread)", ev.Exception);
                ev.Handled = true;
            };
        }

        private async Task InitializeServicesAsync()
        {
            LogService.LogInfo("[STARTUP] Initializing Services...");

            // 1. Media Server Init (HIGH PRIORITY - must start even if DB fails)
            try
            {
                Server = new MediaServer();
                Ssdp = new SsdpService();

                if (Server.Start())
                {
                    Ssdp.Start(Server.Port);
                    LogService.LogInfo($"[STARTUP] MediaServer and SSDP started on port {Server.Port}.");
                }
                else
                {
                    LogService.LogError("[STARTUP] CRITICAL: MediaServer failed to start (Port issue or Listener error).");
                    System.Windows.MessageBox.Show("Uygulama sunucusu başlatılamadı. Port çakışması veya yetki sorunu olabilir.", "Kritik Hata", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("[STARTUP] CRITICAL: Exception during MediaServer/SSDP init", ex);
            }

            // 2. Maintenance
            try
            {
                MaintenanceEngine.EnsureSelfInstallation();
                LogService.LogInfo("[STARTUP] Maintenance check completed.");
            }
            catch (Exception ex) { LogService.LogError("[STARTUP] Maintenance failed", ex); }

            // 3. Init DB (Awaited to ensure tables and migrations are ready before UI loads)
            try
            {
                LogService.LogInfo("[STARTUP] Database initialization starting...");
                var db = new DatabaseEngine();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var dbTask = db.InitializeAsync();

                if (await Task.WhenAny(dbTask, Task.Delay(-1, cts.Token)) == dbTask)
                {
                    await dbTask; // Propagate exceptions if any
                    LogService.LogInfo("[STARTUP] Database initialized successfully.");

                    // Fast initial local logos indexing
                    try
                    {
                        new LogoSyncService().ScanLocalLogosFolder();
                    }
                    catch (Exception logoEx)
                    {
                        LogService.LogWarning($"[STARTUP] Local logo scanning warning: {logoEx.Message}");
                    }
                }
                else
                {
                    LogService.LogWarning("[STARTUP] Database initialization TIMEOUT (20s). Background work might continue.");
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("[STARTUP] Database init failed", ex);
            }

            // 4. AceStream, Logo Sync & Cloud Sync (Arka planda devam etsinler)
            StartBackgroundTasks();

            LogService.LogInfo("[STARTUP] Service initialization sequence finished (DB may still be loading).");
        }

        private void StartBackgroundTasks()
        {
            // 1. Logo Sync (Sync local + online tv-logos if necessary)
            Task.Run(async () => {
                try
                {
                    var logoSync = new LogoSyncService();
                    await logoSync.SyncIfNecessaryAsync();
                }
                catch (Exception ex)
                {
                    LogService.LogError("[STARTUP] LogoSync background task error", ex);
                }
            });

            // 2. AceStream Engine Check / Start
            Task.Run(async () => {
                var ace = new AceEngine();
                if (!ace.IsInstalled())
                {
                    await Current.Dispatcher.InvokeAsync(async () => {
                        var result = System.Windows.MessageBox.Show("AceStream motoru yüklü değil. P2P içerikleri oynatabilmeniz için gerekli bileşenler şimdi indirilsin mi?\n\nNot: İndirme işlemi arka planda yapılacaktır.", "Eksik Bileşen", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (result == MessageBoxResult.Yes)
                        {
                            bool success = await ace.DownloadAndExtractEngineAsync();
                            if (success)
                            {
                                await ace.StartEngineAsync();
                                System.Windows.MessageBox.Show("AceStream başarıyla yüklendi ve başlatıldı.", "Kurulum Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                        }
                    });
                }
                else
                {
                    await ace.StartEngineAsync();
                }
            });

            // 3. GitHub Playlist Sync
            Task.Run(async () => {
                await Task.Delay(15000);
                var sync = new GitHubSyncEngine();
                await sync.PullFromGitHubAsync();
            });
        }

        private static bool BringExistingInstanceToForeground()
        {
            try
            {
                var currentProc = Process.GetCurrentProcess();
                var processes = Process.GetProcessesByName(currentProc.ProcessName);
                foreach (var proc in processes)
                {
                    if (proc.Id != currentProc.Id && proc.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(proc.MainWindowHandle);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static void KillGhostInstances()
        {
            try
            {
                var currentProc = Process.GetCurrentProcess();
                var processes = Process.GetProcessesByName(currentProc.ProcessName);
                foreach (var proc in processes)
                {
                    if (proc.Id != currentProc.Id)
                    {
                        proc.Kill();
                    }
                }
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                // Stop any active AceStream broadcasts on exit
                new AceEngine().StopAllStreamsAsync().Wait(2000);

                // Stop HLS Local Proxy Server and pollers
                HlsProxyEngine.Instance.Stop();

                // Flush and shutdown logger
                LogService.Shutdown();

                if (_appMutex != null)
                {
                    _appMutex.ReleaseMutex();
                    _appMutex.Dispose();
                    _appMutex = null;
                }
            }
            catch { }

            base.OnExit(e);
            Environment.Exit(0);
        }
    }
}
