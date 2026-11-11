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
                var db = new DatabaseEngine();
                await db.InitializeAsync().ConfigureAwait(false);
                LogService.LogInfo("[STARTUP] Veritabanı başarıyla hazırlandı.");
            }
            catch (Exception ex)
            {
                LogService.LogError("[STARTUP] Veritabanı başlatma hatası", ex);
            }

            // 2. Media Server Init (Artık DB hazır olduğundan port ayarını anında okuyabilir)
            try
            {
                var db = new DatabaseEngine();
                string savedPortStr = db.GetSetting("ServerPort", "8088");
                int initialPort = int.TryParse(savedPortStr, out int p) ? p : 8088;

                Server = new MediaServer(initialPort);
                Ssdp = new SsdpService();

                if (Server.Start())
                {
                    db.SetSetting("ServerPort", Server.Port.ToString());
                    Ssdp.Start(Server.Port);
                    LogService.LogInfo($"[STARTUP] MediaServer ve SSDP {Server.Port} portunda başlatıldı.");
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

            // 3. Maintenance (Arka planda sessiz kontrol)
            try
            {
                MaintenanceEngine.EnsureSelfInstallation();
                LogService.LogInfo("[STARTUP] Bakım kontrolü tamamlandı.");
            }
            catch (Exception ex) { LogService.LogError("[STARTUP] Bakım hatası", ex); }

            // 4. AceStream, Logo Sync & Cloud Sync
            StartBackgroundTasks();

            LogService.LogInfo("[STARTUP] Servis başlatma sekansı tamamlandı.");
        }

        private void StartBackgroundTasks()
        {
            // 0. AceStream Engine: İsteğe bağlı (On-Demand) başlatılır. Açılışta mükerrer kopya oluşmasını önlemek için pasife alındı.
            LogService.LogInfo("[STARTUP] AceEngine on-demand modunda: İhtiyaç duyulduğunda başlatılacak.");

            // 1. Logo Sync (Sync local + online tv-logos if necessary)
            Task.Run(async () => {
                try
                {
                    await Task.Delay(25000); // 25s gecikme ile UI açılışını asla yormaz
                    while (GitHubSyncEngine.IsRunning)
                    {
                        await Task.Delay(5000);
                    }
                    var logoSync = new LogoSyncService();
                    await logoSync.SyncIfNecessaryAsync();
                }
                catch (Exception ex)
                {
                    LogService.LogError("[STARTUP] LogoSync background task error", ex);
                }
            });

            // 2. GitHub Playlist Sync: Yalnızca eklenti aktifse ve açılışta güncelleme seçiliyse
            Task.Run(async () => {
                try
                {
                    await Task.Delay(4000); // DB'nin tam stabil oturması için bekle
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

                    var db = new DatabaseEngine();
                    int count = await db.GetTotalChannelCountAsync();
                    if (count == 0 && db.GetSetting("InitialPlaylistImportAttempted", "false") != "true")
                    {
                        db.SetSetting("InitialPlaylistImportAttempted", "true");
                        LogService.LogInfo("[STARTUP] Veritabanında kanal bulunamadı (Count: 0). İlk kurulum için GitHub listesi indiriliyor...");
                        var sync = new GitHubSyncEngine();
                        await sync.PullFromGitHubAsync();
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

            // 3. EPG Auto-Sync (Every 24h or if empty, update silently in background)
            Task.Run(async () => {
                try
                {
                    await Task.Delay(15000); // Wait 15s after startup so UI is fully responsive
                    var db = new DatabaseEngine();
                    int epgCount = await db.GetEpgProgramsCountAsync();
                    string lastUpdateStr = db.GetSetting("LastAutoEpgUpdate", "0");
                    long.TryParse(lastUpdateStr, out long lastUpdateUnix);
                    long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    bool isStale = (nowUnix - lastUpdateUnix) > 86400; // 24 hours

                    if (epgCount == 0 || isStale)
                    {
                        LogService.LogInfo($"[STARTUP] EPG senkronizasyonu başlatılıyor (Kayıt sayısı: {epgCount}, Durum: {(isStale ? "Bayat" : "Boş")})...");
                        var sources = db.GetEpgSources();
                        if (sources.Count == 0)
                        {
                            var defaultEpgs = new[] {
                                "https://iptv-epg.org/files/epg-tr.xml.gz",
                                "https://iptv-epg.org/files/epg-de.xml.gz",
                                "https://raw.githubusercontent.com/epgshare01/XMLTV/master/epg_ripper_TR1.xml.gz",
                                "https://raw.githubusercontent.com/epgshare01/XMLTV/master/epg_ripper_DE1.xml.gz"
                            };
                            foreach (var s in defaultEpgs) db.AddEpgSource(s);
                            sources = db.GetEpgSources();
                        }

                        var epgEng = new EpgEngine();
                        foreach (var src in sources)
                        {
                            try
                            {
                                await epgEng.LoadEpgAsync(src);
                            }
                            catch (Exception ex)
                            {
                                LogService.LogWarning($"[STARTUP] EPG yükleme hatası ({src}): {ex.Message}");
                            }
                        }

                        // Clean old programs (older than 1 day)
                        await db.CleanupOldEpgProgramsAsync(1);
                        db.SetSetting("LastAutoEpgUpdate", nowUnix.ToString());
                        LogService.LogInfo($"[STARTUP] EPG senkronizasyonu tamamlandı. Toplam {await db.GetEpgProgramsCountAsync()} program hazır.");
                        DatabaseEngine.NotifyDatabaseUpdated();
                    }
                    else
                    {
                        LogService.LogInfo($"[STARTUP] EPG verileri güncel ({epgCount} program hazır). Otomatik indirme atlandı.");
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("[STARTUP] Otomatik EPG arka plan hatası", ex);
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
