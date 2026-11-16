using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StreamMesh.Models;
using StreamMesh.UI.ViewModels;
using StreamMesh.UI.Windows;
using StreamMesh.Core.Utils;

namespace StreamMesh.UI.Views
{
    public class MediaCardTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? TvTemplate { get; set; }
        public DataTemplate? VodTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is Channel ch && ch.IsVodMedia)
            {
                return VodTemplate ?? base.SelectTemplate(item, container);
            }
            return TvTemplate ?? base.SelectTemplate(item, container);
        }
    }

    public partial class HomeView : System.Windows.Controls.UserControl
    {
        private HomeViewModel _vm;
        private System.Windows.Point _dragStartPoint;
        private bool _suppressNextMouseUp;

        public HomeView()
        {
            InitializeComponent();
            _vm = new HomeViewModel();
            DataContext = _vm;
        }

        private void Card_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_vm.ShowVodHero && sender is FrameworkElement fe && fe.DataContext is Channel item)
            {
                _vm.CurrentBackdrop = !string.IsNullOrEmpty(item.BackdropUrl) ? item.BackdropUrl : item.PrimaryLogoUrl;
            }
        }

        private void OpenVodItem(Channel item)
        {
            if (item is SeriesGroup series)
            {
                var seriesWin = new SeriesDetailsWindow(series);
                seriesWin.Owner = Window.GetWindow(this);
                seriesWin.ShowDialog();
                return;
            }

            var details = new MediaDetailsWindow(item);
            details.Owner = Window.GetWindow(this);
            details.Show();
        }

        private void Card_Click(object sender, MouseButtonEventArgs e)
        {
            if (_suppressNextMouseUp)
            {
                _suppressNextMouseUp = false;
                return;
            }

            try
            {
                if (sender is FrameworkElement fe && fe.DataContext is Channel item)
                {
                    if (item.IsVodMedia)
                    {
                        _vm.SelectHeroItem(item, enrichIfMissing: true);
                        return;
                    }

                    MainWindow.Instance?.LoadChannelToPlayer(item);
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("HomeView.Card_Click error", ex);
                System.Windows.MessageBox.Show($"İçerik açılırken bir hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Drag & Drop Merge Logic (Live TV) + Double-click Detection (VOD)
        private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);

            if (e.ClickCount >= 2 && sender is FrameworkElement fe && fe.DataContext is Channel item && item.IsVodMedia)
            {
                _suppressNextMouseUp = true;
                e.Handled = true;
                try
                {
                    OpenVodItem(item);
                }
                catch (Exception ex)
                {
                    LogService.LogError("HomeView.DoubleClick open VOD error", ex);
                }
            }
        }

        private void Card_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                System.Windows.Point pos = e.GetPosition(null);
                if (Math.Abs(pos.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(pos.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is FrameworkElement fe && fe.DataContext is Channel ch && !(ch is SeriesGroup))
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

        private void HeroPlay_Click(object sender, RoutedEventArgs e)
        {
            var item = _vm.SelectedHeroItem;
            if (item == null) return;

            if (item is SeriesGroup series)
            {
                var seriesWin = new SeriesDetailsWindow(series);
                seriesWin.Owner = Window.GetWindow(this);
                seriesWin.ShowDialog();
                return;
            }

            MainWindow.Instance?.LoadChannelToPlayer(item);
        }

        private void HeroDetails_Click(object sender, RoutedEventArgs e)
        {
            var item = _vm.SelectedHeroItem;
            if (item == null) return;
            OpenVodItem(item);
        }

        private async void HeroFav_Click(object sender, RoutedEventArgs e)
        {
            var item = _vm.SelectedHeroItem;
            if (item == null) return;
            await _vm.ToggleFavoriteAsync(item);
        }

        private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            _vm.ToggleGroupSidebar();
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

        private async void RefreshSource_Click(object sender, RoutedEventArgs e)
        {
            string? sourceId = null;
            if (sender is FrameworkElement fe)
            {
                if (fe.DataContext is SourceFilterItem sfi) sourceId = sfi.Id;
                else if (fe.Tag is string s) sourceId = s;
            }

            if (!string.IsNullOrEmpty(sourceId))
            {
                await _vm.RefreshSourceAsync(sourceId);
            }
        }

        private async void PurgeSource_Click(object sender, RoutedEventArgs e)
        {
            string? sourceId = null;
            if (sender is FrameworkElement fe)
            {
                if (fe.DataContext is SourceFilterItem sfi) sourceId = sfi.Id;
                else if (fe.Tag is string s) sourceId = s;
            }

            if (!string.IsNullOrEmpty(sourceId))
            {
                await _vm.PurgeSourceAsync(sourceId);
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
        private void Prev_Click(object sender, RoutedEventArgs e) { _vm.PrevPage(); }
        private void Next_Click(object sender, RoutedEventArgs e) { _vm.NextPage(); }

        private void PlayContext_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.CommandParameter is Channel ch)
            {
                if (ch is SeriesGroup series)
                {
                    OpenVodItem(series);
                    return;
                }
                MainWindow.Instance?.LoadChannelToPlayer(ch);
            }
        }

        private void DetailsContext_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.CommandParameter is Channel ch)
            {
                OpenVodItem(ch);
            }
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
            e.Handled = true;
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

        private void ToggleView_Click(object sender, RoutedEventArgs e)
        {
            if (ChannelGrid.Visibility == Visibility.Visible)
            {
                ChannelGrid.Visibility = Visibility.Collapsed;
                ChannelList.Visibility = Visibility.Visible;
            }
            else
            {
                ChannelGrid.Visibility = Visibility.Visible;
                ChannelList.Visibility = Visibility.Collapsed;
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
    }
}
