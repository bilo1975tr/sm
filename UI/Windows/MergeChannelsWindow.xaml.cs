using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using StreamMesh.Models;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media;
using StreamMesh.Core.Utils;

namespace StreamMesh.UI.Windows
{
    public partial class MergeChannelsWindow : Window, INotifyPropertyChanged
    {
        private readonly Channel _mainChannel;
        private readonly DatabaseEngine _db = new DatabaseEngine();
        private List<Channel> _allChannels = new List<Channel>();

        public Channel MainChannel => _mainChannel;

        public string MainChannelLanguageDisplay
        {
            get
            {
                string lang = _mainChannel.Language ?? "und";
                if (lang.Equals("tr", StringComparison.OrdinalIgnoreCase)) return "TR - Türkçe";
                if (lang.Equals("de", StringComparison.OrdinalIgnoreCase)) return "DE - Almanca";
                if (lang.Equals("en", StringComparison.OrdinalIgnoreCase)) return "EN - İngilizce";
                if (lang.Equals("fr", StringComparison.OrdinalIgnoreCase)) return "FR - Fransızca";
                return string.IsNullOrWhiteSpace(lang) || lang == "und" ? "Bilinmeyen Dil" : lang.ToUpperInvariant();
            }
        }

        public string MainChannelSourceCountText
        {
            get
            {
                int count = _mainChannel.GetUrlList().Count;
                return count <= 1 ? "1 Yayın Kaynağı" : $"{count} Yayın Kaynağı";
            }
        }

        public string MainChannelEpgDisplay
        {
            get
            {
                var epgs = _mainChannel.GetEpgIdList();
                return epgs.Count > 0 ? $"EPG: {string.Join(", ", epgs.Take(2))}" : "EPG: Tanımsız";
            }
        }

