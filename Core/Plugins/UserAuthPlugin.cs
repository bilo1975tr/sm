using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StreamMesh.Core.Auth;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Plugins
{
    public class UserAuthPlugin : BasePlugin
    {
        public override string Id => "user_auth";
        public override string Name => "Kullanıcı Kimlik Doğrulama & Ebeveyn Kontrolü";
        public override string Description => "Çoklu kullanıcı profilleri, yetişkin/kilitli kategoriler için 4 haneli PIN kodu koruması ve favori listesi izolasyonu sağlar.";
        public override string Category => "Sistem & Bakım";
        public override string Author => "StreamMesh Security Lab";
        public override string Version => "1.2.0";
        public override string Icon => "🛡️";

        public UserAuthPlugin()
        {
            AddSetting("EnableParentalPin", "Ebeveyn Koruması / PIN Kodu", "true", PluginSettingType.Boolean, "Yetişkin veya kilitli kategorileri şifre ile korur.");
            AddSetting("ParentalPin", "4 Haneli Güvenlik PIN'i", "0000", PluginSettingType.Password, "Kilitli kanalları açmak için girilecek şifre.");
            AddSetting("AutoLockOnIdleMinutes", "Hareketsizlikte Otomatik Kilitle (Dakika)", "15", PluginSettingType.Number, "Kullanıcı işlem yapmadığında şifreli kanalları tekrar kilitler.");
            AddSetting("CurrentProfileName", "Aktif Profil", "Varsayılan Kullanıcı", PluginSettingType.Text, "İzleme geçmişi ve favorilerin kaydedildiği profil adı.");
        }

        public override Task InitializeAsync()
        {
            RecordExecution();
            if (!IsEnabled)
            {
                SetStatus("Devre Dışı", "Kullanıcı yetkilendirme kapalı.");
                return Task.CompletedTask;
            }

            SetStatus("Aktif", $"Profil: {GetString("CurrentProfileName")}, PIN Koruması: {(GetBool("EnableParentalPin") ? "Açık" : "Kapalı")}");
            return Task.CompletedTask;
        }

        public bool VerifyPin(string enteredPin)
        {
            RecordExecution();
            if (!GetBool("EnableParentalPin")) return true;
            string actualPin = GetString("ParentalPin", "0000");
            return string.Equals(enteredPin?.Trim(), actualPin, StringComparison.Ordinal);
        }

        public override Task<string> ExecuteActionAsync(string actionName, Dictionary<string, object>? parameters = null)
        {
            RecordExecution();
            if (actionName == "TestPin")
            {
                string pin = parameters != null && parameters.TryGetValue("pin", out var p) ? p?.ToString() ?? "" : "0000";
                bool ok = VerifyPin(pin);
                return Task.FromResult(ok ? "✅ PIN kodu doğru!" : "❌ Geçersiz PIN kodu.");
            }

            return base.ExecuteActionAsync(actionName, parameters);
        }

        public override Dictionary<string, string> GetStatistics()
        {
            var stats = base.GetStatistics();
            stats["Aktif Profil"] = GetString("CurrentProfileName");
            stats["PIN Koruması"] = GetBool("EnableParentalPin") ? "Aktif" : "Devre Dışı";
            stats["Oto Kilit Süresi"] = $"{GetInt("AutoLockOnIdleMinutes", 15)} dk";
            return stats;
        }
    }
}
