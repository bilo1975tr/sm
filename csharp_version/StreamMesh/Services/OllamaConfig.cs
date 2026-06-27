using System.IO;
using System.Text.Json;

namespace StreamMesh.Services
{
    public class OllamaConfig
    {
        public string Url { get; set; } = "http://localhost:11434/api/generate";
        public string Model { get; set; } = "llama3";
    }

    public static class OllamaConfigManager
    {
        private static readonly string ConfigPath = "ollama_settings.json";

        public static OllamaConfig Load()
        {
            if (!File.Exists(ConfigPath)) return new OllamaConfig();
            return JsonSerializer.Deserialize<OllamaConfig>(File.ReadAllText(ConfigPath));
        }

        public static void Save(OllamaConfig config)
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
