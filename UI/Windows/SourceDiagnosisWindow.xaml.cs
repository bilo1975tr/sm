using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;
using StreamMesh.Models;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media;
using StreamMesh.Core.Utils;

namespace StreamMesh.UI.Windows
{
    public partial class SourceDiagnosisWindow : Window
    {
        private readonly Channel _channel;
        private readonly DatabaseEngine _db = new DatabaseEngine();

        public class EpisodeDiagItem
        {
            public string DisplaySeasonEpisode { get; set; } = "";
            public string Name { get; set; } = "";
            public string Url { get; set; } = "";
        }

        public SourceDiagnosisWindow(Channel channel)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            InitializeComponent();
            PopulateData();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void PopulateData()
        {
            TxtHeaderChannelName.Text = _channel.Name ?? "İsimsiz Kanal";
            TxtPlaylistSource.Text = string.IsNullOrWhiteSpace(_channel.PlaylistUrl) ? "(Kaynak M3U bilgisi kayıtlı değil)" : _channel.PlaylistUrl;

            // Line number
            if (_channel.M3uLineNumber > 0)
            {
                TxtLineNumber.Text = $"Satır: #{_channel.M3uLineNumber:N0}";
            }
            else
            {
                TxtLineNumber.Text = "Satır: Kayıtlı değil (Aşağıdan 'Dosyada Canlı Ara' yapabilirsiniz)";
            }

            // Category & Group
            TxtCategoryGroup.Text = $"Kategori: {_channel.Category ?? "TV"} | Grup: {(!string.IsNullOrWhiteSpace(_channel.GroupTitle) ? _channel.GroupTitle : "Genel")}";

            // Stream URL
            TxtStreamUrl.Text = string.IsNullOrWhiteSpace(_channel.Url) ? "(Akış linki boş)" : _channel.Url;
            var accInfo = IptvAccountHelper.ParseAccountFromUrl(_channel.Url ?? "");
            if (accInfo != null && !string.IsNullOrEmpty(accInfo.Username))
            {
                BtnPurgeAccountDiag.Visibility = Visibility.Visible;
            }
            else
            {
                BtnPurgeAccountDiag.Visibility = Visibility.Collapsed;
            }

            // EPG
            string epgText = "";
            if (!string.IsNullOrWhiteSpace(_channel.EpgId)) epgText += $"tvg-id: {_channel.EpgId} ";
            var altNames = _channel.GetNamesList();
            if (altNames.Count > 1) epgText += $"| tvg-name: {string.Join(", ", altNames.Skip(1))}";
            TxtEpgInfo.Text = string.IsNullOrWhiteSpace(epgText) ? "EPG Tanımı Yok" : epgText;

            // Channel ID
            TxtChannelId.Text = _channel.Id ?? "-";

            // Raw M3U Block
            if (!string.IsNullOrWhiteSpace(_channel.RawM3uBlock))
            {
                TxtRawBlock.Text = _channel.RawM3uBlock;
            }
            else
            {
                TxtRawBlock.Text = "#EXTINF satırı önceden kaydedilmemiş.\nYukarıdaki 'Dosyada Canlı Ara' butonuna tıklayarak kaynak M3U dosyasını anında tarayabilirsiniz.";
            }

            // Check if SeriesGroup
            if (_channel is SeriesGroup sg)
            {
                StatusBanner.BorderBrush = System.Windows.Media.Brushes.MediumPurple;
                TxtStatusIcon.Text = "📁";
                TxtStatusTitle.Text = sg.SeasonCount > 0
                    ? $"Dizi Grubu / Klasörü ({sg.Episodes.Count} Bölüm, {sg.SeasonCount} Sezon)"
                    : $"Dizi Grubu / Klasörü ({sg.Episodes.Count} Bölüm / Parça)";
                TxtStatusDesc.Text = "Bu kayıt, M3U kaynağında dizi/sezon bölümleri tespit edildiği için sanal bir grup olarak listelenmektedir. Üst klasörün tek bir akış adresi yoktur; her bölümün bağımsız akış adresi mevcuttur.";

                SeriesGroupPanel.Visibility = Visibility.Visible;
                TxtSeriesCountSummary.Text = sg.SeasonCount > 0
                    ? $"{sg.SeasonCount} Sezon, {sg.Episodes.Count} Bölüm"
                    : $"{sg.Episodes.Count} Bölüm / Parça";

                var epList = new List<EpisodeDiagItem>();
                foreach (var ep in sg.Episodes)
                {
                    string displayEp = (ep.SeasonNumber > 0 || ep.EpisodeNumber > 0)
                        ? $"S{ep.SeasonNumber:D2}E{ep.EpisodeNumber:D2}"
                        : "Bölüm";
                    epList.Add(new EpisodeDiagItem
                    {
                        DisplaySeasonEpisode = displayEp,
                        Name = string.IsNullOrWhiteSpace(ep.CleanName) ? ep.Name : ep.CleanName,
                        Url = ep.Url
                    });
                }
                ListEpisodes.ItemsSource = epList;

                // If only 1 episode exists, offer quick conversion to standalone Movie
                if (sg.Episodes.Count == 1)
                {
                    BtnQuickFixConvert.Visibility = Visibility.Visible;
                }
            }
            else if (string.IsNullOrWhiteSpace(_channel.Url))
            {
                StatusBanner.BorderBrush = System.Windows.Media.Brushes.Coral;
                TxtStatusIcon.Text = "⚠️";
                TxtStatusTitle.Text = "Akış (Stream) Adresi Boş";
                TxtStatusDesc.Text = "M3U dosyasında bu #EXTINF satırının altında geçerli bir stream URL satırı bulunamadı veya satır geçersiz olduğu için atlandı.";
            }
            else
            {
                StatusBanner.BorderBrush = System.Windows.Media.Brushes.MediumSeaGreen;
                TxtStatusIcon.Text = "✔";
                TxtStatusTitle.Text = "Geçerli Akış Linki Mevcut";
                TxtStatusDesc.Text = "Kanal hem başlık bilgisine hem de doğrudan oynatılabilir bir akış linkine sahiptir.";
            }
        }

