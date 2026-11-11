using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StreamMesh.Core.Media;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Plugins
{
    public class AiAssistantPlugin : BasePlugin
    {
        public override string Id => "ai_assistant";
        public override string Name => "Yapay Zeka Asistanı & Öneri Motoru";
        public override string Description => "Yerel LLM (Ollama, LM Studio) veya bulut yapay zeka modelleriyle akıllı içerik önerileri, özetleme ve sohbet asistanı sağlar.";
        public override string Category => "Yapay Zeka & Meta";
        public override string Author => "StreamMesh AI Lab";
        public override string Version => "1.3.0";
        public override string Icon => "🤖";

        private readonly AiEngine _aiEngine = new();
        private int _totalPromptsServed = 0;
        private double _lastResponseMs = 0;

        public AiAssistantPlugin()
        {
            AddSetting("EndpointUrl", "AI API URL Endpoint", "http://localhost:11434/api/chat", PluginSettingType.Text, "Ollama veya OpenAI uyumlu API adresi.");
            AddSetting("ModelName", "Model Adı", "llama3", PluginSettingType.Text, "Kullanılacak dil modeli (örn: llama3, mistral, gemma2, qwen2.5).");
            AddSetting("Temperature", "Yaratıcılık Oranı (0.0 - 1.0)", "0.7", PluginSettingType.Number, "Düşük değerler daha tutarlı, yüksek değerler daha yaratıcı yanıtlar üretir.");
            AddSetting("AutoDetectOnStart", "Açılışta Yerel LLM'i Otomatik Ara", "true", PluginSettingType.Boolean, "Uygulama açılırken 11434 (Ollama) veya 1234 (LM Studio) portlarını otomatik tarar.");
            AddSetting("SmartRecommendations", "İzleme Geçmişine Dayalı Akıllı Öneriler", "true", PluginSettingType.Boolean, "Kullanıcının izleme alışkanlıklarına göre ana sayfada kişiselleştirilmiş öneriler sunar.");
        }

        public override async Task InitializeAsync()
        {
            RecordExecution();
            if (!IsEnabled)
            {
                SetStatus("Devre Dışı", "Kullanıcı tarafından devre dışı bırakıldı.");
                return;
            }

            try
            {
                if (GetBool("AutoDetectOnStart", true))
                {
                    SetStatus("İşleniyor", "Yerel LLM servisleri taranıyor...");
                    bool detected = await _aiEngine.AutoDetectAndConfigureAsync().ConfigureAwait(false);
                    if (detected)
                    {
                        SetSettingValue("EndpointUrl", _aiEngine.CurrentUrl);
                        SetSettingValue("ModelName", _aiEngine.CurrentModel);
                        SetStatus("Aktif", $"Yerel AI modeli bağlandı: {_aiEngine.CurrentModel}");
                        return;
                    }
                }

                SetStatus("Aktif", $"AI servisi hazır ({GetString("ModelName")})");
            }
            catch (Exception ex)
            {
                LogService.LogError("[AiAssistantPlugin] Başlatma hatası", ex);
                SetStatus("Uyarı", $"AI servisine ulaşılamadı: {ex.Message}");
            }
        }

        public override async Task<string> ExecuteActionAsync(string actionName, Dictionary<string, object>? parameters = null)
        {
            RecordExecution();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            if (actionName == "TestAiConnection" || actionName == "TestConnection")
            {
                try
                {
                    bool detected = await _aiEngine.AutoDetectAndConfigureAsync().ConfigureAwait(false);
                    sw.Stop();
                    _lastResponseMs = sw.ElapsedMilliseconds;
                    _totalPromptsServed++;

                    if (detected)
                    {
                        SetSettingValue("EndpointUrl", _aiEngine.CurrentUrl);
                        SetSettingValue("ModelName", _aiEngine.CurrentModel);
                        SetStatus("Aktif", $"Bağlantı başarılı: {_aiEngine.CurrentModel}");
                        return $"✅ AI Servisiyle başarıyla iletişim kuruldu! Model: {_aiEngine.CurrentModel} ({_lastResponseMs} ms)";
                    }

                    // Test prompt
                    string reply = await _aiEngine.AskAiAsync("Merhaba, StreamMesh AI asistan testi.").ConfigureAwait(false);
                    sw.Stop();
                    _lastResponseMs = sw.ElapsedMilliseconds;
                    _totalPromptsServed++;
                    return $"✅ Test başarılı ({_lastResponseMs} ms). Yanıt: {reply}";
                }
                catch (Exception ex)
                {
                    SetStatus("Hata", ex.Message);
                    return $"❌ Bağlantı başarısız: {ex.Message}";
                }
            }
            else if (actionName == "AutoDetect")
            {
                bool detected = await _aiEngine.AutoDetectAndConfigureAsync().ConfigureAwait(false);
                return detected ? $"✅ Model tespit edildi: {_aiEngine.CurrentModel}" : "⚠️ Yerel çalışan Ollama/LM Studio bulunamadı.";
            }

            return await base.ExecuteActionAsync(actionName, parameters);
        }

        public override Dictionary<string, string> GetStatistics()
        {
            var stats = base.GetStatistics();
            stats["Kullanılan Model"] = GetString("ModelName");
            stats["Toplam Sorgu"] = _totalPromptsServed.ToString();
            stats["Son Yanıt Süresi"] = _lastResponseMs > 0 ? $"{_lastResponseMs:F0} ms" : "Veri yok";
            stats["Öneri Motoru"] = GetBool("SmartRecommendations") ? "Açık" : "Kapalı";
            return stats;
        }
    }
}
