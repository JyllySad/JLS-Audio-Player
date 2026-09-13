using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace JLS.Converters
{
    public class ColorToContrastBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            Color color = Colors.Transparent;

            if (value is Color c)
            {
                color = c;
            }
            else if (value is string hex)
            {
                try { color = (Color)ColorConverter.ConvertFromString(hex); }
                catch { return Brushes.White; }
            }
            else
            {
                return Brushes.White;
            }

            double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
            
            return luminance > 150 ? Brushes.Black : Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}