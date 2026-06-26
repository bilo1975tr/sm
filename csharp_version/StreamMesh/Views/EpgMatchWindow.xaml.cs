using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using StreamMesh.Models;
using StreamMesh.Services;

namespace StreamMesh.Views
{
    public partial class EpgMatchWindow : Window
    {
        private readonly DatabaseService _databaseService;
        private List<LocalChannelItem> _allLocalChannels = new List<LocalChannelItem>();
        private List<EpgChannelItem> _allEpgChannels = new List<EpgChannelItem>();
        
        private LocalChannelItem _selectedLocal;
        private EpgChannelItem _selectedEpg;
        private string _recommendedEpgName;

        public class LocalChannelItem
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Language { get; set; }
            public string EpgId { get; set; }
            public string DisplayText => $"{Name} [{Language}]";
            public string EpgStatusText => string.IsNullOrEmpty(EpgId) ? "❌ Eşleşme Yok" : $"🔗 {EpgId}";
            public string EpgStatusColor => string.IsNullOrEmpty(EpgId) ? "#ef4444" : "#22c55e";
        }

        public class EpgChannelItem
        {
            public string Name { get; set; }
            public string SourceUrl { get; set; }
            public string InferredLanguage { get; set; }
            public string DisplayText => $"{Name} ({InferredLanguage})";
        }

        public EpgMatchWindow()
        {
            InitializeComponent();
            _databaseService = new DatabaseService();
            Loaded += EpgMatchWindow_Loaded;
        }

        private async void EpgMatchWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            SafeAutoMatchBtn.IsEnabled = false;
            LinkBtn.IsEnabled = false;
            
            // Asenkron veri çekimi
            var channelsTask = Task.Run(() => _databaseService.GetAllChannels());
            var epgsTask = Task.Run(() => _databaseService.GetEpgChannelsWithSource());

            await Task.WhenAll(channelsTask, epgsTask);

            var dbChannels = await channelsTask;
            var dbEpgs = await epgsTask;

            // Model dönüşümleri
            _allLocalChannels = dbChannels.Select(c => new LocalChannelItem
            {
                Id = c.Id,
                Name = c.Name,
                Language = c.Language,
                EpgId = c.EpgId
            }).ToList();

            _allEpgChannels = dbEpgs.Select(epg => {
                string name = epg.Item1;
                string url = epg.Item2;
                string inferredLang = InferLanguageFromUrl(url);
                if (inferredLang == "Bilinmiyor")
                {
                    inferredLang = InferEpgLanguage(name);
                }
                return new EpgChannelItem
                {
                    Name = name,
                    SourceUrl = url,
                    InferredLanguage = inferredLang
                };
            }).ToList();

            UpdateStats();
            ApplyFilters();

