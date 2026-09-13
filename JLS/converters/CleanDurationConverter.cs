namespace JLS.Converters
{
    public class CleanDurationConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string text)
            {
                var parts = text.Split('•');
                if (parts.Length >= 3)
                {
                    return $"{parts[0].Trim()}  •  {parts[1].Trim()}";
                }
            }
            return value; 
        }

        public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new System.NotImplementedException();
    }
}