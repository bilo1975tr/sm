using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using StreamMesh.Converters;
using StreamMesh.Core.Database;
using StreamMesh.Core.Utils;
using StreamMesh.Models;

namespace StreamMesh.Core.Media
{
    public class ChannelLogoManager
    {
        private static readonly LogoCacheConverter LogoConverter = new LogoCacheConverter();
        private static readonly DatabaseEngine Db = new DatabaseEngine();

        public static readonly SolidColorBrush[] MonogramPalette = new SolidColorBrush[]
        {
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 64, 175)), // Blue
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105)),  // Emerald
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(124, 58, 237)), // Purple
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(190, 24, 93)),  // Rose
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(217, 119, 6)),  // Amber
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(13, 148, 136)), // Teal
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(79, 70, 229))   // Indigo
        };

        public static async Task<(ImageSource? Source, string Monogram, System.Windows.Media.Brush Background)> ResolveLogoAsync(Channel channel)
        {
            if (channel == null) return (null, "TV", MonogramPalette[0]);

            try
            {
                ImageSource? converted = null;
                if (!string.IsNullOrWhiteSpace(channel.PrimaryLogoUrl))
                {
                    converted = LogoConverter.Convert(channel.PrimaryLogoUrl, typeof(ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture) as ImageSource;
                }

                if (converted == null)
                {
                    string cleanName = ChannelUtils.GetCleanName(channel.PrimaryName ?? channel.Name ?? "TV");
                    var searchResults = await LogoSearchEngine.SearchLogosAsync(cleanName);
                    if (searchResults != null && searchResults.Count > 0)
                    {
                        var firstMatch = searchResults.FirstOrDefault(r => !string.IsNullOrEmpty(r.Url));
                        if (firstMatch != null)
                        {
                            channel.LogoUrl = string.IsNullOrEmpty(channel.LogoUrl) ? firstMatch.Url : $"{channel.LogoUrl},{firstMatch.Url}";
                            try { Db.SaveChannelSync(channel); } catch { }
                            converted = LogoConverter.Convert(firstMatch.Url, typeof(ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture) as ImageSource;
                        }
                    }
                }

                string nameForMonogram = ChannelUtils.GetCleanName(channel.PrimaryName ?? "TV");
                string monogram = GenerateMonogram(nameForMonogram);
                int hash = Math.Abs(nameForMonogram.GetHashCode());
                System.Windows.Media.Brush bg = MonogramPalette[hash % MonogramPalette.Length];

                return (converted, monogram, bg);
            }
            catch
            {
                string cleanName = ChannelUtils.GetCleanName(channel.PrimaryName ?? "TV");
                return (null, GenerateMonogram(cleanName), MonogramPalette[0]);
            }
        }

        public static string GenerateMonogram(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "TV";
            var parts = name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                return parts[0].Length <= 3 ? parts[0].ToUpperInvariant() : parts[0].Substring(0, 2).ToUpperInvariant();
            }
            else if (parts.Length == 2)
            {
                return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
            }
            else
            {
                return $"{parts[0][0]}{parts[1][0]}{parts[2][0]}".ToUpperInvariant();
            }
        }
    }
}
