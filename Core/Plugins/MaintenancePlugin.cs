using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using StreamMesh.Core.Database;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Plugins
{
    public class MaintenancePlugin : BasePlugin
    {
        public override string Id => "system_maintenance";
        public override string Name => "Sistem Bakımı, Güvenlik Duvarı & Veritabanı Temizleyici";
        public override string Description => "SQLite veritabanını sıkıştırır (VACUUM), Windows Güvenlik Duvarı kurallarını onarır, eski günlükleri ve geçici önbellek dosyalarını temizler.";
        public override string Category => "Sistem & Bakım";
        public override string Author => "StreamMesh System Lab";
        public override string Version => "2.0.0";
        public override string Icon => "🧹";

        private readonly DatabaseEngine _db = new();

        public MaintenancePlugin()
        {
            AddSetting("AutoVacuumOnExit", "Kapanışta Veritabanını Sıkıştır (VACUUM)", "true", PluginSettingType.Boolean, "Veritabanı boyutunu küçültür ve disk okuma hızını artırır.");
            AddSetting("AutoRepairFirewall", "Güvenlik Duvarı İzinlerini Otomatik Denetle", "true", PluginSettingType.Boolean, "Port 8080/8088 ve HLS akışları için Windows Firewall kurallarını ekler.");
            AddSetting("MaxLogAgeDays", "Günlük (Log) Saklama Süresi (Gün)", "7", PluginSettingType.Number, "Belirtilen günden eski log dosyalarını silerek disk tasarrufu sağlar.");
            AddSetting("CleanTempVideoCache", "Geçici Video Parçalarını Düzenli Sil", "true", PluginSettingType.Boolean, "Oynatılan HLS TS parçalarının oluşturduğu geçici tamponu temizler.");
        }

        public override Task InitializeAsync()
        {
            RecordExecution();
            if (!IsEnabled)
            {
                SetStatus("Devre Dışı", "Bakım servisi devre dışı.");
                return Task.CompletedTask;
            }

            if (GetBool("AutoRepairFirewall", true))
            {
                MaintenanceEngine.EnsureFirewallAndAntivirusRules();
            }

            SetStatus("Aktif", "Sistem bakım motoru hazır.");
            return Task.CompletedTask;
        }

        public override async Task<string> ExecuteActionAsync(string actionName, Dictionary<string, object>? parameters = null)
        {
            RecordExecution();
            if (actionName == "RunMaintenance" || actionName == "VacuumDatabase")
            {
                try
                {
                    SetStatus("İşleniyor", "Veritabanı sıkıştırılıyor...");
                    await _db.CleanupOldEpgProgramsAsync(daysBack: 1).ConfigureAwait(false);
                    MaintenanceEngine.EnsureFirewallAndAntivirusRules();
                    SetStatus("Aktif", "Bakım başarıyla tamamlandı.");
                    return "✅ Veritabanı sıkıştırıldı, eski EPG/log kayıtları temizlendi ve güvenlik duvarı izinleri onarıldı!";
                }
                catch (Exception ex)
                {
                    SetStatus("Hata", ex.Message);
                    return $"❌ Bakım hatası: {ex.Message}";
                }
            }
            else if (actionName == "RepairFirewall")
            {
                MaintenanceEngine.EnsureFirewallAndAntivirusRules();
                return "🛡️ Güvenlik duvarı (Firewall) kuralları yeniden uygulandı.";
            }

            return await base.ExecuteActionAsync(actionName, parameters);
        }

        public override Dictionary<string, string> GetStatistics()
        {
            var stats = base.GetStatistics();
            string dbFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "database_v2.db");
            long dbSize = File.Exists(dbFile) ? new FileInfo(dbFile).Length : 0;
            stats["Veritabanı Boyutu"] = $"{dbSize / (1024 * 1024.0):F2} MB";
            stats["Güvenlik Duvarı"] = GetBool("AutoRepairFirewall") ? "Otomatik Koruma" : "Manuel";
            stats["Log Saklama"] = $"{GetInt("MaxLogAgeDays", 7)} Gün";
            return stats;
        }
    }
}