        private string _searchQuery = "";
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (_searchQuery != value)
                {
                    _searchQuery = value;
                    OnPropertyChanged();
                    ApplyFilter();
                }
            }
        }

        // Matching modes
        private bool _isExactMatchMode = false;
        public bool IsExactMatchMode
        {
            get => _isExactMatchMode;
            set { if (_isExactMatchMode != value) { _isExactMatchMode = value; OnPropertyChanged(); } }
        }

        private bool _isContainsMode = true; // Default safe mode
        public bool IsContainsMode
        {
            get => _isContainsMode;
            set { if (_isContainsMode != value) { _isContainsMode = value; OnPropertyChanged(); } }
        }

        private bool _isSubstringsMode = false;
        public bool IsSubstringsMode
        {
            get => _isSubstringsMode;
            set { if (_isSubstringsMode != value) { _isSubstringsMode = value; OnPropertyChanged(); } }
        }

        private bool _isSimilarMode = false;
        public bool IsSimilarMode
        {
            get => _isSimilarMode;
            set { if (_isSimilarMode != value) { _isSimilarMode = value; OnPropertyChanged(); } }
        }

        public ObservableCollection<MergeCandidateItem> FilteredCandidates { get; set; } = new ObservableCollection<MergeCandidateItem>();

        public bool HasCandidates => FilteredCandidates.Count > 0;

        public int SelectedCandidatesCount => FilteredCandidates.Count(c => c.IsSelected);

        public MergeChannelsWindow(Channel mainChannel)
        {
            _mainChannel = mainChannel ?? throw new ArgumentNullException(nameof(mainChannel));
            InitializeComponent();
            DataContext = this;

            // Initial search query seeded with clean name of main channel
            _searchQuery = ChannelUtils.GetCleanName(_mainChannel.PrimaryName);

            this.MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Left) DragMove(); };

            this.Closing += (s, e) =>
            {
                MergeCandidateItem.CancelActiveCapture();
            };

            Loaded += async (s, e) => await LoadAllChannelsAsync();
        }

        private async Task LoadAllChannelsAsync()
        {
            try
            {
                var list = await _db.GetAllChannelsAsync();
                // Exclude the main channel itself
                _allChannels = list.Where(c => c.Id != _mainChannel.Id).ToList();
                ApplyFilter();
            }
            catch (Exception ex)
            {
                LogService.LogError("MergeChannelsWindow.LoadAllChannelsAsync failed", ex);
            }
        }

        public void ApplyFilter()
        {
            if (_allChannels == null) return;

            string query = (_searchQuery ?? "").Trim();
            string selectedCategory = (CategoryFilterCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Tüm Kategoriler";
            string selectedLanguage = (LanguageFilterCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Tüm Diller";

            var matchedList = _allChannels.Where(c =>
            {
                // Category Filter
                if (selectedCategory != "Tüm Kategoriler" && !string.Equals(c.Category, selectedCategory, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Language Filter
                if (selectedLanguage.Contains("Türkçe") && !string.Equals(c.Language, "tr", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                if (selectedLanguage.Contains("Almanca") && !string.Equals(c.Language, "de", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                if (selectedLanguage.Contains("İngilizce") && !string.Equals(c.Language, "en", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Text Matching Mode
                if (string.IsNullOrWhiteSpace(query)) return true;

                if (IsExactMatchMode)
                {
                    string cleanCand = ChannelUtils.GetCleanName(c.PrimaryName);
                    string cleanQuery = ChannelUtils.GetCleanName(query);
                    return cleanCand.Equals(cleanQuery, StringComparison.OrdinalIgnoreCase) ||
                           c.GetNamesList().Any(n => ChannelUtils.GetCleanName(n).Equals(cleanQuery, StringComparison.OrdinalIgnoreCase));
                }
                else if (IsContainsMode)
                {
                    return c.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           c.GetNamesList().Any(n => n.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                else if (IsSubstringsMode)
                {
                    var terms = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    return terms.Length > 0 && terms.All(t => c.Name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                else if (IsSimilarMode)
                {
                    string candKey = ChannelUtils.ToNormalizedKey(c.PrimaryName);
                    string queryKey = ChannelUtils.ToNormalizedKey(query);
                    return !string.IsNullOrEmpty(candKey) && !string.IsNullOrEmpty(queryKey) &&
                           (candKey.Contains(queryKey) || queryKey.Contains(candKey));
                }

                return c.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            }).ToList();

            // Preserve selection state if candidate is already in the list
            var currentSelections = new HashSet<string>(FilteredCandidates.Where(c => c.IsSelected).Select(c => c.Channel.Id));

            FilteredCandidates.Clear();
            foreach (var ch in matchedList.Take(150)) // limit view to top 150 candidates for fluid rendering
            {
                var item = new MergeCandidateItem(ch, this)
                {
                    IsSelected = currentSelections.Contains(ch.Id)
                };
                FilteredCandidates.Add(item);
            }

            OnPropertyChanged(nameof(HasCandidates));
            OnPropertyChanged(nameof(SelectedCandidatesCount));
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void RefreshSearch_Click(object sender, RoutedEventArgs e)
        {
            ApplyFilter();
        }

        private void FilterMode_Checked(object sender, RoutedEventArgs e)
        {
            ApplyFilter();
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in FilteredCandidates)
            {
                item.IsSelected = true;
            }
            OnPropertyChanged(nameof(SelectedCandidatesCount));
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in FilteredCandidates)
            {
                item.IsSelected = false;
            }
            OnPropertyChanged(nameof(SelectedCandidatesCount));
        }

        public void NotifyCandidateSelectionChanged()
        {
            OnPropertyChanged(nameof(SelectedCandidatesCount));
        }

        private async void CapturePreview_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is MergeCandidateItem item)
            {
                await item.CaptureSingleFrameAsync();
            }
        }

        private async void MergeSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = FilteredCandidates.Where(c => c.IsSelected).ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("Lütfen ana kanal ile birleştirmek istediğiniz en az 1 adet aday kanal seçin.",
                    "Seçim Yapılmadı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirmResult = MessageBox.Show(
                $"Seçilen {selectedItems.Count} adet kanal '{_mainChannel.PrimaryName}' ana kanalına birleştirilecek.\n\n" +
                $"• Aday kanalların yayın URL'leri, alternatif logoları ve EPG bilgileri ana kanala aktarılacaktır.\n" +
                $"• Birleştirilen aday kanallar veritabanından silinecektir.\n\n" +
                $"İşlemi onaylıyor musunuz?",
                "Kanal Birleştirme Onayı",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes) return;

            try
            {
                var candidateIdsToDelete = new List<string>();

                foreach (var item in selectedItems)
                {
                    _mainChannel.MergeWith(item.Channel);
                    candidateIdsToDelete.Add(item.Channel.Id);
                }

                // 1. Delete merged candidate channels from database
                await _db.DeleteChannelsAsync(candidateIdsToDelete);

                // 2. Save the updated main channel to database
                await _db.SaveChannelAsync(_mainChannel);

                LogService.LogInfo($"[ManualMerge] Successfully merged {selectedItems.Count} channels into main channel '{_mainChannel.PrimaryName}' (ID: {_mainChannel.Id})");

                MessageBox.Show(
                    $"Başarıyla {selectedItems.Count} adet kanal '{_mainChannel.PrimaryName}' ana kanalına birleştirildi!\n\n" +
                    $"Yeni kaynak sayısı: {_mainChannel.GetUrlList().Count}\n" +
                    $"Alternatif isim sayısı: {_mainChannel.GetNamesList().Count}\n" +
                    $"Alternatif logo sayısı: {_mainChannel.GetLogoList().Count}",
                    "Birleştirme Başarılı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                LogService.LogError("MergeChannelsWindow.MergeSelected_Click failed", ex);
                MessageBox.Show($"Birleştirme sırasında bir hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class MergeCandidateItem : INotifyPropertyChanged
    {
        private readonly MergeChannelsWindow _parent;
        public Channel Channel { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                    _parent.NotifyCandidateSelectionChanged();
                }
            }
        }

        private ImageSource? _previewSnapshot;
        public ImageSource? PreviewSnapshot
        {
            get => _previewSnapshot;
            set
            {
                _previewSnapshot = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSnapshot));
            }
        }

        public bool HasSnapshot => _previewSnapshot != null;

        private string _previewStatusText = "Önizleme Alınmadı";
        public string PreviewStatusText
        {
            get => _previewStatusText;
            set { _previewStatusText = value; OnPropertyChanged(); }
        }

        public string LanguageDisplay
        {
            get
            {
                string lang = Channel.Language ?? "und";
                if (lang.Equals("tr", StringComparison.OrdinalIgnoreCase)) return "TR";
                if (lang.Equals("de", StringComparison.OrdinalIgnoreCase)) return "DE";
                if (lang.Equals("en", StringComparison.OrdinalIgnoreCase)) return "EN";
                if (lang.Equals("fr", StringComparison.OrdinalIgnoreCase)) return "FR";
                return string.IsNullOrWhiteSpace(lang) || lang == "und" ? "UND" : lang.ToUpperInvariant();
            }
        }

        public string UrlPreview
        {
            get
            {
                string u = Channel.Url ?? "";
                if (u.Length > 45) return u.Substring(0, 42) + "...";
                return string.IsNullOrWhiteSpace(u) ? "URL Yok" : u;
            }
        }

        public string EpgInfoText
        {
            get
            {
                var e = Channel.GetEpgIdList();
                return e.Count > 0 ? $"EPG: {e[0]}" : "";
            }
        }

        public MergeCandidateItem(Channel channel, MergeChannelsWindow parent)
        {
            Channel = channel;
            _parent = parent;
        }

        private static CancellationTokenSource? _currentCaptureCts;

        public static void CancelActiveCapture()
        {
            try
            {
                _currentCaptureCts?.Cancel();
                _currentCaptureCts?.Dispose();
                _currentCaptureCts = null;
            }
            catch { }
        }

        public async Task CaptureSingleFrameAsync()
        {
            var urls = Channel.GetUrlList();
            if (urls.Count == 0 || string.IsNullOrWhiteSpace(urls[0]))
            {
                PreviewStatusText = "Yayın URL Yok";
                return;
            }

            // Cancel any previously active single-frame capture to avoid concurrent decoders
            CancelActiveCapture();
            _currentCaptureCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var token = _currentCaptureCts.Token;

            PreviewStatusText = "Kare Alınıyor...";

            string targetUrl = urls[0].Trim();

            try
            {
                // Ensure Flyleaf/FFmpeg is started
                FlyleafHelper.SafeStart();

                await Task.Run(async () =>
                {
                    Player? player = null;
                    string tempFilePath = Path.Combine(Path.GetTempPath(), $"streammesh_snap_{Guid.NewGuid():N}.jpg");

                    try
                    {
                        // 1. Resolve AceStream or YouTube URLs if necessary
                        if (targetUrl.StartsWith("acestream://", StringComparison.OrdinalIgnoreCase) || targetUrl.Contains(":6878/ace/"))
                        {
                            var ace = new AceEngine();
                            var aceUrls = await ace.GetHttpUrlsWithTokenAsync(targetUrl).ConfigureAwait(false);
                            if (aceUrls != null && aceUrls.Count > 0) targetUrl = aceUrls[0];
                        }
                        else if (targetUrl.Contains("youtube.com") || targetUrl.Contains("youtu.be"))
                        {
                            var yt = new YoutubeEngine();
                            var resolved = await yt.GetStreamUrlAsync(targetUrl).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(resolved)) targetUrl = resolved;
                        }

                        if (token.IsCancellationRequested) return;

                        // 2. Configure a lightweight, audio-disabled Player for fast single frame decoding
                        var config = new Config();
                        config.Audio.Enabled = false; // Video only, saves CPU & network
                        config.Video.Enabled = true;
                        config.Player.AutoPlay = true;
                        config.Demuxer.FormatOpt["user_agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";
                        config.Demuxer.BufferDuration = 1000 * 10000; // 1s buffer for low latency start

                        player = new Player(config);
                        player.Open(targetUrl);

                        // 3. Wait for playback / first decoded frame with safety timeout
                        int waitCycles = 0;
                        while (player.Status != Status.Playing && player.Status != Status.Failed && !token.IsCancellationRequested && waitCycles < 30) // max ~6s
                        {
                            await Task.Delay(200, token).ConfigureAwait(false);
                            waitCycles++;
                        }

                        if (player.Status == Status.Playing && !token.IsCancellationRequested)
                        {
                            // Brief wait for first frame buffer to populate
                            await Task.Delay(300, token).ConfigureAwait(false);

                            // Take decoded frame snapshot using Flyleaf player engine if available
                            try
                            {
                                var takeSnapMethod = player.GetType().GetMethod("TakeSnapshot", new[] { typeof(string) })
                                                     ?? player.GetType().GetMethod("Snapshot", new[] { typeof(string) });
                                if (takeSnapMethod != null)
                                {
                                    takeSnapMethod.Invoke(player, new object[] { tempFilePath });
                                }
                            }
                            catch { }

                            // Wait up to 1.5s for snapshot file to be written to disk
                            int fileWait = 0;
                            while (!File.Exists(tempFilePath) && fileWait < 15 && !token.IsCancellationRequested)
                            {
                                await Task.Delay(100, token).ConfigureAwait(false);
                                fileWait++;
                            }

                            if (File.Exists(tempFilePath) && new FileInfo(tempFilePath).Length > 0)
                            {
                                var bmp = new BitmapImage();
                                using (var fs = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                                {
                                    bmp.BeginInit();
                                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                                    bmp.StreamSource = fs;
                                    bmp.EndInit();
                                }
                                bmp.Freeze();

                                try { File.Delete(tempFilePath); } catch { }

                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    PreviewSnapshot = bmp;
                                    PreviewStatusText = "Kare Alındı";
                                });
                                return;
                            }
                        }

                        // If snapshot could not be extracted from stream
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            PreviewStatusText = "Canlı görüntü alınamadı";
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        // Cancelled due to another action or window closing
                    }
                    catch (Exception ex)
                    {
                        LogService.LogWarning($"[Snapshot] Failed to capture frame from '{targetUrl}': {ex.Message}");
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            PreviewStatusText = "Canlı görüntü alınamadı";
                        });
                    }
                    finally
                    {
                        // 4. Guaranteed disposal of player and temp files
                        try { player?.Stop(); } catch { }
                        try { player?.Dispose(); } catch { }
                        try { if (File.Exists(tempFilePath)) File.Delete(tempFilePath); } catch { }
                    }
                }, token);
            }
            catch (Exception ex)
            {
                PreviewStatusText = "Canlı görüntü alınamadı";
                LogService.LogError("CaptureSingleFrameAsync outer error", ex);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
