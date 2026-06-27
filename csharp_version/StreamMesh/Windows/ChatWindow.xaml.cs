using System.Windows;
using System.Linq;
using StreamMesh.Services;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace StreamMesh.Windows
{
    public partial class ChatWindow : Window
    {
        private readonly OllamaChatService _chatService = new OllamaChatService();
        private readonly DatabaseService _dbService = new DatabaseService();

        public ChatWindow()
        {
            InitializeComponent();
        }

        private string GetChannelContext()
        {
            var channels = _dbService.GetAllChannels();
            var sb = new StringBuilder();
            sb.AppendLine("Kanal Listesi:");
            foreach (var ch in channels.Take(50))
            {
                sb.AppendLine($"- Ad: {ch.Name}, Kategori: {ch.Category}, Dil: {ch.Language}");
            }
            return sb.ToString();
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

        private void SendMessage()
        {
            string prompt = InputPrompt.Text;
            if (string.IsNullOrWhiteSpace(prompt)) return;
            ChatDisplay.AppendText($"Siz: {prompt}\n");
            InputPrompt.Clear();
            
            string context = GetChannelContext();
            
            Task.Run(async () => {
                string response = await _chatService.AskOllama(prompt, context);
                Dispatcher.Invoke(() => {
                    ChatDisplay.AppendText($"AI: {response}\n\n");
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
