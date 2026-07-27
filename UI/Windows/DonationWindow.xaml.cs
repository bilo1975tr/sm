using System.Windows;

namespace StreamMesh.UI.Windows
{
    public partial class DonationWindow : Window
    {
        public DonationWindow()
        {
            InitializeComponent();
        }
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
