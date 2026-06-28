using System;
using System.Windows;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace StreamMesh
{
    public partial class App : Application
    {
        private static Mutex _mutex = null;

        public App()
        {
            // Error handling
            AppDomain.CurrentDomain.UnhandledException += (s, args) => LogFatalError(args.ExceptionObject as Exception);
            DispatcherUnhandledException += (s, args) => {
                LogFatalError(args.Exception);
                args.Handled = true;
            };
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "StreamMeshApp_SingleInstance_Mutex";
            bool createdNew;

            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                // App is already running. Exit this instance.
                MessageBox.Show("Uygulama zaten farklı bir pencerede veya sistem tepsisinde çalışıyor.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                Application.Current.Shutdown();
                return;
            }

            base.OnStartup(e);

            try
            {
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                // 1. Splash Screen Göster ve Başlangıç Profilerını Başlat
                var splash = new StreamMesh.Windows.SplashWindow();
                splash.Show();
                splash.SetStatus("Sistem gereksinimleri kontrol ediliyor...", 10);

                var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var stepStopwatch = System.Diagnostics.Stopwatch.StartNew();

                // Legal Window Check
                splash.SetStatus("Kullanıcı sözleşmesi kontrol ediliyor...", 25);
                splash.Hide();
                var legalWindow = new StreamMesh.Windows.LegalWindow();
                stepStopwatch.Restart();
                legalWindow.ShowDialog();
                stepStopwatch.Stop();
                StreamMesh.Services.LogService.Log($"[StartupProfiler] LegalWindow.ShowDialog took {stepStopwatch.ElapsedMilliseconds} ms.");

                if (!legalWindow.Accepted)
                {
                    MessageBox.Show("Uygulama kullanım şartlarını kabul etmediğiniz için kapatılıyor.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                    Application.Current.Shutdown();
                    return;
                }
                splash.Show();

                // Auto Login Check
                splash.SetStatus("Kullanıcı profili ve lisans doğrulanıyor...", 45);
                stepStopwatch.Restart();
                bool loggedIn = StreamMesh.Services.P2P.UserService.AutoLogin();
                stepStopwatch.Stop();
                StreamMesh.Services.LogService.Log($"[StartupProfiler] UserService.AutoLogin took {stepStopwatch.ElapsedMilliseconds} ms.");
                if (!loggedIn)
                {
                    splash.Hide();
                    var loginWindow = new StreamMesh.Windows.LoginWindow();
                    stepStopwatch.Restart();
                    loginWindow.ShowDialog();
                    stepStopwatch.Stop();
                    StreamMesh.Services.LogService.Log($"[StartupProfiler] LoginWindow.ShowDialog took {stepStopwatch.ElapsedMilliseconds} ms.");

                    if (!loginWindow.IsLoggedIn)
                    {
                        Application.Current.Shutdown();
                        return;
                    }
                    splash.Show();
                }

                // Gelişmiş dil yükleme (AutoLogin veya LoginWindow sonrası)
                splash.SetStatus("Dil paketleri ve arayüz yükleniyor...", 65);
                stepStopwatch.Restart();
                var profile = StreamMesh.Services.P2P.UserService.GetProfile();
                if (profile != null && !string.IsNullOrEmpty(profile.AppLanguage))
                {
                    StreamMesh.Services.LocalizationManager.Instance.LoadTranslations(profile.AppLanguage);
                }
                stepStopwatch.Stop();
                StreamMesh.Services.LogService.Log($"[StartupProfiler] LocalizationManager initialization took {stepStopwatch.ElapsedMilliseconds} ms.");

                // Start Local Server
                splash.SetStatus("Lokal medya sunucusu başlatılıyor...", 80);
                stepStopwatch.Restart();
                StreamMesh.Services.ServerService.Instance.StartServer();
                stepStopwatch.Stop();
                StreamMesh.Services.LogService.Log($"[StartupProfiler] ServerService.Instance.StartServer took {stepStopwatch.ElapsedMilliseconds} ms.");

                // 5. Start GitHub Sync ve Firebase Queue (Asenkron arka plana alıyoruz)
                splash.SetStatus("Arka plan servisleri kuruluyor...", 90);
                stepStopwatch.Restart();
                _ = Task.Run(() =>
                {
                    try
                    {
                        var bgStopwatch = System.Diagnostics.Stopwatch.StartNew();
                        StreamMesh.Services.GitHubSyncService.Start();

                        StreamMesh.Services.FirebaseQueueService.Instance.Start();
                        bgStopwatch.Stop();
                        StreamMesh.Services.LogService.Log($"[StartupProfiler] Background services (GitHub Sync, Firebase Queue) initialized in {bgStopwatch.ElapsedMilliseconds} ms.");
                    }
                    catch (Exception bgEx)
                    {
                        StreamMesh.Services.LogService.LogError("Background startup services failed", bgEx);
                    }
                });
                stepStopwatch.Stop();
                StreamMesh.Services.LogService.Log($"[StartupProfiler] Background tasks dispatching took {stepStopwatch.ElapsedMilliseconds} ms.");

                // Show MainWindow
                splash.SetStatus("Arayüz hazırlanıyor...", 100);
                stepStopwatch.Restart();
                var mainWindow = new MainWindow();
                Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
                this.MainWindow = mainWindow;
                mainWindow.Show();
                stepStopwatch.Stop();
                StreamMesh.Services.LogService.Log($"[StartupProfiler] MainWindow creation and show took {stepStopwatch.ElapsedMilliseconds} ms.");

                totalStopwatch.Stop();
                StreamMesh.Services.LogService.Log($"[StartupProfiler] TOTAL APP STARTUP took {totalStopwatch.ElapsedMilliseconds} ms.");

                // Splash'i kapat
                splash.Close();
            }
            catch (Exception ex)
            {
                LogFatalError(ex);
                Application.Current.Shutdown();
            }
        }

        private void LogFatalError(Exception ex)
        {
            if (ex == null) return;
            try
            {
                StreamMesh.Services.LogService.LogError("App Unhandled/Fatal Exception", ex);
            }
            catch { }
            string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fatal_error.log");
            string message = $"[{DateTime.Now}] CRASH: {ex.Message}\nStack: {ex.StackTrace}\n\n";
            System.IO.File.AppendAllText(logPath, message);
            MessageBox.Show("İşlem sırasında beklenmeyen bir hata oluştu. Lütfen tekrar deneyiniz.", "Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
