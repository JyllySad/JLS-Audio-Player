using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JLS.Models;
using JLS.Services;

namespace JLS.ViewModels
{
    public partial class SavedAlbumsViewModel : ObservableObject
    {
        private readonly SavedAlbumsManager _manager;

        public Action<FileSystemItem, ObservableCollection<FileSystemItem>>? PlayRequested;

        [ObservableProperty]
        private SavedAlbum? _selectedAlbum;

        public ObservableCollection<SavedAlbum> Albums => _manager.SavedAlbums;

        [ObservableProperty]
        private bool _isRenameDialogOpen;

        [ObservableProperty]
        private string _editTitle = string.Empty;

        [ObservableProperty]
        private string _editArtist = string.Empty;

        [ObservableProperty]
        private string _editYear = string.Empty;

        private SavedAlbum? _albumBeingRenamed;
        public string CurrentlyPlayingPath { get; private set; } = string.Empty;

        [ObservableProperty]
        private System.Windows.Media.ImageSource? _albumCover;

        [ObservableProperty]
        private string _albumDurationInfo = string.Empty;

        [ObservableProperty]
        private string _albumFormat = string.Empty;

        [ObservableProperty]
        private bool _isAlbumFormatVisible;

        [ObservableProperty]
        private string _albumSampleRate = string.Empty;

        [ObservableProperty]
        private bool _isAlbumSampleRateVisible;

        [ObservableProperty]
        private string _albumBitDepth = string.Empty;

        [ObservableProperty]
        private bool _isAlbumBitDepthVisible;

        [ObservableProperty]
        private System.Windows.Media.Color _dominantColor = System.Windows.Media.Color.FromRgb(30, 30, 30);

        [ObservableProperty]
        private System.Windows.Media.Color _lightColor = System.Windows.Media.Color.FromRgb(100, 100, 120);

        [ObservableProperty]
        private System.Windows.Media.ImageSource? _fullAlbumCover;

        [ObservableProperty]
        private bool _isFullCoverVisible;

        [ObservableProperty]
        private bool _showAlbumCovers;

        private string? _cachedCoverPath;
        private byte[]? _cachedCoverBytes;

        [ObservableProperty]
        private string _searchText = string.Empty;

        private System.Windows.Threading.DispatcherTimer? _searchTimer;
        private System.Threading.CancellationTokenSource? _coverCancellationTokenSource;

        public System.ComponentModel.ICollectionView AlbumsView { get; }

