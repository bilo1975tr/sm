using System.Windows;

namespace StreamMesh.UI.Windows
{
    public partial class DonationWindow : Window
    {
        public DonationWindow()
        {
            InitializeComponent();
        }

        private void CopyCrypto_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetText(CryptoBox.Text);
                System.Windows.MessageBox.Show("USDT (BEP20) cüzdan adresi panoya kopyalandı!", "Kopyalandı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
