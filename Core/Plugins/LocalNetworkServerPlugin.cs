using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StreamMesh.Core.Network;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Plugins
{
    public class LocalNetworkServerPlugin : BasePlugin
    {
        public override string Id => "local_network_server";
        public override string Name => "Yerel Ağ Medya Sunucusu (Media Server)";
        public override string Description => "Ev ağınızdaki Smart TV, Android TV, Apple TV, VLC veya telefonlara kanalları canlı aktaran dahili HTTP/HLS proxy sunucusu ve M3U dağıtıcısı.";
        public override string Category => "Ağ & Paylaşım";
        public override string Author => "StreamMesh Network Lab";
        public override string Version => "2.1.0";
        public override string Icon => "🌐";

        public LocalNetworkServerPlugin()
        {
            AddSetting("ServerPort", "Sunucu Dinleme Portu", "8080", PluginSettingType.Number, "Yerel ağdaki istemcilerin bağlanacağı TCP portu.");
            AddSetting("EnableCors", "Evrensel CORS Başlıkları", "true", PluginSettingType.Boolean, "Web tarayıcılarının cross-origin yayın isteklerini engellemesini önler.");
            AddSetting("MaxConcurrentStreams", "Maksimum Eşzamanlı Cihaz / Yayın", "20", PluginSettingType.Number, "Ağ bant genişliğini korumak için eşzamanlı aktif istemci sınırı.");
            AddSetting("AutoStartOnLaunch", "Uygulama Açıldığında Otomatik Başlat", "true", PluginSettingType.Boolean, "StreamMesh açılır açılmaz medya sunucusunu devreye sokar.");
            AddSetting("EnableDirectM3uExport", "Doğrudan M3U Yayını Sağla (/playlist.m3u)", "true", PluginSettingType.Boolean, "Diğer IPTV oynatıcıları için hazır M3U linki oluşturur.");
        }

        public override Task InitializeAsync()
        {
            RecordExecution();
            if (!IsEnabled)
            {
                SetStatus("Devre Dışı", "Medya sunucusu eklentisi kapalı.");
                return Task.CompletedTask;
            }

            int port = GetInt("ServerPort", 8080);
            SetStatus("Aktif", $"Medya sunucusu port {port} üzerinde hazır.");
            return Task.CompletedTask;
        }

        public override Task<string> ExecuteActionAsync(string actionName, Dictionary<string, object>? parameters = null)
        {
            RecordExecution();
            if (actionName == "TestPortAvailability" || actionName == "TestPort")
            {
                int port = GetInt("ServerPort", 8080);
                try
                {
                    using var testListener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, port);
                    testListener.Start();
                    testListener.Stop();
                    SetStatus("Aktif", $"Port {port} boş ve kullanıma uygun.");
                    return Task.FromResult($"✅ TCP Port {port} boş ve tamamen erişilebilir!");
                }
                catch (Exception ex)
                {
                    SetStatus("Uyarı", $"Port {port} meşgul veya engelleniyor: {ex.Message}");
                    return Task.FromResult($"⚠️ Port {port} kullanılamıyor veya başka bir uygulama tarafından dinleniyor: {ex.Message}");
                }
            }

            return base.ExecuteActionAsync(actionName, parameters);
        }

        public override Dictionary<string, string> GetStatistics()
        {
            var stats = base.GetStatistics();
            stats["Port"] = GetString("ServerPort", "8080");
            stats["CORS Durumu"] = GetBool("EnableCors") ? "Etkin" : "Kapalı";
            stats["Maksimum İstemci"] = GetString("MaxConcurrentStreams", "20");
            stats["Otomatik Başlatma"] = GetBool("AutoStartOnLaunch") ? "Açık" : "Kapalı";
            return stats;
        }
    }
}
