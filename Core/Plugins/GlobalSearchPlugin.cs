using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StreamMesh.Core.Media;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Plugins
{
    public class GlobalSearchPlugin : BasePlugin
    {
        public override string Id => "global_search";
        public override string Name => "Küresel Çoklu Kaynak Arama Motoru";
        public override string Description => "Açık IPTV havuzları, AceStream ağları, GitHub depoları ve canlı EPG kayıtları arasında eşzamanlı ve yüksek hızlı arama yapar.";
        public override string Category => "Veri & Rehber";
        public override string Author => "StreamMesh Search Lab";
        public override string Version => "1.5.0";
        public override string Icon => "🔍";

        private readonly GlobalSearchEngine _engine = new();
        private int _totalSearchesPerformed = 0;

        public GlobalSearchPlugin()
        {
            AddSetting("MinCharsToSearch", "Aramayı Başlatmak İçin Minimum Karakter", "2", PluginSettingType.Number, "Kullanıcı kaç harf yazdıktan sonra aramanın başlayacağı.");
            AddSetting("IncludeAceStreamSearch", "AceStream P2P Ağında Ara", "true", PluginSettingType.Boolean, "P2P içerik havuzunda da eşzamanlı tarama yapar.");
            AddSetting("IncludePublicPools", "Genel Açık IPTV Havuzlarında Ara", "true", PluginSettingType.Boolean, "GitHub üzerindeki açık IPTV kaynaklarında tarama yapar.");
            AddSetting("MaxResultsPerSource", "Kaynak Başına Maksimum Sonuç", "30", PluginSettingType.Number, "Arama performansını optimize etmek için sonuç limiti.");
        }

        public override Task InitializeAsync()
        {
            RecordExecution();
            if (!IsEnabled)
            {
                SetStatus("Devre Dışı", "Küresel arama motoru devre dışı.");
                return Task.CompletedTask;
            }

            SetStatus("Aktif", "Küresel arama motoru hazır.");
            return Task.CompletedTask;
        }

        public override async Task<string> ExecuteActionAsync(string actionName, Dictionary<string, object>? parameters = null)
        {
            RecordExecution();
            if (actionName == "TestSearch")
            {
                string q = parameters != null && parameters.TryGetValue("query", out var queryObj) ? queryObj?.ToString() ?? "TRT" : "TRT";
                try
                {
                    SetStatus("İşleniyor", $"'{q}' aranıyor...");
                    var results = await _engine.SearchAsync(q).ConfigureAwait(false);
                    _totalSearchesPerformed++;
                    SetStatus("Aktif", $"{results.Count} sonuç bulundu.");
                    return $"✅ Arama tamamlandı: '{q}' için {results.Count} sonuç bulundu.";
                }
                catch (Exception ex)
                {
                    return $"❌ Arama hatası: {ex.Message}";
                }
            }

            return await base.ExecuteActionAsync(actionName, parameters);
        }

        public override Dictionary<string, string> GetStatistics()
        {
            var stats = base.GetStatistics();
            stats["Toplam Yapılan Arama"] = _totalSearchesPerformed.ToString();
            stats["AceStream Taraması"] = GetBool("IncludeAceStreamSearch") ? "Açık" : "Kapalı";
            stats["Açık Havuz Taraması"] = GetBool("IncludePublicPools") ? "Açık" : "Kapalı";
            stats["Maksimum Sonuç"] = GetString("MaxResultsPerSource", "30");
            return stats;
        }
    }
}
