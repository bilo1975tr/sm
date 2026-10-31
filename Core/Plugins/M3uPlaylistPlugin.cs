using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StreamMesh.Core.Media;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Plugins
{
    public class M3uPlaylistPlugin : BasePlugin
    {
        public override string Id => "m3u_parser";
        public override string Name => "M3U / M3U8 Oynatma Listesi Motoru";
        public override string Description => "Standart ve genişletilmiş EXTINF etiketli M3U dosyalarını akıllıca çözümler; tvg-id, tvg-logo, group-title ve çözünürlük etiketlerini ayıklar.";
        public override string Category => "Medya & Çalma";
        public override string Author => "StreamMesh Parser Lab";
        public override string Version => "2.2.0";
        public override string Icon => "📜";

        public M3uPlaylistPlugin()
        {
            AddSetting("AutoDeduplicate", "Aynı İsimli Kanalları Akıllı Birleştir", "true", PluginSettingType.Boolean, "Farklı kaynaklardaki aynı kanalları tek başlık altında toplayıp yedek link havuzu oluşturur.");
            AddSetting("NormalizeCategories", "Kategori İsimlerini Standartlaştır", "true", PluginSettingType.Boolean, "Farklı yazılan kategori isimlerini (Haber, News, Haberler) standart kategorilere dönüştürür.");
            AddSetting("SkipNonStreamLinks", "Geçersiz / Desteklenmeyen Bağlantıları Filtrele", "true", PluginSettingType.Boolean, "rtsp veya desteklenmeyen protokolleri ayıklayarak liste sağlığını korur.");
            AddSetting("MaxConcurrentDownloads", "Eşzamanlı Parça İndirme Sayısı", "4", PluginSettingType.Number, "Büyük M3U dosyaları indirilirken açılacak paralel HTTP kanalı sayısı.");
        }

        public override Task InitializeAsync()
        {
            RecordExecution();
            if (!IsEnabled)
            {
                SetStatus("Devre Dışı", "M3U motoru devre dışı.");
                return Task.CompletedTask;
            }

            SetStatus("Aktif", "M3U ayrıştırıcı devrede.");
            return Task.CompletedTask;
        }

        public override Dictionary<string, string> GetStatistics()
        {
            var stats = base.GetStatistics();
            stats["Akıllı Birleştirme"] = GetBool("AutoDeduplicate") ? "Açık" : "Kapalı";
            stats["Kategori Normalizasyonu"] = GetBool("NormalizeCategories") ? "Açık" : "Kapalı";
            stats["İndirme Kanalı"] = GetString("MaxConcurrentDownloads", "4");
            return stats;
        }
    }
}
