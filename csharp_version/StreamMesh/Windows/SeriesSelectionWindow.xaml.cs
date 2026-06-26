using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using StreamMesh.Models;
using StreamMesh.Services;

namespace StreamMesh.Windows
{
    public partial class SeriesSelectionWindow : Window
    {
        private readonly Channel _seriesGroup;
        private readonly List<Channel> _episodes;
        private readonly DatabaseService _db;
        private int _selectedSeason = 1;
        private Channel _resumeEpisode = null;

        public Channel SelectedEpisode { get; private set; }

        public SeriesSelectionWindow(Channel seriesGroup)
        {
            InitializeComponent();
            _seriesGroup = seriesGroup;
            _episodes = seriesGroup.SeriesEpisodes ?? new List<Channel>();
            _db = new DatabaseService();

            LoadSeriesMetadata();
            LoadSeasons();
            LoadResumeWatching();
        }

        private void LoadSeriesMetadata()
        {
            SeriesTitleText.Text = _seriesGroup.SeriesName;
            SeriesSeasonsCountText.Text = $"{_seriesGroup.TotalSeasonsCount} Sezon";
            SeriesEpisodesCountText.Text = $"{_seriesGroup.TotalEpisodesCount} Bölüm";
            SeriesLanguageText.Text = _seriesGroup.Language ?? "Türkçe";
        }

        private void LoadSeasons()
        {
            SeasonsContainer.Children.Clear();

            // Tüm sezon numaralarını tekil ve sıralı olarak al
            var seasons = _episodes
                .Select(e => Channel.ParseSeriesDetails(e.Name, e.Url).Season)
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            if (seasons.Count == 0) seasons.Add(1);

            _selectedSeason = seasons[0];

            foreach (var seasonNum in seasons)
            {
                var btn = new ToggleButton
                {
                    Content = $"{seasonNum}. Sezon",
                    Style = (Style)FindResource("SeasonButtonStyle"),
                    IsChecked = (seasonNum == _selectedSeason),
                    Tag = seasonNum
                };

                btn.Click += (s, e) =>
                {
                    // Diğerlerini uncheck yap
                    foreach (ToggleButton other in SeasonsContainer.Children)
                    {
                        if (other != btn) other.IsChecked = false;
                    }
                    
                    btn.IsChecked = true;
                    _selectedSeason = (int)btn.Tag;
                    LoadEpisodes(_selectedSeason);
                };

                SeasonsContainer.Children.Add(btn);
            }

            LoadEpisodes(_selectedSeason);
        }

        private void LoadResumeWatching()
        {
            try
            {
                Channel bestEp = null;
                WatchProgress bestWp = null;

                foreach (var ep in _episodes)
                {
                    var wp = _db.GetWatchProgress(ep.Id);
                    if (wp != null && wp.Seconds > 0 && wp.Duration > 0)
                    {
                        if (bestWp == null || wp.LastWatched > bestWp.LastWatched)
                        {
                            bestWp = wp;
                            bestEp = ep;
                        }
                    }
                }

                if (bestEp != null && bestWp != null)
                {
                    _resumeEpisode = bestEp;
                    var det = Channel.ParseSeriesDetails(bestEp.Name, bestEp.Url);

                    ResumeEpisodeNameText.Text = $"{_seriesGroup.SeriesName} - {det.Season}. Sezon {det.Episode}. Bölüm";
                    
                    var timeSpanCur = TimeSpan.FromMilliseconds(bestWp.Seconds);
                    var timeSpanTotal = TimeSpan.FromMilliseconds(bestWp.Duration);
                    ResumeTimeText.Text = $"{timeSpanCur:mm\\:ss} / {timeSpanTotal:mm\\:ss}";

                    double percentage = ((double)bestWp.Seconds / bestWp.Duration) * 100;
                    ResumePercentText.Text = $"%{percentage:F0}";

                    ResumeWatchingPanel.Visibility = Visibility.Visible;
                }
                else
                {
                    ResumeWatchingPanel.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("LoadResumeWatching failed", ex);
                ResumeWatchingPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadEpisodes(int season)
        {
            EpisodesContainer.Children.Clear();
            var filtered = _episodes.Where(e =>
            {
                var det = Channel.ParseSeriesDetails(e.Name, e.Url);
                return det.Season == season;
            }).OrderBy(e =>
            {
                var det = Channel.ParseSeriesDetails(e.Name, e.Url);
                return det.Episode;
            }).ToList();

            foreach (var ep in filtered)
            {
                var det = Channel.ParseSeriesDetails(ep.Name, ep.Url);

                // Card Border
                var card = new Border
                {
                    Width = 160,
                    Height = 110,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1e293b")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 0, 10, 10),
                    Cursor = Cursors.Hand,
                    Tag = ep
                };

                // Mouse over effect
                card.MouseEnter += (s, ev) =>
                {
                    card.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                    card.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38bdf8"));
                };
                card.MouseLeave += (s, ev) =>
                {
                    card.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1e293b"));
                    card.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                };

                // Click to play
                card.MouseLeftButtonUp += (s, ev) =>
                {
                    SelectedEpisode = ep;
                    DialogResult = true;
                };

                var grid = new Grid { Margin = new Thickness(10) };
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // Play icon & Episode Title
                var playIcon = new TextBlock
                {
                    Text = "▶ " + string.Format("{0}. Bölüm", det.Episode),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetRow(playIcon, 0);
                grid.Children.Add(playIcon);

                // Watch Progress info
                var wp = _db.GetWatchProgress(ep.Id);
                if (wp != null && wp.Seconds > 0 && wp.Duration > 0)
                {
                    var timeSpanCur = TimeSpan.FromMilliseconds(wp.Seconds);
                    var timeSpanTotal = TimeSpan.FromMilliseconds(wp.Duration);

                    var progressText = new TextBlock
                    {
                        Text = string.Format("{0:mm\\:ss} / {1:mm\\:ss}", timeSpanCur, timeSpanTotal),
                        FontSize = 10,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38bdf8")),
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 4, 0, 4)
                    };
                    Grid.SetRow(progressText, 1);
                    grid.Children.Add(progressText);

                    // Progress Bar
                    var pb = new ProgressBar
                    {
                        Height = 4,
                        Maximum = wp.Duration,
                        Value = wp.Seconds,
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0f172a")),
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38bdf8")),
                        BorderThickness = new Thickness(0),
                        Margin = new Thickness(0, 2, 0, 0)
                    };
                    Grid.SetRow(pb, 2);
                    grid.Children.Add(pb);
                }
                else
                {
                    var notWatchedText = new TextBlock
                    {
                        Text = "İzlenmedi",
                        FontSize = 10,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")),
                        Margin = new Thickness(0, 4, 0, 0)
                    };
                    Grid.SetRow(notWatchedText, 1);
                    grid.Children.Add(notWatchedText);
                }

                card.Child = grid;
                EpisodesContainer.Children.Add(card);
            }
        }

        private void ResumeWatchingBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_resumeEpisode != null)
            {
                SelectedEpisode = _resumeEpisode;
                DialogResult = true;
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
