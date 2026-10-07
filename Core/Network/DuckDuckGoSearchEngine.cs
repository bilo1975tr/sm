using System;
using System.Net.Http;
using System.Threading.Tasks;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Network
{
    public static class DuckDuckGoSearchEngine
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        public static async Task<string> SearchWebAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return "";
            try
            {
                string url = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                var resp = await _http.SendAsync(req);
                if (resp.IsSuccessStatusCode)
                {
                    string html = await resp.Content.ReadAsStringAsync();
                    return ExtractSnippetsFromHtml(html);
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"DuckDuckGoSearch failed for query '{query}': {ex.Message}");
            }
            return "";
        }

        private static string ExtractSnippetsFromHtml(string html)
        {
            try
            {
                var snippets = new System.Collections.Generic.List<string>();
                int pos = 0;
                while (true)
                {
                    int classIdx = html.IndexOf("class=\"result__snippet\"", pos, StringComparison.OrdinalIgnoreCase);
                    if (classIdx == -1) break;
                    int tagStart = html.IndexOf('>', classIdx);
                    if (tagStart == -1) break;
                    int tagEnd = html.IndexOf("</", tagStart, StringComparison.OrdinalIgnoreCase);
                    if (tagEnd == -1) break;

                    string snippet = html.Substring(tagStart + 1, tagEnd - tagStart - 1);
                    snippet = System.Text.RegularExpressions.Regex.Replace(snippet, "<.*?>", _ => "");
                    snippets.Add(System.Net.WebUtility.HtmlDecode(snippet).Trim());

                    pos = tagEnd;
                    if (snippets.Count >= 3) break;
                }
                return string.Join(" | ", snippets);
            }
            catch
            {
                return "";
            }
        }
    }
}
