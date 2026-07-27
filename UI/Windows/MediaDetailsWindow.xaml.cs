using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using StreamMesh.Models;
using StreamMesh.Core.Media;

namespace StreamMesh.UI.Windows
{
    public partial class MediaDetailsWindow : Window
    {
        public Channel Media { get; set; }
        public List<string> CastList { get; set; } = new List<string>();
        public List<Episode> Episodes { get; set; } = new List<Episode>();

        private readonly MetadataEngine _meta = new MetadataEngine();

        public MediaDetailsWindow(Channel media)
        {
            InitializeComponent();
            Media = media;
            DataContext = this;

            LoadRealMetadata();
        }

        private async void LoadRealMetadata()
        {
            await _meta.EnrichChannelAsync(Media);

            if (!string.IsNullOrEmpty(Media.Cast))
            {
                CastList.Clear();
                CastList.AddRange(Media.Cast.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            }

            // Logic for Episodes if Series...
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void PlayMain_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.LoadChannelToPlayer(Media);
            Close();
        }

        private void PlayTrailer_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(Media.ImdbId))
            {
                // Logic to play trailer (e.g. open browser)
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(Media.Name)}+trailer", UseShellExecute = true }); } catch { }
            }
        }

        private void Tag_Click(object sender, RoutedEventArgs e)
        {
        }
    }
}
