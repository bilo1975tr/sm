using System;
using System.Windows;
using StreamMesh.Core;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media;
using StreamMesh.Core.Utils;
using StreamMesh.Core.Network;
using System.Threading.Tasks;
using System.Diagnostics;

namespace StreamMesh
{
    public partial class App : System.Windows.Application
    {
        public static MediaServer? Server { get; private set; }
        public static SsdpService? Ssdp { get; private set; }

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
                LogService.LogError("AppDomain UnhandledException", ev.ExceptionObject as Exception);

            this.DispatcherUnhandledException += (s, ev) => {
                LogService.LogError("DispatcherUnhandledException", ev.Exception);
                ev.Handled = true;
            };

            LogService.LogInfo($"App: Baslatiliyor. Process: {Process.GetCurrentProcess().MainModule?.FileName}");

            // 1. Maintenance
            MaintenanceEngine.EnsureSelfInstallation();

            // 2. Init DB
            var db = new DatabaseEngine();

            // 4. Media Server Init
            Server = new MediaServer();
            Ssdp = new SsdpService();
            Server.Start();

            // 5. Logo Index Sync
            Task.Run(async () => await new LogoSyncService().SyncIfNecessaryAsync());

            // 6. AceStream Engine Auto-Start
            Task.Run(async () => await new AceEngine().StartEngineAsync());

            // 3. Background Cloud Sync
            Task.Run(async () => {
                var sync = new GitHubSyncEngine();
                await sync.PullFromGitHubAsync();
            });

            base.OnStartup(e);
        }
    }
}
