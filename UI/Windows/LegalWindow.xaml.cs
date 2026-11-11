using System.Windows;

namespace StreamMesh.UI.Windows
{
    public partial class LegalWindow : Window
    {
        public bool Accepted { get; private set; } = false;

        public LegalWindow()
        {
            InitializeComponent();
        }

        private void Accept_Click(object sender, RoutedEventArgs e)
        {
            Accepted = true;
            this.Close();
        }

        private void Decline_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
