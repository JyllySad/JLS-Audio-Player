using System;
using System.IO;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Globalization;

namespace JLS.Converters
{
    public class SubfolderToCoverConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string folderPath && Directory.Exists(folderPath))
            {
                try
                {
                    var cover = GetCoverFromFolder(folderPath);
                    if (cover != null) return cover;

                    var subdirs = Directory.EnumerateDirectories(folderPath).OrderBy(d => d).ToList();
                    foreach (var subdir in subdirs)
                    {
                        var subCover = GetCoverFromFolder(subdir);
                        if (subCover != null) return subCover; 
                    }
                }
                catch { }
            }
            return null;
        }

        private BitmapImage? GetCoverFromFolder(string path)
        {
            try
            {
                string[] possibleNames = { "cover.jpg", "folder.jpg", "album.jpg", "cover.png", "folder.png", "front.jpg", "front.png" };
                foreach (var name in possibleNames)
                {
                    string fullPath = Path.Combine(path, name);
                    if (File.Exists(fullPath))
                        return LoadBitmap(fullPath, null);
                }

                var extensions = new[] { ".mp3", ".flac", ".wav", ".m4a", ".ogg", ".aac", ".wma", ".alac", ".ape", ".dsf", ".dff" };
                var firstAudio = Directory.EnumerateFiles(path)
                                          .FirstOrDefault(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                if (firstAudio != null)
                {
                    using (var tagFile = TagLib.File.Create(new JLS.Services.SafeFileAbstraction(firstAudio)))
                    {
                        if (tagFile.Tag.Pictures.Length > 0)
                            return LoadBitmap(null, tagFile.Tag.Pictures[0].Data.Data);
                    }
                }
            }
            catch { }
            return null;
        }

        private BitmapImage LoadBitmap(string? path, byte[]? data)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.DecodePixelWidth = 64; 
            bitmap.CacheOption = BitmapCacheOption.OnLoad;

            if (path != null)
            {
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();
            }
            else if (data != null)
            {
                using (var ms = new MemoryStream(data))
                {
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();
                }
            }
            return bitmap;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}