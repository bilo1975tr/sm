using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class LogoSearchResult
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string Source { get; set; } = "";
    }

    public static class LogoSearchEngine
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };

        public static async Task<List<LogoSearchResult>> SearchLogosAsync(string query, string sourceFilter = "ALL")
        {
            var results = new List<LogoSearchResult>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            string clean = ChannelUtils.GetCleanName(query);

            // 1. Check local indexed database (the only source we trust for automatic/filtered results)
            string? indexedLogo = ChannelEnricher.GetLogoFromIndex(query);
            if (!string.IsNullOrEmpty(indexedLogo))
            {
                results.Add(new LogoSearchResult
                {
                    Name = $"{clean} (Veritabanı)",
                    Url = indexedLogo,
                    Source = "İndekslenmiş Logo"
                });
            }

            // 2. Direct GitHub candidates for manual search
            if (sourceFilter == "ALL" || sourceFilter == "TV_LOGOS")
            {
                string[] countries = { "turkey", "germany", "united-kingdom", "united-states" };
                foreach (var country in countries)
                {
                    string suffix = country == "germany" ? "de" : country == "turkey" ? "tr" : "";
                    string dashName = clean.ToLowerInvariant().Replace(" ", "-").Replace("&", "and").Replace("+", "plus");
                    dashName = Regex.Replace(dashName, @"[^a-z0-9-]", "").Trim('-');

                    if (!string.IsNullOrEmpty(suffix) && !dashName.EndsWith("-" + suffix))
                        dashName += "-" + suffix;

                    results.Add(new LogoSearchResult
                    {
                        Name = $"{clean} ({country.ToUpper()})",
                        Url = $"https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/{country}/{dashName}.png",
                        Source = $"GitHub ({country})"
                    });
                }
            }

            return results;
        }
    }
}
