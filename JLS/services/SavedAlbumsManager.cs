using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using JLS.Models;

namespace JLS.Services
{
    public class SavedAlbumsManager
    {
        private readonly string _filePath;

        public ObservableCollection<SavedAlbum> SavedAlbums { get; private set; } = new();

        public SavedAlbumsManager()
        {
            _filePath = Path.Combine(AppPaths.BaseFolder, "saved_albums.json");
            LoadAlbums();
        }

        public void AddAlbum(string title, string artist, string year, string coverPath, Color dominantColor, Color lightColor, System.Collections.Generic.List<string> trackPaths)
        {
            if (SavedAlbums.Any(a => a.Title == title && a.Artist == artist)) return;

            var album = new SavedAlbum
            {
                Title = title,
                Artist = artist,
                Year = year,
                CoverPath = coverPath ?? string.Empty,
                PrimaryColorHex = $"#{dominantColor.A:X2}{dominantColor.R:X2}{dominantColor.G:X2}{dominantColor.B:X2}",

                LightColorHex = $"#{lightColor.A:X2}{lightColor.R:X2}{lightColor.G:X2}{lightColor.B:X2}",

                TrackPaths = trackPaths,
                AddedDate = DateTime.Now
            };

            double luminance = (0.299 * dominantColor.R + 0.587 * dominantColor.G + 0.114 * dominantColor.B);
            album.TextColorHex = luminance > 150 ? "#000000" : "#FFFFFF";

            SavedAlbums.Insert(0, album);
            PopulateItems(album);
            SaveAlbums();
        }

        public void RemoveAlbum(SavedAlbum album)
        {
            if (SavedAlbums.Contains(album))
            {
                SavedAlbums.Remove(album);
                SaveAlbums();
            }
        }

        public void SaveAlbums()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(SavedAlbums, options);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save albums: {ex.Message}");
            }
        }

        private void LoadAlbums()
        {
            if (!File.Exists(_filePath)) return;

            try
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<ObservableCollection<SavedAlbum>>(json);

                if (loaded != null)
                {
                    SavedAlbums = loaded;

                    foreach (var album in SavedAlbums)
                    {
                        PopulateItems(album);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load saved albums: {ex.Message}");
            }
        }

        private void PopulateItems(SavedAlbum album)
        {
            album.Items = new ObservableCollection<FileSystemItem>();
            foreach (var path in album.TrackPaths)
            {
                if (File.Exists(path))
                {
                    var fileInfo = new FileInfo(path);
                    var item = new FileSystemItem
                    {
                        FullPath = path,
                        Name = fileInfo.Name,
                        IsDirectory = false,
                        Size = fileInfo.Length,
                        Modified = fileInfo.LastWriteTime
                    };

                    try
                    {
                        using (var tagFile = TagLib.File.Create(new JLS.Services.SafeFileAbstraction(path)))
                        {
                            item.Duration = tagFile.Properties.Duration.ToString(@"mm\:ss");
                            item.Artist = tagFile.Tag.FirstPerformer;
                            item.Album = tagFile.Tag.Album;
                            item.TrackNumber = (int)tagFile.Tag.Track;
                            item.Title = tagFile.Tag.Title;
                            item.Year = tagFile.Tag.Year > 0 ? tagFile.Tag.Year.ToString() : "";
                            item.Bitrate = tagFile.Properties.AudioBitrate;
                            item.SampleRate = tagFile.Properties.AudioSampleRate;
                            item.BitsPerSample = tagFile.Properties.BitsPerSample;
                        }
                    }
                    catch
                    {
                    }

                    album.Items.Add(item);
                }
            }
        }
    }
}