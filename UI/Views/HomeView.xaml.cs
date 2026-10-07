using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Linq;
using StreamMesh.Models;
using StreamMesh.UI.ViewModels;
using StreamMesh.UI.Windows;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media;
using StreamMesh.Core.Utils;

namespace StreamMesh.UI.Views
{
    public partial class HomeView : System.Windows.Controls.UserControl
    {
        private HomeViewModel _vm;
        private System.Windows.Point _dragStartPoint;
        private readonly DatabaseEngine _db = new DatabaseEngine();

        public HomeView()
        {
            InitializeComponent();
            _vm = new HomeViewModel();
            DataContext = _vm;
        }

        private void Card_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is Channel item)
            {
                _vm.CurrentBackdrop = !string.IsNullOrEmpty(item.BackdropUrl) ? item.BackdropUrl : item.LogoUrl;
            }
        }

        private void Card_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) { }

        private void Card_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement fe && fe.DataContext is Channel item)
                {
                    if (item is SeriesGroup series)
                    {
                        var seriesWin = new SeriesDetailsWindow(series);
                        seriesWin.Owner = Window.GetWindow(this);
                        seriesWin.ShowDialog();
                        return;
                    }

                    string cat = (item.Category ?? "").Trim().ToUpperInvariant();
                    if (cat == "TV" || cat == "RADYO" || cat == "GENEL")
                    {
                        MainWindow.Instance?.LoadChannelToPlayer(item);
                        return;
                    }
                    var details = new MediaDetailsWindow(item);
                    details.Owner = Window.GetWindow(this);
                    details.Show();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("HomeView.Card_Click error", ex);
                System.Windows.MessageBox.Show($"İçerik açılırken bir hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void ManualSync_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sync = new GitHubSyncEngine();
                await sync.PullFromGitHubAsync();
            }
            catch (Exception ex)
            {
                LogService.LogError("HomeView.ManualSync_Click error", ex);
                System.Windows.MessageBox.Show($"Güncelleme başlatılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Drag and Drop Merge Logic
        private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void Card_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                System.Windows.Point pos = e.GetPosition(null);
                if (Math.Abs(pos.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(pos.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is FrameworkElement fe && fe.DataContext is Channel ch)
                    {
                        System.Windows.DragDrop.DoDragDrop(fe, ch, System.Windows.DragDropEffects.Move);
                    }
                }
            }
        }

        private async void Card_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetData(typeof(Channel)) is Channel source && sender is FrameworkElement fe && fe.DataContext is Channel target)
            {
                if (source.Id == target.Id) return;

                if (System.Windows.MessageBox.Show($"{source.Name} kanalını {target.Name} ile birleştirmek istiyor musunuz?\n\nBu işlem tüm yayın linklerini tek kartta toplar.", "Kanal Birleştir", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    await _vm.MergeChannelsAsync(source, target);
                }
            }
        }

        private void Category_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string tag)
            {
                foreach (var child in CategoryPanel.Children)
                {
                    if (child is System.Windows.Controls.Button b)
                    {
                        b.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#334155"));
                        b.Foreground = System.Windows.Media.Brushes.White;
                    }
                }
                btn.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#38bdf8"));
                btn.Foreground = System.Windows.Media.Brushes.Black;
                _vm.SetCategory(tag);
            }
        }

        private void GroupChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string groupName)
            {
                _vm.SetGroup(groupName);
            }
        }

        private void SourceChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string sourceId)
            {
                _vm.SetSource(sourceId);
            }
        }

        private void DirectPlay_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is FrameworkElement fe && fe.DataContext is Channel channel)
            {
                if (channel is SeriesGroup series)
                {
                    var seriesWin = new SeriesDetailsWindow(series);
                    seriesWin.Owner = Window.GetWindow(this);
                    seriesWin.ShowDialog();
                    return;
                }
                MainWindow.Instance?.LoadChannelToPlayer(channel);
            }
        }

        private void Sort_SelectionChanged(object sender, SelectionChangedEventArgs e) { _vm?.SetSort(SortComboBox.SelectedIndex); }
        private void Refresh_Click(object sender, RoutedEventArgs e) { _ = _vm.LoadDataAsync(); }
        private void Prev_Click(object sender, RoutedEventArgs e) { _vm.PrevPage(); }
        private void Next_Click(object sender, RoutedEventArgs e) { _vm.NextPage(); }

        private void SearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Tab && !string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                e.Handled = true;
                System.Windows.MessageBox.Show("AI Asistanı üzerinden gelişmiş sorgu yapabilirsiniz.");
            }
        }

        private void PlayContext_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.CommandParameter is Channel ch) MainWindow.Instance?.LoadChannelToPlayer(ch);
        }

        private void EditContext_Click(object sender, RoutedEventArgs e)
        {
            Channel? ch = null;
            if (sender is MenuItem mi && mi.CommandParameter is Channel c1) ch = c1;
            else if (sender is System.Windows.Controls.Button btn && btn.CommandParameter is Channel c2) ch = c2;

            if (ch != null)
            {
                var editWin = new EditChannelWindow(ch);
                editWin.Owner = Window.GetWindow(this);
                if (editWin.ShowDialog() == true) _ = _vm.LoadDataAsync();
            }
        }

        private async void FavContext_Click(object sender, RoutedEventArgs e)
        {
            Channel? ch = null;
            if (sender is MenuItem mi && mi.CommandParameter is Channel c1) ch = c1;
            else if (sender is System.Windows.Controls.Button btn && btn.CommandParameter is Channel c2) ch = c2;

            if (ch != null)
            {
                await _vm.ToggleFavoriteAsync(ch);
            }
        }

        private async void DeleteContext_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.CommandParameter is Channel ch)
            {
                if (System.Windows.MessageBox.Show($"{ch.Name} silinecek. Emin misiniz?", "Kanal Sil", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    await _vm.DeleteChannelAsync(ch);
                }
            }
        }

        private void ValidateChannels_Click(object sender, RoutedEventArgs e)
        {
            _ = _vm.StartValidationAsync();
        }

        private void CancelValidation_Click(object sender, RoutedEventArgs e)
        {
            _vm.CancelValidation();
        }

        private async void AiEnrichContext_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.CommandParameter is Channel ch)
            {
                var aiEngine = new LocalAiNormalizationEngine();
                bool available = await aiEngine.IsLocalAiAvailableAsync();
                if (!available)
                {
                    System.Windows.MessageBox.Show("Yerel Yapay Zeka (Ollama veya LM Studio) aktif değil.", "AI Uyarısı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                System.Windows.MessageBox.Show($"'{ch.PrimaryName}' için Yapay Zeka & Web Arama (RAG) doğrulaması başlatılıyor...", "AI Doğrulama", MessageBoxButton.OK, MessageBoxImage.Information);

                var db = new StreamMesh.Core.Database.DatabaseEngine();
                if (ch is SeriesGroup sg && sg.Episodes.Count > 0)
                {
                    await aiEngine.NormalizeChannelsWithAiAsync(sg.Episodes, (msg, p, t) => { });
                    foreach (var ep in sg.Episodes)
                    {
                        await db.SaveChannelAsync(ep);
                    }
                }
                else
                {
                    await aiEngine.NormalizeChannelsWithAiAsync(new List<Channel> { ch }, (msg, p, t) => { });
                    await db.SaveChannelAsync(ch);
                }

                StreamMesh.Core.Database.DatabaseEngine.NotifyDatabaseUpdated();
                System.Windows.MessageBox.Show($"'{ch.PrimaryName}' başarıyla güncellendi!\nYeni Ad: {ch.Name}\nYeni Kategori: {ch.Category}\nDil: {ch.Language}", "Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void ValidateContext_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.CommandParameter is Channel ch)
            {
                using var validator = new StreamValidator();
                var res = await validator.ValidateAsync(ch, ValidationLevel.Fast);
                string status = res.IsOnline ? $"Sağlam ({res.Status})" : $"Bozuk / Çevrimdışı ({res.Status})";
                System.Windows.MessageBox.Show($"Kanal Test Sonucu: {ch.PrimaryName}\nDurum: {status}", "Yayın Sağlık Testi", MessageBoxButton.OK, res.IsOnline ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
        }

        private async void AiEnrichGroupContext_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.CommandParameter is GroupCategoryItem groupItem)
            {
                var aiEngine = new LocalAiNormalizationEngine();
                bool available = await aiEngine.IsLocalAiAvailableAsync();
                if (!available)
                {
                    System.Windows.MessageBox.Show("Yerel Yapay Zeka (Ollama veya LM Studio) aktif değil.", "AI Uyarısı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                System.Windows.MessageBox.Show($"'{groupItem.Name}' grubu için Yerel AI & RAG optimizasyonu başlatılıyor...", "Grup AI Optimizasyonu", MessageBoxButton.OK, MessageBoxImage.Information);

                var db = new StreamMesh.Core.Database.DatabaseEngine();
                int page = 1;
                while (true)
                {
                    var (chunk, total) = await db.GetPagedChannelsAsync(null, "All", groupItem.Name, "ALL", 0, page, 500);
                    if (chunk == null || chunk.Count == 0) break;

                    await aiEngine.NormalizeChannelsWithAiAsync(chunk, (msg, p, t) => { });
                    await db.SaveChannelsBatchAsync(chunk);
                    if ((page * 500) >= total) break;
                    page++;
                }

                StreamMesh.Core.Database.DatabaseEngine.NotifyDatabaseUpdated();
                System.Windows.MessageBox.Show($"'{groupItem.Name}' grubundaki kanallar başarıyla optimize edildi!", "Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void ValidateGroupContext_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.CommandParameter is GroupCategoryItem groupItem)
            {
                System.Windows.MessageBox.Show($"'{groupItem.Name}' grubundaki kanalların sağlık taraması başlatılıyor...", "Grup Doğrulama", MessageBoxButton.OK, MessageBoxImage.Information);
                var db = new StreamMesh.Core.Database.DatabaseEngine();
                var (chunk, total) = await db.GetPagedChannelsAsync(null, "All", groupItem.Name, "ALL", 0, 1, 100);

                int online = 0;
                int offline = 0;
                using var validator = new StreamValidator();
                foreach (var ch in chunk)
                {
                    var res = await validator.ValidateAsync(ch, ValidationLevel.Fast);
                    if (res.IsOnline) online++; else offline++;
                }
                System.Windows.MessageBox.Show($"Grup Test Sonucu ('{groupItem.Name}'):\nSağlam: {online}\nBozuk/Çevrimdışı: {offline} (İlk 100 örnek test edildi)", "Grup Sağlık Raporu", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
