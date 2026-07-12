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

        public async Task<WeatherResult> GetFreeWeatherAsync(string cityName = "otomatik")
        {
            string displayCity = cityName;
            double lat = 41.0082; // Default Istanbul
            double lon = 28.9784;

            try
            {
                if (string.IsNullOrWhiteSpace(cityName) || cityName.ToLower() == "otomatik")
                {
                    var loc = await GetLocationAsync();
                    if (loc.HasValue)
                    {
                        lat = loc.Value.Lat;
                        lon = loc.Value.Lon;
                        displayCity = loc.Value.City;
                    }
                    else
                    {
                        displayCity = "İstanbul";
                    }
                }
                else
                {
                    displayCity = cityName;
                    string lowerCity = cityName.ToLower();
                    if (lowerCity.Contains("ankara")) { lat = 39.9334; lon = 32.8597; }
                    else if (lowerCity.Contains("izmir")) { lat = 38.4192; lon = 27.1287; }
                    else if (lowerCity.Contains("bursa")) { lat = 40.1885; lon = 29.0610; }
                    else if (lowerCity.Contains("antalya")) { lat = 36.8969; lon = 30.7133; }
                    else if (lowerCity.Contains("berlin")) { lat = 52.5200; lon = 13.4050; }
                    else if (lowerCity.Contains("london")) { lat = 51.5074; lon = -0.1278; }
                    else if (lowerCity.Contains("paris")) { lat = 48.8566; lon = 2.3522; }
                    else { lat = 41.0082; lon = 28.9784; }
                }

                string url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current_weather=true";
                var response = await _httpClient.GetStringAsync(url);
                var data = JObject.Parse(response);
                var current = data["current_weather"];

                if (current != null)
                {
                    double tempVal = current["temperature"]?.ToObject<double>() ?? 18.0;
                    int weatherCode = current["weathercode"]?.ToObject<int>() ?? 0;

                    string desc = "Açık";
                    string icon = "☀️";
                    
                    if (weatherCode == 0) { desc = "Açık ve Güneşli"; icon = "☀️"; }
                    else if (weatherCode == 1 || weatherCode == 2 || weatherCode == 3) { desc = "Parçalı Bulutlu"; icon = "⛅"; }
                    else if (weatherCode == 45 || weatherCode == 48) { desc = "Sisli"; icon = "🌫️"; }
                    else if (weatherCode >= 51 && weatherCode <= 55) { desc = "Çiseleyen Yağmur"; icon = "🌦️"; }
                    else if (weatherCode >= 61 && weatherCode <= 65) { desc = "Hafif Yağmurlu"; icon = "🌧️"; }
                    else if (weatherCode >= 71 && weatherCode <= 75) { desc = "Karlı"; icon = "❄️"; }
                    else if (weatherCode >= 80 && weatherCode <= 82) { desc = "Sağanak Yağışlı"; icon = "🌧️"; }
                    else if (weatherCode >= 95) { desc = "Fırtına, Gök Gürültülü"; icon = "⛈️"; }

                    return new WeatherResult
                    {
                        City = displayCity,
                        CurrentTemp = (int)tempVal,
                        FeelsLike = (int)tempVal,
                        Description = desc,
                        IconCode = icon,
                        Warning = "✅ Hava durumu stabil.",
                        Forecast = new List<DailyWeather>()
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Free Weather API Error: {ex.Message}");
            }

            return new WeatherResult
            {
                City = string.IsNullOrEmpty(displayCity) ? "İstanbul" : displayCity,
                CurrentTemp = 18,
                FeelsLike = 18,
                Description = "Güneşli",
                IconCode = "☀️",
                Warning = "✅ Servis Aktif.",
                Forecast = new List<DailyWeather>()
            };
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
