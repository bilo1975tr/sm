using System;
using System.Windows;

namespace StreamMesh.UI.Windows
{
    public partial class UpdateProgressWindow : Window
    {
        public UpdateProgressWindow(string targetVersion)
        {
            InitializeComponent();
            VersionText.Text = $"v{targetVersion} sürümü indiriliyor ve hazırlanıyor...";
        }

        public void UpdateProgress(int percent, string message, string? detail = null)
        {
            Dispatcher.Invoke(() =>
            {
                DownloadProgressBar.Value = Math.Clamp(percent, 0, 100);
                PercentText.Text = $"%{Math.Clamp(percent, 0, 100)}";
                StatusMessageText.Text = message;
                if (!string.IsNullOrEmpty(detail))
                {
                    DetailText.Text = detail;
                }
            });
        }

        public void ShowError(string errorMessage)
        {
            Dispatcher.Invoke(() =>
            {
                StatusMessageText.Text = "Hata: " + errorMessage;
                StatusMessageText.Foreground = System.Windows.Media.Brushes.Tomato;
                DetailText.Text = "Güncelleme tamamlanamadı. Lütfen internet bağlantınızı kontrol edin.";
                CloseButton.Visibility = Visibility.Visible;
            });
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
