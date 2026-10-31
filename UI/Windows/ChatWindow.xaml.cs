using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StreamMesh.Core.Media;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace StreamMesh.UI.Windows
{
    public partial class ChatWindow : Window
    {
        private readonly AiEngine _ai = new AiEngine();

        public ChatWindow()
        {
            InitializeComponent();
            TxtModelStatus.Text = $"Model: {_ai.CurrentModel}";
            AddAssistantBubble("Merhaba! Ben StreamMesh AI Asistanı. Yayınlar, kanallar, P2P eşleşmeleri ve oynatma listeleri hakkında bana dilediğinizi sorabilirsiniz.");
        }

        private async void SendMessage_Click(object sender, RoutedEventArgs e)
        {
            await ProcessUserMessageAsync();
        }

        private async void TxtInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await ProcessUserMessageAsync();
            }
        }

        private async Task ProcessUserMessageAsync()
        {
            string text = TxtInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            TxtInput.Text = "";
            AddUserBubble(text);
            BtnSend.IsEnabled = false;

            var typingBubble = AddAssistantBubble("Düşünüyor...");

            try
            {
                string reply = await _ai.AskAiAsync(text, "Sen StreamMesh Hibrit IPTV ve P2P Medya Oynatıcı uygulamasının yardımsever yapay zeka asistanısın. Kullanıcıya net, samimi ve Türkçe yanıtlar ver.");
                if (typingBubble.Child is TextBlock tb)
                {
                    tb.Text = reply;
                }
            }
            catch (Exception ex)
            {
                if (typingBubble.Child is TextBlock tb)
                {
                    tb.Text = $"Yanıt alınamadı: {ex.Message}";
                }
            }
            finally
            {
                BtnSend.IsEnabled = true;
                ChatScrollViewer.ScrollToEnd();
            }
        }

        private Border AddUserBubble(string message)
        {
            var bubble = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(2, 132, 199)),
                CornerRadius = new CornerRadius(12, 12, 2, 12),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(40, 4, 4, 6),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            var text = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            };

            bubble.Child = text;
            MessageContainer.Children.Add(bubble);
            ChatScrollViewer.ScrollToEnd();
            return bubble;
        }

        private Border AddAssistantBubble(string message)
        {
            var bubble = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                CornerRadius = new CornerRadius(12, 12, 12, 2),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(4, 4, 40, 6),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left
            };

            var text = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            };

            bubble.Child = text;
            MessageContainer.Children.Add(bubble);
            ChatScrollViewer.ScrollToEnd();
            return bubble;
        }

        private void QuickPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Content is string content)
            {
                TxtInput.Text = content;
                _ = ProcessUserMessageAsync();
            }
        }

        private void ClearChat_Click(object sender, RoutedEventArgs e)
        {
            MessageContainer.Children.Clear();
            AddAssistantBubble("Sohbet geçmişi temizlendi. Size nasıl yardımcı olabilirim?");
        }
    }
}
