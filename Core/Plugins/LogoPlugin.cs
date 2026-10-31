using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using StreamMesh.Core.Media;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Plugins
{
    public class LogoPlugin : BasePlugin
    {
        public override string Id => "logo_manager";
        public override string Name => "Kanal Logoları & Monogram Yöneticisi";
        public override string Description => "Yerel ve ulusal TV kanallarının şeffaf SVG/PNG logolarını bulur, diskte önbelleğe alır ve bulunamayan kanallar için renkli dinamik monogramlar üretir.";
        public override string Category => "Medya & Çalma";
        public override string Author => "StreamMesh Design Team";
        public override string Version => "1.3.1";
        public override string Icon => "🎨";

        public LogoPlugin()
        {
            AddSetting("EnableDiskCache", "Logoları Diskte Önbelleğe Al", "true", PluginSettingType.Boolean, "Her açılışta internetten tekrar indirmek yerine disk önbelleğinden anında yükler.");
            AddSetting("AutoSearchMissing", "Logosu Eksik Kanalları Otomatik Ara", "true", PluginSettingType.Boolean, "M3U listesinde logosu bulunmayan kanallar için GitHub ve Wikimedia kaynaklarından arar.");
            AddSetting("FallbackMonogram", "Logo Bulunamazsa Renkli Monogram Üret", "true", PluginSettingType.Boolean, "Logosuz kanallara şık baş harf kartı oluşturur.");
            AddSetting("MaxCacheAgeDays", "Önbellek Geçerlilik Süresi (Gün)", "30", PluginSettingType.Number, "Belirtilen günden eski logolar otomatik olarak yenilenir.");
        }

        public override Task InitializeAsync()
        {
            RecordExecution();
            if (!IsEnabled)
            {
                SetStatus("Devre Dışı", "Logo eklentisi devre dışı.");
                return Task.CompletedTask;
            }

            SetStatus("Aktif", "Logo önbellek motoru devrede.");
            return Task.CompletedTask;
        }

        public override Task<string> ExecuteActionAsync(string actionName, Dictionary<string, object>? parameters = null)
        {
            RecordExecution();
            if (actionName == "ClearLogoCache" || actionName == "ResetCache")
            {
                try
                {
                    string cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logos", "cache");
                    int deleted = 0;
                    if (Directory.Exists(cacheDir))
                    {
                        var files = Directory.GetFiles(cacheDir);
                        foreach (var f in files)
                        {
                            try { File.Delete(f); deleted++; } catch { }
                        }
                    }
                    SetStatus("Aktif", $"{deleted} önbellek logosu temizlendi.");
                    return Task.FromResult($"✅ Logo disk önbelleği başarıyla temizlendi ({deleted} dosya silindi).");
                }
                catch (Exception ex)
                {
                    return Task.FromResult($"❌ Temizleme hatası: {ex.Message}");
                }
            }

            return base.ExecuteActionAsync(actionName, parameters);
        }

        public override Dictionary<string, string> GetStatistics()
        {
            var stats = base.GetStatistics();
            stats["Önbellek Durumu"] = GetBool("EnableDiskCache") ? "Etkin" : "Devre Dışı";
            stats["Monogram Üretici"] = GetBool("FallbackMonogram") ? "Aktif" : "Pasif";
            stats["Önbellek Süresi"] = $"{GetInt("MaxCacheAgeDays", 30)} gün";
            return stats;
        }
    }
}
