using System;
using System.Windows;
using StreamMesh.Services;
using StreamMesh.Services.P2P;
using System.Windows.Controls;

namespace StreamMesh.Windows
{
    public partial class LoginWindow : Window
    {
        public bool IsLoggedIn { get; private set; } = false;

        public LoginWindow()
        {
            InitializeComponent();
            PopulateComboBoxes();
        }

        private void PopulateComboBoxes()
        {
            CountryCombo.ItemsSource = LocalizationManager.SystemCultures;
            var defaultCountry = LocalizationManager.SystemCultures.FirstOrDefault(c => c.Contains("Türkçe")) ?? LocalizationManager.SystemCultures.FirstOrDefault();
            CountryCombo.SelectedItem = defaultCountry;

            Lang1Combo.ItemsSource = LocalizationManager.SystemCulturesWithNone;
            Lang1Combo.SelectedItem = "Hiçbiri";

            Lang2Combo.ItemsSource = LocalizationManager.SystemCulturesWithNone;
            Lang2Combo.SelectedItem = "Hiçbiri";

            AppLangCombo.ItemsSource = LocalizationManager.Top50Languages;
            AppLangCombo.SelectedItem = LocalizationManager.Instance.CurrentLanguage;
        }

        private void AppLangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AppLangCombo.SelectedItem is string lang)
            {
                LocalizationManager.Instance.CurrentLanguage = lang;
            }
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            string email = EmailBox.Text.Trim();
            string password = PasswordBox.Password;
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("E-posta ve şifre zorunludur.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string country = CountryCombo.SelectedItem as string ?? "Türkçe (Türkiye)";
            string lang1 = Lang1Combo.SelectedItem as string ?? "";
            string lang2 = Lang2Combo.SelectedItem as string ?? "";
            string appLang = AppLangCombo.SelectedItem as string ?? "Türkçe";

            if (lang1 == "Hiçbiri") lang1 = "";
            if (lang2 == "Hiçbiri") lang2 = "";

            try
            {
                UserService.RegisterOrLogin(email, password, country, lang1, lang2, appLang);
                IsLoggedIn = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Giriş sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GuestLogin_Click(object sender, RoutedEventArgs e)
        {
            string appLang = AppLangCombo.SelectedItem as string ?? "Türkçe";
            try
            {
                UserService.GuestLogin(appLang);
                IsLoggedIn = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Misafir girişi sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
