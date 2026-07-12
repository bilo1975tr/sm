using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using LibVLCSharp.Shared;
using StreamMesh.Models;
using StreamMesh.Services;

namespace StreamMesh.Views
{
    public partial class PlayerView
    {
        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (_mediaPlayer == null) return;

            if (_currentChannel != null && TopOsd.Visibility == Visibility.Visible)
            {
                if (_currentEpg != null && _currentEpg.EndTime > _currentEpg.StartTime)
                {
                    var totalDuration = (_currentEpg.EndTime - _currentEpg.StartTime).TotalSeconds;
                    var elapsed = (DateTime.Now - _currentEpg.StartTime).TotalSeconds;
                    if (elapsed < 0) elapsed = 0;
                    if (elapsed > totalDuration) elapsed = totalDuration;
                    EpgProgressBar.Value = (elapsed / totalDuration) * 100;
                }
            }

            if (!_mediaPlayer.IsPlaying || _isDragging) return;

            long time = _mediaPlayer.Time;
            long length = _mediaPlayer.Length;

            if (length > 0)
            {
                // Kalınan yerden devam etme (Seek to Saved Progress)
                if (_needsSeekToProgress)
                {
                    _needsSeekToProgress = false;
                    var wp = _databaseService.GetWatchProgress(_currentChannel.Id);
                    if (wp != null && wp.Seconds > 0 && wp.Seconds < length - 5000)
                    {
                        _mediaPlayer.Time = wp.Seconds;
                        time = wp.Seconds; // Anlık UI güncellemesi için yerel değişkeni de güncelle
                        LogService.Log($"WatchProgress: Auto-seeked to {wp.Seconds}ms for channel {_currentChannel.Name}");
                    }
                }
                else
                {
                    // Periyodik izleme geçmişi kaydı (Her 5 saniyede bir - 10 ticks)
                    _saveProgressCounter++;
                    if (_saveProgressCounter >= 10)
                    {
                        _saveProgressCounter = 0;
                        _databaseService.SaveWatchProgress(_currentChannel.Id, _currentChannel.Name, time, length);
                    }
                }

                SeekSlider.Visibility = Visibility.Visible;
                TimeTextBlock.Visibility = Visibility.Visible;
                SeekSlider.Maximum = length;
                SeekSlider.Value = time;
                TimeSpan t = TimeSpan.FromMilliseconds(time);
                TimeSpan l = TimeSpan.FromMilliseconds(length);
                TimeTextBlock.Text = $"{t:mm\\:ss} / {l:mm\\:ss}";
            }
            else
            {
                SeekSlider.Visibility = Visibility.Collapsed;
                TimeTextBlock.Text = "CANLI";
            }
        }

