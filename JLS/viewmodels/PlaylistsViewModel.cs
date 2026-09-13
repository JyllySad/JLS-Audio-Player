using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JLS.Models;
using JLS.Services;
using Microsoft.Win32;
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace JLS.ViewModels
{
    public partial class PlaylistsViewModel : ObservableObject
    {
        private static readonly string[] DurationFormats =
        {
            @"m\:ss",
            @"mm\:ss",
            @"h\:mm\:ss",
            @"hh\:mm\:ss"
        };

        public PlaylistManager Manager { get; }

        [ObservableProperty]
        private Playlist? _selectedPlaylist;

        [ObservableProperty]
        private string _currentlyPlayingPath = string.Empty;

        [ObservableProperty]
        private string _searchTrackText = string.Empty;

        partial void OnSearchTrackTextChanged(string value)
        {
            if (SelectedPlaylist == null) return;

            var view = CollectionViewSource.GetDefaultView(SelectedPlaylist.Items);
            if (string.IsNullOrWhiteSpace(value))
            {
                view.Filter = null;
            }
            else
            {
                view.Filter = item =>
                {
                    if (item is FileSystemItem f)
                    {
                        return (f.Name != null && f.Name.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
                               (f.Title != null && f.Title.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
                               (f.Artist != null && f.Artist.Contains(value, StringComparison.OrdinalIgnoreCase));
                    }
                    return false;
                };
            }
        }

        public void UpdateItemMetadata(string filePath)
        {
            if (Manager?.Playlists == null) return;

            bool wasUpdated = false;

            foreach (var playlist in Manager.Playlists)
            {
                var item = playlist.Items.FirstOrDefault(x => x.FullPath == filePath);
                if (item != null)
                {
                    item.ReloadMetadata();
                    wasUpdated = true;
                }
            }

            if (wasUpdated)
            {
                Manager.SavePlaylists();

                if (SelectedPlaylist != null && SelectedPlaylist.Items.Any(x => x.FullPath == filePath))
                {
                    RefreshView(SelectedPlaylist.Items);
                }
            }
        }

        [ObservableProperty]
        private string _searchPlaylistText = string.Empty;

        partial void OnSearchPlaylistTextChanged(string value)
        {
            if (Manager?.Playlists == null) return;

            var view = CollectionViewSource.GetDefaultView(Manager.Playlists);
            if (string.IsNullOrWhiteSpace(value))
            {
                view.Filter = null;
            }
            else
            {
                view.Filter = item =>
                {
                    if (item is Playlist p)
                    {
                        return p.Name != null && p.Name.Contains(value, StringComparison.OrdinalIgnoreCase);
                    }
                    return false;
                };
            }
        }

        [ObservableProperty]
        private string _playlistStats = "0 tracks  •  00:00  •  0 MB";

        [ObservableProperty]
        private string _playlistTotalDuration = "00:00";

        partial void OnSelectedPlaylistChanged(Playlist? oldValue, Playlist? newValue)
        {
            if (oldValue != null)
            {
                var oldView = CollectionViewSource.GetDefaultView(oldValue.Items);
                if (oldView != null)
                {
                    oldView.Filter = null;
                }
            }

            SearchTrackText = string.Empty;

            if (oldValue != null)
                oldValue.Items.CollectionChanged -= PlaylistItems_CollectionChanged;

            if (newValue != null)
                newValue.Items.CollectionChanged += PlaylistItems_CollectionChanged;

            CalculatePlaylistStats();
        }

        private void PlaylistItems_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            CalculatePlaylistStats();
        }

        private void CalculatePlaylistStats()
        {
            if (SelectedPlaylist == null || SelectedPlaylist.Items.Count == 0)
            {
                PlaylistStats = "0 tracks  •  00:00  •  0 MB";
                PlaylistTotalDuration = "00:00";
                return;
            }

            long totalBytes = 0;
            TimeSpan totalDuration = TimeSpan.Zero;

            foreach (var f in SelectedPlaylist.Items)
            {
                totalBytes += f.Size;
                if (!string.IsNullOrEmpty(f.Duration))
                {
                    if (TimeSpan.TryParseExact(f.Duration, DurationFormats, null, System.Globalization.TimeSpanStyles.None, out TimeSpan parsedTime))
                    {
                        totalDuration += parsedTime;
                    }
                }
            }

            string sizeStr = totalBytes >= 1073741824 ? $"{totalBytes / 1073741824.0:F2} GB" :
                             totalBytes >= 1048576 ? $"{totalBytes / 1048576.0:F2} MB" :
                             $"{totalBytes / 1024.0:F2} KB";

            string durStr = totalDuration.TotalHours >= 1 ?
                $"{(int)totalDuration.TotalHours:D2}:{totalDuration.Minutes:D2}:{totalDuration.Seconds:D2}" :
                $"{totalDuration.Minutes:D2}:{totalDuration.Seconds:D2}";

            PlaylistTotalDuration = durStr;
            PlaylistStats = $"{SelectedPlaylist.Items.Count} tracks  •  {durStr}  •  {sizeStr}";
        }

        [ObservableProperty]
        private bool _isRenameDialogOpen;

        [ObservableProperty]
        private string _editTitle = string.Empty;

        [ObservableProperty]
        private string _editArtist = string.Empty;

        [ObservableProperty]
        private string _editAlbum = string.Empty;

        [ObservableProperty]
        private string _editYear = string.Empty;

        [ObservableProperty]
        private string _editGenre = string.Empty;

        [ObservableProperty]
        private string _editTrackNumber = string.Empty;

        private FileSystemItem? _itemBeingRenamed;
        private bool _isResetToDefault;

        [RelayCommand]
        private void RenameTrack(FileSystemItem? track)
        {
            if (track == null) return;

            _itemBeingRenamed = track;
            EditTitle = track.Title ?? track.Name;
            EditArtist = track.Artist ?? "";
            EditAlbum = track.Album ?? "";
            EditYear = track.Year ?? "";
            EditTrackNumber = track.TrackNumber?.ToString() ?? "";
            EditGenre = track.Genre ?? "";

            _isResetToDefault = false;
            IsRenameDialogOpen = true;
        }

        [RelayCommand]
        private void ConfirmRename()
        {
            if (_itemBeingRenamed != null)
            {
                _itemBeingRenamed.Title = EditTitle;
                _itemBeingRenamed.Artist = EditArtist;
                _itemBeingRenamed.Album = EditAlbum;
                _itemBeingRenamed.Year = EditYear;
                _itemBeingRenamed.Genre = EditGenre;

                if (int.TryParse(EditTrackNumber, out int trackNum))
                    _itemBeingRenamed.TrackNumber = trackNum;
                else
                    _itemBeingRenamed.TrackNumber = null;

                _itemBeingRenamed.IsCustomNamed = !_isResetToDefault;

                if (SelectedPlaylist?.Items != null)
                {
                    RefreshView(SelectedPlaylist.Items);
                }

                Manager.SavePlaylists();
            }

            IsRenameDialogOpen = false;
            _itemBeingRenamed = null;
        }

        [RelayCommand]
        private void CancelRename()
        {
            IsRenameDialogOpen = false;
            _itemBeingRenamed = null;
        }

        [RelayCommand]
        private void ResetToDefault()
        {
            if (_itemBeingRenamed == null || !File.Exists(_itemBeingRenamed.FullPath)) return;

            try
            {
                using (var tagFile = TagLib.File.Create(new JLS.Services.SafeFileAbstraction(_itemBeingRenamed.FullPath)))
                {
                    EditTitle = tagFile.Tag.Title ?? Path.GetFileNameWithoutExtension(_itemBeingRenamed.FullPath);
                    EditArtist = tagFile.Tag.FirstPerformer ?? "";
                    EditAlbum = tagFile.Tag.Album ?? "";
                    EditYear = tagFile.Tag.Year > 0 ? tagFile.Tag.Year.ToString() : "";
                    EditTrackNumber = tagFile.Tag.Track > 0 ? tagFile.Tag.Track.ToString() : "";
                    EditGenre = tagFile.Tag.FirstGenre ?? "";
                }
            }
            catch
            {
                EditTitle = Path.GetFileNameWithoutExtension(_itemBeingRenamed.FullPath);
                EditArtist = "";
                EditAlbum = "";
                EditYear = "";
                EditTrackNumber = "";
                EditGenre = "";
            }

            _isResetToDefault = true;
        }

        public void MarkAsPlaying(string path)
        {
            foreach (var playlist in Manager.Playlists)
            {
                foreach (var item in playlist.Items)
                {
                    item.IsPlaying = (item.FullPath == path);
                }
            }
        }

        [RelayCommand]
        private void DeletePlaylist(Playlist? playlist)
        {
            if (playlist == null) return;

            var dialog = new JLS.Views.ConfirmDialog(
                $"Are you sure you want to delete the playlist '{playlist.Name}'?\nTracks will not be deleted from your disk.")
            {
                Owner = App.Current.MainWindow
            };

            if (dialog.ShowDialog() == true)
            {
                Manager.DeletePlaylist(playlist);
                if (SelectedPlaylist == playlist)
                {
                    SelectedPlaylist = null;
                }
            }
            DeleteCoverFile(playlist.CoverPath);
        }

        [RelayCommand]
        private void SaveChanges()
        {
            Manager.SavePlaylists();
        }

        public PlaylistsViewModel(PlaylistManager manager)
        {
            Manager = manager;
        }

        public event Action<string>? PlayRequested;

        [RelayCommand]
        private void TrackDoubleClick(FileSystemItem? track)
        {
            if (track != null)
            {
                PlayRequested?.Invoke(track.FullPath);
            }
        }

        private FileSystemItem? CreateTrackItem(string filename)
        {
            if (!File.Exists(filename)) return null;        

            var fileInfo = new FileInfo(filename);
            var newItem = new FileSystemItem
            {
                FullPath = filename,
                Name = fileInfo.Name,
                IsDirectory = false,
                Size = fileInfo.Length,
                Modified = fileInfo.LastWriteTime
            };

            try
            {
                using (var tagFile = TagLib.File.Create(new JLS.Services.SafeFileAbstraction(filename)))
                {
                    var d = tagFile.Properties.Duration;
                    newItem.Duration = d.TotalHours >= 1 
                        ? $"{(int)d.TotalHours:D2}:{d.Minutes:D2}:{d.Seconds:D2}" 
                        : $"{(int)d.TotalMinutes:D2}:{d.Seconds:D2}";

                    newItem.Artist = tagFile.Tag.FirstPerformer;
                    newItem.Album = tagFile.Tag.Album;
                    newItem.TrackNumber = (int)tagFile.Tag.Track;
                    newItem.Title = tagFile.Tag.Title;
                    newItem.Genre = tagFile.Tag.FirstGenre;
                    newItem.Year = tagFile.Tag.Year > 0 ? tagFile.Tag.Year.ToString() : "";
                    newItem.Bitrate = tagFile.Properties.AudioBitrate;
                    newItem.SampleRate = tagFile.Properties.AudioSampleRate;
                    newItem.BitsPerSample = tagFile.Properties.BitsPerSample;
                }
            }
            catch {         }

            return newItem;
        }

        [RelayCommand]
        private void AddTrack()
        {
            if (SelectedPlaylist == null) return;

            var openFileDialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Audio files (*.mp3;*.flac;*.wav;*.aif;*.aiff;*.m4a;*.ogg;*.wma;*.aac;*.alac;*.ape)|*.mp3;*.flac;*.wav;*.aif;*.aiff;*.m4a;*.ogg;*.wma;*.aac;*.alac;*.ape|All files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (string filename in openFileDialog.FileNames)
                {
                    var newItem = CreateTrackItem(filename);
                    if (newItem != null)
                    {
                        Manager.AddTrackToPlaylist(SelectedPlaylist, newItem);
                    }
                }
            }
        }

        public void AddFiles(string[] filePaths)
        {
            if (SelectedPlaylist == null) return;

            foreach (string filename in filePaths)
            {
                var ext = Path.GetExtension(filename).ToLower();
                if (ext != ".mp3" && ext != ".flac" && ext != ".wav" && ext != ".aif" && ext != ".aiff" &&
                    ext != ".m4a" && ext != ".ogg" && ext != ".wma" && ext != ".aac" && ext != ".alac" && ext != ".ape") continue;

                var newItem = CreateTrackItem(filename);
                if (newItem != null)
                {
                    Manager.AddTrackToPlaylist(SelectedPlaylist, newItem);
                }
            }
        }

        private string _currentSortColumn = string.Empty;
        private bool? _isSortAscending = null;

        public int SortTracks(string columnHeader)
        {
            if (SelectedPlaylist == null || SelectedPlaylist.Items.Count == 0) return 0;

            if (_currentSortColumn != columnHeader)
            {
                _currentSortColumn = columnHeader;
                _isSortAscending = true;
            }
            else
            {
                if (_isSortAscending == true) _isSortAscending = false;
                else if (_isSortAscending == false)
                {
                    _isSortAscending = null;
                    _currentSortColumn = string.Empty;
                }
            }

            ApplySort();
            if (_isSortAscending == null) return 0;
            return _isSortAscending == true ? 1 : 2;
        }

        private void ApplySort()
        {
            if (SelectedPlaylist == null) return;

            var items = SelectedPlaylist.Items.ToList();

            if (_isSortAscending == null)
            {
                items = items.OrderBy(f => f.Name).ToList();
            }
            else
            {
                bool asc = _isSortAscending.Value;
                items = _currentSortColumn switch
                {
                    "Title" => asc ? items.OrderBy(f => f.Title ?? f.Name).ToList() : items.OrderByDescending(f => f.Title ?? f.Name).ToList(),
                    "Artist" => asc ? items.OrderBy(f => f.Artist).ToList() : items.OrderByDescending(f => f.Artist).ToList(),
                    "Album" => asc ? items.OrderBy(f => f.Album).ToList() : items.OrderByDescending(f => f.Album).ToList(),
                    "Year" => asc ? items.OrderBy(f => f.Year).ToList() : items.OrderByDescending(f => f.Year).ToList(),
                    "Genre" => asc ? items.OrderBy(f => f.Genre).ToList() : items.OrderByDescending(f => f.Genre).ToList(),
                    "Duration" => asc ? items.OrderBy(f => f.Duration).ToList() : items.OrderByDescending(f => f.Duration).ToList(),
                    "Format" => asc ? items.OrderBy(f => f.Extension).ToList() : items.OrderByDescending(f => f.Extension).ToList(),
                    "Size" => asc ? items.OrderBy(f => f.Size).ToList() : items.OrderByDescending(f => f.Size).ToList(),
                    "Bitrate" => asc ? items.OrderBy(f => f.Bitrate ?? 0).ToList() : items.OrderByDescending(f => f.Bitrate ?? 0).ToList(),
                    "SampleRate" => asc ? items.OrderBy(f => f.SampleRate ?? 0).ToList() : items.OrderByDescending(f => f.SampleRate ?? 0).ToList(),
                    "BitsPerSample" => asc ? items.OrderBy(f => f.BitsPerSample ?? 0).ToList() : items.OrderByDescending(f => f.BitsPerSample ?? 0).ToList(),
                    "Modified" => asc ? items.OrderBy(f => f.Modified).ToList() : items.OrderByDescending(f => f.Modified).ToList(),
                    "#" => asc ? items.OrderBy(f => f.TrackNumber ?? 0).ToList() : items.OrderByDescending(f => f.TrackNumber ?? 0).ToList(),
                    _ => asc ? items.OrderBy(f => f.Name).ToList() : items.OrderByDescending(f => f.Name).ToList()
                };
            }

            SelectedPlaylist.Items.Clear();
            foreach (var item in items)
            {
                SelectedPlaylist.Items.Add(item);
            }
            Manager.SavePlaylists();
        }

        [RelayCommand]
        private void RemoveTrack(FileSystemItem? track)
        {
            if (SelectedPlaylist != null && track != null)
            {
                Manager.RemoveTrackFromPlaylist(SelectedPlaylist, track);
            }
        }

        [RelayCommand]
        private void OpenInExplorer(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                JLS.Services.ExplorerHelper.OpenFolderAndSelectFile(path);
            }
        }

        [RelayCommand]
        private void CreatePlaylist()
        {
            string baseName = "New Playlist";
            string finalName = baseName;

            if (Manager.Playlists != null)
            {
                var usedNumbers = new System.Collections.Generic.HashSet<int>();

                foreach (var p in Manager.Playlists)
                {
                    if (p.Name == baseName)
                    {
                        usedNumbers.Add(0);        
                    }
                    else if (p.Name != null && p.Name.StartsWith(baseName + " "))
                    {
                        string numberPart = p.Name.Substring(baseName.Length + 1);
                        if (int.TryParse(numberPart, out int num))
                        {
                            usedNumbers.Add(num);
                        }
                    }
                }

                int freeNumber = 0;
                while (usedNumbers.Contains(freeNumber))
                {
                    freeNumber++;
                }

                if (freeNumber > 0)
                {
                    finalName = $"{baseName} {freeNumber}";
                }
            }

            var newPlaylist = Manager.CreatePlaylist(finalName);
            var rnd = new Random();

            double hue = rnd.Next(0, 361);
            double sat = 0.44 + (rnd.NextDouble() * 0.4);
            double light = 0.24 + (rnd.NextDouble() * 0.4);

            newPlaylist.Hue = hue;
            newPlaylist.Saturation = sat;
            newPlaylist.Lightness = light;

            newPlaylist.SavedUserHue = hue;
            newPlaylist.SavedUserSaturation = sat;
            newPlaylist.SavedUserLightness = light;

            newPlaylist.UseCoverColor = false;

            Manager.SavePlaylists();
            SelectedPlaylist = newPlaylist;
        }

        [RelayCommand]
        private void TogglePlaylist(object[]? parameters)
        {
            if (parameters != null && parameters.Length == 2 &&
                parameters[0] is Playlist targetPlaylist &&
                parameters[1] is FileSystemItem track)
            {
                if (targetPlaylist.TrackPaths.Contains(track.FullPath))
                {
                    Manager.RemoveTrackFromPlaylist(targetPlaylist, track);

                    if (SelectedPlaylist == targetPlaylist)
                    {
                        SelectedPlaylist.Items.Remove(track);
                    }
                }
                else
                {
                    Manager.AddTrackToPlaylist(targetPlaylist, track);
                }
            }
        }

        private static void RefreshView(IEnumerable items)
        {
            var view = CollectionViewSource.GetDefaultView(items);
            if (view == null) return;

            if (Application.Current.Dispatcher.CheckAccess())
            {
                view.Refresh();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(view.Refresh);
            }
        }

        public void BulkAddToPlaylist(Playlist targetPlaylist, System.Collections.Generic.List<FileSystemItem> tracks)
        {
            foreach (var track in tracks)
            {
                if (!targetPlaylist.TrackPaths.Contains(track.FullPath))
                {
                    Manager.AddTrackToPlaylist(targetPlaylist, track);
                }
            }
        }

        public void BulkRemoveFromPlaylist(System.Collections.Generic.List<FileSystemItem> tracks)
        {
            if (SelectedPlaylist == null) return;

            foreach (var track in tracks)
            {
                Manager.RemoveTrackFromPlaylist(SelectedPlaylist, track);
            }
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task SelectCover(Playlist? playlist)
        {
            if (playlist == null) return;

            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image files (*.jpg, *.jpeg, *.png, *.bmp)|*.jpg;*.jpeg;*.png;*.bmp",
                Title = "Select Playlist Cover"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string fileName = Guid.NewGuid().ToString() + ".png";
                    string destPath = Path.Combine(JLS.Services.AppPaths.CoversFolder, fileName);

                    var bmp = new BitmapImage();
                    using (var stream = new FileStream(openFileDialog.FileName, FileMode.Open, FileAccess.Read))
                    {
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = stream;
                        bmp.DecodePixelWidth = 200;       
                        bmp.EndInit();
                        bmp.Freeze();
                    }

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bmp));
                    using (var fileStream = new FileStream(destPath, FileMode.Create))
                    {
                        encoder.Save(fileStream);
                    }

                    DeleteCoverFile(playlist.CoverPath);

                    playlist.CoverPath = destPath;

                    if (playlist.UseCoverColor)
                    {
                        await ApplyCoverColor(playlist);
                    }
                    else
                    {
                        Manager.SavePlaylists();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save cover: {ex.Message}");
                }
            }
        }

        private void DeleteCoverFile(string? path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try { File.Delete(path); }
                catch {          }
            }
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task AskRemoveCover(Playlist? playlist)
        {
            if (playlist == null || string.IsNullOrEmpty(playlist.CoverPath)) return;

            var dialog = new JLS.Views.ConfirmDialog(
                "Remove Cover",
                "Are you sure you want to remove the cover from this playlist?",
                "Remove")
            {
                Owner = App.Current.MainWindow
            };

            if (dialog.ShowDialog() == true)
            {
                string pathToTrash = playlist.CoverPath;   

                playlist.CoverPath = null;     

                if (playlist.UseCoverColor)
                {
                    await playlist.AnimateColorsTo(playlist.SavedUserHue, playlist.SavedUserSaturation, playlist.SavedUserLightness);
                }

                Manager.SavePlaylists();

                DeleteCoverFile(pathToTrash);
            }
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task ApplyCoverColor(Playlist? playlist)
        {
            if (playlist == null) return;

            if (playlist.UseCoverColor)
            {
                if (string.IsNullOrEmpty(playlist.CoverPath) || !File.Exists(playlist.CoverPath)) return;

                try
                {
                    var bmp = new BitmapImage();
                    using (var stream = new FileStream(playlist.CoverPath, FileMode.Open, FileAccess.Read))
                    {
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = stream;
                        bmp.DecodePixelWidth = 100;
                        bmp.EndInit();
                        bmp.Freeze();
                    }

                    var palette = JLS.Services.ColorExtractor.GetPaletteFromImage(bmp);
                    var color = palette.Primary;

                    float r = color.R / 255f;
                    float g = color.G / 255f;
                    float b = color.B / 255f;
                    float max = Math.Max(r, Math.Max(g, b));
                    float min = Math.Min(r, Math.Min(g, b));

                    float h = 0, s = 0, l = (max + min) / 2f;

                    if (max != min)
                    {
                        float d = max - min;
                        s = l > 0.5f ? d / (2f - max - min) : d / (max + min);

                        if (max == r) h = (g - b) / d + (g < b ? 6f : 0f);
                        else if (max == g) h = (b - r) / d + 2f;
                        else if (max == b) h = (r - g) / d + 4f;
                        h *= 60f;
                    }

                    await playlist.AnimateColorsTo(h, s, l);
                }
                catch {       }
            }
            else
            {
                await playlist.AnimateColorsTo(playlist.SavedUserHue, playlist.SavedUserSaturation, playlist.SavedUserLightness);
            }

            Manager.SavePlaylists();
        }

    }
}