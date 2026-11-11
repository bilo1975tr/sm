using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Plugins
{
    public class XtreamPlugin : BasePlugin
    {
        public override string Id => "xtream_client";
        public override string Name => "Xtream Codes / IPTV API İstemcisi";
        public override string Description => "Xtream Codes API tabanlı IPTV sağlayıcılarına bağlanır, üyelik süresi, canlı kanallar, VOD film ve dizi arşivini kütüphaneye senkronize eder.";
        public override string Category => "Medya & Çalma";
        public override string Author => "StreamMesh IPTV Team";
        public override string Version => "2.0.0";
        public override string Icon => "⚡";

        private readonly XtreamService _xtreamService = new();
        private readonly DatabaseEngine _db = new();
        private int _syncedAccounts = 0;

        public XtreamPlugin()
        {
            AddSetting("RequestTimeoutSeconds", "İstek Zaman Aşımı (Saniye)", "8", PluginSettingType.Number, "Yavaş Xtream sunucuları için bekleme süresi.");
            AddSetting("UserAgentOverride", "Özel User-Agent Başlığı", "IPTVSmartersPro/1.0.0 (Linux;Android 11) StreamMesh/1.0", PluginSettingType.Text, "IPTV sağlayıcılarının erişim engellerini aşmak için gönderilen başlık.");
            AddSetting("AutoSyncExpiryOnStart", "Açılışta Abonelik Bitiş Süresini Güncelle", "true", PluginSettingType.Boolean, "Hesabın kalan gün sayısını ve durumunu otomatik kontrol eder.");
            AddSetting("IncludeVodMovies", "VOD Filmleri Kütüphaneye Dahil Et", "true", PluginSettingType.Boolean, "Sağlayıcıdaki film kütüphanesini kütüphaneye indirir.");
            AddSetting("IncludeSeries", "Dizileri ve Bölümleri Kütüphaneye Dahil Et", "true", PluginSettingType.Boolean, "Dizi kategorilerini ve bölümlerini kütüphaneye ekler.");
        }

        public override Task InitializeAsync()
        {
            RecordExecution();
            if (!IsEnabled)
            {
                SetStatus("Devre Dışı", "Xtream istemcisi devre dışı.");
                return Task.CompletedTask;
            }

            SetStatus("Aktif", "Xtream API köprüsü devrede.");
            return Task.CompletedTask;
        }

        public override async Task<string> ExecuteActionAsync(string actionName, Dictionary<string, object>? parameters = null)
        {
            RecordExecution();
            if (actionName == "TestAllAccounts" || actionName == "CheckAccounts")
            {
                try
                {
                    var accounts = _db.GetAllIptvAccounts();
                    if (accounts.Count == 0) return "⚠️ Kayıtlı herhangi bir IPTV hesabı bulunamadı.";

                    int success = 0;
                    foreach (var acc in accounts)
                    {
                        bool ok = await _xtreamService.SyncAccountAsync(acc).ConfigureAwait(false);
                        if (ok) success++;
                    }

                    _syncedAccounts = success;
                    SetStatus("Aktif", $"{success}/{accounts.Count} hesap doğrulandı.");
                    return $"✅ Hesap kontrolü tamamlandı: {success} aktif, {accounts.Count - success} yanıt vermiyor.";
                }
                catch (Exception ex)
                {
                    return $"❌ Hata: {ex.Message}";
                }
            }

            return await base.ExecuteActionAsync(actionName, parameters);
        }

        public override Dictionary<string, string> GetStatistics()
        {
            var stats = base.GetStatistics();
            stats["Zaman Aşımı"] = $"{GetInt("RequestTimeoutSeconds", 8)} sn";
            stats["Film Senkronu"] = GetBool("IncludeVodMovies") ? "Açık" : "Kapalı";
            stats["Dizi Senkronu"] = GetBool("IncludeSeries") ? "Açık" : "Kapalı";
            return stats;
        }
    }
}
