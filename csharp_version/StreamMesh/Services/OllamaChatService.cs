using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;

namespace StreamMesh.Services
{
    public class OllamaChatService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        public async Task<string> AskOllama(string prompt, string context)
        {
            try
            {
                var config = OllamaConfigManager.Load();
                var url = config.Url;
                if (!url.EndsWith("/api/generate")) {
                    url = url.TrimEnd('/') + "/api/generate";
                }
                var payload = new
                {
                    model = config.Model,
                    prompt = $"Bağlam: {context}\nSoru: {prompt}",
                    stream = false
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                var jsonString = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JsonElement>(jsonString);
                return result.GetProperty("response").GetString();
            }
            catch (Exception ex)
            {
                return $"Hata: {ex.Message}";
            }
        }

        public async Task<List<string>> GetModels()
        {
            try
            {
                var config = OllamaConfigManager.Load();
                var baseUrl = config.Url.Replace("/api/generate", "");
                var response = await _httpClient.GetAsync(baseUrl.TrimEnd('/') + "/api/tags");
                response.EnsureSuccessStatusCode();

                var jsonString = await response.Content.ReadAsStringAsync();
                var root = JsonSerializer.Deserialize<JsonElement>(jsonString);
                var models = new List<string>();
                foreach (var model in root.GetProperty("models").EnumerateArray())
                {
                    models.Add(model.GetProperty("name").GetString());
                }
                return models;
            }
            catch
            {
                return new List<string> { "llama3" }; // Fallback
            }
        }
    }
}
