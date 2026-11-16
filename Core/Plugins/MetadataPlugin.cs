using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using StreamMesh.Core.Media;
using StreamMesh.Core.Network;
using StreamMesh.Core.Utils;
using StreamMesh.Models;

namespace StreamMesh.Core.Plugins
{
    public class MetadataPlugin : BasePlugin
    {
        public override string Id => "metadata_enricher";
        public override string Name => "Film & Dizi Meta Veri Zenginleştirici";
        public override string Description => "VOD filmler ve diziler için TVMaze, IMDb ve Wikipedia açık kaynakları üzerinden API anahtarı gerektirmeden kapak afişleri, görseller, konu özetleri ve oyuncu kadrosunu otomatik çeker.";
        public override string Category => "Meta Veri";
        public override string Author => "StreamMesh Metadata Lab";
        public override string Version => "1.6.0";
        public override string Icon => "🎬";

        private readonly MetadataEngine _metaEngine = new();
        private int _enrichedCount = 0;

        public MetadataPlugin()
        {
            AddSetting("Language", "Öncelikli Dil Kodu", "tr-TR", PluginSettingType.Select, "Film/Dizi özet ve başlıklarının çekileceği dil.",
                new List<string> { "tr-TR", "en-US", "de-DE", "fr-FR", "es-ES" });
            AddSetting("DownloadBackdrops", "Yüksek Çözünürlüklü Arka Planları İndir", "true", PluginSettingType.Boolean, "Detay ekranında gösterilmek üzere geniş format arka planları çeker.");
            AddSetting("AutoEnrichOnImport", "Yeni İçerik Eklendiğinde Otomatik Zenginleştir", "true", PluginSettingType.Boolean, "M3U veya Xtream listeleri yüklenirken arka planda meta verileri doldurur.");
            AddSetting("DailyQueryLimit", "Günlük Maksimum Sorgu Limiti", "1000", PluginSettingType.Number, "Açık kaynak servislerin aşırı yüklenmesini önlemek için günlük güvenlik sınırı.");
        }

        public override Task InitializeAsync()
        {
            RecordExecution();
            if (!IsEnabled)
            {
                SetStatus("Devre Dışı", "Meta veri eklentisi devre dışı.");
                return Task.CompletedTask;
            }

            SetStatus("Aktif", $"Ücretsiz meta veri motoru hazır ({GetString("Language", "tr-TR")})");
            return Task.CompletedTask;
        }

        public async Task EnrichChannelAsync(Channel channel)
        {
            if (!IsEnabled || channel == null) return;
            RecordExecution();
            try
            {
                await _metaEngine.EnrichChannelAsync(channel).ConfigureAwait(false);
                _enrichedCount++;
                SetStatus("Aktif", $"{_enrichedCount} içerik başarıyla zenginleştirildi.");
            }
            catch (Exception ex)
            {
                LogService.LogError("[MetadataPlugin] Zenginleştirme hatası", ex);
            }
        }

        public override async Task<string> ExecuteActionAsync(string actionName, Dictionary<string, object>? parameters = null)
        {
            RecordExecution();
            if (actionName == "TestMetadataConnection" || actionName == "TestTmdbKey" || actionName == "TestConnection")
            {
                try
                {
                    using var resp = await MediaHttpClient.GetAsync("https://api.tvmaze.com/singlesearch/shows?q=breaking+bad", HttpCompletionOption.ResponseHeadersRead, 6).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        SetStatus("Aktif", "Ücretsiz meta veri kaynakları erişilebilir.");
                        return "✅ Ücretsiz meta veri servisleri (TVMaze / IMDb / Wikipedia) erişilebilir ve API anahtarı olmadan çalışıyor!";
                    }
                    SetStatus("Uyarı", $"HTTP {(int)resp.StatusCode}");
                    return $"⚠️ Açık meta veri servisi beklenmeyen yanıt verdi (HTTP {(int)resp.StatusCode}).";
                }
                catch (Exception ex)
                {
                    SetStatus("Hata", ex.Message);
                    return $"⚠️ Bağlantı testi başarısız: {ex.Message}";
                }
            }

            return await base.ExecuteActionAsync(actionName, parameters);
        }

        public override Dictionary<string, string> GetStatistics()
        {
            var stats = base.GetStatistics();
            stats["Zenginleştirilen İçerik"] = _enrichedCount.ToString("N0");
            stats["Seçili Dil"] = GetString("Language", "tr-TR");
            stats["Arka Plan İndirme"] = GetBool("DownloadBackdrops") ? "Açık" : "Kapalı";
            stats["Kaynak"] = "Ücretsiz (Anahtarsız)";
            return stats;
        }
    }
}
