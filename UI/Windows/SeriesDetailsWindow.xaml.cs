using System;
using System.Collections.Generic;
using System.Windows;
using StreamMesh.Models;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media;

namespace StreamMesh.UI.Windows
{
    public partial class SeriesDetailsWindow : Window
    {
        public SeriesGroup Series { get; set; }
        public List<string> CastList { get; set; } = new List<string>();
        private readonly DatabaseEngine _db = new DatabaseEngine();
        private readonly MetadataEngine _meta = new MetadataEngine();

        public SeriesDetailsWindow(SeriesGroup series)
        {
            InitializeComponent();
            Series = series;
            this.DataContext = this;

            LoadRealMetadata();
        }

        private async void LoadRealMetadata()
        {
            if (Series != null)
            {
                await _meta.EnrichChannelAsync(Series);

                if (!string.IsNullOrEmpty(Series.Cast))
                {
                    CastList.Clear();
                    CastList.AddRange(Series.Cast.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                }

                Dispatcher.Invoke(() =>
                {
                    DataContext = null;
                    DataContext = this;
                });
            }
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
