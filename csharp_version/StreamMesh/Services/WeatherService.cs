using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace StreamMesh.Services
{
    public class WeatherService
    {
        private const string BASE_URL = "https://api.openweathermap.org/data/2.5/forecast";
        private const string GEO_URL = "http://ip-api.com/json";
        private readonly HttpClient _httpClient = new HttpClient();

        public async Task<(string City, double Lat, double Lon)?> GetLocationAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync(GEO_URL);
                var data = JObject.Parse(response);
                if (data["status"]?.ToString() == "success")
                {
                    return (
                        data["city"]?.ToString(),
                        data["lat"]?.ToObject<double>() ?? 0,
                        data["lon"]?.ToObject<double>() ?? 0
                    );
                }
            }
            catch { }
            return null;
        }

        public async Task<WeatherResult> GetWeatherAsync(string apiKey, string cityName = "otomatik")
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return null;

            var url = $"{BASE_URL}?appid={apiKey}&units=metric&lang=tr";
            string displayCity = cityName;

            if (string.IsNullOrWhiteSpace(cityName) || cityName.ToLower() == "otomatik")
            {
                var loc = await GetLocationAsync();
                if (loc.HasValue)
                {
                    url += $"&lat={loc.Value.Lat}&lon={loc.Value.Lon}";
                    displayCity = loc.Value.City;
                }
                else
                {
                    url += "&q=Istanbul";
                    displayCity = "Istanbul";
                }
            }
            else
            {
                url += $"&q={cityName}";
            }

            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var data = JObject.Parse(response);
                var list = data["list"] as JArray;

                if (list == null || list.Count == 0) return null;

                var first = list[0];
                int currentTemp = (int)(first["main"]["temp"]?.ToObject<double>() ?? 0);
                int feelsLike = (int)(first["main"]["feels_like"]?.ToObject<double>() ?? 0);
                string desc = first["weather"]?[0]?["description"]?.ToString() ?? "";
                string iconCode = first["weather"]?[0]?["icon"]?.ToString() ?? "";

                if (!string.IsNullOrEmpty(desc) && desc.Length > 0)
                {
                    desc = char.ToUpper(desc[0]) + desc.Substring(1);
                }

                // Warnings
                string warningMsg = "✅ Önümüzdeki 12 saat hava sakin.";
                for (int i = 0; i < 4 && i < list.Count; i++)
                {
                    int wId = list[i]["weather"]?[0]?["id"]?.ToObject<int>() ?? 0;
                    if (wId >= 200 && wId < 600) { warningMsg = "⚠️ Dikkat: Önümüzdeki saatlerde yağmur/fırtına bekleniyor."; break; }
                    if (wId >= 600 && wId < 700) { warningMsg = "❄️ Dikkat: Kar yağışı uyarısı."; break; }
                }

                // 3 days forecast
                var dict = new Dictionary<string, (double Min, double Max, string Icon)>();
                foreach (var item in list)
                {
                    var dt = DateTimeOffset.FromUnixTimeSeconds(item["dt"]?.ToObject<long>() ?? 0).DateTime;
                    
                    var turkishCulture = new System.Globalization.CultureInfo("tr-TR");
                    string day = dt.ToString("ddd", turkishCulture);
                    
                    double temp = item["main"]["temp"]?.ToObject<double>() ?? 0;
                    string ic = item["weather"]?[0]?["icon"]?.ToString() ?? "";

                    if (!dict.ContainsKey(day))
                    {
                        dict[day] = (temp, temp, ic);
                    }
                    else
                    {
                        var stats = dict[day];
                        dict[day] = (
                            Math.Min(stats.Min, temp),
                            Math.Max(stats.Max, temp),
                            dt.Hour >= 11 && dt.Hour <= 14 ? ic : stats.Icon
                        );
                    }
                }

                var forecast = dict.Take(3).Select(kvp => new DailyWeather
                {
                    Day = kvp.Key,
                    Min = (int)kvp.Value.Min,
                    Max = (int)kvp.Value.Max,
                    IconUrl = $"http://openweathermap.org/img/wn/{kvp.Value.Icon}@2x.png"
                }).ToList();

                return new WeatherResult
                {
                    City = displayCity,
                    CurrentTemp = currentTemp,
                    FeelsLike = feelsLike,
                    Description = desc,
                    IconCode = iconCode,
                    Warning = warningMsg,
                    Forecast = forecast
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Weather Error: {ex.Message}");
                return null;
            }
        }
    }

    public class WeatherResult
    {
        public string City { get; set; }
        public int CurrentTemp { get; set; }
        public int FeelsLike { get; set; }
        public string Description { get; set; }
        public string IconCode { get; set; }
        public string Warning { get; set; }
        public List<DailyWeather> Forecast { get; set; }
    }

    public class DailyWeather
    {
        public string Day { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public string IconUrl { get; set; }
    }
}
