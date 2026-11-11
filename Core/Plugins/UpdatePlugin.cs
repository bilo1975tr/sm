using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Plugins
{
    public class UpdatePlugin : BasePlugin
    {
        public override string Id => "auto_updater";
        public override string Name => "Otomatik Güncelleme & Sürüm Denetleyicisi";
        public override string Description => "GitHub Releases üzerinden StreamMesh'in en güncel sürümlerini, hata düzeltmelerini ve yeni özellikleri denetler; tek tıkla güncelleme indirir.";
        public override string Category => "Sistem & Bakım";
        public override string Author => "StreamMesh DevOps";
        public override string Version => "1.4.0";
        public override string Icon => "🆙";

        private string _lastCheckedVersion = "0.1.0";
        private bool _isUpdateAvailable = false;

        public UpdatePlugin()
        {
            AddSetting("CheckOnStartup", "Açılışta Güncellemeleri Denetle", "true", PluginSettingType.Boolean, "Uygulama başladığında yeni sürüm olup olmadığını sessizce sorgular.");
            AddSetting("IncludeBeta", "Beta & Önizleme Sürümlerini Dahil Et", "false", PluginSettingType.Boolean, "Geliştirme aşamasındaki deneysel sürümleri gösterir.");
            AddSetting("AutoDownloadInstaller", "Güncellemeyi Otomatik İndir", "false", PluginSettingType.Boolean, "Yeni sürüm bulunduğunda kullanıcıyı uyarıp kurulum dosyasını indirir.");
            AddSetting("UpdateCheckIntervalHours", "Denetleme Sıklığı (Saat)", "24", PluginSettingType.Number, "Yeni sürüm tarama periyodu.");
        }

        public override Task InitializeAsync()
        {
            RecordExecution();
            if (!IsEnabled)
            {
                SetStatus("Devre Dışı", "Güncelleme servisi kapalı.");
                return Task.CompletedTask;
            }

            SetStatus("Aktif", "Güncelleme denetleyicisi devrede.");
            return Task.CompletedTask;
        }

        public override async Task<string> ExecuteActionAsync(string actionName, Dictionary<string, object>? parameters = null)
        {
            RecordExecution();
            if (actionName == "CheckForUpdates" || actionName == "CheckNow")
            {
                try
                {
                    SetStatus("İşleniyor", "GitHub sürümleri denetleniyor...");
                    var updater = new UpdateService();
                    var info = await updater.CheckForUpdateAsync().ConfigureAwait(false);
                    if (info.HasUpdate)
                    {
                        _isUpdateAvailable = true;
                        _lastCheckedVersion = info.RemoteVersion;
                        SetStatus("Uyarı", $"Yeni sürüm mevcut: v{info.RemoteVersion}");
                        return $"🎉 Yeni sürüm bulundu: v{info.RemoteVersion}! İndirme linki hazır.";
                    }
                    else
                    {
                        _isUpdateAvailable = false;
                        SetStatus("Aktif", "En güncel sürümdesiniz.");
                        return "✅ Tebrikler! StreamMesh'in en güncel sürümünü (v0.1.0) kullanıyorsunuz.";
                    }
                }
                catch (Exception ex)
                {
                    SetStatus("Uyarı", $"Güncelleme kontrolü başarısız: {ex.Message}");
                    return $"⚠️ Denetleme hatası: {ex.Message}";
                }
            }

            return await base.ExecuteActionAsync(actionName, parameters);
        }

        public override Dictionary<string, string> GetStatistics()
        {
            var stats = base.GetStatistics();
            stats["Son Denetlenen Sürüm"] = _lastCheckedVersion;
            stats["Güncelleme Durumu"] = _isUpdateAvailable ? "Yeni Sürüm Mevcut!" : "Güncel";
            stats["Beta Sürümleri"] = GetBool("IncludeBeta") ? "Açık" : "Kapalı";
            return stats;
        }
    }
}
