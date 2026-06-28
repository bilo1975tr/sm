using System;
using System.Windows;
using System.Linq;
using StreamMesh.Services;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Controls;

namespace StreamMesh.Windows
{
    public partial class ChatWindow : Window
    {
        private readonly OllamaChatService _chatService = new OllamaChatService();
        private readonly DatabaseService _dbService = new DatabaseService();
        private System.Threading.CancellationTokenSource _cts;

        public ChatWindow()
        {
            InitializeComponent();
            
            // Başlangıç hoş geldiniz mesajı
            AddMessage("AI", "Merhaba! Ben StreamMesh Yapay Zeka Veritabanı Asistanı. Veritabanındaki tüm kanalları, filmleri, dizileri ve kaynakları doğrudan sorgulayabilir, güncelleyebilir veya düzenleyebilirim. Nasıl yardımcı olabilirim?");
        }

        private string GetChannelContext()
        {
            try
            {
                int totalCount = _dbService.GetTotalChannelCount();
                var sb = new StringBuilder();
                sb.AppendLine("StreamMesh Veritabanı Özeti:");
                sb.AppendLine($"- Toplam Kanal/İçerik Sayısı: {totalCount}");
                return sb.ToString();
            }
            catch
            {
                return "StreamMesh veritabanı aktif.";
            }
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }

        private void InputPrompt_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SendMessage();
            }
        }

        private void AddMessage(string sender, string text)
        {
            var isUser = sender.Equals("Siz", StringComparison.OrdinalIgnoreCase);
            
            var container = new Grid
            {
                Margin = new Thickness(0, 5, 0, 5),
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left
            };

            // Mesaj baloncuğu genişlik sınırı
            container.MaxWidth = 320;

            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(isUser ? System.Windows.Media.Color.FromRgb(37, 99, 235) : System.Windows.Media.Color.FromRgb(30, 41, 59)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 8, 12, 8),
                BorderBrush = new System.Windows.Media.SolidColorBrush(isUser ? System.Windows.Media.Color.FromRgb(37, 99, 235) : System.Windows.Media.Color.FromRgb(51, 65, 85)),
                BorderThickness = new Thickness(1)
            };

            var panel = new StackPanel();

            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var senderLabel = new TextBlock
            {
                Text = sender,
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Foreground = new System.Windows.Media.SolidColorBrush(isUser ? System.Windows.Media.Color.FromRgb(191, 219, 254) : System.Windows.Media.Color.FromRgb(148, 163, 184)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(senderLabel, 0);
            headerGrid.Children.Add(senderLabel);

            var copyButton = new Button
            {
                Content = "📋",
                FontSize = 10,
                Width = 20,
                Height = 20,
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = new System.Windows.Media.SolidColorBrush(isUser ? System.Windows.Media.Color.FromRgb(191, 219, 254) : System.Windows.Media.Color.FromRgb(148, 163, 184)),
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = "Yazıyı Kopyala",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };

            copyButton.Click += (s, ev) =>
            {
                try
                {
                    Clipboard.SetText(text);
                    copyButton.Content = "✓";
                    copyButton.ToolTip = "Kopyalandı!";
                    Task.Delay(1000).ContinueWith(t =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            copyButton.Content = "📋";
                            copyButton.ToolTip = "Yazıyı Kopyala";
                        });
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Kopyalama hatası: {ex.Message}");
                }
            };
            Grid.SetColumn(copyButton, 1);
            headerGrid.Children.Add(copyButton);

            panel.Children.Add(headerGrid);

            var messageText = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 250, 252)),
                FontSize = 13
            };
            panel.Children.Add(messageText);

            border.Child = panel;
            container.Children.Add(border);

            MessagesList.Children.Add(container);
            
            // Otomatik olarak son mesaja odaklan
            ChatScroll.ScrollToEnd();
        }

        private void SendMessage()
        {
            if (_cts != null)
            {
                try
                {
                    _cts.Cancel();
                }
                catch { }
                _cts = null;
                SendButton.Content = "Gönder";
                StatusBorder.Visibility = Visibility.Collapsed;
                AddMessage("Sistem", "İşlem iptal edildi.");
                return;
            }

            string prompt = InputPrompt.Text;
            if (string.IsNullOrWhiteSpace(prompt)) return;
            
            AddMessage("Siz", prompt);
            InputPrompt.Clear();
            
            string context = GetChannelContext();
            
            _cts = new System.Threading.CancellationTokenSource();
            var token = _cts.Token;
            SendButton.Content = "İptal";
            StatusBorder.Visibility = Visibility.Visible;
            StatusText.Text = "Düşünüyor...";

            Task.Run(async () => {
                string response;
                try
                {
                    response = await _chatService.AskOllama(prompt, context, token, (status) => {
                        Dispatcher.Invoke(() => {
                            StatusText.Text = status;
                        });
                    });
                }
                catch (Exception ex)
                {
                    response = $"Hata oluştu veya işlem iptal edildi: {ex.Message}";
                }

                Dispatcher.Invoke(() => {
                    if (_cts != null && _cts.Token == token)
                    {
                        SendButton.Content = "Gönder";
                        _cts = null;
                    }
                    StatusBorder.Visibility = Visibility.Collapsed;
                    AddMessage("AI", response);
                });
            });
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new OllamaSettingsWindow();
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
        }
    }
}
