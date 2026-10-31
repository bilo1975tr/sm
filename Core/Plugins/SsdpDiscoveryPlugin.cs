using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StreamMesh.Core.Network;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Plugins
{
    public class SsdpDiscoveryPlugin : BasePlugin
    {
        public override string Id => "ssdp_discovery";
        public override string Name => "SSDP & UPnP Cihaz Keşif Protokolü";
        public override string Description => "Yerel ağdaki Smart TV'ler (Samsung Tizen, LG webOS, Android TV) ve UPnP/DLNA oynatıcılar için StreamMesh varlığını yayınlar ve otomatik tanınmasını sağlar.";
        public override string Category => "Ağ & Paylaşım";
        public override string Author => "StreamMesh Network Lab";
        public override string Version => "1.2.0";
        public override string Icon => "📡";

        private SsdpService? _ssdp;

        public SsdpDiscoveryPlugin()
        {
            AddSetting("MulticastAddress", "Multicast IP Adresi", "239.255.255.250", PluginSettingType.Text, "UPnP standardı multicast yayın adresi.");
            AddSetting("SsdpPort", "SSDP Portu", "1900", PluginSettingType.Number, "Standart SSDP UDP keşif dinleme portu.");
            AddSetting("AnnounceIntervalSeconds", "Varlık Bildirim Sıklığı (Saniye)", "30", PluginSettingType.Number, "Ağdaki yeni cihazlara bildirim gönderme aralığı.");
            AddSetting("DeviceFriendlyName", "Görünen Cihaz Adı", "StreamMesh Home Media Center", PluginSettingType.Text, "Smart TV ekranında görünecek sunucu ismi.");
        }

        public override Task InitializeAsync()
        {
            RecordExecution();
            if (!IsEnabled)
            {
                SetStatus("Devre Dışı", "SSDP yayını kapalı.");
                return Task.CompletedTask;
            }

            try
            {
                _ssdp = new SsdpService();
                _ssdp.Start();
                SetStatus("Aktif", "Ağda UPnP / SSDP yayını yapılıyor (Port 1900).");
            }
            catch (Exception ex)
            {
                LogService.LogError("[SsdpDiscoveryPlugin] SSDP başlatma hatası", ex);
                SetStatus("Uyarı", $"SSDP soket hatası: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        public override Task ShutdownAsync()
        {
            try
            {
                _ssdp?.Stop();
                _ssdp = null;
            }
            catch { }
            return base.ShutdownAsync();
        }

        public override Task<string> ExecuteActionAsync(string actionName, Dictionary<string, object>? parameters = null)
        {
            RecordExecution();
            if (actionName == "BroadcastNow" || actionName == "PingNetwork")
            {
                try
                {
                    _ssdp?.Stop();
                    _ssdp = new SsdpService();
                    _ssdp.Start();
                    SetStatus("Aktif", "Tüm yerel ağa SSDP varlık paketi gönderildi.");
                    return Task.FromResult("✅ SSDP NOTIFY paketi yerel ağa başarıyla anons edildi.");
                }
                catch (Exception ex)
                {
                    return Task.FromResult($"❌ Hata: {ex.Message}");
                }
            }

            return base.ExecuteActionAsync(actionName, parameters);
        }

        public override Dictionary<string, string> GetStatistics()
        {
            var stats = base.GetStatistics();
            stats["Yayın Adresi"] = $"{GetString("MulticastAddress")}:{GetString("SsdpPort")}";
            stats["Cihaz Adı"] = GetString("DeviceFriendlyName");
            stats["Yayın Sıklığı"] = $"{GetInt("AnnounceIntervalSeconds", 30)} sn";
            return stats;
        }
    }
}
