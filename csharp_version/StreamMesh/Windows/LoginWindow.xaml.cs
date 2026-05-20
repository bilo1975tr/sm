using System;
using System.Windows;
using StreamMesh.Services.P2P;

namespace StreamMesh.Windows
{
    public partial class LoginWindow : Window
    {
        public bool IsLoggedIn { get; private set; } = false;

        public LoginWindow()
        {
            InitializeComponent();
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

            string country = (CountryCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content.ToString() ?? "Türkiye";
            string lang1 = (Lang1Combo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content.ToString();
            string lang2 = (Lang2Combo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content.ToString();

            if (lang1 == "Seçili Değil") lang1 = "";
            if (lang2 == "Seçili Değil") lang2 = "";

            try
            {
                UserService.RegisterOrLogin(email, password, country, lang1, lang2);
                IsLoggedIn = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Giriş sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
