using System.Windows;
using System.Threading;
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

                // Legal Window Check
                var legalWindow = new StreamMesh.Windows.LegalWindow();
                legalWindow.ShowDialog();

                if (!legalWindow.Accepted)
                {
                    MessageBox.Show("Uygulama kullanım şartlarını kabul etmediğiniz için kapatılıyor.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                    Application.Current.Shutdown();
                    return;
                }

                // Auto Login Check
                bool loggedIn = StreamMesh.Services.P2P.UserService.AutoLogin();
                if (!loggedIn)
                {
                    var loginWindow = new StreamMesh.Windows.LoginWindow();
                    loginWindow.ShowDialog();

                    if (!loginWindow.IsLoggedIn)
                    {
                        Application.Current.Shutdown();
                        return;
                    }
                }

                // Gelişmiş dil yükleme (AutoLogin veya LoginWindow sonrası)
                var profile = StreamMesh.Services.P2P.UserService.GetProfile();
                if (profile != null && !string.IsNullOrEmpty(profile.AppLanguage))
                {
                    StreamMesh.Services.LocalizationManager.Instance.LoadTranslations(profile.AppLanguage);
                }

                // Start Local Server
                StreamMesh.Services.ServerService.Instance.StartServer();

                // Start GitHub Sync
                StreamMesh.Services.GitHubSyncService.Start();

                // Show MainWindow
                var mainWindow = new MainWindow();
                Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
                this.MainWindow = mainWindow;
                mainWindow.Show();
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
            string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fatal_error.log");
            string message = $"[{DateTime.Now}] CRASH: {ex.Message}\nStack: {ex.StackTrace}\n\n";
            System.IO.File.AppendAllText(logPath, message);
            MessageBox.Show($"Uygulama başlatılırken kritik bir hata oluştu. Log dosyasına bakınız: fatal_error.log\n\nHata: {ex.Message}", "Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
