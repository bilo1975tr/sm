using System.Windows;
using LibVLCSharp.Shared;

namespace StreamMesh
{
    public partial class App : Application
    {
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
            base.OnStartup(e);

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

            // Start P2P Mesh Network
            _ = StreamMesh.Services.P2P.P2pService.StartAsync();

            // Start GitHub Sync
            StreamMesh.Services.GitHubSyncService.Start();

            // Show MainWindow
            var mainWindow = new MainWindow();
            Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            this.MainWindow = mainWindow;
            mainWindow.Show();
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
