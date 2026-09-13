using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using JLS.Models; 

namespace JLS.Converters
{
    public class PlaylistStatsConverter : IMultiValueConverter
    {
        private static readonly string[] DurationFormats = { @"m\:ss", @"mm\:ss", @"h\:mm\:ss", @"hh\:mm\:ss" };

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length > 0 && values[0] is IEnumerable<FileSystemItem> items)
            {
                int count = 0;
                TimeSpan totalDuration = TimeSpan.Zero;

                foreach (var f in items)
                {
                    count++;
                    if (!string.IsNullOrEmpty(f.Duration))
                    {
                        if (TimeSpan.TryParseExact(f.Duration, DurationFormats, null, TimeSpanStyles.None, out TimeSpan parsedTime))
                        {
                            totalDuration += parsedTime;
                        }
                    }
                }

                string durStr = totalDuration.TotalHours >= 1 ?
                    $"{(int)totalDuration.TotalHours:D2}:{totalDuration.Minutes:D2}:{totalDuration.Seconds:D2}" :
                    $"{totalDuration.Minutes:D2}:{totalDuration.Seconds:D2}";

                return $"{count} tracks  •  {durStr}";
            }
            return "0 tracks  •  00:00";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}