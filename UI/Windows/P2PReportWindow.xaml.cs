using System;
using System.Windows;
using StreamMesh.Core.Network;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;

namespace StreamMesh.UI.Windows
{
    public partial class P2PReportWindow : Window
    {
        private readonly StunEngine _stun = new StunEngine();

        public P2PReportWindow() : this("StreamMesh P2P Durum Raporu", "")
        {
        }

        public P2PReportWindow(string title, string reportText)
        {
            InitializeComponent();
            TxtTitle.Text = title;
            TxtReport.Text = string.IsNullOrWhiteSpace(reportText) 
                ? _stun.GenerateDiagnosticReport() 
                : reportText;
        }

        private async void RetestStun_Click(object sender, RoutedEventArgs e)
        {
            TxtReport.Text = "STUN sunucuları taranıyor (stun.l.google.com:19302)...\nLütfen bekleyin...";
            await _stun.ResolvePublicEndpointAsync();
            await _stun.GetOnlinePeerCountAsync();
            TxtReport.Text = _stun.GenerateDiagnosticReport();
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(TxtReport.Text);
                MessageBox.Show("Rapor panoya kopyalandı.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
