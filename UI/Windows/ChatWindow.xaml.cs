using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Threading.Tasks;
using StreamMesh.Core.Media;

namespace StreamMesh.UI.Windows
{
    public partial class ChatWindow : System.Windows.Window
    {
        private readonly AiEngine _ai = new AiEngine();
        private System.Threading.CancellationTokenSource? _cts;

        public ChatWindow()
        {
            InitializeComponent();
            AddMessage("AI", "Merhaba! Ben StreamMesh asistanı. Size nasıl yardımcı olabilirim?");
        }

        private async void Send_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_cts != null) { _cts.Cancel(); return; }

            string prompt = InputPrompt.Text.Trim();
            if (string.IsNullOrEmpty(prompt)) return;

            AddMessage("Siz", prompt);
            InputPrompt.Clear();

            StatusBorder.Visibility = System.Windows.Visibility.Visible;
            SendButton.Content = "İptal";

            _cts = new System.Threading.CancellationTokenSource();
            try
            {
                string response = await _ai.AskAiAsync(prompt, _cts.Token);
                AddMessage("AI", response);
            }
            catch (System.OperationCanceledException)
            {
                AddMessage("Sistem", "İşlem iptal edildi.");
            }
            finally
            {
                StatusBorder.Visibility = System.Windows.Visibility.Collapsed;
                SendButton.Content = "Gönder";
                _cts = null;
            }
        }

        private void AddMessage(string sender, string text)
        {
            bool isUser = sender == "Siz";
            var border = new System.Windows.Controls.Border
            {
                Style = (System.Windows.Style)Resources["MessageBubbleStyle"],
                Background = isUser ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2563EB")) : new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#334155")),
                HorizontalAlignment = isUser ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left
            };

            var stack = new System.Windows.Controls.StackPanel();
            stack.Children.Add(new System.Windows.Controls.TextBlock { Text = sender, FontSize = 10, FontWeight = System.Windows.FontWeights.Bold, Foreground = System.Windows.Media.Brushes.Gray, Margin = new System.Windows.Thickness(0,0,0,3) });
            stack.Children.Add(new System.Windows.Controls.TextBlock { Text = text, TextWrapping = System.Windows.TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.White });

            border.Child = stack;
            MessagesList.Children.Add(border);
            ChatScroll.ScrollToEnd();
        }
    }
}
