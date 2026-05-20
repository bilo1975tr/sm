using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace StreamMesh.Converters
{
    public class LogoCacheConverter : IValueConverter
    {
        private static readonly string CacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StreamMesh", "LogoCache");
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        static LogoCacheConverter()
        {
            if (!Directory.Exists(CacheDir))
            {
                Directory.CreateDirectory(CacheDir);
            }
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var url = value as string;
            if (string.IsNullOrWhiteSpace(url)) return null;
            
            url = url.Split(',')[0].Trim();
            if (string.IsNullOrWhiteSpace(url)) return null;

            if (!url.StartsWith("http")) return url; // Might be local already

            string cacheFileName = GetHash(url) + Path.GetExtension(url.Split('?')[0]);
            // If it has no extension, assume .png
            if (string.IsNullOrEmpty(Path.GetExtension(cacheFileName)))
            {
                cacheFileName += ".png";
            }

            string localPath = Path.Combine(CacheDir, cacheFileName);

            if (File.Exists(localPath))
            {
                return localPath; // Return local file path
            }
            else
            {
                // Download in background
                Task.Run(async () =>
                {
                    try
                    {
                        var bytes = await _httpClient.GetByteArrayAsync(url);
                        await File.WriteAllBytesAsync(localPath, bytes);
                    }
                    catch
                    {
                        // Ignore errors (e.g. 404, timeout)
                    }
                });

                // Return original URL for now so it loads via WPF native mechanism
                return url;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private static string GetHash(string input)
        {
            using (var md5 = MD5.Create())
            {
                byte[] data = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder sBuilder = new StringBuilder();
                for (int i = 0; i < data.Length; i++)
                {
                    sBuilder.Append(data[i].ToString("x2"));
                }
                return sBuilder.ToString();
            }
        }
    }
}
