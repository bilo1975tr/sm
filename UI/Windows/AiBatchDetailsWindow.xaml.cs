using System.Windows;
using StreamMesh.Models;

namespace StreamMesh.UI.Windows
{
    public partial class AiBatchDetailsWindow : Window
    {
        public AiBatchDetailsWindow(AiBatchReportItem reportItem)
        {
            InitializeComponent();
            if (reportItem != null)
            {
                ReportSubtitleText.Text = reportItem.DisplaySummary;
                ChangesGrid.ItemsSource = reportItem.Records;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
