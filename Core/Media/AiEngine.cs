using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StreamMesh.Core.Database;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class AiEngine
    {
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private readonly DatabaseEngine _db = new DatabaseEngine();

        public string CurrentUrl => _db.GetSetting("AiUrl", "http://localhost:11434/api/chat");
        public string CurrentModel => _db.GetSetting("AiModel", "llama3");

        /// <summary>
        /// Arka planda yerel yapay zeka servislerini (Ollama / LM Studio) otomatik tespit edip ayarları yapılandırır.
        /// </summary>
        public async Task<bool> AutoDetectAndConfigureAsync()
        {
            try
            {
                // 1. Ollama Kontrolü (11434 Portu)
                try
                {
                    var ollamaResp = await _http.GetAsync("http://127.0.0.1:11434/api/tags").ConfigureAwait(false);
                    if (ollamaResp.IsSuccessStatusCode)
                    {
                        string json = await ollamaResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var obj = JObject.Parse(json);
                        var models = obj["models"] as JArray;
                        string selectedModel = "llama3";
                        if (models != null && models.Count > 0)
                        {
                            selectedModel = models[0]["name"]?.ToString() ?? "llama3";
                        }

                        _db.SetSetting("AiUrl", "http://localhost:11434/api/chat");
                        _db.SetSetting("AiModel", selectedModel);
                        LogService.LogInfo($"[AiEngine] Yerel Ollama tespit edildi. Model: {selectedModel}");
                        return true;
                    }
                }
                catch { }

                // 2. LM Studio / Yerel OpenAI Uyumlu Sunucu (1234 Portu)
                try
                {
                    var lmResp = await _http.GetAsync("http://127.0.0.1:1234/v1/models").ConfigureAwait(false);
                    if (lmResp.IsSuccessStatusCode)
                    {
                        string json = await lmResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var obj = JObject.Parse(json);
                        var data = obj["data"] as JArray;
                        string selectedModel = "local-model";
                        if (data != null && data.Count > 0)
                        {
                            selectedModel = data[0]["id"]?.ToString() ?? "local-model";
                        }

                        _db.SetSetting("AiUrl", "http://localhost:1234/v1/chat/completions");
                        _db.SetSetting("AiModel", selectedModel);
                        LogService.LogInfo($"[AiEngine] Yerel LM Studio tespit edildi. Model: {selectedModel}");
                        return true;
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[AiEngine] Yerel AI taramasında hata: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Kullanıcının sorusunu yerel veya harici AI servisine gönderir ve yanıtı döndürür.
        /// </summary>
        public async Task<string> AskAiAsync(string prompt, string? systemPrompt = null)
        {
            string url = CurrentUrl;
            string model = CurrentModel;

            if (string.IsNullOrWhiteSpace(url))
            {
                url = "http://localhost:11434/api/chat";
            }

            try
            {
                if (url.Contains("11434") || url.EndsWith("/api/chat") || url.EndsWith("/api/generate"))
                {
                    // Ollama API
                    var body = new JObject
                    {
                        ["model"] = model,
                        ["stream"] = false
                    };

                    if (url.EndsWith("/api/chat"))
                    {
                        var messages = new JArray();
                        if (!string.IsNullOrEmpty(systemPrompt))
                        {
                            messages.Add(new JObject { ["role"] = "system", ["content"] = systemPrompt });
                        }
                        messages.Add(new JObject { ["role"] = "user", ["content"] = prompt });
                        body["messages"] = messages;
                    }
                    else
                    {
                        body["prompt"] = (!string.IsNullOrEmpty(systemPrompt) ? systemPrompt + "\n\n" : "") + prompt;
                    }

                    var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                    var resp = await _http.PostAsync(url, content).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        string resJson = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var parsed = JObject.Parse(resJson);
                        return parsed["message"]?["content"]?.ToString() 
                            ?? parsed["response"]?.ToString() 
                            ?? "AI yanıt verdi ancak metin boştu.";
                    }
                }
                else
                {
                    // OpenAI / LM Studio / Standart v1/chat/completions
                    var body = new JObject
                    {
                        ["model"] = model,
                        ["temperature"] = 0.7
                    };
                    var messages = new JArray();
                    if (!string.IsNullOrEmpty(systemPrompt))
                    {
                        messages.Add(new JObject { ["role"] = "system", ["content"] = systemPrompt });
                    }
                    messages.Add(new JObject { ["role"] = "user", ["content"] = prompt });
                    body["messages"] = messages;

                    var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                    var resp = await _http.PostAsync(url, content).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        string resJson = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var parsed = JObject.Parse(resJson);
                        return parsed["choices"]?[0]?["message"]?["content"]?.ToString()
                            ?? "AI yanıtı başarıyla alındı.";
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[AiEngine] İstek hatası: {ex.Message}");
                return $"[Bağlantı Uyarısı] Yerel AI sunucusuna ({url}) bağlanılamadı. Lütfen Ollama veya LM Studio'nun açık olduğundan emin olun.\nHata: {ex.Message}";
            }

            return "AI servisinden yanıt alınamadı. Lütfen Ayarlar > Yapay Zeka sekmesini kontrol edin.";
        }
    }
}
