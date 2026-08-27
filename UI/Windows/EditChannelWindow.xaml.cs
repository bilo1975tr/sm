using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.RegularExpressions;
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

        public ObservableCollection<StringWrapper> TempNameList { get; set; } = new ObservableCollection<StringWrapper>();
        public ObservableCollection<StringWrapper> TempUrlList { get; set; } = new ObservableCollection<StringWrapper>();
        public ObservableCollection<StringWrapper> TempLogoList { get; set; } = new ObservableCollection<StringWrapper>();
        public ObservableCollection<StringWrapper> TempEpgList { get; set; } = new ObservableCollection<StringWrapper>();
        public ObservableCollection<StringWrapper> TempEpgUrlList { get; set; } = new ObservableCollection<StringWrapper>();

        public ObservableCollection<LogoSearchResult> LogoSearchResults { get; set; } = new ObservableCollection<LogoSearchResult>();
        public ObservableCollection<EpgChannelSearchResult> EpgSearchResults { get; set; } = new ObservableCollection<EpgChannelSearchResult>();

        private string _languageText = "";
        public string LanguageText
        {
            get => _languageText;
            set
            {
                _languageText = value;
                _channel.Language = value;
                OnPropertyChanged();
            }
        }

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

            // Handle 'und' (undefined) language gracefully without hardcoding
            if (string.Equals(channel.Language, "und", StringComparison.OrdinalIgnoreCase))
            {
                _channel.Language = "";
            }
            LanguageText = _channel.Language ?? "";

            // Load multi-alternative data
            foreach (var n in channel.GetNamesList()) TempNameList.Add(new StringWrapper { Value = n });
            foreach (var u in channel.GetUrlList()) TempUrlList.Add(new StringWrapper { Value = u });
            foreach (var l in channel.GetLogoList()) TempLogoList.Add(new StringWrapper { Value = l });
            foreach (var e in channel.GetEpgIdList()) TempEpgList.Add(new StringWrapper { Value = e });
            var urls = (channel.EpgUrl ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var u in urls) TempEpgUrlList.Add(new StringWrapper { Value = u.Trim() });

            if (TempNameList.Count == 0 && !string.IsNullOrWhiteSpace(channel.Name))
                TempNameList.Add(new StringWrapper { Value = channel.Name });

            if (TempLogoList.Count > 0) SelectedLogoUrl = TempLogoList[0].Value;

            this.DataContext = this;
            this.MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Left) DragMove(); };

            StreamMesh.Core.Utils.LogService.LogInfo($"[EditChannel] Opened editor for channel '{channel.Name}' (Lang: {LanguageText})");
        }

        private void LangPill_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string tag)
            {
                LanguageText = tag;
                StreamMesh.Core.Utils.LogService.LogInfo($"[EditChannel] Language set to '{tag}'");
            }
        }

        private void ToggleLogoSearch_Click(object sender, RoutedEventArgs e)
        {
            LogoSearchPanel.Visibility = LogoSearchPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (LogoSearchPanel.Visibility == Visibility.Visible)
            {
                LogoSearchQueryBox.Text = ChannelUtils.GetCleanName(_channel.Name);
                DoLogoSearch_Click(sender, e);
            }
        }

        private async void DoLogoSearch_Click(object sender, RoutedEventArgs e)
        {
            string query = LogoSearchQueryBox.Text;
            string sourceFilter = "ALL";
            if (LogoSourceCombo.SelectedIndex == 1) sourceFilter = "TV_LOGOS";
            else if (LogoSourceCombo.SelectedIndex == 2) sourceFilter = "IPTV_ORG";
            else if (LogoSourceCombo.SelectedIndex == 3) sourceFilter = "CLEARBIT";

            LogoSearchResults.Clear();
            var results = await LogoSearchEngine.SearchLogosAsync(query, sourceFilter);
            foreach (var r in results) LogoSearchResults.Add(r);

            StreamMesh.Core.Utils.LogService.LogInfo($"[EditChannel] Logo search completed for '{query}', found {results.Count} candidates.");
        }

        private void SelectSearchResultLogo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.DataContext is LogoSearchResult res)
            {
                TempLogoList.Insert(0, new StringWrapper { Value = res.Url });
                SelectedLogoUrl = res.Url;
                StreamMesh.Core.Utils.LogService.LogInfo($"[EditChannel] Added logo URL '{res.Url}' from {res.Source}");
                System.Windows.MessageBox.Show($"Logo kütüphaneye eklendi ve birincil logo yapıldı:\n{res.Name}", "Logo Eklendi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ToggleEpgSearch_Click(object sender, RoutedEventArgs e)
        {
            EpgSearchPanel.Visibility = EpgSearchPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (EpgSearchPanel.Visibility == Visibility.Visible)
            {
                EpgSearchQueryBox.Text = ChannelUtils.GetCleanName(_channel.Name);
                DoEpgSearch_Click(sender, e);
            }
        }

        private async void DoEpgSearch_Click(object sender, RoutedEventArgs e)
        {
            string query = EpgSearchQueryBox.Text;
            bool allSources = EpgScopeCombo.SelectedIndex == 0;

            EpgSearchResults.Clear();
            var results = await _db.SearchEpgChannelsAsync(query, allSources);
            foreach (var r in results) EpgSearchResults.Add(r);

            StreamMesh.Core.Utils.LogService.LogInfo($"[EditChannel] EPG search completed for '{query}', scope: {(allSources ? "All" : "Current")}, found {results.Count} EPG items.");
        }

        private void SelectSearchResultEpg_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.DataContext is EpgChannelSearchResult res)
            {
                // Add EpgId to the top
                var existingId = TempEpgList.FirstOrDefault(x => x.Value.Equals(res.EpgId, StringComparison.OrdinalIgnoreCase));
                if (existingId != null) TempEpgList.Remove(existingId);
                TempEpgList.Insert(0, new StringWrapper { Value = res.EpgId });

                // Also store the Source URL to prioritize this source later
                if (!string.IsNullOrEmpty(res.SourceUrl))
                {
                    if (!TempEpgUrlList.Any(x => x.Value.Equals(res.SourceUrl, StringComparison.OrdinalIgnoreCase)))
                    {
                        TempEpgUrlList.Insert(0, new StringWrapper { Value = res.SourceUrl });
                    }
                }

                StreamMesh.Core.Utils.LogService.LogInfo($"[EditChannel] Added EPG ID '{res.EpgId}' and Source '{res.SourceUrl}' for channel '{_channel.Name}'");
                System.Windows.MessageBox.Show($"EPG ID ve Kaynak URL eklendi. Bu ID artık öncelikle bu kaynaktan okunacak:\n{res.EpgId}", "EPG ID Eklendi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void LogoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LogoList.SelectedItem is StringWrapper sw) SelectedLogoUrl = sw.Value;
        }

        private void AddName_Click(object sender, RoutedEventArgs e) => TempNameList.Add(new StringWrapper { Value = "" });
        private void RemoveName_Click(object sender, RoutedEventArgs e) { if (sender is System.Windows.Controls.Button b && b.DataContext is StringWrapper sw) TempNameList.Remove(sw); }
        private void SetPrimaryName_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b && b.DataContext is StringWrapper sw)
            {
                TempNameList.Remove(sw);
                TempNameList.Insert(0, sw);
            }
        }

        private void AddUrl_Click(object sender, RoutedEventArgs e) => TempUrlList.Add(new StringWrapper { Value = "" });
        private void RemoveUrl_Click(object sender, RoutedEventArgs e) { if (sender is System.Windows.Controls.Button b && b.DataContext is StringWrapper sw) TempUrlList.Remove(sw); }
        private void SetPrimaryUrl_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b && b.DataContext is StringWrapper sw)
            {
                TempUrlList.Remove(sw);
                TempUrlList.Insert(0, sw);
            }
        }

        private void AddLogo_Click(object sender, RoutedEventArgs e) => TempLogoList.Add(new StringWrapper { Value = "" });
        private void RemoveLogo_Click(object sender, RoutedEventArgs e) { if (sender is System.Windows.Controls.Button b && b.DataContext is StringWrapper sw) TempLogoList.Remove(sw); }
        private void SetPrimaryLogo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b && b.DataContext is StringWrapper sw)
            {
                TempLogoList.Remove(sw);
                TempLogoList.Insert(0, sw);
                SelectedLogoUrl = sw.Value;
            }
        }

        private void AddEpg_Click(object sender, RoutedEventArgs e) => TempEpgList.Add(new StringWrapper { Value = "" });
        private void RemoveEpg_Click(object sender, RoutedEventArgs e) { if (sender is System.Windows.Controls.Button b && b.DataContext is StringWrapper sw) TempEpgList.Remove(sw); }
        private void RemoveEpgUrl_Click(object sender, RoutedEventArgs e) { if (sender is System.Windows.Controls.Button b && b.DataContext is StringWrapper sw) TempEpgUrlList.Remove(sw); }
        private void SetPrimaryEpg_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b && b.DataContext is StringWrapper sw)
            {
                TempEpgList.Remove(sw);
                TempEpgList.Insert(0, sw);
            }
        }

        private void OpenMergeDialog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var mergeWin = new MergeChannelsWindow(_channel)
                {
                    Owner = this
                };

                if (mergeWin.ShowDialog() == true)
                {
                    // Reload all fields to reflect merged data
                    TempNameList.Clear();
                    foreach (var n in _channel.GetNamesList()) TempNameList.Add(new StringWrapper { Value = n });

                    TempUrlList.Clear();
                    foreach (var u in _channel.GetUrlList()) TempUrlList.Add(new StringWrapper { Value = u });

                    TempLogoList.Clear();
                    foreach (var l in _channel.GetLogoList()) TempLogoList.Add(new StringWrapper { Value = l });

                    TempEpgList.Clear();
                    foreach (var ep in _channel.GetEpgIdList()) TempEpgList.Add(new StringWrapper { Value = ep });

                    TempEpgUrlList.Clear();
                    var urls = (_channel.EpgUrl ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var u in urls) TempEpgUrlList.Add(new StringWrapper { Value = u.Trim() });

                    if (TempLogoList.Count > 0) SelectedLogoUrl = TempLogoList[0].Value;
                    LanguageText = _channel.Language ?? "";

                    OnPropertyChanged(nameof(ChannelObj));
                    OnPropertyChanged(nameof(LanguageText));
                    OnPropertyChanged(nameof(SelectedLogoUrl));
                }
            }
            catch (Exception ex)
            {
                StreamMesh.Core.Utils.LogService.LogError("EditChannelWindow.OpenMergeDialog_Click failed", ex);
                System.Windows.MessageBox.Show($"Kanal birleştirme penceresi açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AutoMatch_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string name = _channel.PrimaryName;
                if (string.IsNullOrWhiteSpace(name)) return;

                // Use the unified index-based logo matching (strictly following tv-logos convention)
                string? indexedLogo = ChannelEnricher.GetLogoFromIndex(name);

                if (!string.IsNullOrEmpty(indexedLogo))
                {
                    TempLogoList.Clear();
                    TempLogoList.Add(new StringWrapper { Value = indexedLogo });
                    SelectedLogoUrl = indexedLogo;
                }
                else
                {
                    System.Windows.MessageBox.Show("Veritabanında uygun bir logo bulunamadı. Lütfen manuel aramayı deneyin.", "Logo Bulunamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            var validNames = TempNameList.Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => x.Value.Trim()).ToList();
            if (validNames.Count > 0) _channel.Name = string.Join(", ", validNames);

            _channel.Url = string.Join(",", TempUrlList.Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => x.Value.Trim()));
            _channel.LogoUrl = string.Join(",", TempLogoList.Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => x.Value.Trim()));

            string newEpgId = string.Join(",", TempEpgList.Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => x.Value.Trim()));
            if (_channel.EpgId != newEpgId)
            {
                _channel.EpgId = newEpgId;
                _channel.IsEpgLocked = true; // Mark as locked because user manually changed EPG list
            }

            _channel.EpgUrl = string.Join(",", TempEpgUrlList.Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => x.Value.Trim()));

            await _db.SaveChannelAsync(_channel);
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
