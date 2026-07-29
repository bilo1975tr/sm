using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace StreamMesh.Converters
{
    public class LogoCacheConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string? url = value as string;
            if (string.IsNullOrEmpty(url)) return null;

            try
            {
                return new BitmapImage(new Uri(url));
            }
            catch { return null; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return System.Windows.Data.Binding.DoNothing;
        }
    }
}
