using System;
using System.Windows;
using System.Windows.Controls;
using StreamMesh.Services;

namespace StreamMesh.Windows
{
    public partial class OllamaSettingsWindow : Window
    {
        private bool _isInitializing = true;

        public OllamaSettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        private async void LoadSettings()
        {
            try
            {
                var config = OllamaConfigManager.Load();
                UrlTextBox.Text = config.Url;

                if (config.Provider == "LM Studio")
                {
                    ProviderComboBox.SelectedIndex = 1;
                }
                else
                {
                    ProviderComboBox.SelectedIndex = 0;
                }

                _isInitializing = false;

                var service = new OllamaChatService();
                var models = await service.GetModels();
                ModelComboBox.ItemsSource = models;
                ModelComboBox.Text = config.Model;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ayarlar yüklenirken bir hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                _isInitializing = false;
            }
        }

        private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (ProviderComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string provider = selectedItem.Content.ToString();
                if (provider == "LM Studio")
                {
                    if (string.IsNullOrWhiteSpace(UrlTextBox.Text) || UrlTextBox.Text.Contains("11434") || UrlTextBox.Text.Contains("api/generate"))
                    {
                        UrlTextBox.Text = "http://localhost:1234/v1/chat/completions";
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(UrlTextBox.Text) || UrlTextBox.Text.Contains("1234") || UrlTextBox.Text.Contains("v1/chat/completions"))
                    {
                        UrlTextBox.Text = "http://localhost:11434/api/generate";
                    }
                }
            }
        }

        private async void RefreshModels_Click(object sender, RoutedEventArgs e)
        {
            RefreshModelsBtn.IsEnabled = false;
            RefreshModelsBtn.Content = "⌛ Çekiliyor...";

            try
            {
                // Temporarily save current URL and provider so the service fetches from the correct endpoint
                string selectedProvider = "Ollama";
                if (ProviderComboBox.SelectedItem is ComboBoxItem item)
                {
                    selectedProvider = item.Content.ToString();
                }

                var tempConfig = new OllamaConfig
                {
                    Provider = selectedProvider,
                    Url = UrlTextBox.Text,
                    Model = ModelComboBox.Text
                };
                OllamaConfigManager.Save(tempConfig);

                var service = new OllamaChatService();
                var models = await service.GetModels();
                ModelComboBox.ItemsSource = models;
                if (models.Count > 0)
                {
                    ModelComboBox.SelectedIndex = 0;
                }
                
                MessageBox.Show($"{models.Count} adet model başarıyla çekildi.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Modeller çekilirken hata oluştu: {ex.Message}\nSunucunun açık ve erişilebilir olduğundan emin olun.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                RefreshModelsBtn.IsEnabled = true;
                RefreshModelsBtn.Content = "🔄 Modelleri Çek";
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string selectedProvider = "Ollama";
            if (ProviderComboBox.SelectedItem is ComboBoxItem item)
            {
                selectedProvider = item.Content.ToString();
            }

            var config = new OllamaConfig
            {
                Provider = selectedProvider,
                Url = UrlTextBox.Text,
                Model = ModelComboBox.Text
            };
            OllamaConfigManager.Save(config);
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
