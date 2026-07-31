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

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            // 0. Single Instance Check (Prevent overlapping app instances & clean zombie processes)
            _appMutex = new Mutex(true, MUTEX_NAME, out bool createdNew);
            if (!createdNew)
            {
                bool broughtToFront = BringExistingInstanceToForeground();
                if (!broughtToFront)
                {
                    // Previous process was a zombie (hung in background without window). Terminate it cleanly.
                    KillGhostInstances();
                    _appMutex = new Mutex(true, MUTEX_NAME, out createdNew);
                }
                else
                {
                    System.Windows.MessageBox.Show("StreamMesh zaten çalışıyor! Uygulama penceresi ön plana getirildi.", "StreamMesh", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    Shutdown();
                    return;
                }
            }

            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
                LogService.LogError("AppDomain UnhandledException", ev.ExceptionObject as Exception);

            this.DispatcherUnhandledException += (s, ev) => {
                LogService.LogError("DispatcherUnhandledException", ev.Exception);
                ev.Handled = true;
            };

            LogService.LogInfo($"App: Baslatiliyor. Process: {Process.GetCurrentProcess().MainModule?.FileName}");

            // 1. Maintenance
            try { MaintenanceEngine.EnsureSelfInstallation(); }
            catch (Exception ex) { LogService.LogError("App: Maintenance failed", ex); }

            // 2. Init DB
            try { var db = new DatabaseEngine(); }
            catch (Exception ex) { LogService.LogError("App: Database init failed", ex); }

            // 4. Media Server Init
            Server = new MediaServer();
            Ssdp = new SsdpService();
            Server.Start();

            // 5. AceStream Engine Auto-Start
            Task.Run(async () => await new AceEngine().StartEngineAsync());

            // 3. Background Cloud Sync (Delayed by 15s to allow fast startup)
            Task.Run(async () => {
                await Task.Delay(15000);
                var sync = new GitHubSyncEngine();
                await sync.PullFromGitHubAsync();
            });

            base.OnStartup(e);
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
