using System;
using System.Threading.Tasks;
using System.Windows;
using StreamMesh.Services;

namespace StreamMesh.Windows
{
    public partial class MissingComponentsWindow : Window
    {
        public MissingComponentsWindow()
        {
            InitializeComponent();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void DownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            DownloadBtn.IsEnabled = false;
            CancelBtn.IsEnabled = false;
            
            // AceStream.zip link directly assigned here just like in Settings
            string githubAceStreamUrl = "https://github.com/bilo1975tr/sm/releases/download/v1.0/AceStream.zip";

            await Task.Run(async () =>
            {
                await InventoryService.DownloadComponentsManuallyAsync(githubAceStreamUrl, (message) => 
                {
                    Dispatcher.Invoke(() => StatusText.Text = message);
                });
            });

            Dispatcher.Invoke(() => 
            {
                MessageBox.Show("Eksik bileşenler başarıyla kuruldu!", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Close();
            });
        }
    }
}
