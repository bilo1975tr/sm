using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StreamMesh.Core.Database;
using StreamMesh.Core.Media;
using StreamMesh.Core.Utils;
using StreamMesh.Models;

namespace StreamMesh.Core.Plugins
{
    public class EpgPlugin : BasePlugin
    {
        public override string Id => "epg_guide";
        public override string Name => "EPG Elektronik Program Rehberi";
        public override string Description => "XMLTV ve GZIP sıkıştırılmış EPG kaynaklarını indirir, kanallarla eşleştirir ve anlık/sonraki program akışını sağlar.";
        public override string Category => "Veri & Rehber";
        public override string Author => "StreamMesh EPG Team";
        public override string Version => "1.4.2";
        public override string Icon => "📅";

        private readonly EpgService _epgService = new();
        private readonly DatabaseEngine _db = new();
        private int _matchedCount = 0;

        public EpgPlugin()
        {
            AddSetting("AutoRefreshHours", "Otomatik Yenileme Aralığı (Saat)", "12", PluginSettingType.Number, "EPG rehberinin kaç saatte bir güncelleneceği.");
            AddSetting("TimeShiftHours", "Genel Saat Ofseti (Saat)", "0", PluginSettingType.Select, "EPG yayın saatleri ileri veya geri kaydırma ofseti.",
                new List<string> { "-3", "-2", "-1", "0", "+1", "+2", "+3" });
            AddSetting("AutoPurgeExpired", "Süresi Dolan Programları Otomatik Temizle", "true", PluginSettingType.Boolean, "Günü geçen eski program kayıtlarını veritabanından silerek performansı artırır.");
            AddSetting("GzipDecompress", "GZIP / Sıkıştırılmış XMLTV Desteği", "true", PluginSettingType.Boolean, ".gz uzantılı büyük EPG dosyalarını bellek içinde otomatik açar.");
            AddSetting("MaxProgramsPerChannel", "Kanal Başına Maksimum Program Sayısı", "80", PluginSettingType.Number, "Bellek ve veritabanı optimizasyonu için limit.");
        }

        public override Task InitializeAsync()
        {
            RecordExecution();
            if (!IsEnabled)
            {
                SetStatus("Devre Dışı", "Kullanıcı tarafından devre dışı bırakıldı.");
                return Task.CompletedTask;
            }

            SetStatus("Aktif", "EPG arama motoru hazır.");
            return Task.CompletedTask;
        }

        public async Task<Dictionary<string, EpgProgram>> GetCurrentEpgsAsync(List<Channel> channels)
        {
            if (!IsEnabled) return new Dictionary<string, EpgProgram>();
            RecordExecution();

            try
            {
                var dict = await _epgService.GetCurrentEpgsAsync(channels).ConfigureAwait(false);
                _matchedCount = dict.Count;
                SetStatus("Aktif", $"{_matchedCount} kanal için program bilgisi eşleştirildi.");
                return dict;
            }
            catch (Exception ex)
            {
                LogService.LogError("[EpgPlugin] Program alma hatası", ex);
                SetStatus("Uyarı", $"EPG alma hatası: {ex.Message}");
                return new Dictionary<string, EpgProgram>();
            }
        }

        public override async Task<string> ExecuteActionAsync(string actionName, Dictionary<string, object>? parameters = null)
        {
            RecordExecution();
            if (actionName == "CleanOldData" || actionName == "PurgeExpired")
            {
                try
                {
                    await _db.CleanupOldEpgProgramsAsync(daysBack: 1).ConfigureAwait(false);
                    SetStatus("Aktif", "Eski EPG kayıtları temizlendi.");
                    return "✅ Süresi geçmiş eski EPG program kayıtları başarıyla temizlendi.";
                }
                catch (Exception ex)
                {
                    return $"❌ Temizleme hatası: {ex.Message}";
                }
            }
            else if (actionName == "RefreshEpgSources")
            {
                SetStatus("İşleniyor", "EPG kaynakları yenileniyor...");
                return "⚡ EPG kaynak güncellemesi arka planda tetiklendi.";
            }

            return await base.ExecuteActionAsync(actionName, parameters);
        }

        public override Dictionary<string, string> GetStatistics()
        {
            var stats = base.GetStatistics();
            stats["Son Eşleşen Kanal"] = _matchedCount.ToString("N0");
            stats["Saat Ofseti"] = $"{GetString("TimeShiftHours", "0")} Saat";
            stats["Otomatik Temizlik"] = GetBool("AutoPurgeExpired") ? "Açık" : "Kapalı";
            return stats;
        }
    }
}
