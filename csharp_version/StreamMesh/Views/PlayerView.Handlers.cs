using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using StreamMesh.Models;
using StreamMesh.Services;

namespace StreamMesh.Views
{
    public partial class PlayerView
    {
        private void MenuBtn_Click(object sender, RoutedEventArgs e)
        {
            SidebarBorder.Visibility = SidebarBorder.Visibility == Visibility.Visible 
                ? Visibility.Collapsed 
                : Visibility.Visible;
            LoadChannelsFromDb();
        }

        private void PlayPauseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer == null) return;
            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.SetPause(true);
                PlayPauseBtn.Content = "▶";
                StatusTextBlock.Text = "Duraklatıldı (Tampon Kaydediliyor)";
            }
            else
            {
                _mediaPlayer.SetPause(false);
                PlayPauseBtn.Content = "⏸";
                StatusTextBlock.Text = "Oynatılıyor";
            }
        }

        private void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            _mediaPlayer?.Stop();
            PlayPauseBtn.Content = "▶";
            StatusTextBlock.Text = "Durduruldu";
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Volume = (int)e.NewValue;
            }
        }

        private void RatioBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer == null) return;
            
            _currentRatioIndex = (_currentRatioIndex + 1) % _ratios.Length;
            string ratio = _ratios[_currentRatioIndex];
            
            // VLC Aspect Ratio setting
            try {
                // VLC expects "16:9", "4:3", etc.
                _mediaPlayer.AspectRatio = string.IsNullOrEmpty(ratio) ? null : ratio;
            } catch { }

            // UI Stretch setting for better user experience
            if (string.IsNullOrEmpty(ratio))
            {
                VideoImage.Stretch = Stretch.Uniform;
                StatusTextBlock.Text = "Ratio: Normal";
            }
            else if (ratio == "1:1") // Special case for Fill
            {
                 VideoImage.Stretch = Stretch.Fill;
                 StatusTextBlock.Text = "Ratio: Tam Ekran (Uzat/Yay)";
            }
            else
            {
                // Custom ratio might need Uniform to keep proportions within the selected ratio
                VideoImage.Stretch = Stretch.Uniform;
                StatusTextBlock.Text = "Ratio: " + ratio;
            }
        }

        private void OsnBtn_Click(object sender, RoutedEventArgs e)
        {
            _isOsnEnabled = !_isOsnEnabled;
            OsnBtn.Background = _isOsnEnabled ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4ade80")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33ffffff"));
            OsnBtn.Foreground = _isOsnEnabled ? Brushes.Black : Brushes.White;
            StatusTextBlock.Text = _isOsnEnabled ? "OSN (Ses Normalizasyonu) Aktif (Sonraki yayında etki eder)" : "OSN Devre Dışı (Sonraki yayında etki eder)";
            // if (_currentChannel != null && _mediaPlayer != null && _mediaPlayer.IsPlaying) await PlayChannelAsync(_currentChannel); // Artık yayını koparmaması için yeniden başlatmayı kaldırdık
        }

        private void GoBtn_Click(object sender, RoutedEventArgs e)
        {
            _isGoEnabled = !_isGoEnabled;
            GoBtn.Background = _isGoEnabled ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4ade80")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33ffffff"));
            GoBtn.Foreground = _isGoEnabled ? Brushes.Black : Brushes.White;
            StatusTextBlock.Text = _isGoEnabled ? "GO (Görüntü Onarıcı) Aktif" : "GO Devre Dışı";
            
            if (_mediaPlayer != null)
            {
                try
                {
                    _mediaPlayer.SetAdjustInt(VideoAdjustOption.Enable, _isGoEnabled ? 1 : 0);
                    if (_isGoEnabled)
                    {
                        _mediaPlayer.SetAdjustFloat(VideoAdjustOption.Contrast, 1.25f);
                        _mediaPlayer.SetAdjustFloat(VideoAdjustOption.Brightness, 1.05f);
                        _mediaPlayer.SetAdjustFloat(VideoAdjustOption.Saturation, 1.15f);
                    }
                }
                catch { }
            }
        }

        private void FullscreenBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow window)
            {
                bool isCurrentlyFullscreen = window.WindowState == WindowState.Maximized && window.WindowStyle == WindowStyle.None;
                window.ToggleFullscreen(!isCurrentlyFullscreen);
            }
        }

        private void ChannelListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ChannelListView.SelectedItem is Channel selectedChannel)
            {
                LoadChannel(selectedChannel);
                ChannelListView.SelectedItem = null;
            }
        }

        private void OsdGrid_MouseMove(object sender, MouseEventArgs e) => ShowOsd();

        private void OsdGrid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) FullscreenBtn_Click(null, null);
            else ShowOsd();
        }

        private void OsdTimer_Tick(object sender, EventArgs e)
        {
            _osdTimer.Stop();
            HideOsd();
        }
    }
}
