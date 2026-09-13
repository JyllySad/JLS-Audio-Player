using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JLS.ViewModels
{
    public partial class TrackInfoViewModel : ObservableObject
    {
        public Func<string, Task>? BeforeSaveHook { get; set; }
        public Func<string, Task>? AfterSaveHook { get; set; }

        [ObservableProperty] private bool _isOpen;
        [ObservableProperty] private bool _isEditingTags;
        [ObservableProperty] private bool _removeAlbumArt;

        [ObservableProperty] private string _trackFullPath = string.Empty;
        [ObservableProperty] private string _trackName = "Unknown Title";
        [ObservableProperty] private string _artistName = "Unknown Artist";
        [ObservableProperty] private string _albumName = "Unknown Album";
        [ObservableProperty] private string _trackYear = "Unknown Year";
        [ObservableProperty] private string _genre = "Unknown Genre";
        [ObservableProperty] private string _trackNumber = "---";
        [ObservableProperty] private string _trackFileSize = "0 MB";
        [ObservableProperty] private string _audioFormat = "---";
        [ObservableProperty] private string _audioBitDepth = "---";
        [ObservableProperty] private string _audioSampleRate = "---";
        [ObservableProperty] private string _audioBitrate = "---";
        [ObservableProperty] private ImageSource? _albumArt;

        [ObservableProperty] private string _editTrackName = string.Empty;
        [ObservableProperty] private string _editArtistName = string.Empty;
        [ObservableProperty] private string _editAlbumName = string.Empty;
        [ObservableProperty] private string _editGenre = string.Empty;
        [ObservableProperty] private string _editTrackYear = string.Empty;
        [ObservableProperty] private string _editTrackNumber = string.Empty;
        [ObservableProperty] private string _editAlbumArtPath = string.Empty;
        [ObservableProperty] private ImageSource? _editAlbumArtPreview;
        [ObservableProperty] private string _trackDuration = "00:00";

        [RelayCommand]
        private void OpenTrackLocationInExplorer()
        {
            JLS.Services.ExplorerHelper.OpenFolderAndSelectFile(TrackFullPath);
        }

        public void Open(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

            TrackFullPath = filePath;
            IsEditingTags = false;
            IsOpen = true;

            _ = LoadMetadataAsync(filePath);
        }

        [RelayCommand]
        private void Close()
        {
            IsOpen = false;
        }

        [RelayCommand]
        private void ClearCoverArt()
        {
            EditAlbumArtPreview = null;
            EditAlbumArtPath = string.Empty;
            RemoveAlbumArt = true;
        }

        private void ResetMetadata(string filePath)
        {
            TrackName = Path.GetFileNameWithoutExtension(filePath);
            ArtistName = "Unknown Artist";
            AlbumName = "Unknown Album";
            TrackYear = "Unknown Year";
            Genre = "Unknown Genre";
            TrackNumber = "---";
            TrackFileSize = "0 MB";
            AudioFormat = "---";
            AudioBitDepth = "---";
            AudioSampleRate = "---";
            AudioBitrate = "---";
            AlbumArt = null;
            TrackDuration = "00:00";
        }

        private async Task LoadMetadataAsync(string filePath)
        {
            ResetMetadata(filePath);

            try
            {
                string artist = "", album = "", title = "", year = "", genre = "", trackNum = "";
                string fileSize = "Unknown Size";
                string format = "", freqStr = "", bitrateStr = "", bitDepthStr = "";
                string durationStr = "00:00";
                byte[]? picBytes = null;
                await Task.Run(() =>
                {
                    using var file = TagLib.File.Create(new JLS.Services.SafeFileAbstraction(filePath));

                    artist = !string.IsNullOrEmpty(file.Tag.FirstPerformer) ? file.Tag.FirstPerformer : "Unknown Artist";
                    album = !string.IsNullOrEmpty(file.Tag.Album) ? file.Tag.Album : "Unknown Album";
                    title = !string.IsNullOrEmpty(file.Tag.Title) ? file.Tag.Title : Path.GetFileNameWithoutExtension(filePath);
                    year = file.Tag.Year > 0 ? file.Tag.Year.ToString() : "Unknown Year";
                    genre = !string.IsNullOrEmpty(file.Tag.FirstGenre) ? file.Tag.FirstGenre : "Unknown Genre";
                    trackNum = file.Tag.Track > 0 ? file.Tag.Track.ToString() : "---";

                    try { fileSize = $"{(new FileInfo(JLS.Services.PathHelper.GetSafePath(filePath)).Length / 1048576.0):0.00} MB"; } catch { }

                    format = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
                    int freq = file.Properties.AudioSampleRate;
                    int bitrate = file.Properties.AudioBitrate;
                    int bits = file.Properties.BitsPerSample;

                    var duration = file.Properties.Duration;
                    durationStr = duration.TotalHours >= 1 ? duration.ToString(@"hh\:mm\:ss") : duration.ToString(@"mm\:ss");

                    freqStr = freq > 0 ? $"{freq / 1000.0} kHz" : "---";
                    bitrateStr = bitrate > 0 ? $"{bitrate} kbps" : "---";
                    bitDepthStr = bits > 0 ? $"{bits} Bit" : "---";

                    if (file.Tag.Pictures.Length > 0)
                    {
                        picBytes = file.Tag.Pictures[0].Data.Data;
                    }
                });

                if (filePath != TrackFullPath) return;

                ArtistName = artist;
                AlbumName = album;
                TrackName = title;
                TrackYear = year;
                Genre = genre;
                TrackNumber = trackNum;
                TrackFileSize = fileSize;
                AudioFormat = format;
                AudioSampleRate = freqStr;
                AudioBitrate = bitrateStr;
                AudioBitDepth = bitDepthStr;
                TrackDuration = durationStr;

                if (picBytes != null)       
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.DecodePixelWidth = 800;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;

                        using var ms = new MemoryStream(picBytes);
                        bitmap.StreamSource = ms;
                        bitmap.EndInit();

                        bitmap.Freeze();
                        AlbumArt = bitmap;
                    }
                    catch (Exception imgEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to decode album art: {imgEx.Message}");
                        AlbumArt = null;
                    }
                }
                else
                {
                    AlbumArt = null;        
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Metadata error: {ex.Message}");
            }
        }

        [RelayCommand]
        private void BeginEditTags()
        {
            EditTrackName = TrackName;
            EditArtistName = ArtistName;
            EditAlbumName = AlbumName;
            EditGenre = Genre;
            EditTrackYear = TrackYear == "Unknown Year" ? "" : TrackYear;
            EditTrackNumber = TrackNumber == "---" ? "" : TrackNumber;

            EditAlbumArtPreview = AlbumArt;
            EditAlbumArtPath = string.Empty;
            RemoveAlbumArt = false;
            IsEditingTags = true;
        }

        [RelayCommand]
        private void CancelEditTags()
        {
            IsEditingTags = false;
        }

        [RelayCommand]
        private async Task SaveTags()
        {
            if (!File.Exists(TrackFullPath))
            {
                System.Windows.MessageBox.Show(
                    "The file could not be found. It may have been moved or deleted.",
                    "File Not Found", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            bool isHookTriggered = false;
            bool wasReadOnly = false;

            try
            {
                if (BeforeSaveHook is not null)
                {
                    isHookTriggered = true;      
                    await BeforeSaveHook(TrackFullPath);
                }

                var fileInfo = new FileInfo(JLS.Services.PathHelper.GetSafePath(TrackFullPath));
                if (fileInfo.Exists && fileInfo.IsReadOnly)
                {
                    wasReadOnly = true;
                    fileInfo.IsReadOnly = false;
                }

                int maxRetries = 3;
                bool isSaved = false;

                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        await Task.Delay(150);

                        await Task.Run(() =>
                        {
                            using var file = TagLib.File.Create(new JLS.Services.SafeFileAbstraction(TrackFullPath));
                            file.Tag.Title = EditTrackName;
                            file.Tag.Performers = new[] { EditArtistName };
                            file.Tag.Album = EditAlbumName;
                            file.Tag.Genres = new[] { EditGenre };

                            if (uint.TryParse(EditTrackYear, out uint year)) file.Tag.Year = year;
                            else file.Tag.Year = 0;

                            if (uint.TryParse(EditTrackNumber, out uint trackNum)) file.Tag.Track = trackNum;
                            else file.Tag.Track = 0;

                            if (RemoveAlbumArt)
                            {
                                file.Tag.Pictures = Array.Empty<TagLib.IPicture>();
                            }
                            else if (!string.IsNullOrEmpty(EditAlbumArtPath) && File.Exists(EditAlbumArtPath))
                            {
                                file.Tag.Pictures = new[] { new TagLib.Picture(JLS.Services.PathHelper.GetSafePath(EditAlbumArtPath)) };
                            }

                            file.Save();
                        });

                        isSaved = true;
                        break;
                    }
                    catch (IOException)
                    {
                        if (i == maxRetries - 1) throw;
                        await Task.Delay(300);
                    }
                }

                if (!isSaved) return;

                await LoadMetadataAsync(TrackFullPath);
                IsEditingTags = false;
            }
            catch (UnauthorizedAccessException)
            {
                System.Windows.MessageBox.Show("Access denied. You may not have permission to edit files in this folder.", "Access Denied", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            catch (IOException)
            {
                System.Windows.MessageBox.Show("Failed to save tags. The file is still being used by another process.", "File in Use", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error saving tags: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                if (wasReadOnly && File.Exists(TrackFullPath))
                {
                    try { new FileInfo(JLS.Services.PathHelper.GetSafePath(TrackFullPath)).IsReadOnly = true; } catch { }
                }

                if (isHookTriggered && AfterSaveHook is not null)
                {
                    try
                    {
                        await AfterSaveHook(TrackFullPath);
                    }
                    catch (Exception hookEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"AfterSaveHook error: {hookEx.Message}");
                    }
                }
            }
        }

        [RelayCommand]
        private void ChangeCoverArt()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                Title = "Select Track Cover"
            };

            if (!string.IsNullOrEmpty(TrackFullPath))
            {
                string? trackDirectory = Path.GetDirectoryName(TrackFullPath);

                if (!string.IsNullOrEmpty(trackDirectory) && Directory.Exists(trackDirectory))
                {
                    openFileDialog.InitialDirectory = trackDirectory;
                }
            }

            if (openFileDialog.ShowDialog() == true)
            {
                EditAlbumArtPath = openFileDialog.FileName;
                RemoveAlbumArt = false;

                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;

                    var imgData = System.IO.File.ReadAllBytes(JLS.Services.PathHelper.GetSafePath(EditAlbumArtPath));
                    using (var ms = new System.IO.MemoryStream(imgData))
                    {
                        bitmap.StreamSource = ms;
                        bitmap.DecodePixelWidth = 300;
                        bitmap.EndInit();
                    }
                    bitmap.Freeze();

                    EditAlbumArtPreview = bitmap;
                }
                catch (Exception)
                {
                    System.Windows.MessageBox.Show(
                        "Failed to load the selected image. It might be corrupted or in an unsupported format.",
                        "Invalid Image",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);

                    EditAlbumArtPath = string.Empty;
                    EditAlbumArtPreview = null;
                }
            }
        }
    }
}