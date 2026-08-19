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
            if (!EnsureSingleInstance()) return;

            SetupExceptionHandling();

            LogService.ClearLogs();
            LogService.LogInfo($"App: Baslatiliyor. Process: {Process.GetCurrentProcess().MainModule?.FileName}");

            InitializeServices();

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

            this.DispatcherUnhandledException += (s, ev) => {
                LogService.LogError("DispatcherUnhandledException", ev.Exception);
                ev.Handled = true;
            };
        }

        private void InitializeServices()
        {
            // 1. Maintenance
            try { MaintenanceEngine.EnsureSelfInstallation(); }
            catch (Exception ex) { LogService.LogError("App: Maintenance failed", ex); }

            // 2. Init DB
            try { _ = new DatabaseEngine(); }
            catch (Exception ex) { LogService.LogError("App: Database init failed", ex); }

            // 3. Media Server Init
            Server = new MediaServer();
            Ssdp = new SsdpService();
            Server.Start();

            // 4. AceStream & Cloud Sync
            StartBackgroundTasks();
        }

        private void StartBackgroundTasks()
        {
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
