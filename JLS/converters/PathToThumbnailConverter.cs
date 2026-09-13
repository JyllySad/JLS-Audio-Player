using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq; 
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace JLS.Converters
{
    public class PathToThumbnailConverter : IValueConverter
    {
        private static readonly Dictionary<string, BitmapImage?> _cache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly object _cacheLock = new object();

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string filePath && File.Exists(filePath))
            {
                bool isSyncOnly = parameter as string == "SyncOnly";

                lock (_cacheLock)
                {
                    if (_cache.TryGetValue(filePath, out var cachedBitmap))
                        return cachedBitmap;
                }

                if (isSyncOnly)
                {
                    return DependencyProperty.UnsetValue;
                }

                BitmapImage? finalBitmap = null;
                string? directory = Path.GetDirectoryName(filePath);
                string folderKey = directory + "\\*FOLDER_COVER*";

                try
                {
                    using (var file = TagLib.File.Create(new JLS.Services.SafeFileAbstraction(filePath)))
                    {
                        if (file.Tag.Pictures.Length > 0)
                        {
                            var bin = file.Tag.Pictures[0].Data.Data;
                            finalBitmap = CreateBitmap(null, bin);
                        }
                    }

                    if (finalBitmap == null && !string.IsNullOrEmpty(directory))
                    {
                        lock (_cacheLock)
                        {
                            if (_cache.TryGetValue(folderKey, out var folderBitmap))
                                finalBitmap = folderBitmap;
                        }

                        if (finalBitmap == null)
                        {
                            string[] possibleNames = { "cover.jpg", "cover.png", "folder.jpg", "folder.png", "front.jpg", "front.png" };
                            foreach (var name in possibleNames)
                            {
                                string coverPath = Path.Combine(directory, name);
                                if (File.Exists(coverPath))
                                {
                                    finalBitmap = CreateBitmap(JLS.Services.PathHelper.GetSafePath(coverPath), null);
                                    lock (_cacheLock)
                                    {
                                        ManageCacheSize();
                                        _cache[folderKey] = finalBitmap;
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
                catch
                {
                   
                }

                lock (_cacheLock)
                {
                    ManageCacheSize();
                    _cache[filePath] = finalBitmap;
                }

                return finalBitmap;
            }

            return null;
        }

        private BitmapImage CreateBitmap(string? uriPath, byte[]? data)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.DecodePixelWidth = 192;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;

            if (data != null)
                bitmap.StreamSource = new MemoryStream(data);
            else if (uriPath != null)
            {
                var imgData = System.IO.File.ReadAllBytes(JLS.Services.PathHelper.GetSafePath(uriPath));
                bitmap.StreamSource = new System.IO.MemoryStream(imgData);
            }

            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private void ManageCacheSize()
        {
            if (_cache.Count >= 1000)
            {
                var keysToRemove = _cache.Keys.Take(500).ToList();
                foreach (var key in keysToRemove)
                {
                    _cache.Remove(key);
                }
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public static void ClearCacheForFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            lock (_cacheLock)
            {
                _cache.Remove(filePath);

                string? directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    _cache.Remove(directory + "\\*FOLDER_COVER*");
                }
            }
        }
    }
}