using System;
using System.Collections.Generic;
using System.Windows;
using StreamMesh.Models;
using StreamMesh.Core.Media;

namespace StreamMesh.UI.Windows
{
    public partial class MediaDetailsWindow : Window
    {
        public Channel Media { get; set; }
        public List<string> CastList { get; set; } = new List<string>();
        public List<Episode> Episodes { get; set; } = new List<Episode>();

        public string HeroImageUrl => !string.IsNullOrEmpty(Media?.BackdropUrl) ? Media.BackdropUrl : Media?.LogoUrl ?? "";
        public string OverviewText => !string.IsNullOrWhiteSpace(Media?.Overview) ? Media.Overview : "Bu içerik için açıklama bulunamadı veya henüz yüklenmedi.";

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
            if (Media != null)
            {
                await _meta.EnrichChannelAsync(Media);

                if (!string.IsNullOrEmpty(Media.Cast))
                {
                    CastList.Clear();
                    foreach (var c in Media.Cast.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string trimmed = c.Trim();
                        if (!string.IsNullOrEmpty(trimmed) && !CastList.Contains(trimmed)) CastList.Add(trimmed);
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    DataContext = null;
                    DataContext = this;
                });
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void PlayMain_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.LoadChannelToPlayer(Media);
            Close();
        }

        private void PlayTrailer_Click(object sender, RoutedEventArgs e)
        {
            string query = Media?.PrimaryName ?? Media?.Name ?? "";
            if (!string.IsNullOrEmpty(query))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}+fragman",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        private void Tag_Click(object sender, RoutedEventArgs e)
        {
        }
    }
}