        partial void OnSearchTextChanged(string value)
        {
            if (_searchTimer == null)
            {
                _searchTimer = new System.Windows.Threading.DispatcherTimer();
                _searchTimer.Interval = TimeSpan.FromMilliseconds(150);
                _searchTimer.Tick += (s, e) =>
                {
                    _searchTimer.Stop();
                    AlbumsView.Refresh();
                };
            }

            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private bool FilterAlbums(object obj)
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            if (obj is SavedAlbum album)
            {
                return (album.Title != null && album.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                       (album.Artist != null && album.Artist.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                       (album.Year != null && album.Year.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            }
            return false;
        }

        [ObservableProperty]
        private string _searchTrackText = string.Empty;

        partial void OnSearchTrackTextChanged(string value)
        {
            if (SelectedAlbum == null) return;

            var view = CollectionViewSource.GetDefaultView(SelectedAlbum.Items);
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

        partial void OnSelectedAlbumChanged(SavedAlbum? oldValue, SavedAlbum? newValue)
        {
            if (oldValue != null)
            {
                var oldView = CollectionViewSource.GetDefaultView(oldValue.Items);
                if (oldView != null) oldView.Filter = null;
            }

            SearchTrackText = string.Empty;

            AlbumFormat = string.Empty;
            IsAlbumFormatVisible = false;
            AlbumSampleRate = string.Empty;
            IsAlbumSampleRateVisible = false;
            AlbumBitDepth = string.Empty;
            IsAlbumBitDepthVisible = false;

            if (newValue == null)
            {
                AlbumCover = null;
                AlbumDurationInfo = string.Empty;
                DominantColor = System.Windows.Media.Color.FromRgb(30, 30, 30);
                return;
            }

            try
            {
                if (!string.IsNullOrEmpty(newValue.PrimaryColorHex))
                {
                    DominantColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(newValue.PrimaryColorHex);
                }
                else
                {
                    DominantColor = System.Windows.Media.Color.FromRgb(30, 30, 30);
                }

                if (!string.IsNullOrEmpty(newValue.LightColorHex))
                {
                    LightColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(newValue.LightColorHex);
                }
                else
                {
                    LightColor = System.Windows.Media.Color.FromRgb(100, 100, 120);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Color conversion error: {ex.Message}");
                DominantColor = System.Windows.Media.Color.FromRgb(30, 30, 30);
                LightColor = System.Windows.Media.Color.FromRgb(100, 100, 120);
            }

            int trackCount = newValue.Items.Count;
            TimeSpan totalTime = TimeSpan.Zero;

            string[] formats = { @"mm\:ss", @"m\:ss", @"h\:mm\:ss", @"hh\:mm\:ss" };
            foreach (var item in newValue.Items)
            {
                if (!string.IsNullOrEmpty(item.Duration) && TimeSpan.TryParseExact(item.Duration, formats, null, out var ts))
                {
                    totalTime += ts;
                }
            }

            string timeString = totalTime.TotalHours >= 1
                ? $"{(int)totalTime.TotalHours:D2}:{totalTime.Minutes:D2}:{totalTime.Seconds:D2}"
                : $"{totalTime.Minutes:D2}:{totalTime.Seconds:D2}";

            AlbumDurationInfo = $"{trackCount} tracks • {timeString}";

            if (newValue.Items.Count > 0)
            {
                var tracksToCheck = newValue.Items.Take(50).ToList();

                var formatsList = tracksToCheck.Select(f => f.Extension?.Trim().ToLowerInvariant()).Distinct().ToList();
                var firstFormat = formatsList.FirstOrDefault();    

                if (formatsList.Count == 1 && !string.IsNullOrWhiteSpace(firstFormat))
                {
                    AlbumFormat = firstFormat.Replace(".", "").ToUpperInvariant();
                    IsAlbumFormatVisible = true;
                }

                var rates = tracksToCheck.Select(f => f.SampleRate).Distinct().ToList();
                var firstRate = rates.FirstOrDefault();          

                if (rates.Count == 1 && firstRate.HasValue && firstRate.Value > 0)
                {
                    AlbumSampleRate = $"{firstRate.Value / 1000.0:0.#} kHz";
                    IsAlbumSampleRateVisible = true;
                }

                var depths = tracksToCheck.Select(f => f.BitsPerSample).Distinct().ToList();
                var firstDepth = depths.FirstOrDefault();          

                if (depths.Count == 1 && firstDepth.HasValue && firstDepth.Value > 0)
                {
                    bool isLossy = formatsList.Count == 1 && (formatsList[0] == ".mp3" || formatsList[0] == ".aac" || formatsList[0] == ".ogg" || formatsList[0] == ".wma");
                    if (!isLossy)
                    {
                        AlbumBitDepth = $"{firstDepth.Value} Bit";
                        IsAlbumBitDepthVisible = true;
                    }
                }
            }

            _coverCancellationTokenSource?.Cancel();
            _coverCancellationTokenSource?.Dispose();
            _coverCancellationTokenSource = new System.Threading.CancellationTokenSource();

            _ = LoadAlbumCoverAsync(newValue, _coverCancellationTokenSource.Token);

            MarkAsPlaying(CurrentlyPlayingPath);
        }

        private async System.Threading.Tasks.Task LoadAlbumCoverAsync(SavedAlbum album, System.Threading.CancellationToken token)
        {
            AlbumCover = null;
            FullAlbumCover = null;
            _cachedCoverPath = null;
            _cachedCoverBytes = null;

            var firstItem = album.Items.FirstOrDefault();

            if (firstItem == null || string.IsNullOrEmpty(firstItem.FullPath)) return;

            string firstFilePath = firstItem.FullPath;
            string directory = System.IO.Path.GetDirectoryName(firstFilePath) ?? string.Empty;
            var capturedAlbum = album;

            try
            {
                var (bitmap, cachedPath, cachedBytes) = await System.Threading.Tasks.Task.Run(() =>
                {
                    System.Windows.Media.Imaging.BitmapImage? bgBitmap = null;
                    string? bgPath = null;
                    byte[]? bgBytes = null;

                    string[] possibleNames = { "cover.jpg", "cover.png", "folder.jpg", "folder.png", "front.jpg", "front.png" };
                    foreach (var name in possibleNames)
                    {
                        if (token.IsCancellationRequested) return (null, null, null);

                        string path = System.IO.Path.Combine(directory, name);
                        if (System.IO.File.Exists(path))
                        {
                            bgPath = path;
                            bgBitmap = new System.Windows.Media.Imaging.BitmapImage();
                            bgBitmap.BeginInit();
                            bgBitmap.UriSource = new Uri(path);
                            bgBitmap.DecodePixelWidth = 400;
                            bgBitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            bgBitmap.EndInit();
                            bgBitmap.Freeze();
                            break;
                        }
                    }

                    if (bgBitmap == null && !token.IsCancellationRequested && System.IO.File.Exists(firstFilePath))
                    {
                        using (var file = TagLib.File.Create(new JLS.Services.SafeFileAbstraction(firstFilePath)))
                        {
                            if (file.Tag.Pictures.Length > 0)
                            {
                                bgBytes = file.Tag.Pictures[0].Data.Data;
                                using (var ms = new System.IO.MemoryStream(bgBytes))
                                {
                                    bgBitmap = new System.Windows.Media.Imaging.BitmapImage();
                                    bgBitmap.BeginInit();
                                    bgBitmap.StreamSource = ms;
                                    bgBitmap.DecodePixelWidth = 400;
                                    bgBitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                                    bgBitmap.EndInit();
                                    bgBitmap.Freeze();
                                }
                            }
                        }
                    }

                    return (bgBitmap, bgPath, bgBytes);
                }, token);

                if (!token.IsCancellationRequested && SelectedAlbum == capturedAlbum)
                {
                    _cachedCoverPath = cachedPath;
                    _cachedCoverBytes = cachedBytes;
                    AlbumCover = bitmap;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadAlbumCoverAsync error: {ex.Message}");
            }
        }

        public void MarkAsPlaying(string currentTrackPath)
        {
            CurrentlyPlayingPath = currentTrackPath;
            if (SelectedAlbum != null)
            {
                foreach (var item in SelectedAlbum.Items)
                {
                    item.IsPlaying = (item.FullPath == currentTrackPath);
                }
            }
        }

        public void BulkAddToPlaylist(Playlist playlist, System.Collections.Generic.List<FileSystemItem> tracks)
        {
            foreach (var track in tracks)
            {
                if (!playlist.TrackPaths.Contains(track.FullPath))
                {
                    PlaylistManager.AddTrackToPlaylist(playlist, track);
                }
            }
        }

        public void UpdateItemMetadata(string filePath)
        {
            if (_manager?.SavedAlbums == null) return;

            SavedAlbum? affectedAlbum = null;

            foreach (var album in _manager.SavedAlbums)
            {
                var item = album.Items.FirstOrDefault(x => x.FullPath == filePath);
                if (item != null)
                {
                    item.ReloadMetadata();
                    affectedAlbum = album;

                    if (!string.IsNullOrEmpty(item.Album)) album.Title = item.Album;
                    if (!string.IsNullOrEmpty(item.Artist)) album.Artist = item.Artist;
                    if (!string.IsNullOrEmpty(item.Year)) album.Year = item.Year;

                    break;          
                }
            }

            _manager.SaveAlbums();

            if (SelectedAlbum != null && affectedAlbum == SelectedAlbum)
            {
                System.Windows.Data.CollectionViewSource.GetDefaultView(SelectedAlbum.Items)?.Refresh();
                OnSelectedAlbumChanged(SelectedAlbum, SelectedAlbum);
            }
        }

        public PlaylistManager PlaylistManager { get; }

        public SavedAlbumsViewModel(SavedAlbumsManager manager, PlaylistManager playlistManager)
        {
            _manager = manager;
            PlaylistManager = playlistManager;

            AlbumsView = System.Windows.Data.CollectionViewSource.GetDefaultView(_manager.SavedAlbums);
            AlbumsView.Filter = FilterAlbums;
        }

        public void SaveAlbumsOrder()
        {
            _manager.SaveAlbums();
        }

        [RelayCommand]
        private void DeleteAlbum(SavedAlbum? album)
        {
            if (album == null) return;

            var dialog = new JLS.Views.ConfirmDialog(
                "Delete Album",
                $"Are you sure you want to remove \"{album.Title}\" from your saved albums?",
                "Delete"
            );

            dialog.Owner = System.Windows.Application.Current.MainWindow;

            if (dialog.ShowDialog() == true)
            {
                _manager.RemoveAlbum(album);
                if (SelectedAlbum == album)
                {
                    SelectedAlbum = null;
                }
            }
        }

        [RelayCommand]
        private void RenameAlbum(SavedAlbum? album)
        {
            if (album == null) return;

            _albumBeingRenamed = album;
            EditTitle = album.Title;
            EditArtist = album.Artist;
            EditYear = album.Year;

            IsRenameDialogOpen = true;
        }

        [RelayCommand]
        private void ConfirmRename()
        {
            if (_albumBeingRenamed != null)
            {
                _albumBeingRenamed.Title = EditTitle;
                _albumBeingRenamed.Artist = EditArtist;
                _albumBeingRenamed.Year = EditYear;

                _manager.SaveAlbums();
            }

            IsRenameDialogOpen = false;
            _albumBeingRenamed = null;
        }

        [RelayCommand]
        private void CancelRename()
        {
            IsRenameDialogOpen = false;
            _albumBeingRenamed = null;
        }

        [RelayCommand]
        private void PlayTrack(FileSystemItem? track)
        {
            if (track != null && SelectedAlbum != null)
            {
                PlayRequested?.Invoke(track, SelectedAlbum.Items);
            }
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task ShowFullCoverAsync()
        {
            if (AlbumCover == null) return;

            IsFullCoverVisible = true;

            if (FullAlbumCover == null)
            {
                var capturedAlbum = SelectedAlbum;

                var fullBmp = await System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        byte[]? data = _cachedCoverBytes;

                        if (data == null && _cachedCoverPath != null)
                        {
                            data = System.IO.File.ReadAllBytes(_cachedCoverPath);
                        }

                        if (data != null)
                        {
                            var bmp = new System.Windows.Media.Imaging.BitmapImage();
                            using (var ms = new System.IO.MemoryStream(data))
                            {
                                bmp.BeginInit();
                                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                                bmp.StreamSource = ms;
                                bmp.EndInit();
                                bmp.Freeze();
                            }
                            return bmp;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ShowFullCover error: {ex.Message}");
                    }
                    return null;
                });

                if (SelectedAlbum == capturedAlbum)
                {
                    FullAlbumCover = fullBmp;
                }
            }
        }

        [RelayCommand]
        private void HideFullCover()
        {
            IsFullCoverVisible = false;
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
        private void TogglePlaylist(object[]? parameters)
        {
            if (parameters != null && parameters.Length == 2 &&
                parameters[0] is Playlist playlist &&
                parameters[1] is FileSystemItem track)
            {
                if (playlist.TrackPaths.Contains(track.FullPath))
                {
                    PlaylistManager.RemoveTrackFromPlaylist(playlist, track);
                }
                else
                {
                    PlaylistManager.AddTrackToPlaylist(playlist, track);
                }
            }
        }
    }
}