        private async void LiveScan_Click(object sender, RoutedEventArgs e)
        {
            BtnLiveScan.IsEnabled = false;
            LiveScannerPanel.Visibility = Visibility.Visible;
            TxtLiveScannerStatus.Text = "🔍 Kaynak dosya taranıyor...";
            TxtLiveScannerOutput.Text = "M3U kaynağı okunuyor, lütfen bekleyin...";

            try
            {
                string pathOrUrl = _channel.PlaylistUrl;
                if (string.IsNullOrWhiteSpace(pathOrUrl))
                {
                    TxtLiveScannerOutput.Text = "Kanalın kaynak M3U yolu (PlaylistUrl) boş. Tarama yapılamadı.";
                    return;
                }

                string[] lines;
                if (File.Exists(pathOrUrl))
                {
                    lines = await File.ReadAllLinesAsync(pathOrUrl);
                }
                else if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
                    string content = await client.GetStringAsync(pathOrUrl);
                    lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                }
                else
                {
                    TxtLiveScannerOutput.Text = $"Dosya bulunamadı veya erişilemiyor:\n{pathOrUrl}";
                    return;
                }

                int foundLine = -1;
                string searchTarget = _channel.Name ?? "";
                string urlTarget = _channel.Url ?? "";

                for (int i = 0; i < lines.Length; i++)
                {
                    string l = lines[i];
                    if (!string.IsNullOrWhiteSpace(urlTarget) && l.Contains(urlTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        foundLine = i + 1;
                        break;
                    }
                    if (!string.IsNullOrWhiteSpace(searchTarget) && l.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase) && l.Contains(searchTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        foundLine = i + 1;
                        break;
                    }
                }

                if (foundLine > 0)
                {
                    _channel.M3uLineNumber = foundLine;
                    TxtLineNumber.Text = $"Satır: #{foundLine:N0} (Doğrulandı)";

                    // Extract context lines (5 lines before and 5 lines after)
                    int start = Math.Max(0, foundLine - 6);
                    int end = Math.Min(lines.Length - 1, foundLine + 5);

                    var sb = new StringBuilder();
                    sb.AppendLine($"=== Satır #{foundLine} Çevresindeki M3U İçeriği ===");
                    for (int j = start; j <= end; j++)
                    {
                        string prefix = (j + 1 == foundLine) ? ">>> " : "    ";
                        sb.AppendLine($"{prefix}Satır {j + 1:D5}: {lines[j]}");
                    }

                    TxtLiveScannerOutput.Text = sb.ToString();
                    TxtLiveScannerStatus.Text = $"✔ Başarılı: #{foundLine}. satırda tespit edildi!";

                    // If RawM3uBlock was empty, update it
                    if (string.IsNullOrWhiteSpace(_channel.RawM3uBlock))
                    {
                        var rawBlockSb = new StringBuilder();
                        for (int j = Math.Max(0, foundLine - 1); j <= Math.Min(lines.Length - 1, foundLine + 1); j++)
                        {
                            rawBlockSb.AppendLine(lines[j]);
                        }
                        _channel.RawM3uBlock = rawBlockSb.ToString();
                        TxtRawBlock.Text = _channel.RawM3uBlock;
                    }

                    // Save updated diagnostic data to repository
                    await _db.SaveChannelAsync(_channel);
                }
                else
                {
                    TxtLiveScannerStatus.Text = "Sonuç Bulunamadı";
                    TxtLiveScannerOutput.Text = $"'{searchTarget}' araması M3U dosyasındaki {lines.Length:N0} satır arasında doğrudan eşleşmedi.\nM3U dosyası güncellenmiş veya kanal silinmiş olabilir.";
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("LiveScan_Click", ex);
                TxtLiveScannerStatus.Text = "Hata Oluştu";
                TxtLiveScannerOutput.Text = $"Tarama sırasında hata: {ex.Message}";
            }
            finally
            {
                BtnLiveScan.IsEnabled = true;
            }
        }

        private async void QuickFixConvert_Click(object sender, RoutedEventArgs e)
        {
            if (_channel is SeriesGroup sg && sg.Episodes.Count == 1)
            {
                var ep = sg.Episodes[0];
                var res = MessageBox.Show(
                    $"Bu dizi grubunda yalnızca 1 bölüm bulunmaktadır:\n\n{ep.Name}\nURL: {ep.Url}\n\nBu kaydı doğrudan tekil bir 'Film' yayınına dönüştürmek ve URL'yi bağlamak istiyor musunuz?",
                    "Tekil Filme Dönüştür",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res == MessageBoxResult.Yes)
                {
                    _channel.Url = ep.Url;
                    _channel.Category = "Film";
                    if (_channel.M3uLineNumber <= 0 && ep.M3uLineNumber > 0)
                    {
                        _channel.M3uLineNumber = ep.M3uLineNumber;
                    }
                    if (string.IsNullOrWhiteSpace(_channel.RawM3uBlock) && !string.IsNullOrWhiteSpace(ep.RawM3uBlock))
                    {
                        _channel.RawM3uBlock = ep.RawM3uBlock;
                    }

                    await _db.SaveChannelAsync(_channel);
                    MessageBox.Show("Kanal başarıyla tekil filme dönüştürüldü ve URL adresi bağlandı!", "İşlem Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                    PopulateData();
                }
            }
        }

        private void CopySource_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(TxtPlaylistSource.Text))
            {
                Clipboard.SetText(TxtPlaylistSource.Text);
                MessageBox.Show("Kaynak yolu panoya kopyalandı.", "Kopyalandı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CopyStreamUrl_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_channel.Url))
            {
                Clipboard.SetText(_channel.Url);
                MessageBox.Show("Akış (stream) URL adresi panoya kopyalandı.", "Kopyalandı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CopyRawBlock_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(TxtRawBlock.Text))
            {
                Clipboard.SetText(TxtRawBlock.Text);
                MessageBox.Show("Ham M3U bloğu panoya kopyalandı.", "Kopyalandı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CopyFullReport_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== StreamMesh M3U Kaynak Tanı Raporu ===");
            sb.AppendLine($"Kanal Adı: {_channel.Name}");
            sb.AppendLine($"Sistem ID: {_channel.Id}");
            sb.AppendLine($"Kategori: {_channel.Category} | Grup: {_channel.GroupTitle}");
            sb.AppendLine($"Kaynak M3U: {_channel.PlaylistUrl}");
            sb.AppendLine($"M3U Satır Numarası: #{_channel.M3uLineNumber}");
            sb.AppendLine($"Akış URL: {_channel.Url}");
            sb.AppendLine($"EPG ID: {_channel.EpgId}");
            sb.AppendLine();
            sb.AppendLine("--- Okunan Ham M3U Satırları ---");
            sb.AppendLine(_channel.RawM3uBlock);

            if (_channel is SeriesGroup sg)
            {
                sb.AppendLine();
                sb.AppendLine($"--- Dizi Bölüm Dökümü ({sg.Episodes.Count} Bölüm) ---");
                foreach (var ep in sg.Episodes)
                {
                    sb.AppendLine($"S{ep.SeasonNumber:D2}E{ep.EpisodeNumber:D2} - {ep.Name} => {ep.Url}");
                }
            }

            Clipboard.SetText(sb.ToString());
            MessageBox.Show("Tüm tanı raporu panoya kopyalandı. Dilediğiniz yere yapıştırabilirsiniz.", "Rapor Kopyalandı", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void PurgeAccountDiag_Click(object sender, RoutedEventArgs e)
        {
            var acc = IptvAccountHelper.ParseAccountFromUrl(_channel.Url ?? "");
            if (acc == null || string.IsNullOrWhiteSpace(acc.Username))
            {
                MessageBox.Show("Geçerli bir IPTV hesap bilgisi bulunamadı.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var counts = await IptvAccountHelper.CountAccountOccurrencesAsync(acc);
            string confirmMsg = $"IPTV HESAP BİLGİSİ:\n" +
                               $"• Sunucu: {acc.HostWithPort}\n" +
                               $"• Kullanıcı Adı: {acc.Username}\n\n" +
                               $"Bu hesaba ait veritabanında toplam {counts.totalUrls} link ({counts.totalChannels} kanal/içerik) var.\n" +
                               $"Bu hesaba ait TÜM yayın linklerini topluca silmek istediğinizden emin misiniz?";

            var res = MessageBox.Show(confirmMsg, "Toplu IPTV Hesabı Temizleme Onayı", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;

            try
            {
                var purgeResult = await IptvAccountHelper.PurgeAccountFromDatabaseAsync(acc);
                MessageBox.Show(
                    $"İşlem Tamamlandı:\n• {purgeResult.TotalUrlsRemoved} URL silindi.\n• {purgeResult.ChannelsDeletedEntirely} kanal tamamen kaldırıldı.",
                    "Hesap Temizlendi",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Temizleme hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
