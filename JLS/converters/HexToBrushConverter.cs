using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace JLS.Converters
{
    public class HexToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hex && !string.IsNullOrEmpty(hex))
            {
                try
                {
                    return new BrushConverter().ConvertFrom(hex) as SolidColorBrush ?? Brushes.Gray;
                }
                catch
                {
                    return Brushes.Gray; 
                }
            }
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}