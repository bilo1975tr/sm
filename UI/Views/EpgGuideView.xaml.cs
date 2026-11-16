using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media;
using StreamMesh.Core.Media.Iptv;
using StreamMesh.Core.Utils;
using StreamMesh.Models;
using StreamMesh.UI.Windows;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace StreamMesh.UI.Views
{
    public class EpgGuideProgramTile
    {
        public Channel Channel { get; set; } = null!;
        public EpgProgram Program { get; set; } = null!;
        public bool IsLive { get; set; }
        public bool IsPast { get; set; }
        public bool CanCatchup { get; set; }
        public string TimeRange => $"{Program.StartTime:HH:mm} - {Program.EndTime:HH:mm}";
        public string StatusLabel => IsLive ? "🔴 CANLI" : (IsPast ? (CanCatchup ? "⏮️ İZLE" : "BİTTİ") : "YAKINDA");
        public Brush StatusColor => IsLive ? Brushes.LightCoral : (CanCatchup ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : Brushes.LightGray);
        public Brush CardBackground => IsLive
            ? new SolidColorBrush(Color.FromRgb(12, 74, 110))
            : (CanCatchup ? new SolidColorBrush(Color.FromRgb(17, 54, 52)) : new SolidColorBrush(Color.FromRgb(15, 23, 42)));
        public Brush CardBorder => IsLive
            ? new SolidColorBrush(Color.FromRgb(56, 189, 248))
            : (CanCatchup ? new SolidColorBrush(Color.FromRgb(20, 184, 166)) : new SolidColorBrush(Color.FromRgb(51, 65, 85)));
    }

    public class EpgGuideRow
    {
        public Channel Channel { get; set; } = null!;
        public List<EpgGuideProgramTile> Programs { get; set; } = new();
    }

    public partial class EpgGuideView : System.Windows.Controls.UserControl
    {
        private readonly DatabaseEngine _db = DatabaseEngine.Instance;
        private List<EpgGuideRow> _allRows = new();
        private bool _loadedOnce = false;

        public EpgGuideView()
        {
            InitializeComponent();
            this.Loaded += async (s, e) =>
            {
                if (!_loadedOnce)
                {
                    _loadedOnce = true;
                    await LoadGuideAsync();
                }
            };
        }

        private (DateTime start, DateTime end) GetSelectedWindow()
        {
            DateTime now = DateTime.Now;
            string tag = (WindowRangeCombo?.SelectedItem as ComboBoxItem)?.Tag as string ?? "live";
            return tag switch
            {
                "yesterday" => (now.AddHours(-24), now),
                "upcoming" => (now, now.AddHours(24)),
                _ => (now.AddHours(-4), now.AddHours(8))
            };
        }

        public async Task LoadGuideAsync()
        {
            try
            {
                if (RefreshGuideBtn != null) RefreshGuideBtn.IsEnabled = false;
                var (start, end) = GetSelectedWindow();
                DateTime now = DateTime.Now;

                var channelsTask = _db.GetAllChannelsAsync();
                var programsTask = _db.GetEpgProgramsInRangeAsync(start, end);
                await Task.WhenAll(channelsTask, programsTask);

                var channels = (await channelsTask)
                    .Where(c => string.Equals(c.Category, "TV", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(c.Category))
                    .Take(300)
                    .ToList();
                var programs = await programsTask;

                var progLookup = new Dictionary<string, List<EpgProgram>>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in programs)
                {
                    string key = ChannelUtils.NormalizeForMatch(p.ChannelName);
                    if (string.IsNullOrEmpty(key)) continue;
                    if (!progLookup.TryGetValue(key, out var list))
                    {
                        list = new List<EpgProgram>();
                        progLookup[key] = list;
                    }
                    list.Add(p);
                }

                var rows = new List<EpgGuideRow>();
                foreach (var ch in channels)
                {
                    var matched = new List<EpgProgram>();
                    foreach (var idCandidate in ch.GetEpgIdList())
                    {
                        string k = ChannelUtils.NormalizeForMatch(idCandidate);
                        if (!string.IsNullOrEmpty(k) && progLookup.TryGetValue(k, out var list))
                        {
                            matched.AddRange(list);
                        }
                    }

                    if (matched.Count == 0)
                    {
                        string nameKey = ChannelUtils.NormalizeForMatch(ch.CleanName ?? ch.PrimaryName);
                        if (!string.IsNullOrEmpty(nameKey) && progLookup.TryGetValue(nameKey, out var list))
                        {
                            matched.AddRange(list);
                        }
                    }

                    if (matched.Count == 0) continue;

                    var tiles = matched
                        .OrderBy(p => p.StartTime)
                        .GroupBy(p => p.StartTime)
                        .Select(g => g.First())
                        .Take(16)
                        .Select(p =>
                        {
                            bool isLive = now >= p.StartTime && now <= p.EndTime;
                            bool isPast = p.EndTime < now;
                            return new EpgGuideProgramTile
                            {
                                Channel = ch,
                                Program = p,
                                IsLive = isLive,
                                IsPast = isPast,
                                CanCatchup = isPast && XtreamCatchupHelper.CanCatchup(ch, p)
                            };
                        })
                        .ToList();

                    if (tiles.Count > 0)
                    {
                        rows.Add(new EpgGuideRow { Channel = ch, Programs = tiles });
                    }
                }

                _allRows = rows;
                ApplySearchFilter();
                if (GuideSubtitleText != null)
                {
                    GuideSubtitleText.Text = $"{rows.Count} kanal için {start:dd.MM HH:mm} - {end:dd.MM HH:mm} yayın akışı gösteriliyor";
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("[EpgGuideView] LoadGuideAsync error", ex);
            }
            finally
            {
                if (RefreshGuideBtn != null) RefreshGuideBtn.IsEnabled = true;
            }
        }

        private void ApplySearchFilter()
        {
            if (EpgRowsList == null) return;
            string q = (GuideSearchBox?.Text ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(q))
            {
                EpgRowsList.ItemsSource = _allRows;
                return;
            }

            EpgRowsList.ItemsSource = _allRows.Where(r =>
                (r.Channel.PrimaryName?.ToLowerInvariant().Contains(q) == true) ||
                r.Programs.Any(p => p.Program.Title?.ToLowerInvariant().Contains(q) == true)).ToList();
        }

        private void GuideSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplySearchFilter();

        private void WindowRangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadedOnce) _ = LoadGuideAsync();
        }

        private async void RefreshGuideBtn_Click(object sender, RoutedEventArgs e)
        {
            await LoadGuideAsync();
        }

        private void ChannelHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Channel ch)
            {
                MainWindow.Instance?.LoadChannelToPlayer(ch);
            }
        }

        private void ProgramTile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is EpgGuideProgramTile tile)
            {
                if (tile.IsPast)
                {
                    if (tile.CanCatchup)
                    {
                        MainWindow.Instance?.PlayCatchup(tile.Channel, tile.Program);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Bu kanalın sağlayıcısı geçmiş yayın (Catch-up / Timeshift) desteği sunmuyor.", "Geçmiş Yayın", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return;
                }

                MainWindow.Instance?.LoadChannelToPlayer(tile.Channel);
            }
        }
    }
}
