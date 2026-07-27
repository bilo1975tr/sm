using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StreamMesh.Core.Database;

namespace StreamMesh.Core.Media
{
    public class LogoSyncService
    {
        private static readonly HttpClient _client = new HttpClient();
        private readonly DatabaseEngine _db = new DatabaseEngine();

        public async Task SyncIfNecessaryAsync()
        {
            string last = _db.GetSetting("LogoSyncDate", "");
            if (DateTime.TryParse(last, out DateTime dt) && (DateTime.Now - dt).TotalDays < 30) return;

            await SyncNowAsync();
        }

        public async Task SyncNowAsync()
        {
            try
            {
                _client.DefaultRequestHeaders.UserAgent.Clear();
                _client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                var response = await _client.GetStringAsync("https://api.github.com/repos/tv-logo/tv-logos/contents/countries/turkey");
                var items = JArray.Parse(response);
                var list = new List<(string key, string file)>();

                foreach (var item in items)
                {
                    string name = item["name"]?.ToString() ?? "";
                    if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        string key = name.ToLower().Replace("-tr.png", "").Replace(".png", "").Replace("-", "");
                        list.Add((key, name));
                    }
                }

                if (list.Count > 0)
                {
                    _db.UpdateLogoIndex(list);
                    _db.SetSetting("LogoSyncDate", DateTime.Now.ToString("o"));
                }
            }
            catch (Exception ex) { Utils.LogService.LogError("LogoSync Error", ex); }
        }
    }
}
