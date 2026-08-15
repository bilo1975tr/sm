using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using StreamMesh.Core.Database;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class AiEngine
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private readonly DatabaseEngine _db = new DatabaseEngine();

        public async Task<string> AskAiAsync(string prompt, System.Threading.CancellationToken ct = default)
        {
            try
            {
                // Auto-detect and configure if not configured or failing
                string url = _db.GetSetting("AiUrl", "");
                string model = _db.GetSetting("AiModel", "");

                if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(model))
                {
                    var detected = await AutoDetectAndConfigureAsync();
                    if (detected.success)
                    {
                        url = detected.url;
                        model = detected.model;
                    }
                    else
                    {
                        url = "http://localhost:11434/api/chat";
                        model = "llama3";
                    }
                }

                // Format 1: LM Studio / OpenAI compatible chat completions
                if (url.Contains("/v1/chat/completions") || url.Contains(":1234"))
                {
                    var payload = new
                    {
                        model = model,
                        messages = new[] { new { role = "user", content = prompt } },
                        temperature = 0.7,
                        stream = false
                    };
                    var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(url, content, ct);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        return choices[0].GetProperty("message").GetProperty("content").GetString() ?? "Yanıt alınamadı.";
                    }
                }
                else
                {
                    // Format 2: Ollama standard /api/chat
                    var payload = new { model = model, messages = new[] { new { role = "user", content = prompt } }, stream = false };
                    var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(url, content, ct);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("message", out var msg))
                    {
                        return msg.GetProperty("content").GetString() ?? "Yanıt alınamadı.";
                    }
                    else if (doc.RootElement.TryGetProperty("response", out var resp))
                    {
                        return resp.GetString() ?? "Yanıt alınamadı.";
                    }
                }

                return "Yanıt işlenemedi.";
            }
            catch (HttpRequestException httpEx)
            {
                return $"Yerel Yapay Zeka Servisine Bağlanılamadı: {httpEx.Message}\nİpucu: Bilgisayarınızda Ollama (http://localhost:11434) veya LM Studio (http://localhost:1234) servisinin açık olduğundan emin olun.";
            }
            catch (Exception ex)
            {
                return $"AI İşlem Hatası: {ex.Message}";
            }
        }

        public async Task<(bool success, string provider, string url, string model, List<string> models)> AutoDetectAndConfigureAsync()
        {
            var allModels = new List<string>();

            // 1. Check Ollama (Port 11434)
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2.5));
                var response = await _httpClient.GetAsync("http://localhost:11434/api/tags", cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("models", out var models))
                    {
                        foreach (var m in models.EnumerateArray())
                        {
                            string mName = m.GetProperty("name").GetString() ?? "";
                            if (!string.IsNullOrEmpty(mName)) allModels.Add(mName);
                        }
                    }

                    if (allModels.Count > 0)
                    {
                        string selectedModel = allModels.FirstOrDefault() ?? "llama3";
                        string chatUrl = "http://localhost:11434/api/chat";
                        _db.SetSetting("AiUrl", chatUrl);
                        _db.SetSetting("AiModel", selectedModel);
                        LogService.LogInfo($"AiEngine: Ollama otomatik algılandı ve yapılandırıldı. Model: {selectedModel} ({allModels.Count} model mevcut)");
                        return (true, "Ollama", chatUrl, selectedModel, allModels);
                    }
                }
            }
            catch { }

            // 2. Check LM Studio (Port 1234)
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2.5));
                var response = await _httpClient.GetAsync("http://localhost:1234/v1/models", cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("data", out var data))
                    {
                        foreach (var m in data.EnumerateArray())
                        {
                            string mName = m.GetProperty("id").GetString() ?? "";
                            if (!string.IsNullOrEmpty(mName)) allModels.Add(mName);
                        }
                    }

                    if (allModels.Count > 0)
                    {
                        string selectedModel = allModels.FirstOrDefault() ?? "";
                        string chatUrl = "http://localhost:1234/v1/chat/completions";
                        _db.SetSetting("AiUrl", chatUrl);
                        _db.SetSetting("AiModel", selectedModel);
                        LogService.LogInfo($"AiEngine: LM Studio otomatik algılandı ve yapılandırıldı. Model: {selectedModel} ({allModels.Count} model mevcut)");
                        return (true, "LM Studio", chatUrl, selectedModel, allModels);
                    }
                }
            }
            catch { }

            // 3. Fallback: Check existing configured custom URL
            var existingModels = await GetLocalModelsAsync();
            if (existingModels.Count > 0)
            {
                string curUrl = _db.GetSetting("AiUrl", "http://localhost:11434/api/chat");
                string curModel = _db.GetSetting("AiModel", existingModels[0]);
                return (true, curUrl.Contains("1234") ? "LM Studio" : "Ollama", curUrl, curModel, existingModels);
            }

            return (false, "None", "", "", new List<string>());
        }

        public async Task<List<string>> GetLocalModelsAsync()
        {
            var list = new List<string>();
            try
            {
                string url = _db.GetSetting("AiUrl", "http://localhost:11434/api/chat");

                // 1. Try Ollama (/api/tags)
                try
                {
                    string ollamaUrl = "http://localhost:11434/api/tags";
                    if (url.Contains("/api/chat") || url.Contains("11434"))
                    {
                        ollamaUrl = url.Replace("/api/chat", "/api/tags");
                        if (!ollamaUrl.Contains("/api/tags")) ollamaUrl = "http://localhost:11434/api/tags";
                    }

                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2.5));
                    var response = await _httpClient.GetAsync(ollamaUrl, cts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("models", out var models))
                        {
                            foreach (var m in models.EnumerateArray()) list.Add(m.GetProperty("name").GetString() ?? "");
                        }
                    }
                }
                catch { }

                // 2. Try LM Studio (/v1/models) if list still empty
                if (list.Count == 0)
                {
                    try
                    {
                        string lmUrl = "http://localhost:1234/v1/models";
                        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2.5));
                        var response = await _httpClient.GetAsync(lmUrl, cts.Token);
                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync();
                            using var doc = JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("data", out var data))
                            {
                                foreach (var m in data.EnumerateArray()) list.Add(m.GetProperty("id").GetString() ?? "");
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return list;
        }
    }
}
