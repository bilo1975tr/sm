using System.Windows;
using System.Windows.Controls;

namespace StreamMesh.Windows
{
    public partial class YoutubeMetadataWindow : Window
    {
        public bool IsConfirmed { get; private set; }
        
        public string SelectedType { get; private set; } // Normal, Movie, Series
        public string GroupTitle { get; private set; }
        public string StreamLanguage { get; private set; }
        
        public bool AutoNumbering { get; private set; }
        public int StartSeason { get; private set; }
        public int StartEpisode { get; private set; }

        public YoutubeMetadataWindow()
        {
            InitializeComponent();
        }

        private void ContentTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NormalPanel == null) return; // Prevent NullReference during initialization
            
            NormalPanel.Visibility = Visibility.Collapsed;
            MoviePanel.Visibility = Visibility.Collapsed;
            SeriesPanel.Visibility = Visibility.Collapsed;

            int index = ContentTypeCombo.SelectedIndex;
            if (index == 0) NormalPanel.Visibility = Visibility.Visible;
            else if (index == 1) MoviePanel.Visibility = Visibility.Visible;
            else if (index == 2) SeriesPanel.Visibility = Visibility.Visible;
        }

        private void ConfirmBtn_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = true;
            
            int index = ContentTypeCombo.SelectedIndex;
            if (index == 0) // Normal/LiveTV
            {
                SelectedType = "Normal";
                GroupTitle = NormalGroupBox.Text.Trim();
                StreamLanguage = NormalLanguageBox.Text.Trim();
            }
            else if (index == 1) // Movie
            {
                SelectedType = "Movie";
                GroupTitle = MovieGroupBox.Text.Trim();
                StreamLanguage = MovieLanguageBox.Text.Trim();
            }
            else if (index == 2) // Series
            {
                SelectedType = "Series";
                // For series, typically we use Series Name as Group Title for grouping
                GroupTitle = SeriesNameBox.Text.Trim(); 
                StreamLanguage = SeriesLanguageBox.Text.Trim();
                AutoNumbering = AutoSeasonEpisodeCheck.IsChecked == true;
                
                int.TryParse(SeasonBox.Text, out int s);
                int.TryParse(EpisodeBox.Text, out int ep);
                StartSeason = s > 0 ? s : 1;
                StartEpisode = ep > 0 ? ep : 1;
            }

            this.DialogResult = true;
            this.Close();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            this.DialogResult = false;
            this.Close();
        }
    }
}
