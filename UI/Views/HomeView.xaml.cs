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
            if (sender is FrameworkElement fe && fe.DataContext is Channel item)
            {
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
                    var existingUrls = target.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    var sourceUrls = source.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var u in sourceUrls)
                    {
                        if (!existingUrls.Contains(u.Trim())) existingUrls.Add(u.Trim());
                    }

                    target.Url = string.Join(",", existingUrls);
                    await _db.SaveChannelAsync(target);
                    _db.ExecuteRawNonQuery($"DELETE FROM Channels WHERE Id='{source.Id}'");
                    _vm.LoadData();
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

        private void Sort_SelectionChanged(object sender, SelectionChangedEventArgs e) { _vm?.SetSort(SortComboBox.SelectedIndex); }
        private void Refresh_Click(object sender, RoutedEventArgs e) { _vm.LoadData(); }
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
                if (editWin.ShowDialog() == true) _vm.LoadData();
            }
        }

        private async void FavContext_Click(object sender, RoutedEventArgs e)
        {
            Channel? ch = null;
            if (sender is MenuItem mi && mi.CommandParameter is Channel c1) ch = c1;
            else if (sender is System.Windows.Controls.Button btn && btn.CommandParameter is Channel c2) ch = c2;

            if (ch != null)
            {
                ch.IsFavorite = !ch.IsFavorite;
                await _db.SaveChannelAsync(ch);
                _vm.LoadData();
            }
        }

        private void DeleteContext_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.CommandParameter is Channel ch)
            {
                if (System.Windows.MessageBox.Show($"{ch.Name} silinecek. Emin misiniz?", "Kanal Sil", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    _db.ExecuteRawNonQuery($"DELETE FROM Channels WHERE Id='{ch.Id}'");
                    _vm.LoadData();
                }
            }
        }
    }
}
