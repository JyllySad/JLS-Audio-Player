using System;
using System.Globalization;
using System.Windows.Data;
using JLS.Models;

namespace JLS.Converters
{
    public class PlaylistTrackMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && values[0] is Playlist playlist && values[1] is FileSystemItem track)
            {
                if (targetType == typeof(bool) || targetType == typeof(bool?))
                {
                    return playlist.TrackPaths.Contains(track.FullPath);
                }
                
                return values.Clone(); 
            }
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) 
            => throw new NotImplementedException();
    }
}