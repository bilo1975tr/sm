using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using StreamMesh.Core.Utils;

namespace StreamMesh.Converters
{
    public class LogoCacheConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string? raw = value as string;
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // Split multiple logos if comma-separated (e.g. from merged channels)
            var candidates = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim())
                                .Where(s => !string.IsNullOrEmpty(s));

            foreach (var candidate in candidates)
            {
                var bitmap = TryLoadBitmap(candidate);
                if (bitmap != null) return bitmap;
            }

            return null;
        }

        private BitmapImage? TryLoadBitmap(string pathOrUrl)
        {
            try
            {
                // 1. Check pack URI or web URL
                if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uriResult))
                {
                    if (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps)
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = uriResult;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        // Note: Asynchronous web URIs must not be frozen while downloading
                        if (bitmap.CanFreeze)
                        {
                            bitmap.Freeze();
                        }
                        return bitmap;
                    }
                    else if (uriResult.Scheme == "pack")
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = uriResult;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        if (bitmap.CanFreeze)
                        {
                            bitmap.Freeze();
                        }
                        return bitmap;
                    }
                    else if (uriResult.Scheme == Uri.UriSchemeFile)
                    {
                        if (File.Exists(uriResult.LocalPath))
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.UriSource = uriResult;
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                            if (bitmap.CanFreeze)
                            {
                                bitmap.Freeze();
                            }
                            return bitmap;
                        }
                    }
                }

                // 2. Check local disk absolute path
                if (File.Exists(pathOrUrl))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(Path.GetFullPath(pathOrUrl), UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    if (bitmap.CanFreeze)
                    {
                        bitmap.Freeze();
                    }
                    return bitmap;
                }

                // 3. Check relative path in App Domain Base Directory (e.g. "logos/StreamMesh_Icon.ico")
                string cleanRelative = pathOrUrl.TrimStart('/', '\\');
                string localBasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, cleanRelative);
                if (File.Exists(localBasePath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(localBasePath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    if (bitmap.CanFreeze)
                    {
                        bitmap.Freeze();
                    }
                    return bitmap;
                }

                // 4. Check LocalAppData logos cache directory
                string appDataLogoPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamMesh", "logos", Path.GetFileName(cleanRelative));
                if (File.Exists(appDataLogoPath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(appDataLogoPath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    if (bitmap.CanFreeze)
                    {
                        bitmap.Freeze();
                    }
                    return bitmap;
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[LogoCache] Failed to load logo from '{pathOrUrl}': {ex.Message}");
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return System.Windows.Data.Binding.DoNothing;
        }
    }
}
