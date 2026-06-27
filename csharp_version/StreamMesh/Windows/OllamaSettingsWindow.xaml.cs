using System.Windows;
using StreamMesh.Services;

namespace StreamMesh.Windows
{
    public partial class OllamaSettingsWindow : Window
    {
        public OllamaSettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        private async void LoadSettings()
        {
            var config = OllamaConfigManager.Load();
            UrlTextBox.Text = config.Url;
            
            var service = new OllamaChatService();
            var models = await service.GetModels();
            ModelComboBox.ItemsSource = models;
            ModelComboBox.SelectedItem = config.Model;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var config = new OllamaConfig
            {
                Url = UrlTextBox.Text,
                Model = ModelComboBox.Text
            };
            OllamaConfigManager.Save(config);
            Close();
        }
    }
}
