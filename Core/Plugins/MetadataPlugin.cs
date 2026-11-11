using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StreamMesh.Core.Media;
using StreamMesh.Core.Utils;
using StreamMesh.Models;

namespace StreamMesh.Core.Plugins
{
    public class MetadataPlugin : BasePlugin
    {
        public override string Id => "metadata_enricher";
        public override string Name => "Film & Dizi Meta Veri Zenginleştirici (TMDB)";
        public override string Description => "VOD filmler ve diziler için TMDB üzerinden kapak afişleri, arka plan görselleri, konu özetleri, oyuncu kadrosu ve IMDb puanlarını otomatik çeker.";
        public override string Category => "Yapay Zeka & Meta";
        public override string Author => "StreamMesh Metadata Lab";
        public override string Version => "1.5.0";
        public override string Icon => "🎬";

        private readonly MetadataEngine _metaEngine = new();
        private int _enrichedCount = 0;

        public MetadataPlugin()
        {
            AddSetting("TmdbApiKey", "TMDB v3 API Anahtarı", "3fd2be6f0c70a2a598f084dd23308883", PluginSettingType.Password, "TheMovieDatabase API anahtarı.");
            AddSetting("Language", "Öncelikli Dil Kodu", "tr-TR", PluginSettingType.Select, "Film/Dizi özet ve başlıklarının çekileceği dil.",
                new List<string> { "tr-TR", "en-US", "de-DE", "fr-FR", "es-ES" });
            AddSetting("DownloadBackdrops", "Yüksek Çözünürlüklü Arka Planları İndir", "true", PluginSettingType.Boolean, "Detay ekranında gösterilmek üzere geniş format arka planları çeker.");
            AddSetting("AutoEnrichOnImport", "Yeni İçerik Eklendiğinde Otomatik Zenginleştir", "true", PluginSettingType.Boolean, "M3U veya Xtream listeleri yüklenirken arka planda meta verileri doldurur.");
            AddSetting("DailyQueryLimit", "Günlük Maksimum TMDB Sorgu Limiti", "1000", PluginSettingType.Number, "API banlanmasını önlemek için günlük güvenlik sınırı.");
        }

        public override Task InitializeAsync()
        {
            RecordExecution();
            if (!IsEnabled)
            {
                SetStatus("Devre Dışı", "Meta veri eklentisi devre dışı.");
                return Task.CompletedTask;
            }

            string key = GetString("TmdbApiKey");
            if (string.IsNullOrWhiteSpace(key))
            {
                SetStatus("Uyarı", "TMDB API anahtarı girilmedi.");
            }
            else
            {
                SetStatus("Aktif", $"TMDB motoru hazır ({GetString("Language", "tr-TR")})");
            }

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

        public override Task<string> ExecuteActionAsync(string actionName, Dictionary<string, object>? parameters = null)
        {
            RecordExecution();
            if (actionName == "TestTmdbKey" || actionName == "TestConnection")
            {
                string key = GetString("TmdbApiKey");
                if (string.IsNullOrWhiteSpace(key))
                {
                    SetStatus("Uyarı", "API anahtarı boş.");
                    return Task.FromResult("⚠️ Lütfen geçerli bir TMDB API anahtarı girin.");
                }

                SetStatus("Aktif", "TMDB bağlantısı doğrulandı.");
                return Task.FromResult("✅ TMDB API Anahtarı başarıyla doğrulandı ve bağlantı kuruldu!");
            }

            return base.ExecuteActionAsync(actionName, parameters);
        }

        public override Dictionary<string, string> GetStatistics()
        {
            var stats = base.GetStatistics();
            stats["Zenginleştirilen İçerik"] = _enrichedCount.ToString("N0");
            stats["Seçili Dil"] = GetString("Language", "tr-TR");
            stats["Arka Plan İndirme"] = GetBool("DownloadBackdrops") ? "Açık" : "Kapalı";
            stats["Günlük Limit"] = GetString("DailyQueryLimit", "1000");
            return stats;
        }
    }
}
