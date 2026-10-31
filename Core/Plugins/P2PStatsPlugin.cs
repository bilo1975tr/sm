using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StreamMesh.Core.Media;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Plugins
{
    public class P2PStatsPlugin : BasePlugin
    {
        public override string Id => "p2p_stats";
        public override string Name => "P2P / AceStream Canlı İstatistik Monitörü";
        public override string Description => "AceStream ve BitTorrent tabanlı canlı P2P yayınların anlık eş (peer) sayısını, indirme/yükleme hızlarını ve bağlantı sağlığını ölçer.";
        public override string Category => "Yayın & Oynatıcı";
        public override string Author => "StreamMesh P2P Lab";
        public override string Version => "1.3.0";
        public override string Icon => "📊";

        private int _totalStreamsTracked = 0;
        private int _lastReportedPeers = 0;

        public P2PStatsPlugin()
        {
            AddSetting("AcePort", "AceStream Engine HTTP Portu", "6878", PluginSettingType.Number, "Yerel AceStream çekirdek dinleme portu.");
            AddSetting("PollIntervalSeconds", "İstatistik Yoklama Sıklığı (Saniye)", "3", PluginSettingType.Number, "Canlı eş ve hız değerlerinin ekrana yansıtılma süresi.");
            AddSetting("TrackBandwidth", "Bant Genişliği Kullanımını Kaydet", "true", PluginSettingType.Boolean, "Oynatma boyunca harcanan P2P veri miktarını hesaplar.");
            AddSetting("MinHealthyPeers", "Minimum Sağlıklı Eş (Peer) Eşiği", "5", PluginSettingType.Number, "Bu sayının altına düşüldüğünde tampon uyarısı verir.");
        }

        public override Task InitializeAsync()
        {
            RecordExecution();
            if (!IsEnabled)
            {
                SetStatus("Devre Dışı", "P2P monitörü devre dışı.");
                return Task.CompletedTask;
            }

            SetStatus("Aktif", "P2P telemetri izleyici devrede.");
            return Task.CompletedTask;
        }

        public async Task<int> GetPeersAsync(string streamUrl)
        {
            if (!IsEnabled) return 0;
            RecordExecution();
            _totalStreamsTracked++;

            try
            {
                var ace = AceEngine.Instance;
                int count = await ace.GetStreamPeersAsync(streamUrl).ConfigureAwait(false);
                _lastReportedPeers = count;
                SetStatus("Aktif", $"Son eşleşme: {count} aktif peer.");
                return count;
            }
            catch (Exception ex)
            {
                LogService.LogError("[P2PStatsPlugin] Eş sayısı alma hatası", ex);
                return 0;
            }
        }

        public override Task<string> ExecuteActionAsync(string actionName, Dictionary<string, object>? parameters = null)
        {
            RecordExecution();
            if (actionName == "TestEngine" || actionName == "CheckAceStream")
            {
                try
                {
                    int port = GetInt("AcePort", 6878);
                    using var tcp = new System.Net.Sockets.TcpClient();
                    var connectTask = tcp.ConnectAsync("127.0.0.1", port);
                    bool ok = connectTask.Wait(1500);
                    if (ok)
                    {
                        SetStatus("Aktif", $"AceStream Engine (Port {port}) çalışıyor.");
                        return Task.FromResult($"✅ AceStream Engine (127.0.0.1:{port}) aktif ve yanıt veriyor!");
                    }
                    else
                    {
                        SetStatus("Uyarı", "AceStream motoruna ulaşılamadı.");
                        return Task.FromResult($"⚠️ AceStream Engine (Port {port}) kapalı veya yanıt vermiyor.");
                    }
                }
                catch (Exception ex)
                {
                    return Task.FromResult($"❌ Bağlantı başarısız: {ex.Message}");
                }
            }

            return base.ExecuteActionAsync(actionName, parameters);
        }

        public override Dictionary<string, string> GetStatistics()
        {
            var stats = base.GetStatistics();
            stats["Son Eş (Peer) Sayısı"] = _lastReportedPeers.ToString();
            stats["İzlenen P2P Yayın"] = _totalStreamsTracked.ToString();
            stats["Motor Portu"] = GetString("AcePort", "6878");
            return stats;
        }
    }
}
