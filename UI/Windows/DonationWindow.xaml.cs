using System.Windows;

namespace StreamMesh.UI.Windows
{
    public partial class DonationWindow : Window
    {
        public DonationWindow()
        {
            InitializeComponent();
        }

        private void CopyIban_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetText(IbanBox.Text);
                System.Windows.MessageBox.Show("IBAN adresi panoya kopyalandı!", "Kopyalandı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        private void CopyCrypto_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetText(CryptoBox.Text);
                System.Windows.MessageBox.Show("Kripto cüzdan adresi panoya kopyalandı!", "Kopyalandı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