            SafeAutoMatchBtn.IsEnabled = true;
        }

        private void UpdateStats()
        {
            int total = _allLocalChannels.Count;
            int matched = _allLocalChannels.Count(c => !string.IsNullOrEmpty(c.EpgId));
            int missing = total - matched;
            int uniqueEpg = _allEpgChannels.Count;

            TxtTotalChannels.Text = total.ToString();
            TxtMatchedChannels.Text = matched.ToString();
            TxtMissingChannels.Text = missing.ToString();
            TxtEpgChannels.Text = uniqueEpg.ToString();
        }

        private void ApplyFilters()
        {
            string searchLocal = SearchLocalBox.Text.Trim().ToLowerInvariant();
            bool onlyMissing = ChkOnlyMissing.IsChecked == true;

            var filteredLocals = _allLocalChannels.Where(c => {
                if (onlyMissing && !string.IsNullOrEmpty(c.EpgId)) return false;
                if (!string.IsNullOrEmpty(searchLocal))
                {
                    return c.Name.ToLowerInvariant().Contains(searchLocal) || 
                           c.Language.ToLowerInvariant().Contains(searchLocal) ||
                           (c.EpgId ?? "").ToLowerInvariant().Contains(searchLocal);
                }
                return true;
            }).ToList();

            LocalChannelsListBox.ItemsSource = filteredLocals;

            string searchEpg = SearchEpgBox.Text.Trim().ToLowerInvariant();
            var filteredEpgs = _allEpgChannels.Where(e => {
                if (!string.IsNullOrEmpty(searchEpg))
                {
                    return e.Name.ToLowerInvariant().Contains(searchEpg) || 
                           e.InferredLanguage.ToLowerInvariant().Contains(searchEpg) ||
                           e.SourceUrl.ToLowerInvariant().Contains(searchEpg);
                }
                return true;
            }).ToList();

            EpgChannelsListBox.ItemsSource = filteredEpgs;
        }

        private void SearchLocalBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void ChkOnlyMissing_Changed(object sender, RoutedEventArgs e)
        {
            ApplyFilters();
        }

        private void SearchEpgBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void LocalChannelsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedLocal = LocalChannelsListBox.SelectedItem as LocalChannelItem;
            UpdateSelectionDetails();
        }

        private void EpgChannelsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedEpg = EpgChannelsListBox.SelectedItem as EpgChannelItem;
            UpdateSelectionDetails();
        }

        private void UpdateSelectionDetails()
        {
            if (_selectedLocal == null)
            {
                TxtSelectedLocalChannel.Text = "Seçim Yok";
                RecommendationPanel.Visibility = Visibility.Collapsed;
                UnlinkBtn.Visibility = Visibility.Collapsed;
                LinkBtn.IsEnabled = false;
                return;
            }

            TxtSelectedLocalChannel.Text = $"{_selectedLocal.Name} [{_selectedLocal.Language}]";

            // Eşleşmeyi kaldır butonu
            if (!string.IsNullOrEmpty(_selectedLocal.EpgId))
            {
                UnlinkBtn.Visibility = Visibility.Visible;
            }
            else
            {
                UnlinkBtn.Visibility = Visibility.Collapsed;
            }

            // Bağlama butonu kontrolü
            LinkBtn.IsEnabled = _selectedEpg != null;

            // Akıllı Öneri (Eğer kanalda EPG eşleşmesi yoksa)
            if (string.IsNullOrEmpty(_selectedLocal.EpgId))
            {
                string normLocal = NormalizeName(_selectedLocal.Name);
                
                // 1. Aşama: Tam normalize isim eşleşmesi
                var recommended = _allEpgChannels.FirstOrDefault(epg => 
                    NormalizeName(epg.Name) == normLocal && 
                    epg.InferredLanguage.Equals(_selectedLocal.Language, StringComparison.OrdinalIgnoreCase)
                );

                // 2. Aşama: Dil bağımsız tam normalize isim eşleşmesi
                if (recommended == null)
                {
                    recommended = _allEpgChannels.FirstOrDefault(epg => NormalizeName(epg.Name) == normLocal);
                }

                // 3. Aşama: Benzer/Kısmi isim eşleşmesi (Fuzzy)
                if (recommended == null)
                {
                    recommended = _allEpgChannels.FirstOrDefault(epg => IsSimilar(_selectedLocal.Name, epg.Name));
                }

                if (recommended != null)
                {
                    _recommendedEpgName = recommended.Name;
                    TxtRecommendedEpg.Text = $"{recommended.Name} ({recommended.InferredLanguage})";
                    RecommendationPanel.Visibility = Visibility.Visible;
                }
                else
                {
                    RecommendationPanel.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                RecommendationPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyRecommendationBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedLocal == null || string.IsNullOrEmpty(_recommendedEpgName)) return;

            _databaseService.UpdateChannelEpg(_selectedLocal.Id, _recommendedEpgName);
            
            // UI state güncelleme
            _selectedLocal.EpgId = _recommendedEpgName;
            
            UpdateStats();
            LocalChannelsListBox.Items.Refresh();
            UpdateSelectionDetails();
        }

        private void UnlinkBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedLocal == null) return;

            _databaseService.UpdateChannelEpg(_selectedLocal.Id, "");
            _selectedLocal.EpgId = "";

            UpdateStats();
            LocalChannelsListBox.Items.Refresh();
            UpdateSelectionDetails();
        }

        private void LinkBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedLocal == null || _selectedEpg == null) return;

            _databaseService.UpdateChannelEpg(_selectedLocal.Id, _selectedEpg.Name);
            _selectedLocal.EpgId = _selectedEpg.Name;

            UpdateStats();
            LocalChannelsListBox.Items.Refresh();
            UpdateSelectionDetails();
        }

        private async void SafeAutoMatchBtn_Click(object sender, RoutedEventArgs e)
        {
            SafeAutoMatchBtn.IsEnabled = false;
            
            int matchedCount = 0;
            var localItemsToMatch = _allLocalChannels.Where(c => string.IsNullOrEmpty(c.EpgId)).ToList();

            await Task.Run(() =>
            {
                foreach (var local in localItemsToMatch)
                {
                    string normLocal = NormalizeName(local.Name);
                    // Sadece hem normalize ismi aynı olan hem de dili kesinlikle uyuşanları güvenli eşleştir
                    var match = _allEpgChannels.FirstOrDefault(epg => 
                        NormalizeName(epg.Name) == normLocal && 
                        epg.InferredLanguage.Equals(local.Language, StringComparison.OrdinalIgnoreCase)
                    );

                    if (match != null)
                    {
                        _databaseService.UpdateChannelEpg(local.Id, match.Name);
                        local.EpgId = match.Name;
                        matchedCount++;
                    }
                }
            });

            MessageBox.Show($"{matchedCount} adet kanal güvenli otomatik kurallar çerçevesinde başarıyla eşleştirildi!", "Güvenli Otomatik Eşleştirme", MessageBoxButton.OK, MessageBoxImage.Information);
            
            UpdateStats();
            LocalChannelsListBox.Items.Refresh();
            UpdateSelectionDetails();
            SafeAutoMatchBtn.IsEnabled = true;
        }

        // --- YARDIMCI METOTLAR ---

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            string upper = name.ToUpperInvariant();
            
            string[] tags = { "HD", "SD", "FHD", "UHD", "4K", "1080P", "720P", "HEVC", "H265", "H.265", "H264", "H.264", "RAW", "YEDEK", "BACKUP", "VIP", "MPEG", "PREMIUM", "CANLI", "LIVE", "TV" };
            foreach (var tag in tags)
            {
                upper = upper.Replace(tag, "");
            }

            var sb = new System.Text.StringBuilder();
            foreach (char c in upper)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString().Trim();
        }

        private static bool IsSimilar(string name1, string name2)
        {
            string norm1 = NormalizeName(name1);
            string norm2 = NormalizeName(name2);
            if (string.IsNullOrEmpty(norm1) || string.IsNullOrEmpty(norm2)) return false;
            
            if (norm1 == norm2) return true;
            if (norm1.Contains(norm2) || norm2.Contains(norm1)) return true;
            
            int dist = LevenshteinDistance(norm1, norm2);
            int maxLen = Math.Max(norm1.Length, norm2.Length);
            if (maxLen > 4 && dist <= 2) return true;
            
            return false;
        }

        private static int LevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s)) return t?.Length ?? 0;
            if (string.IsNullOrEmpty(t)) return s?.Length ?? 0;
            
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; d[i, 0] = i++) ;
            for (int j = 0; j <= m; d[0, j] = j++) ;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }

        private static string InferLanguageFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "Bilinmiyor";
            string lower = url.ToLowerInvariant();
            if (lower.Contains("turkey") || lower.Contains("/tr") || lower.Contains("tr.") || lower.Contains("turkish") || lower.Contains("kanallar_tr"))
                return "Türkçe";
            if (lower.Contains("germany") || lower.Contains("/de") || lower.Contains("de.") || lower.Contains("deutsch") || lower.Contains("german"))
                return "Almanca";
            if (lower.Contains("english") || lower.Contains("/en") || lower.Contains("en.") || lower.Contains("uk.") || lower.Contains("us."))
                return "İngilizce";
            if (lower.Contains("french") || lower.Contains("/fr") || lower.Contains("fr."))
                return "Fransızca";
            return "Bilinmiyor";
        }

        private static string InferEpgLanguage(string epgChannelName)
        {
            if (string.IsNullOrEmpty(epgChannelName)) return "Bilinmiyor";
            string upper = epgChannelName.ToUpperInvariant();
            
            if (upper.Contains(" TR") || upper.Contains("(TR)") || upper.Contains(".TR") || upper.Contains("TÜRK") || upper.Contains("TURK") || upper.Contains("TUR"))
                return "Türkçe";
            if (upper.Contains(" DE") || upper.Contains("(DE)") || upper.Contains(".DE") || upper.Contains("GER") || upper.Contains("ALM") || upper.Contains("DEUTSCH"))
                return "Almanca";
            if (upper.Contains(" EN") || upper.Contains("(EN)") || upper.Contains(".EN") || upper.Contains("ENG") || upper.Contains("UK ") || upper.Contains("USA") || upper.Contains("ENGLISH"))
                return "İngilizce";
            if (upper.Contains(" FR") || upper.Contains("(FR)") || upper.Contains(".FR") || upper.Contains("FRA") || upper.Contains("FRENCH"))
                return "Fransızca";
            
            return "Bilinmiyor";
        }
    }
}
