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
        public static MediaServer? Server { get; set; }
        public static SsdpService? Ssdp { get; set; }

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            SetupExceptionHandling();

            try
            {
                CleanStaleStreamMeshProcesses();
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[STARTUP] Temizleme uyarısı: {ex.Message}");
            }

            try
            {
                LogService.ClearLogs();
                LogService.LogInfo($"App: Başlatılıyor. Process: {Process.GetCurrentProcess().MainModule?.FileName}");

                base.OnStartup(e);

                // Ana pencereyi oluştur ve görünür kıl
                try
                {
                    if (this.MainWindow == null)
                    {
                        this.MainWindow = new UI.Windows.MainWindow();
                    }

                    this.MainWindow.Show();
                    this.MainWindow.WindowState = WindowState.Normal;
                    this.MainWindow.Visibility = Visibility.Visible;
                    this.MainWindow.Activate();
                    this.MainWindow.Focus();
                    LogService.LogInfo("[STARTUP] MainWindow başarıyla açıldı ve gösterildi.");
                }
                catch (Exception ex)
                {
                    LogService.LogFatal("[STARTUP] MainWindow açılış hatası", ex);
                    System.Windows.MessageBox.Show(
                        $"StreamMesh Penceresi Başlatılamadı:\n\n{ex.Message}\n\nDetay:\n{ex}",
                        "Kritik Başlatma Hatası",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }

                // Arka plan servisleri ana pencereyi kilitlemeden başlatılır
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await InitializeServicesAsync();
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError("[STARTUP] Arka plan servisleri başlatma hatası", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                LogService.LogFatal("[STARTUP FATAL] OnStartup genel hatası", ex);
                try
                {
                    System.Windows.MessageBox.Show(
                        $"StreamMesh başlatılırken kritik bir hata meydana geldi:\n\n{ex.Message}\n\nDetay:\n{ex}",
                        "StreamMesh Kritik Hata",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch { }
            }
        }

        private void SetupExceptionHandling()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
            {
                var ex = ev.ExceptionObject as Exception;
                LogService.LogFatal("AppDomain UnhandledException", ex);
                try
                {
                    System.Windows.MessageBox.Show(
                        $"StreamMesh beklenmeyen bir hatayla karşılaştı:\n\n{ex?.Message ?? ev.ExceptionObject?.ToString()}\n\nDetaylar log dosyasına kaydedildi.",
                        "StreamMesh Kritik Hata",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch { }
            };

            TaskScheduler.UnobservedTaskException += (s, ev) =>
            {
                LogService.LogError("UnobservedTaskException (Background Task)", ev.Exception);
                ev.SetObserved();
            };

            this.DispatcherUnhandledException += (s, ev) =>
            {
                LogService.LogFatal("DispatcherUnhandledException (UI Thread)", ev.Exception);
                try
                {
                    System.Windows.MessageBox.Show(
                        $"StreamMesh arayüz işlem hatası:\n\n{ev.Exception.Message}\n\n{(ev.Exception.InnerException != null ? ev.Exception.InnerException.Message : "")}",
                        "StreamMesh Arayüz Hatası",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch { }
                ev.Handled = true;
            };
        }

        private async Task InitializeServicesAsync()
        {
            LogService.LogInfo("[STARTUP] Servisler başlatılıyor...");

            // 1. Veritabanını HEMEN VE İLK SIRADA başlat (Böylece UI ve tüm servisler hazır DB'yi bulur)
            try
            {
                LogService.LogInfo("[STARTUP] Veritabanı başlatılıyor...");
                await DatabaseEngine.Instance.InitializeAsync().ConfigureAwait(false);
                LogService.LogInfo("[STARTUP] Veritabanı başarıyla hazırlandı.");
            }
            catch (Exception ex)
            {
                LogService.LogError("[STARTUP] Veritabanı başlatma hatası", ex);
            }

            // 2. Media Server & SSDP Init (Eklenti ayarlarına göre başlat)
            var db = DatabaseEngine.Instance;
            var serverPlugin = StreamMesh.Core.Plugins.PluginManager.Instance.GetPluginById("local_network_server") as StreamMesh.Core.Plugins.LocalNetworkServerPlugin;
            var ssdpPlugin = StreamMesh.Core.Plugins.PluginManager.Instance.GetPluginById("ssdp_discovery") as StreamMesh.Core.Plugins.SsdpDiscoveryPlugin;

            bool shouldStartServer = serverPlugin == null || (serverPlugin.IsEnabled && serverPlugin.GetBool("AutoStartOnLaunch"));
            if (shouldStartServer)
            {
                try
                {
                    string fallbackPort = serverPlugin != null ? serverPlugin.GetString("ServerPort", "8088") : "8088";
                    string savedPortStr = db.GetSetting("ServerPort", fallbackPort);
                    int initialPort = int.TryParse(savedPortStr, out int p) && p > 0 ? p : 8088;

                    Server = new MediaServer(initialPort);
                    if (Server.Start())
                    {
                        db.SetSetting("ServerPort", Server.Port.ToString());
                        if (ssdpPlugin == null || ssdpPlugin.IsEnabled)
                        {
                            Ssdp = new SsdpService();
                            Ssdp.Start(Server.Port);
                        }
                        LogService.LogInfo($"[STARTUP] MediaServer {Server.Port} portunda başlatıldı.");
                    }
                    else
                    {
                        LogService.LogWarning("[STARTUP] MediaServer adayı portlarda açılamadı, yerel oynatma devam edecek.");
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("[STARTUP] MediaServer/SSDP başlatma istisnası", ex);
                }
            }
            else
            {
                LogService.LogInfo("[STARTUP] Yerel Medya Sunucusu eklentisi devre dışı veya otomatik başlatma kapalı.");
            }

            // 3. Eklenti yaşam döngüsünü başlat
            try
            {
                await StreamMesh.Core.Plugins.PluginManager.Instance.InitializeAllAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.LogError("[STARTUP] PluginManager InitializeAllAsync hatası", ex);
            }

            // 4. Maintenance (Arka planda sessiz kontrol)
            try
            {
                MaintenanceEngine.EnsureSelfInstallation();
                LogService.LogInfo("[STARTUP] Bakım kontrolü tamamlandı.");
            }
            catch (Exception ex) { LogService.LogError("[STARTUP] Bakım hatası", ex); }

            // 5. AceStream, Logo Sync & Cloud Sync
            StartBackgroundTasks();

            LogService.LogInfo("[STARTUP] Servis başlatma sekansı tamamlandı.");
        }

        private void StartBackgroundTasks()
        {
            // 0. AceStream Engine: İsteğe bağlı (On-Demand) başlatılır. Açılışta mükerrer kopya oluşmasını önlemek için pasife alındı.
            LogService.LogInfo("[STARTUP] AceEngine on-demand modunda: İhtiyaç duyulduğunda başlatılacak.");

            // 1. Logo Sync (Sync local + online tv-logos if LogoPlugin enabled)
            _ = Task.Run(async () => {
                try
                {
                    await Task.Delay(25000).ConfigureAwait(false); // 25s gecikme ile UI açılışını asla yormaz
                    var logoPlugin = StreamMesh.Core.Plugins.PluginManager.Instance.GetPluginById("logo_manager");
                    if (logoPlugin != null && !logoPlugin.IsEnabled)
                    {
                        LogService.LogInfo("[STARTUP] Logo yöneticisi eklentisi kapalı olduğu için LogoSync atlandı.");
                        return;
                    }
                    while (GitHubSyncEngine.IsRunning)
                    {
                        await Task.Delay(5000).ConfigureAwait(false);
                    }
                    var logoSync = new LogoSyncService();
                    await logoSync.SyncIfNecessaryAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogService.LogError("[STARTUP] LogoSync background task error", ex);
                }
            });

            // 2. GitHub Playlist Sync: Yalnızca eklenti aktifse ve açılışta güncelleme seçiliyse
            _ = Task.Run(async () => {
                try
                {
                    await Task.Delay(4000).ConfigureAwait(false); // DB'nin tam stabil oturması için bekle
                    var plugin = StreamMesh.Core.Plugins.PluginManager.Instance.GetPluginById("github_sync") as StreamMesh.Core.Plugins.GitHubSyncPlugin;
                    if (plugin != null)
                    {
                        if (!plugin.IsEnabled)
                        {
                            LogService.LogInfo("[STARTUP] Bulut & GitHub eklentisi (github_sync) kapalı olduğu için açılış senkronizasyonu atlandı.");
                            return;
                        }
                        if (!plugin.GetBool("AutoSyncOnLaunch"))
                        {
                            LogService.LogInfo("[STARTUP] Bulut & GitHub eklentisinin 'Açılışta Otomatik Güncelle' ayarı kapalı olduğu için açılış senkronizasyonu atlandı.");
                            return;
                        }
                    }

                    var db = DatabaseEngine.Instance;
                    int count = await db.GetTotalChannelCountAsync().ConfigureAwait(false);
                    if (count == 0 && db.GetSetting("InitialPlaylistImportAttempted", "false") != "true")
                    {
                        db.SetSetting("InitialPlaylistImportAttempted", "true");
                        LogService.LogInfo("[STARTUP] Veritabanında kanal bulunamadı (Count: 0). İlk kurulum için GitHub listesi indiriliyor...");
                        var sync = new GitHubSyncEngine();
                        await sync.PullFromGitHubAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        LogService.LogInfo($"[STARTUP] Veritabanında {count} adet kanal mevcut. Açılışta otomatik internet indirmesi atlandı, yerel liste yüklendi.");
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("[STARTUP] GitHubSyncEngine background task error", ex);
                }
            });
        }

        private static void CleanStaleStreamMeshProcesses()
        {
            try
            {
                var currentProc = Process.GetCurrentProcess();
                if (!currentProc.ProcessName.Contains("StreamMesh", StringComparison.OrdinalIgnoreCase)) return;

                var processes = Process.GetProcessesByName(currentProc.ProcessName);
                foreach (var proc in processes)
                {
                    if (proc.Id != currentProc.Id)
                    {
                        try
                        {
                            proc.Kill();
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                // Shutdown AceStream motor process completely on exit
                AceEngine.Instance.ShutdownEngine();

                // Stop HLS Local Proxy Server and pollers
                HlsProxyEngine.Instance.Stop();

                // Flush and shutdown logger
                LogService.Shutdown();
            }
            catch { }

            base.OnExit(e);
            Environment.Exit(0);
        }
    }
}