        private void QualityBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer == null) return;
            var menu = QualityBtn.ContextMenu;
            if (menu == null) return;
            menu.Items.Clear();

            if (_currentYtVideoStreams != null && _currentYtVideoStreams.Count > 0)
            {
                foreach (var stv in _currentYtVideoStreams)
                {
                    var menuItem = new MenuItem { Header = stv.Item1, Tag = stv.Item2, IsCheckable = true, IsChecked = _currentYtVideoUrl == stv.Item2 };
                    menuItem.Click += (s, ev) => {
                        var clickedUrl = (string)((MenuItem)s).Tag;
                        if (clickedUrl != _currentYtVideoUrl) {
                            _currentYtVideoUrl = clickedUrl;
                            long currentTime = _mediaPlayer.Time;
                            var newMedia = new Media(_libVLC, new Uri(clickedUrl));
                            if (!string.IsNullOrEmpty(_currentYtAudioUrl)) newMedia.AddOption($":input-slave={_currentYtAudioUrl}");
                            if (currentTime > 0) newMedia.AddOption($":start-time={currentTime / 1000.0}");
                            ApplyFiltersToMedia(newMedia);
                            _mediaPlayer.Play(newMedia);
                            StatusTextBlock.Text = $"Kalite Değiştiriliyor...";
                        }
                    };
                    menu.Items.Add(menuItem);
                }
            }
            else
            {
                var tracks = _mediaPlayer.VideoTrackDescription;
                if (tracks == null || tracks.Length <= 1) {
                    StatusTextBlock.Text = "Kalite bilgisi alınamıyor (Yayın başlamalı)";
                    return;
                }

                MediaTrack[] mediaTracks = _mediaPlayer.Media?.Tracks;

                foreach (var track in tracks)
                {
                    if (track.Id == -1) continue;
                    string header = string.IsNullOrEmpty(track.Name) ? $"Track {track.Id}" : track.Name;
                    if (mediaTracks != null)
                    {
                        foreach (var mt in mediaTracks)
                        {
                            if (mt.TrackType == TrackType.Video && mt.Id == track.Id) {
                                if (mt.Data.Video.Height > 0) {
                                    header = $"{mt.Data.Video.Height}p";
                                    if (mt.Data.Video.Width > 0) header += $" ({mt.Data.Video.Width}x{mt.Data.Video.Height})";
                                }
                                break;
                            }
                        }
                    }

                    var menuItem = new MenuItem { Header = header, Tag = track.Id, IsCheckable = true, IsChecked = _mediaPlayer.VideoTrack == track.Id };
                    menuItem.Click += (s, ev) => {
                        _mediaPlayer.SetVideoTrack((int)((MenuItem)s).Tag);
                        StatusTextBlock.Text = $"Kalite: {((MenuItem)s).Header}";
                    };
                    menu.Items.Add(menuItem);
                }
            }
            menu.PlacementTarget = QualityBtn;
            menu.IsOpen = true;
        }

        private void SourceBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentChannel == null) return;
            var menu = SourceBtn.ContextMenu;
            if (menu == null) return;
            menu.Items.Clear();

            var urls = _currentChannel.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            int i = 1;
            foreach (var rawUrl in urls)
            {
                string url = rawUrl.Trim();
                string type = "M3U8";
                if (url.Contains("youtube.com") || url.Contains("youtu.be")) type = "YouTube";
                else if (url.StartsWith("acestream://")) type = "AceStream";

                string header = $"Kaynak {i} ({type})";
                if (url == _currentChannelUrl) header = $"⭐ [VARSAYILAN] Kaynak {i}";
                
                var menuItem = new MenuItem { Header = header, Tag = url, IsCheckable = true, IsChecked = _currentChannelUrl == url };
                
                // Add icon or tooltip
                menuItem.ToolTip = url;

                menuItem.Click += async (s, ev) => {
                    var clickedUrl = (string)((MenuItem)s).Tag;
                    if (clickedUrl == _currentChannelUrl) return;

                    // Temp channel with single URL but we keep the structure
                    var tempChannel = new Channel { 
                        Id = _currentChannel.Id,
                        Name = _currentChannel.Name, 
                        Url = clickedUrl, 
                        SourceType = type.ToUpper(), 
                        Category = _currentChannel.Category,
                        Language = _currentChannel.Language,
                        LogoUrl = _currentChannel.LogoUrl,
                        EpgId = _currentChannel.EpgId
                    };
                    await PlayChannelAsync(tempChannel);
                };
                menu.Items.Add(menuItem);
                i++;
            }

            if (menu.Items.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = "Kaynak Yok", IsEnabled = false });
            }

            menu.PlacementTarget = SourceBtn;
            menu.IsOpen = true;
        }

        private void SeekSlider_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isDragging = true;
        }

        private void SeekSlider_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_mediaPlayer == null || _mediaPlayer.Length <= 0) 
            {
                _isDragging = false;
                return;
            }

            double mousePos = e.GetPosition(SeekSlider).X;
            double percentage = mousePos / SeekSlider.ActualWidth;
            if (percentage < 0) percentage = 0;
            if (percentage > 1) percentage = 1;

            long seekTime = (long)(percentage * _mediaPlayer.Length);
            
            if (_currentYtVideoStreams != null && _currentYtVideoStreams.Count > 0)
            {
                var newMedia = new Media(_libVLC, new Uri(_currentYtVideoUrl));
                if (!string.IsNullOrEmpty(_currentYtAudioUrl)) newMedia.AddOption($":input-slave={_currentYtAudioUrl}");
                newMedia.AddOption($":start-time={seekTime / 1000.0}");
                ApplyFiltersToMedia(newMedia);
                _mediaPlayer.Play(newMedia);
                StatusTextBlock.Text = $"Atlanıyor...";
            }
            else
            {
                _mediaPlayer.Time = seekTime;
            }

            _isDragging = false;
            ResetOsdTimer();
        }

        private void AudioBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer == null) return;
            var menu = AudioBtn.ContextMenu;
            if (menu == null) return;
            menu.Items.Clear();

            var tracks = _mediaPlayer.AudioTrackDescription;
            if (tracks == null || tracks.Length <= 0)
            {
                StatusTextBlock.Text = "Ses kanalı bilgisi alınamıyor (Yayın başlamalı)";
                return;
            }

            foreach (var track in tracks)
            {
                string header = track.Name;
                if (track.Id == -1)
                {
                    header = "Sessiz (Devre Dışı)";
                }
                else if (string.IsNullOrEmpty(header))
                {
                    header = $"Ses Kanalı {track.Id}";
                }

                var menuItem = new MenuItem { Header = header, Tag = track.Id, IsCheckable = true, IsChecked = _mediaPlayer.AudioTrack == track.Id };
                menuItem.Click += (s, ev) => {
                    int trackId = (int)((MenuItem)s).Tag;
                    _mediaPlayer.SetAudioTrack(trackId);
                    StatusTextBlock.Text = $"Ses: {((MenuItem)s).Header}";
                };
                menu.Items.Add(menuItem);
            }

            menu.PlacementTarget = AudioBtn;
            menu.IsOpen = true;
        }
    }
}
