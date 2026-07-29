using System.Windows;
using StreamMesh.Models;
using StreamMesh.Core.Database;

namespace StreamMesh.UI.Windows
{
    public partial class SeriesDetailsWindow : Window
    {
        public SeriesGroup Series { get; set; }
        private readonly DatabaseEngine _db = new DatabaseEngine();

        public SeriesDetailsWindow(SeriesGroup series)
        {
            InitializeComponent();
            Series = series;
            this.DataContext = this;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void PlayEpisode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.CommandParameter is Channel episode)
            {
                MainWindow.Instance?.LoadChannelToPlayer(episode);
                this.Close();
            }
        }

        private async void Watched_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox cb && cb.DataContext is Channel episode)
            {
                episode.IsWatched = cb.IsChecked == true;
                await _db.SaveChannelAsync(episode);
            }
        }
    }
}
