using System;
using System.Globalization;
using System.Windows.Data;

namespace JLS.Converters
{
    public class DirectoryToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isDirectory)
            {
                return isDirectory ? "\uE8B7" : "\uE189";
            }
            return "\uE189";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}