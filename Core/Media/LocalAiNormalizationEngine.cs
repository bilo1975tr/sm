using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StreamMesh.Models;
using StreamMesh.Core.Utils;
using StreamMesh.Core.Network;

namespace StreamMesh.Core.Media
{
    public class LocalAiNormalizationEngine
    {
        private readonly AiEngine _aiEngine = new AiEngine();

        public async Task<bool> IsLocalAiAvailableAsync()
        {
            try
            {
                var models = await _aiEngine.GetLocalModelsAsync();
                return models.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task NormalizeChannelsWithAiAsync(List<Channel> channels, Action<string, int, int>? progressCallback = null)
        {
            if (channels == null || channels.Count == 0) return;

            bool isAiAvailable = await IsLocalAiAvailableAsync();
            if (!isAiAvailable)
            {
                LogService.LogInfo("[LocalAiNormalization] Yerel Yapay Zeka (Ollama / LM Studio) aktif değil. Rule Engine normalizasyonu ile devam ediliyor.");
                return;
            }

            var ambiguousChannels = channels.Where(c =>
                string.IsNullOrWhiteSpace(c.Name) ||
                c.Name.Length < 4 ||
                c.Name.Contains("İsimsiz", StringComparison.OrdinalIgnoreCase) ||
                c.Name.Contains("stream", StringComparison.OrdinalIgnoreCase) ||
                System.Text.RegularExpressions.Regex.IsMatch(c.Name, @"^\d+[\d\.\ x\-]*$") ||
                c.Name.Contains("BÖLÜM", StringComparison.OrdinalIgnoreCase) ||
                (c.Category == "TV" && (c.GroupTitle?.Contains("VOD", StringComparison.OrdinalIgnoreCase) == true || c.GroupTitle?.Contains("Film", StringComparison.OrdinalIgnoreCase) == true || c.Url?.Contains("/movie/", StringComparison.OrdinalIgnoreCase) == true || c.Url?.Contains("/series/", StringComparison.OrdinalIgnoreCase) == true))
            ).ToList();

            if (ambiguousChannels.Count == 0)
            {
                LogService.LogInfo("[LocalAiNormalization] İşlenecek şüpheli kanal bulunamadı.");
                return;
            }

            LogService.LogInfo($"[LocalAiNormalization] {ambiguousChannels.Count} kanal için Canlı Web Araması (RAG) destekli Yerel Yapay Zeka analizi başlatılıyor...");

            int batchSize = 10; // Smaller batch for web search context
            int total = ambiguousChannels.Count;
            int processed = 0;

            for (int i = 0; i < total; i += batchSize)
            {
                var batch = ambiguousChannels.Skip(i).Take(batchSize).ToList();
                processed += batch.Count;

                progressCallback?.Invoke($"Yerel AI canlı web araması (RAG) ile doğruluyor...", processed, total);

                try
                {
                    var promptList = new List<object>();
                    foreach (var c in batch)
                    {
                        string webContext = "";
                        try
                        {
                            webContext = await DuckDuckGoSearchEngine.SearchWebAsync(c.Name);
                        }
                        catch { }

                        promptList.Add(new { id = c.Id, name = c.Name, group = c.GroupTitle, url = c.Url, webSearchContext = webContext });
                    }

                    string jsonInput = System.Text.Json.JsonSerializer.Serialize(promptList);

                    string prompt = $"Sen bir IPTV Medya Asistanısın. Aşağıdaki JSON formatındaki kanal listesini analiz et. Her kayıt için 'name', 'group', 'url' ve 'webSearchContext' verilerini inceleyerek doğrulayıp:\n" +
                                    $"1. 'cleanName': Kanal, film veya dizinin doğrulanmış gerçek ve temiz adını çıkar.\n" +
                                    $"2. 'category': Kesin olarak 'TV', 'Film', 'Dizi' veya 'Radyo' olarak sınıflandır.\n" +
                                    $"3. 'language': Dil kodunu belirle (örn. 'tr', 'en', 'de', 'fr', 'ru', 'es', 'az', 'und').\n" +
                                    $"Sadece saf JSON dizisi döndür (Markdown blokları kullanma): [ {{ \"id\": \"...\", \"cleanName\": \"...\", \"category\": \"...\", \"language\": \"...\" }} ].\n\n" +
                                    $"Girdi:\n{jsonInput}";

                    string response = await _aiEngine.AskAiAsync(prompt);
                    if (!string.IsNullOrEmpty(response) && response.TrimStart().StartsWith("["))
                    {
                        string cleanJson = response.Trim();
                        if (cleanJson.StartsWith("```json")) cleanJson = cleanJson.Substring(7);
                        if (cleanJson.StartsWith("```")) cleanJson = cleanJson.Substring(3);
                        if (cleanJson.EndsWith("```")) cleanJson = cleanJson.Substring(0, cleanJson.Length - 3);

                        using var doc = System.Text.Json.JsonDocument.Parse(cleanJson.Trim());
                        foreach (var elem in doc.RootElement.EnumerateArray())
                        {
                            string id = elem.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                            string cleanName = elem.TryGetProperty("cleanName", out var nameProp) ? nameProp.GetString() ?? "" : "";
                            string category = elem.TryGetProperty("category", out var catProp) ? catProp.GetString() ?? "" : "";
                            string language = elem.TryGetProperty("language", out var langProp) ? langProp.GetString() ?? "" : "";

                            var targetCh = batch.FirstOrDefault(c => c.Id == id);
                            if (targetCh != null)
                            {
                                if (!string.IsNullOrEmpty(cleanName)) targetCh.Name = cleanName;
                                if (!string.IsNullOrEmpty(category) && (category == "TV" || category == "Film" || category == "Dizi" || category == "Radyo"))
                                {
                                    targetCh.Category = category;
                                }
                                if (!string.IsNullOrEmpty(language))
                                {
                                    targetCh.Language = Channel.NormalizeLanguage(language);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogWarning($"[LocalAiNormalization] Batch AI with Web Search error: {ex.Message}");
                }

                await Task.Delay(200);
            }

            LogService.LogInfo("[LocalAiNormalization] Canlı Web Araması destekli AI analizi başarıyla tamamlandı.");
        }
    }
}
