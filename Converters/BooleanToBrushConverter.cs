using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace StreamMesh.Converters
{
    public class BooleanToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool val = value is bool b && b;
            string param = parameter as string ?? "";

            if (param == "Fav")
            {
                return val ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 215, 0)) : System.Windows.Media.Brushes.Gray;
            }

            return val ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return System.Windows.Data.Binding.DoNothing;
        }
    }
}
