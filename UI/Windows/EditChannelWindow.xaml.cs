using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StreamMesh.Models;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media;

namespace StreamMesh.UI.Windows
{
    public partial class EditChannelWindow : Window, INotifyPropertyChanged
    {
        private Channel _channel;
        private readonly DatabaseEngine _db = new DatabaseEngine();

        public class StringWrapper : INotifyPropertyChanged
        {
            private string _value = "";
            public string Value { get => _value; set { _value = value; OnPropertyChanged(); } }
            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public ObservableCollection<StringWrapper> TempUrlList { get; set; } = new ObservableCollection<StringWrapper>();
        public ObservableCollection<StringWrapper> TempLogoList { get; set; } = new ObservableCollection<StringWrapper>();
        public ObservableCollection<StringWrapper> TempEpgList { get; set; } = new ObservableCollection<StringWrapper>();

        private string? _selectedLogoUrl;
        public string? SelectedLogoUrl
        {
            get => _selectedLogoUrl;
            set { _selectedLogoUrl = value; OnPropertyChanged(); }
        }

        public Channel ChannelObj => _channel;

        public EditChannelWindow(Channel channel)
        {
            _channel = channel;
            InitializeComponent();

            // Load data
            foreach (var u in channel.GetUrlList()) TempUrlList.Add(new StringWrapper { Value = u });
            foreach (var l in channel.GetLogoList()) TempLogoList.Add(new StringWrapper { Value = l });
            foreach (var e in channel.GetEpgIdList()) TempEpgList.Add(new StringWrapper { Value = e });

            if (TempLogoList.Count > 0) SelectedLogoUrl = TempLogoList[0].Value;

            this.DataContext = this;
            this.MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Left) DragMove(); };
        }

        private void LogoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LogoList.SelectedItem is StringWrapper sw) SelectedLogoUrl = sw.Value;
        }

        private void AddUrl_Click(object sender, RoutedEventArgs e) => TempUrlList.Add(new StringWrapper { Value = "" });
        private void RemoveUrl_Click(object sender, RoutedEventArgs e) { if (sender is System.Windows.Controls.Button b && b.DataContext is StringWrapper sw) TempUrlList.Remove(sw); }

        private void AddLogo_Click(object sender, RoutedEventArgs e) => TempLogoList.Add(new StringWrapper { Value = "" });
        private void RemoveLogo_Click(object sender, RoutedEventArgs e) { if (sender is System.Windows.Controls.Button b && b.DataContext is StringWrapper sw) TempLogoList.Remove(sw); }

        private void AddEpg_Click(object sender, RoutedEventArgs e) => TempEpgList.Add(new StringWrapper { Value = "" });
        private void RemoveEpg_Click(object sender, RoutedEventArgs e) { if (sender is System.Windows.Controls.Button b && b.DataContext is StringWrapper sw) TempEpgList.Remove(sw); }

        private void AutoMatch_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string name = _channel.Name ?? "";
                if (string.IsNullOrWhiteSpace(name)) return;

                // 1. Logo Match (Improved logic with Index)
                string searchKey = name.ToLower().Replace(" ", "").Replace("hd", "").Replace("sd", "").Replace("-", "");
                string? file = _db.FindLogoInIndex(searchKey);

                if (file != null)
                {
                    string suggestion = $"https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/{file}";
                    TempLogoList.Clear();
                    TempLogoList.Add(new StringWrapper { Value = suggestion });
                    SelectedLogoUrl = suggestion;
                }
                else
                {
                    // Fallback to Clearbit
                    string suggestion = $"https://logo.clearbit.com/{searchKey}.com";
                    TempLogoList.Add(new StringWrapper { Value = suggestion });
                    SelectedLogoUrl = suggestion;
                }

                // 2. EPG Match
                if (TempEpgList.Count == 0 || string.IsNullOrWhiteSpace(TempEpgList[0].Value))
                {
                    string epgId = name.ToUpper().Replace(" ", ".").Replace("HD", "").Trim('.');
                    TempEpgList.Add(new StringWrapper { Value = $"{epgId}.tr" });
                }

                System.Windows.MessageBox.Show("Akıllı eşleme tamamlandı. Lütfen listeden logoları seçerek önizlemeyi kontrol edin.", "Akıllı Sihirbaz", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            _channel.Url = string.Join(",", TempUrlList.Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => x.Value));
            _channel.LogoUrl = string.Join(",", TempLogoList.Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => x.Value));
            _channel.EpgId = string.Join(",", TempEpgList.Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => x.Value));

            await _db.SaveChannelAsync(_channel);
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
