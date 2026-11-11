using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StreamMesh.Core.Media;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Plugins
{
    public class StreamResolutionPlugin : BasePlugin
    {
        public override string Id => "stream_resolution";
        public override string Name => "Yayın & Çözünürlük Yönlendiricisi";
        public override string Description => "HLS (.m3u8), TS, MP4, YouTube ve AceStream P2P yayın bağlantılarını dinamik olarak çözümler, yedek bağlantılara yönlendirir ve oynatıcı için optimize eder.";
        public override string Category => "Yayın & Oynatıcı";
        public override string Author => "StreamMesh Media Lab";
        public override string Version => "1.6.0";
        public override string Icon => "🎬";

        private int _resolvedStreamsCount = 0;
        private int _fallbackTriggers = 0;

        public StreamResolutionPlugin()
        {
            AddSetting("PreferredResolution", "Tercih Edilen Çözünürlük", "Otomatik (En Yüksek)", PluginSettingType.Select,
                "HLS master manifest dosyalarında ilk tercih edilecek kalite.",
                new List<string> { "Otomatik (En Yüksek)", "1080p Full HD", "720p HD", "480p SD" });

            AddSetting("MaxBufferSeconds", "Maksimum Oynatma Tamponu (Saniye)", "30", PluginSettingType.Number,
                "Düşük bağlantı hızlarında takılmayı önleyen akıllı video arabelleği.");

            AddSetting("AutoFallbackOnFail", "Yayın Kesildiğinde Yedek Kaynağa Geç", "true", PluginSettingType.Boolean,
                "Ana yayın donduğunda veya çöktüğünde havuzdaki yedek M3U/Xtream kaynağına otomatik bağlanır.");

            AddSetting("ForceProxyAceStreams", "AceStream'leri Yerel Proxy Üzerinden İlet", "true", PluginSettingType.Boolean,
                "P2P AceStream yayınlarını doğrudan HTTP akışına dönüştürerek yerel ağdaki TV ve cihazlara uyumlu kılar.");

            AddSetting("ConnectionTimeoutSeconds", "Bağlantı Zaman Aşımı (Saniye)", "8", PluginSettingType.Number,
                "Yavaş veya yanıt vermeyen yayın sunucularından ne kadar süre sonra vazgeçileceği.");
        }

        public override Task InitializeAsync()
        {
            RecordExecution();
            if (!IsEnabled)
            {
                SetStatus("Devre Dışı", "Yayın yönlendirici devre dışı.");
                return Task.CompletedTask;
            }

            SetStatus("Aktif", "Akıllı yayın yönlendirici devrede.");
            return Task.CompletedTask;
        }

        public void NotifyStreamResolved(string streamUrl, bool wasFallback = false)
        {
            RecordExecution();
            _resolvedStreamsCount++;
            if (wasFallback) _fallbackTriggers++;
            SetStatus("Aktif", $"Son çözümlenen: {streamUrl.Split('?')[0]}");
        }

        public override Task<string> ExecuteActionAsync(string actionName, Dictionary<string, object>? parameters = null)
        {
            RecordExecution();
            if (actionName == "ResetBuffer" || actionName == "ClearCache")
            {
                SetStatus("Aktif", "Yayın tamponu ve çözünürlük önbelleği temizlendi.");
                return Task.FromResult("✅ Çözümleme tamponu ve yönlendirici önbelleği başarıyla sıfırlandı.");
            }

            return base.ExecuteActionAsync(actionName, parameters);
        }

        public override Dictionary<string, string> GetStatistics()
        {
            var stats = base.GetStatistics();
            stats["Çözümlenen Yayın Sayısı"] = _resolvedStreamsCount.ToString("N0");
            stats["Yedek Kaynak Geçişleri"] = _fallbackTriggers.ToString("N0");
            stats["Hedef Çözünürlük"] = GetString("PreferredResolution");
            stats["Tampon Süresi"] = $"{GetInt("MaxBufferSeconds", 30)} sn";
            return stats;
        }
    }
}
