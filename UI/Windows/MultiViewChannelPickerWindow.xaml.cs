using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media;
using StreamMesh.Core.Plugins;
using StreamMesh.Models;

namespace StreamMesh.UI.Windows
{
    public class MultiViewChannelPickItem
    {
        public Channel Channel { get; set; } = new Channel();
        public string Name => Channel.PrimaryName;
        public string LogoUrl => Channel.LogoUrl;
        public string Subtitle { get; set; } = "";
        public string ServerBadge { get; set; } = "";
        public string GroupTitle => string.IsNullOrWhiteSpace(Channel.GroupTitle) ? "Genel" : Channel.GroupTitle;
        public bool IsVod { get; set; }
    }

    public partial class MultiViewChannelPickerWindow : Window
    {
        private readonly int _slotIndex;
        private readonly HashSet<string> _blockedServerKeys;
        private readonly bool _blockAceStream;
        private readonly List<MultiViewChannelPickItem> _eligibleItems = new();
        private bool _isInitialized;

        public Channel? SelectedChannel { get; private set; }

        public MultiViewChannelPickerWindow(int slotIndex, IReadOnlySet<string>? blockedServerKeys, bool blockAceStream)
        {
            InitializeComponent();
            _slotIndex = slotIndex;
            _blockedServerKeys = blockedServerKeys != null
                ? new HashSet<string>(blockedServerKeys, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _blockAceStream = blockAceStream;

            SlotBadgeText.Text = $"EKRAN {slotIndex + 1}";
            TitleText.Text = $"{slotIndex + 1}. Ekran İçin Kanal Seçin";

            UpdateConflictBanner();
            Loaded += async (_, _) => await LoadChannelsAsync();
        }

        private void UpdateConflictBanner()
        {
            var lockedLabels = new List<string>();
            if (_blockAceStream) lockedLabels.Add("AceStream P2P");
            foreach (var key in _blockedServerKeys)
            {
                if (!string.Equals(key, MultiViewConflictHelper.AceStreamServerKey, StringComparison.OrdinalIgnoreCase))
                    lockedLabels.Add(key);
            }

            if (lockedLabels.Count == 0)
            {
                ConflictStatusText.Text = "✔ Aktif sunucu kısıtı yok — tüm uygun kanallar listeleniyor.";
            }
            else
            {
                ConflictStatusText.Text = $"🔒 Diğer ekranlarda aktif olanlar gizlendi: {string.Join(", ", lockedLabels)}";
            }
        }

        private async Task LoadChannelsAsync()
        {
            EmptyStateText.Text = "Kanallar taranıyor ve çakışan sunucular filtreleniyor...";
            EmptyStateText.Visibility = Visibility.Visible;

            try
            {
                var authPlugin = PluginManager.Instance.GetPluginById("user_auth") as UserAuthPlugin;
                bool hideAdult = authPlugin is { IsEnabled: true } && authPlugin.GetBool("HideAdultContent");

                var allChannels = await DatabaseEngine.Instance.GetAllChannelsAsync().ConfigureAwait(false);

                var (eligible, groups, hiddenConflictCount) = await Task.Run(() =>
                {
                    var list = new List<MultiViewChannelPickItem>();
                    var groupSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    int skippedByConflict = 0;

                    foreach (var ch in allChannels)
                    {
                        if (ch == null || string.IsNullOrWhiteSpace(ch.Url)) continue;
                        if (hideAdult && ch.IsLocked) continue;

                        if (!MultiViewConflictHelper.HasAnyAvailableUrl(ch, _blockedServerKeys, _blockAceStream))
                        {
                            skippedByConflict++;
                            continue;
                        }

                        string serverLabel = MultiViewConflictHelper.GetPreferredAvailableServerLabel(ch, _blockedServerKeys, _blockAceStream);
                        if (string.IsNullOrWhiteSpace(serverLabel)) serverLabel = ch.SourceType;

                        string cat = (ch.Category ?? "TV").Trim();
                        string lowerCat = cat.ToLowerInvariant();
                        string lowerUrl = (ch.Url ?? "").ToLowerInvariant();
                        bool isVod = lowerCat.Contains("film") || lowerCat.Contains("dizi") ||
                                     lowerCat.Contains("movie") || lowerCat.Contains("series") ||
                                     lowerCat.Contains("vod") ||
                                     lowerUrl.EndsWith(".mp4") || lowerUrl.EndsWith(".mkv") || lowerUrl.EndsWith(".avi") ||
                                     lowerUrl.Contains("/movie/") || lowerUrl.Contains("/series/");

                        string grp = string.IsNullOrWhiteSpace(ch.GroupTitle) ? "Genel" : ch.GroupTitle.Trim();
                        int altCount = ch.GetOrderedUrlList().Count;
                        string altText = altCount > 1 ? $" • {altCount} kaynak" : "";

                        list.Add(new MultiViewChannelPickItem
                        {
                            Channel = ch,
                            ServerBadge = serverLabel,
                            Subtitle = $"{cat} • {grp}{altText}",
                            IsVod = isVod
                        });

                        if (!isVod) groupSet.Add(grp);
                    }

                    var sorted = list
                        .OrderByDescending(x => x.Channel.IsFavorite)
                        .ThenByDescending(x => x.Channel.PersonalWatchCount)
                        .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var orderedGroups = groupSet.OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
                    return (sorted, orderedGroups, skippedByConflict);
                }).ConfigureAwait(false);

                await Dispatcher.InvokeAsync(() =>
                {
                    _eligibleItems.Clear();
                    _eligibleItems.AddRange(eligible);

                    GroupFilterCombo.Items.Clear();
                    GroupFilterCombo.Items.Add("Tüm Gruplar");
                    foreach (var g in groups) GroupFilterCombo.Items.Add(g);
                    GroupFilterCombo.SelectedIndex = 0;

                    _isInitialized = true;
                    ApplyFilter(hiddenConflictCount);
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    EmptyStateText.Text = $"Kanal listesi yüklenemedi: {ex.Message}";
                    EmptyStateText.Visibility = Visibility.Visible;
                });
            }
        }

        private void FilterChanged(object sender, EventArgs e)
        {
            if (SearchPlaceholder != null && SearchBox != null)
            {
                SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            if (_isInitialized) ApplyFilter();
        }

        private void ApplyFilter(int? hiddenByConflict = null)
        {
            string query = (SearchBox?.Text ?? "").Trim();
            string selectedGroup = GroupFilterCombo?.SelectedItem as string ?? "Tüm Gruplar";
            bool includeVod = IncludeVodCheck?.IsChecked == true;

            IEnumerable<MultiViewChannelPickItem> filtered = _eligibleItems;
            if (!includeVod)
                filtered = filtered.Where(x => !x.IsVod);

            if (!string.IsNullOrWhiteSpace(selectedGroup) && selectedGroup != "Tüm Gruplar")
                filtered = filtered.Where(x => string.Equals(x.GroupTitle, selectedGroup, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(x =>
                    x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.GroupTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.ServerBadge.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            var finalList = filtered.ToList();
            ChannelListBox.ItemsSource = finalList;

            if (finalList.Count == 0)
            {
                EmptyStateText.Text = "Seçili filtrelere uygun ve çakışmayan kanal bulunamadı.";
                EmptyStateText.Visibility = Visibility.Visible;
            }
            else
            {
                EmptyStateText.Visibility = Visibility.Collapsed;
            }

            string conflictNote = hiddenByConflict is > 0 ? $" ({hiddenByConflict:N0} kanal aynı sunucu/AceStream kısıtından gizlendi)" : "";
            CountSummaryText.Text = $"{finalList.Count:N0} uygun kanal listelendi{conflictNote}";
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }

        private void ChannelListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectBtn.IsEnabled = ChannelListBox.SelectedItem is MultiViewChannelPickItem;
        }

        private void ChannelListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ChannelListBox.SelectedItem is MultiViewChannelPickItem item)
            {
                SelectedChannel = item.Channel;
                DialogResult = true;
            }
        }

        private void Select_Click(object sender, RoutedEventArgs e)
        {
            if (ChannelListBox.SelectedItem is MultiViewChannelPickItem item)
            {
                SelectedChannel = item.Channel;
                DialogResult = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
