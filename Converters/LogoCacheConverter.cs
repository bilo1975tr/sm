using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using StreamMesh.Core.Network;
using StreamMesh.Core.Utils;

namespace StreamMesh.Converters
{
    public class LogoCacheConverter : IValueConverter
    {
        private static string CacheDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamMesh", "logos", "cache");

        public static string GetDiskCachePath(string url)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(url.Trim()));
            string hex = System.Convert.ToHexString(hash).Substring(0, 32).ToLowerInvariant();
            return Path.Combine(CacheDirectory, $"{hex}.img");
        }

        public static async Task<BitmapImage?> LoadFrozenBitmapAsync(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var candidates = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim())
                                .Where(s => !string.IsNullOrEmpty(s));

            foreach (var candidate in candidates)
            {
                var bmp = await TryLoadFrozenBitmapAsync(candidate).ConfigureAwait(false);
                if (bmp != null) return bmp;
            }

            return null;
        }

        private static async Task<BitmapImage?> TryLoadFrozenBitmapAsync(string pathOrUrl)
        {
            try
            {
                if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uriResult) &&
                    (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
                {
                    if (pathOrUrl.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return null;

                    string cacheFile = GetDiskCachePath(pathOrUrl);
                    if (File.Exists(cacheFile))
                    {
                        var info = new FileInfo(cacheFile);
                        if (info.Length > 32 && (DateTime.Now - info.LastWriteTime).TotalDays < 30)
                        {
                            var cachedBmp = DecodeBytesFrozen(await File.ReadAllBytesAsync(cacheFile).ConfigureAwait(false));
                            if (cachedBmp != null) return cachedBmp;
                        }
                    }

                    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(5000));
                    using var req = new HttpRequestMessage(HttpMethod.Get, uriResult);
                    req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) StreamMesh/1.0");
                    using var resp = await MediaHttpClient.Client.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) return null;

                    string contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
                    if (contentType.Contains("svg", StringComparison.OrdinalIgnoreCase) ||
                        contentType.Contains("html", StringComparison.OrdinalIgnoreCase) ||
                        contentType.Contains("text", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    byte[] bytes = await resp.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
                    if (bytes == null || bytes.Length < 32) return null;

                    var bitmap = DecodeBytesFrozen(bytes);
                    if (bitmap != null)
                    {
                        try
                        {
                            Directory.CreateDirectory(CacheDirectory);
                            await File.WriteAllBytesAsync(cacheFile, bytes).ConfigureAwait(false);
                        }
                        catch { }
                        return bitmap;
                    }
                    return null;
                }

                // Local / file / pack fallback (already frozen)
                return TryLoadLocalOnlyBitmap(pathOrUrl);
            }
            catch
            {
                return null;
            }
        }

        private static BitmapImage? DecodeBytesFrozen(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 16) return null;
            try
            {
                using var ms = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = ms;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                if (bitmap.CanFreeze) bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string? raw = value as string;
            if (string.IsNullOrWhiteSpace(raw)) return null;

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

        private static BitmapImage? TryLoadLocalOnlyBitmap(string pathOrUrl)
        {
            try
            {
                if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uriResult))
                {
                    if (uriResult.Scheme == "pack")
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = uriResult;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        if (bitmap.CanFreeze) bitmap.Freeze();
                        return bitmap;
                    }
                    else if (uriResult.Scheme == Uri.UriSchemeFile && File.Exists(uriResult.LocalPath))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = uriResult;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        if (bitmap.CanFreeze) bitmap.Freeze();
                        return bitmap;
                    }
                }

                if (File.Exists(pathOrUrl))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(Path.GetFullPath(pathOrUrl), UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    if (bitmap.CanFreeze) bitmap.Freeze();
                    return bitmap;
                }

                string cleanRelative = pathOrUrl.TrimStart('/', '\\');
                string localBasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, cleanRelative);
                if (File.Exists(localBasePath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(localBasePath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    if (bitmap.CanFreeze) bitmap.Freeze();
                    return bitmap;
                }

                string appDataLogoPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamMesh", "logos", Path.GetFileName(cleanRelative));
                if (File.Exists(appDataLogoPath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(appDataLogoPath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    if (bitmap.CanFreeze) bitmap.Freeze();
                    return bitmap;
                }
            }
            catch { }
            return null;
        }

        private BitmapImage? TryLoadBitmap(string pathOrUrl)
        {
            try
            {
                if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uriResult) &&
                    (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
                {
                    if (pathOrUrl.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return null;

                    // Serve immediately from disk cache if available
                    string cacheFile = GetDiskCachePath(pathOrUrl);
                    if (File.Exists(cacheFile))
                    {
                        var info = new FileInfo(cacheFile);
                        if (info.Length > 32 && (DateTime.Now - info.LastWriteTime).TotalDays < 30)
                        {
                            var cached = DecodeBytesFrozen(File.ReadAllBytes(cacheFile));
                            if (cached != null) return cached;
                        }
                    }

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = uriResult;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    bitmap.CacheOption = BitmapCacheOption.Default;
                    bitmap.EndInit();
                    return bitmap;
                }

                return TryLoadLocalOnlyBitmap(pathOrUrl);
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
