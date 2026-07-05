using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;

namespace StreamMesh.Services
{
    public class OllamaChatService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        private readonly DatabaseService _dbService = new DatabaseService();

        public async Task<string> AskOllama(string prompt, string context, System.Threading.CancellationToken cancellationToken = default, System.Action<string> onStatusUpdate = null)
        {
            var systemPrompt = @"Sen StreamMesh IPTV uygulamasının akıllı veritabanı yapay zeka asistanısın. Kullanıcıların kanal, film, dizi, kaynak ve EPG verilerini sorgulama ve düzenleme isteklerini doğrudan SQLite veritabanı üzerinden gerçekleştirme yeteneğine sahipsin.
Sistemindeki SQLite tabloları:
1. `Channels` (Id, Name, EpgId, EpgUrl, Url, GroupTitle, LogoUrl, SourceType, AddedDate, Category, Language, PlaylistUrl, IsFavorite, IsVerified, PersonalWatchCount, IsLocked, Notes, IsPremium)
   - Category değerleri genellikle: 'TV', 'Film', 'Dizi'
2. `EpgPrograms` (Id, ChannelName, Title, Description, StartTime, EndTime, SourceUrl)
3. `Settings` (Key, Value)

Yetkilerin:
- SELECT sorguları ile veri okuma, sayma, gruplama.
- UPDATE sorguları ile kategorileri düzeltme (örn: dizi kategorisindeki tek bölümlük kanalları Film yapma), isim güncelleme vb.
- DELETE sorguları ile silme.

SQLite ve Veritabanı Kuralları:
- SQLite'ta `=` ile yapılan metin karşılaştırmaları büyük/küçük harfe duyarlıdır (case-sensitive). Aramalarda `LIKE` kullanmaya çalış veya hem küçük hem büyük baş harfli hallerini hesaba kat (örn: `Language LIKE 'bilinmiyor%'` veya `Language = 'Bilinmiyor'`).
- `Channels` tablosunda boş, null veya bilinmeyen diller veritabanında genellikle `'Bilinmiyor'` (baş harfi büyük 'B') olarak saklanır. Kullanıcı 'bilinmeyen', 'bilinmitor' gibi ifadeler kullandığında `Language = 'Bilinmiyor'` ya da `Language LIKE 'bilin%'` veya `Language IS NULL` veya `Language = ''` durumlarını kontrol etmelisin.
- Kullanıcı dil bilgisini null, boş veya bilinmiyor yapmak istediğinde, `UPDATE Channels SET Language = 'Bilinmiyor'` yapabilirsin (çünkü uygulamadaki Channel modeli boş/null dilleri otomatik olarak 'Bilinmiyor'a dönüştürür).

Proaktif Güncelleme ve Analiz Kuralları (ÇOK ÖNEMLİ):
- Eğer kullanıcı 'tüm kanalları güncelle', 'dilleri ve isimleri düzenle/temizle', 'logoları düzelt' gibi geniş kapsamlı veya belirsiz bir istekte bulunursa, kullanıcıya soru sormak yerine VERİTABANINDAN ÖNCE SELECT YAPIP ANALİZ ET VE DOĞRUDAN OTONOM OLARAK GÜNCELLE.
- Örneğin:
  - İsim tespiti yapıp dil tahmininde bulun: İsminde 'TR', 'Türkçe', 'Turkish', 'Kanal', 'Sinema', 'Belgesel' geçenlerin dilini 'Türkçe' yap. İsminde 'EN', 'English', 'BBC', 'HBO' geçenlerin dilini 'İngilizce' yap. İsminde 'DE', 'German', 'Sky' geçenlerin dilini 'Almanca' yap.
  - İsimleri temizle: Kanal isimlerinin sonundaki '.m3u8', '.ts', 'vlc', 'stream', gereksiz kodlar veya tarihleri UPDATE sorgularıyla temizle.
  - Boş logoları temizle veya varsayılanlara taşı.
  - Dili 'Bilinmiyor', boş veya null olan kanalları yukarıdaki kurallara göre isminden çıkarım yaparak otomatik Türkçe, İngilizce vb. olarak güncelle.
  - İşlem bittikten sonra ne kadar satırı güncellediğini kullanıcıya Türkçe olarak raporla.

Önemli Kurallar:
- Veritabanı işlemleri yapmak için sadece şu formatta yanıt ver: [SQL: <sqlite_sorgusu>]
- Her yanıtında EN FAZLA BİR adet [SQL: ...] komutu gönderebilirsin.
- Sistem bu sorguyu çalıştırıp sonucunu sana getirecektir. Sonucu aldığında kullanıcıya Türkçe olarak açıklayıcı bir yanıt yazabilirsin ya da gerekirse başka bir sorgu daha çalıştırabilirsin.
- Eğer sorgu çalıştırmana gerek yoksa, doğrudan kullanıcının sorusunu Türkçe olarak yanıtla.
- Yanıtlarında teknik detaylardan ziyade kullanıcının anlayacağı dilde konuş. Örneğin: 'Şu kadar adet tek bölümlük diziyi film kategorisine taşıdım.'
- SQL sorgularında markdown kod blokları (```sql gibi) KULLANMA. Sadece [SQL: SELECT ...] şeklinde yaz.";

            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = $"Kullanıcı Sorusu: {prompt}" }
            };

            int step = 0;
            string finalResponse = "";
            int maxSteps = 10;

            while (step < maxSteps)
            {
                step++;
                if (cancellationToken.IsCancellationRequested)
                {
                    finalResponse = "İşlem iptal edildi.";
                    break;
                }

                try
                {
                    if (step == maxSteps)
                    {
                        messages.Add(new { role = "user", content = "[Sistem] İşlem adım limitine ulaşıldı. Lütfen yeni bir SQL komutu [SQL: ...] yazmak yerine, şu ana kadar yapabildiğin veritabanı işlemlerini veya analizleri kullanıcıya Türkçe olarak özetle." });
                    }

                    onStatusUpdate?.Invoke("Düşünüyor...");
                    var responseText = await CallOllamaChat(messages, cancellationToken);
                    
                    // Parse if there is an SQL command: [SQL: ...]
                    int sqlStart = responseText.IndexOf("[SQL:");
                    if (sqlStart >= 0 && step < maxSteps)
                    {
                        int sqlEnd = responseText.IndexOf("]", sqlStart);
                        if (sqlEnd > sqlStart)
                        {
                            string sqlQuery = responseText.Substring(sqlStart + 5, sqlEnd - (sqlStart + 5)).Trim();
                            
                            // Remove any markdown inside SQL query if the LLM outputted them
                            sqlQuery = sqlQuery.Replace("```sql", "").Replace("```", "").Trim();

                            if (cancellationToken.IsCancellationRequested)
                            {
                                finalResponse = "İşlem iptal edildi.";
                                break;
                            }

                            string queryResult = "";
                            try
                            {
                                if (sqlQuery.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                                {
                                    onStatusUpdate?.Invoke($"Veritabanı taranıyor... [SQL: {sqlQuery}]");
                                    var rows = _dbService.ExecuteRawQuery(sqlQuery);
                                    if (rows.Count == 0)
                                    {
                                        queryResult = "Sorgu başarıyla çalıştırıldı ancak hiçbir satır dönmedi.";
                                    }
                                    else
                                    {
                                        // Format rows as JSON or simple text
                                        queryResult = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = false });
                                        if (queryResult.Length > 2000)
                                        {
                                            queryResult = queryResult.Substring(0, 2000) + "... (sonuç çok uzun olduğu için kırpıldı)";
                                        }
                                    }
                                }
                                else
                                {
                                    onStatusUpdate?.Invoke($"Veritabanı güncelleniyor... [SQL: {sqlQuery}]");
                                    int affectedRows = _dbService.ExecuteRawNonQuery(sqlQuery);
                                    queryResult = $"Sorgu başarıyla çalıştırıldı. Etkilenen satır sayısı: {affectedRows}";
                                }
                            }
                            catch (Exception dbEx)
                            {
                                queryResult = $"Hata: {dbEx.Message}";
                            }

                            // Add to messages and continue loop
                            messages.Add(new { role = "assistant", content = responseText });
                            messages.Add(new { role = "user", content = $"[Sistem] SQL Sorgu Sonucu:\n{queryResult}\n\nLütfen bu sonuca göre kullanıcıya yanıt ver veya sonraki sorguyu çalıştır." });
                            continue;
                        }
                    }

                    // No SQL command found or step limit reached, this is the final response
                    finalResponse = responseText;
                    break;
                }
                catch (OperationCanceledException)
                {
                    finalResponse = "İşlem iptal edildi.";
                    break;
                }
                catch (Exception ex)
                {
                    finalResponse = $"Ollama ile iletişim kurulurken bir hata oluştu: {ex.Message}";
                    break;
                }
            }

            if (string.IsNullOrEmpty(finalResponse))
            {
                finalResponse = "Üzgünüm, isteğinizi işlerken bir döngü oluştu veya yanıt alınamadı.";
            }

            return finalResponse;
        }

        public async Task<string> GenerateMovieMetadataJsonAsync(string rawMovieName, System.Threading.CancellationToken cancellationToken = default)
        {
            var systemPrompt = @"Sen bir film analiz asistanısın. Sana verilen ham IPTV film/kanal ismi/dosya adından filmin gerçek, temiz Türkçe ismini (ve eğer varsa orijinal ismini ve çıkış yılını) tespit et ve bu film hakkında çok kısa (en fazla 2 cümle, maksimum 150 karakter) Türkçe bir özet/tanıtım yaz.
Yanıtını kesinlikle sadece şu JSON formatında ver, başka hiçbir açıklama veya markdown kod bloğu ekleme:
{
  ""title"": ""Temiz Film İsmi (Yıl)"",
  ""summary"": ""Çok kısa film özeti...""
}";

            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = $"Ham Film İsmi: {rawMovieName}" }
            };

            try
            {
                return await CallOllamaChat(messages, cancellationToken);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ollama GenerateMovieMetadataJsonAsync error for {rawMovieName}: {ex.Message}");
                return null;
            }
        }

        private async Task<string> CallOllamaChat(List<object> messages, System.Threading.CancellationToken cancellationToken = default)
        {
            var config = OllamaConfigManager.Load();
            var url = config.Url;
            bool isLMStudio = config.Provider == "LM Studio";

            if (isLMStudio)
            {
                // LM Studio uses OpenAI format: POST /v1/chat/completions
                if (!url.Contains("/v1/chat/completions"))
                {
                    if (url.Contains("/api/generate") || url.Contains("/api/chat"))
                    {
                        url = "http://localhost:1234/v1/chat/completions";
                    }
                    else
                    {
                        url = url.TrimEnd('/') + "/v1/chat/completions";
                    }
                }

                var payload = new
                {
                    model = config.Model,
                    messages = messages,
                    temperature = 0.7,
                    stream = false
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content, cancellationToken);
                response.EnsureSuccessStatusCode();

                var jsonString = await response.Content.ReadAsStringAsync();
                var root = JsonSerializer.Deserialize<JsonElement>(jsonString);
                
                if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var contentProp))
                    {
                        return contentProp.GetString();
                    }
                }
                throw new Exception("LM Studio yanıt formatı çözülemedi.");
            }
            else
            {
                // Ollama
                if (url.Contains("/v1/chat/completions"))
                {
                    url = "http://localhost:11434/api/chat";
                }
                else if (url.EndsWith("/api/generate"))
                {
                    url = url.Replace("/api/generate", "/api/chat");
                }
                else if (!url.EndsWith("/api/chat"))
                {
                    url = url.TrimEnd('/') + "/api/chat";
                }

                var payload = new
                {
                    model = config.Model,
                    messages = messages,
                    stream = false
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content, cancellationToken);
                response.EnsureSuccessStatusCode();

                var jsonString = await response.Content.ReadAsStringAsync();
                var root = JsonSerializer.Deserialize<JsonElement>(jsonString);
                return root.GetProperty("message").GetProperty("content").GetString();
            }
        }

        public async Task EnsureModelSelectedAsync()
        {
            try
            {
                var config = OllamaConfigManager.Load();
                if (string.IsNullOrEmpty(config.Model) || config.Model == "auto")
                {
                    var models = await GetModels();
                    if (models != null && models.Count > 0)
                    {
                        var bestModel = models.FirstOrDefault(m => m.Contains("llama3"))
                                       ?? models.FirstOrDefault(m => m.Contains("mistral"))
                                       ?? models.First();
                        config.Model = bestModel;
                        OllamaConfigManager.Save(config);
                    }
                }
            }
            catch { }
        }

        public async Task<List<string>> GetModels()
        {
            try
            {
                var config = OllamaConfigManager.Load();
                var baseUrl = config.Url;
                bool isLMStudio = config.Provider == "LM Studio";

                if (isLMStudio)
                {
                    if (baseUrl.Contains("/v1/chat/completions"))
                    {
                        baseUrl = baseUrl.Replace("/v1/chat/completions", "");
                    }
                    else if (baseUrl.Contains("/api/generate") || baseUrl.Contains("/api/chat"))
                    {
                        baseUrl = "http://localhost:1234";
                    }

                    var response = await _httpClient.GetAsync(baseUrl.TrimEnd('/') + "/v1/models");
                    response.EnsureSuccessStatusCode();

                    var jsonString = await response.Content.ReadAsStringAsync();
                    var root = JsonSerializer.Deserialize<JsonElement>(jsonString);
                    var models = new List<string>();
                    
                    if (root.TryGetProperty("data", out var dataProp))
                    {
                        foreach (var model in dataProp.EnumerateArray())
                        {
                            if (model.TryGetProperty("id", out var idProp))
                            {
                                models.Add(idProp.GetString());
                            }
                        }
                    }
                    return models;
                }
                else
                {
                    if (baseUrl.Contains("/v1/chat/completions"))
                    {
                        baseUrl = "http://localhost:11434";
                    }
                    baseUrl = baseUrl.Replace("/api/generate", "").Replace("/api/chat", "");
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
            }
            catch
            {
                var config = OllamaConfigManager.Load();
                return config.Provider == "LM Studio" 
                    ? new List<string> { "local-model" } 
                    : new List<string> { "llama3" }; // Fallback
            }
        }
    }
}
