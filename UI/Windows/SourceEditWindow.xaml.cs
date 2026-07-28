using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
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
        public ObservableCollection<Channel> Channels { get; set; } = new ObservableCollection<Channel>();

        public SourceEditWindow(string sourceUrl)
        {
            InitializeComponent();
            _sourceUrl = sourceUrl;
            SourceTitle.Text = $"Kaynak: {sourceUrl}";
            LoadChannels();
        }

        private void LoadChannels()
        {
            var list = _db.GetChannelsBySource(_sourceUrl);
            Channels.Clear();
            foreach (var c in list) Channels.Add(c);
            ChannelList.ItemsSource = Channels;
            SourceStats.Text = $"{Channels.Count} Kanal bulundu.";
        }

        private async void TestLinks_Click(object sender, RoutedEventArgs e)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            foreach (var ch in Channels)
            {
                ch.Notes = "⏳ Test ediliyor...";
                try
                {
                    var sw = Stopwatch.StartNew();
                    var request = new HttpRequestMessage(HttpMethod.Head, ch.Url.Split(',')[0]);
                    var response = await client.SendAsync(request);
                    sw.Stop();

                    if (response.IsSuccessStatusCode) ch.Notes = $"✅ {sw.ElapsedMilliseconds}ms";
                    else ch.Notes = $"❌ Hata: {(int)response.StatusCode}";
                }
                catch { ch.Notes = "💀 Kapalı"; }
            }
        }

        private void DeleteDead_Click(object sender, RoutedEventArgs e)
        {
            var dead = Channels.Where(c => c.Notes.Contains("💀") || c.Notes.Contains("Hata")).ToList();
            if (dead.Count == 0) return;

            if (System.Windows.MessageBox.Show($"{dead.Count} adet çalışmayan kanal silinecek. Emin misiniz?", "Ölü Link Temizliği", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                foreach (var d in dead)
                {
                    _db.ExecuteRawNonQuery($"DELETE FROM Channels WHERE Id='{d.Id}'");
                    Channels.Remove(d);
                }
                SourceStats.Text = $"{Channels.Count} Kanal kaldı.";
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            foreach (var ch in Channels)
            {
                await _db.SaveChannelAsync(ch);
            }
            System.Windows.MessageBox.Show("Değişiklikler kaydedildi.");
            this.Close();
        }
    }
}
