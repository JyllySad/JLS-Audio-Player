using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JLS.ViewModels
{
    public partial class BulkTrackInfoViewModel : ObservableObject
    {
        public Func<string, Task>? AfterSaveHook { get; set; }

        public Func<string, Task>? BeforeSaveHook { get; set; }

        [ObservableProperty] private bool _isOpen;
        [ObservableProperty] private bool _removeAlbumArt;

        [ObservableProperty] private bool _isArtistCleared;
        [ObservableProperty] private bool _isAlbumCleared;
        [ObservableProperty] private bool _isGenreCleared;
        [ObservableProperty] private bool _isYearCleared;

        [ObservableProperty] private bool _isArtistModified;
        [ObservableProperty] private bool _isAlbumModified;
        [ObservableProperty] private bool _isGenreModified;
        [ObservableProperty] private bool _isYearModified;
        [ObservableProperty] private bool _isCoverModified;

        [ObservableProperty] private string _editArtistName = string.Empty;
        [ObservableProperty] private string _editAlbumName = string.Empty;
        [ObservableProperty] private string _editGenre = string.Empty;
        [ObservableProperty] private string _editTrackYear = string.Empty;

        [ObservableProperty] private string _originalArtistName = string.Empty;
        [ObservableProperty] private string _originalAlbumName = string.Empty;
        [ObservableProperty] private string _originalGenre = string.Empty;
        [ObservableProperty] private string _originalTrackYear = string.Empty;

        [ObservableProperty] private string _editAlbumArtPath = string.Empty;
        [ObservableProperty] private ImageSource? _editAlbumArtPreview;
        [ObservableProperty] private ImageSource? _originalAlbumArtPreview;

        [ObservableProperty] private string _headerText = "Edit Multiple Tracks";

        private List<string> _filePaths = new();

        partial void OnEditArtistNameChanged(string value)
        {
            if (!string.IsNullOrEmpty(value)) IsArtistCleared = false;
            IsArtistModified = IsArtistCleared || (value != OriginalArtistName);
        }
        partial void OnEditAlbumNameChanged(string value)
        {
            if (!string.IsNullOrEmpty(value)) IsAlbumCleared = false;
            IsAlbumModified = IsAlbumCleared || (value != OriginalAlbumName);
        }
        partial void OnEditGenreChanged(string value)
        {
            if (!string.IsNullOrEmpty(value)) IsGenreCleared = false;
            IsGenreModified = IsGenreCleared || (value != OriginalGenre);
        }
        partial void OnEditTrackYearChanged(string value)
        {
            if (!string.IsNullOrEmpty(value)) IsYearCleared = false;
            IsYearModified = IsYearCleared || (value != OriginalTrackYear);
        }

        public void Open(List<string> filePaths)
        {
            if (filePaths == null || filePaths.Count == 0) return;

            _filePaths = filePaths;
            HeaderText = $"Editing {filePaths.Count} Tracks";

            IsArtistCleared = false; IsAlbumCleared = false;
            IsGenreCleared = false; IsYearCleared = false;
            RemoveAlbumArt = false;

            EditArtistName = string.Empty; OriginalArtistName = string.Empty;
            EditAlbumName = string.Empty; OriginalAlbumName = string.Empty;
            EditGenre = string.Empty; OriginalGenre = string.Empty;
            EditTrackYear = string.Empty; OriginalTrackYear = string.Empty;
            EditAlbumArtPath = string.Empty;
            EditAlbumArtPreview = null; OriginalAlbumArtPreview = null;

            IsOpen = true;

            Task.Run(() =>
            {
                string? commonArtist = null; string? commonAlbum = null;
                string? commonGenre = null; string? commonYear = null;
                byte[]? commonPicData = null; uint commonPicChecksum = 0;

                bool mixedArtist = false, mixedAlbum = false, mixedGenre = false, mixedYear = false, mixedPic = false;
                bool first = true;

                foreach (var path in filePaths)
                {
                    if (!File.Exists(path)) continue;

                    try
                    {
                        using var file = TagLib.File.Create(new JLS.Services.SafeFileAbstraction(path));
                        var tag = file.Tag;

                        string artist = tag.FirstPerformer ?? "";
                        string album = tag.Album ?? "";
                        string genre = tag.FirstGenre ?? "";
                        string year = tag.Year > 0 ? tag.Year.ToString() : "";

                        uint picChecksum = 0; byte[]? picData = null;
                        if (tag.Pictures.Length > 0)
                        {
                            picData = tag.Pictures[0].Data.Data;
                            picChecksum = tag.Pictures[0].Data.Checksum;
                        }

                        if (first)
                        {
                            commonArtist = artist; commonAlbum = album;
                            commonGenre = genre; commonYear = year;
                            commonPicChecksum = picChecksum; commonPicData = picData;
                            first = false;
                        }
                        else
                        {
                            if (!mixedArtist && commonArtist != artist) mixedArtist = true;
                            if (!mixedAlbum && commonAlbum != album) mixedAlbum = true;
                            if (!mixedGenre && commonGenre != genre) mixedGenre = true;
                            if (!mixedYear && commonYear != year) mixedYear = true;
                            if (!mixedPic && commonPicChecksum != picChecksum) mixedPic = true;
                        }
                    }
                    catch { }
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (!mixedArtist && !string.IsNullOrEmpty(commonArtist)) { OriginalArtistName = commonArtist; EditArtistName = commonArtist; }
                    if (!mixedAlbum && !string.IsNullOrEmpty(commonAlbum)) { OriginalAlbumName = commonAlbum; EditAlbumName = commonAlbum; }
                    if (!mixedGenre && !string.IsNullOrEmpty(commonGenre)) { OriginalGenre = commonGenre; EditGenre = commonGenre; }
                    if (!mixedYear && !string.IsNullOrEmpty(commonYear)) { OriginalTrackYear = commonYear; EditTrackYear = commonYear; }

                    if (!mixedPic && commonPicData != null)
                    {
                        try
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            using var ms = new MemoryStream(commonPicData);
                            bitmap.StreamSource = ms;
                            bitmap.DecodePixelWidth = 300;
                            bitmap.EndInit();
                            bitmap.Freeze();

                            OriginalAlbumArtPreview = bitmap;
                            EditAlbumArtPreview = bitmap;
                        }
                        catch { }
                    }

                    IsArtistModified = false;
                    IsAlbumModified = false;
                    IsGenreModified = false;
                    IsYearModified = false;
                    IsCoverModified = false;
                });
            });
        }

        [RelayCommand] private void Close() { IsOpen = false; }

        [RelayCommand]
        private void ToggleArtist()
        {
            if (IsArtistModified) { EditArtistName = OriginalArtistName; IsArtistCleared = false; }
            else { EditArtistName = string.Empty; IsArtistCleared = true; }
            IsArtistModified = IsArtistCleared || (EditArtistName != OriginalArtistName);
        }

        [RelayCommand]
        private void ToggleAlbum()
        {
            if (IsAlbumModified) { EditAlbumName = OriginalAlbumName; IsAlbumCleared = false; }
            else { EditAlbumName = string.Empty; IsAlbumCleared = true; }
            IsAlbumModified = IsAlbumCleared || (EditAlbumName != OriginalAlbumName);
        }

        [RelayCommand]
        private void ToggleGenre()
        {
            if (IsGenreModified) { EditGenre = OriginalGenre; IsGenreCleared = false; }
            else { EditGenre = string.Empty; IsGenreCleared = true; }
            IsGenreModified = IsGenreCleared || (EditGenre != OriginalGenre);
        }

        [RelayCommand]
        private void ToggleYear()
        {
            if (IsYearModified) { EditTrackYear = OriginalTrackYear; IsYearCleared = false; }
            else { EditTrackYear = string.Empty; IsYearCleared = true; }
            IsYearModified = IsYearCleared || (EditTrackYear != OriginalTrackYear);
        }

        [RelayCommand]
        private void ToggleCoverArt()
        {
            if (IsCoverModified)
            {
                EditAlbumArtPreview = OriginalAlbumArtPreview;
                EditAlbumArtPath = string.Empty;
                RemoveAlbumArt = false;
                IsCoverModified = false;
            }
            else
            {
                EditAlbumArtPreview = null;
                EditAlbumArtPath = string.Empty;
                RemoveAlbumArt = true;
                IsCoverModified = true;
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

            if (_filePaths != null && _filePaths.Count >= 2)
            {
                string? dir1 = System.IO.Path.GetDirectoryName(_filePaths[0]);
                string? dir2 = System.IO.Path.GetDirectoryName(_filePaths[1]);

                if (!string.IsNullOrEmpty(dir1) && string.Equals(dir1, dir2, StringComparison.OrdinalIgnoreCase))
                {
                    openFileDialog.InitialDirectory = dir1;
                }
            }
            else if (_filePaths != null && _filePaths.Count == 1)
            {
                string? dir = System.IO.Path.GetDirectoryName(_filePaths[0]);
                if (!string.IsNullOrEmpty(dir))
                {
                    openFileDialog.InitialDirectory = dir;
                }
            }
            if (openFileDialog.ShowDialog() == true)
            {
                EditAlbumArtPath = openFileDialog.FileName;
                RemoveAlbumArt = false;
                IsCoverModified = true;      

                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    var imgData = File.ReadAllBytes(JLS.Services.PathHelper.GetSafePath(EditAlbumArtPath));
                    using (var ms = new MemoryStream(imgData))
                    {
                        bitmap.StreamSource = ms;
                        bitmap.DecodePixelWidth = 300;
                        bitmap.EndInit();
                    }
                    bitmap.Freeze();
                    EditAlbumArtPreview = bitmap;
                }
                catch
                {
                    EditAlbumArtPath = string.Empty;
                    EditAlbumArtPreview = null;
                }
            }
        }

        [RelayCommand]
        private async Task SaveTags()
        {
            try
            {
                if (BeforeSaveHook != null)
                {
                    foreach (var path in _filePaths)
                    {
                        await BeforeSaveHook(path);
                    }
                }

                await Task.Run(() =>
                {
                    foreach (var path in _filePaths)
                    {
                        if (!File.Exists(path)) continue;

                        try
                        {
                            var fileInfo = new FileInfo(JLS.Services.PathHelper.GetSafePath(path));
                            bool wasReadOnly = false;
                            if (fileInfo.Exists && fileInfo.IsReadOnly)
                            {
                                wasReadOnly = true;
                                fileInfo.IsReadOnly = false;
                            }

                            using var file = TagLib.File.Create(new JLS.Services.SafeFileAbstraction(path));
                            bool changed = false;

                            if (IsArtistCleared) { file.Tag.Performers = Array.Empty<string>(); changed = true; }
                            else if (!string.IsNullOrWhiteSpace(EditArtistName) && IsArtistModified) { file.Tag.Performers = new[] { EditArtistName.Trim() }; changed = true; }

                            if (IsAlbumCleared) { file.Tag.Album = ""; changed = true; }
                            else if (!string.IsNullOrWhiteSpace(EditAlbumName) && IsAlbumModified) { file.Tag.Album = EditAlbumName.Trim(); changed = true; }

                            if (IsGenreCleared) { file.Tag.Genres = Array.Empty<string>(); changed = true; }
                            else if (!string.IsNullOrWhiteSpace(EditGenre) && IsGenreModified) { file.Tag.Genres = new[] { EditGenre.Trim() }; changed = true; }

                            if (IsYearCleared) { file.Tag.Year = 0; changed = true; }
                            else if (!string.IsNullOrWhiteSpace(EditTrackYear) && uint.TryParse(EditTrackYear.Trim(), out uint year) && IsYearModified)
                            { file.Tag.Year = year; changed = true; }

                            if (RemoveAlbumArt) { file.Tag.Pictures = Array.Empty<TagLib.IPicture>(); changed = true; }
                            else if (!string.IsNullOrEmpty(EditAlbumArtPath) && File.Exists(EditAlbumArtPath) && IsCoverModified)
                            {
                                file.Tag.Pictures = new[] { new TagLib.Picture(JLS.Services.PathHelper.GetSafePath(EditAlbumArtPath)) };
                                changed = true;
                            }

                            if (changed) file.Save();

                            if (wasReadOnly) fileInfo.IsReadOnly = true;
                        }
                        catch { }
                    }
                });

                if (AfterSaveHook != null)
                {
                    foreach (var path in _filePaths)
                    {
                        await AfterSaveHook(path);
                    }
                }

                IsOpen = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving tags: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}