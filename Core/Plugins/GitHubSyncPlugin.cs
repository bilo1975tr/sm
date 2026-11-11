using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StreamMesh.Core.Media;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Plugins
{
    public class GitHubSyncPlugin : BasePlugin
    {
        public override string Id => "github_sync";
        public override string Name => "Bulut & GitHub Otomatik Senkronizasyon";
        public override string Description => "GitHub üzerindeki açık kaynaklı ve yasal TV, radyo, VOD ve EPG listelerini periyodik olarak tarar ve kütüphanenizi en güncel yayınlarla otomatik günceller.";
        public override string Category => "Veri & Senkron";
        public override string Author => "StreamMesh Cloud Sync Team";
        public override string Version => "1.8.0";
        public override string Icon => "🔄";

        private readonly GitHubSyncEngine _syncEngine = new();

        public GitHubSyncPlugin()
        {
            AddSetting("ConfigUrl", "auto_update.json Depo URL", "https://raw.githubusercontent.com/Free-TV/IPTV/master/playlists/playlist_turkey.m3u8", PluginSettingType.Text, "Güncellemelerin çekileceği birincil yapılandırma linki.");
            AddSetting("SyncIntervalHours", "Senkronizasyon Sıklığı (Saat)", "6", PluginSettingType.Number, "Bulut listelerinin kaç saatte bir denetleneceği (0 = Kapalı).");
            AddSetting("AutoSyncOnLaunch", "Uygulama Açıldığında Otomatik Güncelle", "true", PluginSettingType.Boolean, "Program her başlatıldığında arka planda bulut güncellemelerini çeker.");
            AddSetting("PreserveCustomOrder", "Kullanıcı Kanal Sıralamasını Koru", "true", PluginSettingType.Boolean, "Güncelleme yapılırken kullanıcının yaptığı özel kanal sıralamalarını bozmaz.");
            
            // Alt Görev Tarafı Özelleştirilebilir Senkronizasyon Ayarları
            AddSetting("SyncTvChannels", "TV Kanallarını Senkronize Et", "true", PluginSettingType.Boolean, "Canlı TV kanallarını ve yayın kategorilerini günceller.");
            AddSetting("SyncMovies", "Film Kataloğunu Senkronize Et", "true", PluginSettingType.Boolean, "Film ve VOD arşivini günceller.");
            AddSetting("SyncSeries", "Dizi Kataloğunu Senkronize Et", "true", PluginSettingType.Boolean, "Dizi ve sezon arşivini günceller.");
            AddSetting("SyncRadio", "Radyo Kanallarını Senkronize Et", "true", PluginSettingType.Boolean, "Radyo kanallarını günceller.");
            AddSetting("SyncEpg", "EPG Rehberini İndir ve Güncelle", "true", PluginSettingType.Boolean, "Kanal yayın akışlarını ve EPG rehber verilerini indirir.");
            AddSetting("SyncLogos", "Kanal Logolarını ve Monogramları Yenile", "true", PluginSettingType.Boolean, "Eksik ve güncel kanal logolarını indirir ve eşleştirir.");
            AddSetting("DiscoverIptvAccounts", "IPTV Hesaplarını Otomatik Tara ve Doğrula", "true", PluginSettingType.Boolean, "Bulut listelerindeki IPTV hesaplarını keşfeder ve doğrular.");
        }

        public override Task InitializeAsync()
        {
            RecordExecution();
            if (!IsEnabled)
            {
                SetStatus("Devre Dışı", "Bulut senkronizasyonu devre dışı.");
                return Task.CompletedTask;
            }

            SetStatus("Aktif", "Bulut senkronizasyon motoru devrede.");
            return Task.CompletedTask;
        }

        public override async Task<string> ExecuteActionAsync(string actionName, Dictionary<string, object>? parameters = null)
        {
            RecordExecution();
            if (actionName == "StartSync" || actionName == "PullFromGitHub")
            {
                try
                {
                    SetStatus("Senkronize Ediliyor", "GitHub depolarından listeler çekiliyor...");
                    _ = _syncEngine.PullFromGitHubAsync();
                    return "🚀 Bulut güncelleme işlemi arka planda başlatıldı! İlerlemeyi bildirimlerden takip edebilirsiniz.";
                }
                catch (Exception ex)
                {
                    SetStatus("Hata", ex.Message);
                    return $"❌ Hata: {ex.Message}";
                }
            }

            return await base.ExecuteActionAsync(actionName, parameters);
        }

        public override Dictionary<string, string> GetStatistics()
        {
            var stats = base.GetStatistics();
            int interval = GetInt("SyncIntervalHours", 6);
            stats["Senkronizasyon Aralığı"] = interval > 0 ? $"{interval} Saat" : "Kapalı";
            stats["Açılışta Güncelle"] = GetBool("AutoSyncOnLaunch") ? "Açık" : "Kapalı";
            stats["Sıralama Koruması"] = GetBool("PreserveCustomOrder") ? "Etkin" : "Devre Dışı";

            var activeSubtasks = new List<string>();
            if (GetBool("SyncTvChannels")) activeSubtasks.Add("TV");
            if (GetBool("SyncMovies")) activeSubtasks.Add("Film");
            if (GetBool("SyncSeries")) activeSubtasks.Add("Dizi");
            if (GetBool("SyncRadio")) activeSubtasks.Add("Radyo");
            if (GetBool("SyncEpg")) activeSubtasks.Add("EPG");
            if (GetBool("SyncLogos")) activeSubtasks.Add("Logo");
            if (GetBool("DiscoverIptvAccounts")) activeSubtasks.Add("IPTV");

            stats["Aktif Alt Görevler"] = activeSubtasks.Count > 0 ? string.Join(", ", activeSubtasks) : "Tümü Kapalı";
            return stats;
        }
    }
}
