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

        public override async Task<string> ExecuteActionAsync(string actionName, Dictionary<string, object>? parameters = null)
        {
            RecordExecution();
            if (actionName == "ClearLogoCache" || actionName == "ResetCache")
            {
                try
                {
                    int deleted = 0;
                    var cacheDirs = new[]
                    {
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logos", "cache"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamMesh", "logos", "cache")
                    };
                    foreach (var cacheDir in cacheDirs)
                    {
                        if (!Directory.Exists(cacheDir)) continue;
                        foreach (var f in Directory.GetFiles(cacheDir))
                        {
                            try { File.Delete(f); deleted++; } catch { }
                        }
                    }

                    int cleanedTv = await StreamMesh.Core.Database.DatabaseEngine.Instance.CleanupPollutedTvLogosAsync().ConfigureAwait(false);
                    SetStatus("Aktif", $"{deleted} önbellek logosu temizlendi.");
                    return $"✅ Logo önbelleği temizlendi ({deleted} dosya, {cleanedTv} TV kaydı düzeltildi).";
                }
                catch (Exception ex)
                {
                    return $"❌ Temizleme hatası: {ex.Message}";
                }
            }

            return await base.ExecuteActionAsync(actionName, parameters);
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
