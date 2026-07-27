using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Generic;
using StreamMesh.Core.Database;

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
                string url = _db.GetSetting("AiUrl", "http://localhost:11434/api/chat");
                string model = _db.GetSetting("AiModel", "llama3");

                var payload = new { model = model, messages = new[] { new { role = "user", content = prompt } }, stream = false };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content, ct);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var root = JsonDocument.Parse(json).RootElement;
                return root.GetProperty("message").GetProperty("content").GetString() ?? "Yanıt alınamadı.";
            }
            catch (Exception ex) { return $"AI Bağlantı Hatası: {ex.Message}"; }
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
                    string ollamaUrl = url.Replace("/api/chat", "/api/tags");
                    if (!ollamaUrl.Contains("/api/tags")) ollamaUrl = "http://localhost:11434/api/tags";

                    var response = await _httpClient.GetAsync(ollamaUrl);
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
                        var response = await _httpClient.GetAsync(lmUrl);
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
