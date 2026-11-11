using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using StreamMesh.Models;
using StreamMesh.Core.Database;
using System.Diagnostics;
using System.Linq;

namespace StreamMesh.UI.Windows
{
    public partial class SourceEditWindow : Window
    {
        private readonly string _sourceUrl;
        private readonly DatabaseEngine _db = new DatabaseEngine();
        private List<Channel> _allChannels = new List<Channel>();
        public ObservableCollection<Channel> DisplayedChannels { get; set; } = new ObservableCollection<Channel>();

        public SourceEditWindow(string sourceUrl)
        {
            InitializeComponent();
            _sourceUrl = sourceUrl;
            SourceTitle.Text = $"Kaynak: {sourceUrl}";
            ChannelList.ItemsSource = DisplayedChannels;
            LoadChannels();
        }

        private void LoadChannels()
        {
            try
            {
                var list = _db.GetChannelsBySource(_sourceUrl);
                _allChannels = list ?? new List<Channel>();
                ApplyFilter();
                UpdateSelectionCount();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Kanallar yüklenirken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyFilter()
        {
            string search = (SearchBox?.Text ?? "").Trim();
            bool onlySelected = CheckOnlySelected?.IsChecked == true;

            var filtered = _allChannels.AsEnumerable();

            if (onlySelected)
            {
                filtered = filtered.Where(c => c.IsSelected);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                filtered = filtered.Where(c =>
                    (c.Name != null && c.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (c.GroupTitle != null && c.GroupTitle.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (c.Category != null && c.Category.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (c.Language != null && c.Language.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (c.Url != null && c.Url.Contains(search, StringComparison.OrdinalIgnoreCase))
                );
            }

            var resultList = filtered.ToList();

            DisplayedChannels.Clear();
            foreach (var ch in resultList)
            {
                DisplayedChannels.Add(ch);
            }

            SourceStats.Text = $"Toplam: {_allChannels.Count} kanal | Görüntülenen: {resultList.Count} kanal";
        }

        private void UpdateSelectionCount()
        {
            int selectedCount = _allChannels.Count(c => c.IsSelected);
            if (SelectionCountText != null)
            {
                SelectionCountText.Text = $"Seçili: {selectedCount} kanal";
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            // If search filter is active, select all currently displayed channels; otherwise all channels
            var targetList = DisplayedChannels.Count > 0 ? DisplayedChannels.ToList() : _allChannels;
            foreach (var ch in targetList)
            {
                ch.IsSelected = true;
            }
            UpdateSelectionCount();
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var ch in _allChannels)
            {
                ch.IsSelected = false;
            }
            UpdateSelectionCount();
            if (CheckOnlySelected?.IsChecked == true)
            {
                ApplyFilter();
            }
        }

        private void ChannelCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateSelectionCount();
            if (CheckOnlySelected?.IsChecked == true)
            {
                ApplyFilter();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void CheckOnlySelected_Changed(object sender, RoutedEventArgs e)
        {
            ApplyFilter();
        }

        private void ApplyBulkEdit_Click(object sender, RoutedEventArgs e)
        {
            var selectedChannels = _allChannels.Where(c => c.IsSelected).ToList();
            if (selectedChannels.Count == 0)
            {
                var ask = System.Windows.MessageBox.Show(
                    "Hiçbir kanal seçilmedi. Bu değişiklikleri Listedeki TÜM KANALLARA uygulamak istiyor musunuz?",
                    "Tümüne Uygulansın mı?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (ask == MessageBoxResult.Yes)
                {
                    selectedChannels = _allChannels.ToList();
                }
                else
                {
                    return;
                }
            }

            string? newCat = (BulkCategoryBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            string? newLang = (BulkLanguageBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            string newGroup = BulkGroupTitleBox.Text.Trim();

            int changedCount = 0;
            foreach (var ch in selectedChannels)
            {
                if (!string.IsNullOrWhiteSpace(newCat)) ch.Category = newCat;
                if (!string.IsNullOrWhiteSpace(newLang)) ch.Language = newLang;
                if (!string.IsNullOrWhiteSpace(newGroup)) ch.GroupTitle = newGroup;
                changedCount++;
            }

            ApplyFilter();
            System.Windows.MessageBox.Show(
                $"Toplam {changedCount} kanalın bilgileri başarıyla güncellendi.\nKalıcı olması için 'Değişiklikleri Kaydet' butonuna basmayı unutmayın.",
                "Toplu Düzenleme Tamamlandı",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private async void TestLinks_Click(object sender, RoutedEventArgs e)
        {
            var targets = _allChannels.Where(c => c.IsSelected).ToList();
            if (targets.Count == 0) targets = DisplayedChannels.ToList();
            if (targets.Count == 0) return;

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

            foreach (var ch in targets)
            {
                ch.Notes = "⏳ Test ediliyor...";
                try
                {
                    var sw = Stopwatch.StartNew();
                    string testUrl = (ch.Url ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                    using var req = new HttpRequestMessage(HttpMethod.Head, testUrl);
                    var response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                    sw.Stop();

                    if (response.IsSuccessStatusCode) ch.Notes = $"✅ {sw.ElapsedMilliseconds}ms";
                    else ch.Notes = $"❌ Hata: {(int)response.StatusCode}";
                }
                catch { ch.Notes = "💀 Kapalı"; }
            }
        }

        private void DeleteDead_Click(object sender, RoutedEventArgs e)
        {
            var dead = _allChannels.Where(c => c.Notes.Contains("💀") || c.Notes.Contains("Hata")).ToList();
            if (dead.Count == 0)
            {
                System.Windows.MessageBox.Show("Test edilmiş ölü link bulunamadı. Önce 'Linkleri Test Et' butonunu çalıştırabilirsiniz.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (System.Windows.MessageBox.Show($"{dead.Count} adet çalışmayan kanal kütüphaneden silinecek. Emin misiniz?", "Ölü Link Temizliği", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                foreach (var d in dead)
                {
                    _db.ExecuteRawNonQuery($"DELETE FROM Channels WHERE Id='{d.Id}'");
                    _allChannels.Remove(d);
                }
                ApplyFilter();
                UpdateSelectionCount();
                DatabaseEngine.NotifyDatabaseUpdated();
            }
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = _allChannels.Where(c => c.IsSelected).ToList();
            if (selected.Count == 0)
            {
                System.Windows.MessageBox.Show("Lütfen silmek istediğiniz kanalları işaretleyin.", "Kanal Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (System.Windows.MessageBox.Show($"Seçili {selected.Count} kanal kalıcı olarak silinecek. Emin misiniz?", "Seçilenleri Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                foreach (var s in selected)
                {
                    _db.ExecuteRawNonQuery($"DELETE FROM Channels WHERE Id='{s.Id}'");
                    _allChannels.Remove(s);
                }
                ApplyFilter();
                UpdateSelectionCount();
                DatabaseEngine.NotifyDatabaseUpdated();
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var ch in _allChannels)
                {
                    await _db.SaveChannelAsync(ch);
                }
                DatabaseEngine.NotifyDatabaseUpdated();
                System.Windows.MessageBox.Show("Tüm kanal ve grup değişiklikleri başarıyla kaydedildi.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
