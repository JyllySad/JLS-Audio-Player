using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using JLS.Models;

namespace JLS.Converters
{
    public class AlbumToCoverConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SavedAlbum album && album.TrackPaths != null && album.TrackPaths.Count > 0)
            {
                string firstFilePath = album.TrackPaths[0];
                string directory = Path.GetDirectoryName(firstFilePath) ?? string.Empty;

                string[] possibleNames = { "cover.jpg", "cover.png", "folder.jpg", "folder.png", "front.jpg", "front.png" };
                foreach (var name in possibleNames)
                {
                    string path = Path.Combine(directory, name);
                    if (File.Exists(path))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(path);
                        bmp.DecodePixelWidth = 100; 
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze();
                        return bmp;
                    }
                }

                if (File.Exists(firstFilePath))
                {
                    try
                    {
                        using var file = TagLib.File.Create(new JLS.Services.SafeFileAbstraction(firstFilePath));
                        if (file.Tag.Pictures.Length > 0)
                        {
                            using var ms = new MemoryStream(file.Tag.Pictures[0].Data.Data);
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.StreamSource = ms;
                            bmp.DecodePixelWidth = 100;
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.EndInit();
                            bmp.Freeze();
                            return bmp;
                        }
                    }
                    catch
                    {

                    }
                }
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}