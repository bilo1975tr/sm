using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using StreamMesh.Models;
using StreamMesh.Services;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StreamMesh.Windows
{
    public partial class SourceEditorWindow : Window
    {
        private string _playlistUrl;
        private List<ChannelEditViewModel> _viewModels;
        private DatabaseService _dbService;

        public SourceEditorWindow(string playlistUrl)
        {
            InitializeComponent();
            _playlistUrl = playlistUrl;
            _dbService = new DatabaseService();
            SourceTitleText.Text = $"Kaynak: {playlistUrl}";
            LoadChannels();
        }

        private void LoadChannels()
        {
            var channels = _dbService.GetChannelsByPlaylistUrl(_playlistUrl);
            _viewModels = channels.Select(c => {
                var vm = new ChannelEditViewModel { 
                    Id = c.Id,
                    Name = c.Name,
                    Language = c.Language,
                    Category = c.Category,
                    GroupTitle = c.GroupTitle,
                    IsSelected = false
                };
                vm.PropertyChanged += (s, e) => { if(e.PropertyName == nameof(ChannelEditViewModel.IsSelected)) UpdateSelectionCount(); };
                return vm;
            }).ToList();
            
            ChannelsDataGrid.ItemsSource = _viewModels;
            UpdateSelectionCount();
        }

        private void ApplyLanguageBtn_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = _viewModels.Where(v => v.IsSelected).ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("Lütfen önce listeden kanal seçin (kutucukları işaretleyin).", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string targetLang = (LanguageCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrEmpty(targetLang)) return;

            var ids = selectedItems.Select(s => s.Id).ToList();
            _dbService.BulkUpdateLanguage(ids, targetLang);
            
            // UI Update
            foreach (var item in selectedItems) item.Language = targetLang;
            
            LogService.Log($"Bulk Language Update: {selectedItems.Count} channels set to {targetLang}");
        }

        private void SmartGuessBtn_Click(object sender, RoutedEventArgs e)
        {
            int guessedCount = 0;
            foreach (var item in _viewModels)
            {
                string name = item.Name?.ToLower() ?? "";
                string newLang = null;

                // Smart Detection Logic
                if (name.Contains("tr ") || name.Contains(" tr") || name.Contains("türkiye") || name.Contains("turkish") || name.Contains("|tr") || name.Contains("[tr]"))
                    newLang = "Türkçe";
                else if (name.Contains("de ") || name.Contains(" de") || name.Contains("germany") || name.Contains("german") || name.Contains("|de") || name.Contains("[de]"))
                    newLang = "Almanca";
                else if (name.Contains("en ") || name.Contains(" en") || name.Contains("usa") || name.Contains("uk") || name.Contains("english") || name.Contains("|en") || name.Contains("[en]"))
                    newLang = "İngilizce";
                else if (name.Contains("fr ") || name.Contains(" fr") || name.Contains("france") || name.Contains("french") || name.Contains("|fr") || name.Contains("[fr]"))
                    newLang = "Fransızca";
                else if (name.Contains("es ") || name.Contains(" es") || name.Contains("spain") || name.Contains("spanish") || name.Contains("|es") || name.Contains("[es]"))
                    newLang = "İspanyolca";
                else if (name.Contains("az ") || name.Contains(" az") || name.Contains("azerbaijan") || name.Contains("azer") || name.Contains("|az") || name.Contains("[az]"))
                    newLang = "Azerice";

                if (newLang != null)
                {
                    item.Language = newLang; // This will trigger property change if we implement it correctly
                    item.IsSelected = true; 
                    guessedCount++;
                }
            }

            if (guessedCount > 0)
            {
                MessageBox.Show($"{guessedCount} kanal için dil tahmini yapıldı. 'Dili Uygula' butonuna basarak kaydedebilirsiniz.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Kanal isimlerinden dil tahmini yapılamadı.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SelectAllBtn_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _viewModels) item.IsSelected = true;
        }

        private void DeselectAllBtn_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _viewModels) item.IsSelected = false;
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void UpdateSelectionCount()
        {
            int count = _viewModels?.Count(v => v.IsSelected) ?? 0;
            SelectionCountText.Text = $"{count} kanal seçili";
        }
    }

    public class ChannelEditViewModel : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string Name { get; set; }
        
        private string _language;
        public string Language { 
            get => _language; 
            set { _language = value; OnPropertyChanged(); } 
        }

        public string Category { get; set; }
        public string GroupTitle { get; set; }

        private bool _isSelected;
        public bool IsSelected { 
            get => _isSelected; 
            set { _isSelected = value; OnPropertyChanged(); } 
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
