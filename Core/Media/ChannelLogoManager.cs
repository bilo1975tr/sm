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
        private static readonly DatabaseEngine Db = DatabaseEngine.Instance;

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

            string displayBase = ChannelUtils.GetDisplayBaseName(channel.PrimaryName ?? channel.Name ?? "TV");
            string monogram = GenerateMonogram(displayBase);
            System.Windows.Media.Brush bg = MonogramPalette[Math.Abs(displayBase.GetHashCode()) % MonogramPalette.Length];

            try
            {
                ImageSource? converted = null;
                if (!string.IsNullOrWhiteSpace(channel.LogoUrl))
                {
                    string candidateList = !string.IsNullOrWhiteSpace(channel.PrimaryLogoUrl)
                        ? $"{channel.PrimaryLogoUrl},{channel.LogoUrl}"
                        : channel.LogoUrl;
                    converted = await LogoCacheConverter.LoadFrozenBitmapAsync(candidateList).ConfigureAwait(false);
                }

                if (converted == null && MetadataEngine.IsLikelyLiveTv(channel))
                {
                    string rawSearch = channel.PrimaryName ?? channel.Name ?? "TV";
                    var searchResults = await LogoSearchEngine.SearchLogosAsync(rawSearch).ConfigureAwait(false);
                    if (searchResults != null && searchResults.Count > 0)
                    {
                        foreach (var match in searchResults.Where(r => !string.IsNullOrEmpty(r.Url)))
                        {
                            var bmp = await LogoCacheConverter.LoadFrozenBitmapAsync(match.Url).ConfigureAwait(false);
                            if (bmp != null)
                            {
                                converted = bmp;
                                channel.LogoUrl = string.IsNullOrEmpty(channel.LogoUrl) ? match.Url : $"{match.Url},{channel.LogoUrl}";
                                try { await Db.SaveChannelLogoAndPosAsync(channel, notifyUpdated: false).ConfigureAwait(false); } catch { }
                                break;
                            }
                        }
                    }
                }

                return (converted, monogram, bg);
            }
            catch
            {
                return (null, monogram, bg);
            }
        }

        public static string GenerateMonogram(string name)
        {
            string baseName = ChannelUtils.GetDisplayBaseName(name);
            if (string.IsNullOrWhiteSpace(baseName)) return "TV";

            var parts = baseName.Split(new[] { ' ', '-', '_', '.', '|', ':' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(p => new string(p.Where(char.IsLetterOrDigit).ToArray()))
                                .Where(p => !string.IsNullOrEmpty(p))
                                .ToList();

            if (parts.Count == 0) return "TV";
            if (parts.Count == 1)
            {
                return parts[0].Length <= 3 ? parts[0].ToUpperInvariant() : parts[0].Substring(0, 2).ToUpperInvariant();
            }
            if (parts.Count == 2)
            {
                return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
            }
            return $"{parts[0][0]}{parts[1][0]}{parts[2][0]}".ToUpperInvariant();
        }
    }
}
