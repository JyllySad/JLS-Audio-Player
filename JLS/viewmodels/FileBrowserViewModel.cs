using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JLS.Models;
using JLS.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;


namespace JLS.ViewModels
{
    public partial class FileBrowserViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<TreeViewNode> _driveNodes = new();

        [ObservableProperty]
        private TreeViewNode? _selectedNode;

        [ObservableProperty]
        private string _currentFolderPath = string.Empty;

        

        [ObservableProperty]
        private ObservableCollection<FileSystemItem> _files = new();

        [ObservableProperty]
        private FileSystemItem? _selectedFile;

        [ObservableProperty]
        private string _currentlyPlayingPath = string.Empty;

        [ObservableProperty]
        private string _folderStats = "0 tracks  •  00:00  •  0 MB";

        [ObservableProperty]
        private bool _isAlbumViewEnabled;

        private bool _isRestoringTree = false;

        private readonly System.Collections.Generic.Dictionary<string, FileSystemWatcher> _activeWatchers = new(StringComparer.OrdinalIgnoreCase);
        private System.Timers.Timer _debounceTimer;
        private string _currentSortColumn = string.Empty;
        private bool? _isSortAscending = null;
        private Task? _drivesLoadingTask;

        private SavedAlbumsManager? _savedAlbumsManager;

        [ObservableProperty]
        private bool _isCurrentAlbumSaved;

        [ObservableProperty]
        private string _searchFolderText = string.Empty;

        partial void OnSearchFolderTextChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                SetVisibilityRecursive(DriveNodes, true);
            else
                foreach (var node in DriveNodes) FilterNodeRecursive(node, value);
        }

        [ObservableProperty]
        private string _searchFileText = string.Empty;

        partial void OnSearchFileTextChanged(string value)
        {
            ApplySortAndFilter();
        }

        private System.Collections.Generic.List<FileSystemItem> _rawFiles = new();

        [ObservableProperty]
        private ImageSource? _albumCover;

        [ObservableProperty]
        private string _albumTitle = string.Empty;

        [ObservableProperty]
        private string _albumArtist = string.Empty;

        [ObservableProperty]
        private string _albumYear = string.Empty;

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
        private Color _dominantColor = Color.FromRgb(30, 30, 30);

        [ObservableProperty]
        private Color _lightColor = Color.FromRgb(100, 100, 120);


        [ObservableProperty]
        private ImageSource? _fullAlbumCover;

        [ObservableProperty]
        private bool _isHeartVisible = true;

        [ObservableProperty]
        private bool _isShowingFoldersOnly;

        private string? _cachedCoverPath;
        private byte[]? _cachedCoverBytes;

        [ObservableProperty]
        private bool _isFullCoverVisible;

        [RelayCommand]
        private async System.Threading.Tasks.Task ShowFullCoverAsync()
        {
            if (AlbumCover == null) return;

            IsFullCoverVisible = true;

            if (FullAlbumCover == null)
            {
                FullAlbumCover = await System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        byte[]? data = _cachedCoverBytes;

                        if (data == null && _cachedCoverPath != null)
                        {
                            data = File.ReadAllBytes(_cachedCoverPath);
                        }

                        if (data != null)
                        {
                            var fullBmp = new BitmapImage();
                            using (var ms = new MemoryStream(data))
                            {
                                fullBmp.BeginInit();
                                fullBmp.CacheOption = BitmapCacheOption.OnLoad;
                                fullBmp.StreamSource = ms;
                                fullBmp.EndInit();
                                fullBmp.Freeze();      
                            }
                            return fullBmp;
                        }
                    }
                    catch { }
                    return null;
                });
            }
        }

        [RelayCommand]
        private void HideFullCover()
        {
            IsFullCoverVisible = false;
        }

        [RelayCommand]
        private void ToggleSaveAlbum()
        {
            if (_savedAlbumsManager == null || Files.Count == 0) return;

            var firstTrack = Files.FirstOrDefault(f => !f.IsDirectory);
            if (firstTrack == null) return;

            var existing = _savedAlbumsManager.SavedAlbums.FirstOrDefault(a =>
                a.Items.Any(item => item.FullPath == firstTrack.FullPath));

            if (existing != null)
            {
                _savedAlbumsManager.RemoveAlbum(existing);
            }
            else
            {
                var paths = Files.Where(f => !f.IsDirectory).Select(f => f.FullPath).ToList();

                string titleToSave = string.IsNullOrEmpty(AlbumTitle) ? "Unknown Album" : AlbumTitle;
                string artistToSave = string.IsNullOrEmpty(AlbumArtist) ? "Unknown Artist" : AlbumArtist;

                _savedAlbumsManager.AddAlbum(
                    titleToSave,
                    artistToSave,
                    AlbumYear,
                    string.Empty,      
                    DominantColor,
                    LightColor,
                    paths
                );
            }
            UpdateHeartState();
        }

        private void UpdateHeartState()
        {
            if (_savedAlbumsManager == null || Files.Count == 0)
            {
                IsCurrentAlbumSaved = false;
                return;
            }

            var firstTrack = Files.FirstOrDefault(f => !f.IsDirectory);
            if (firstTrack == null)
            {
                IsCurrentAlbumSaved = false;
                return;
            }

            IsCurrentAlbumSaved = _savedAlbumsManager.SavedAlbums.Any(a =>
                a.Items.Any(item => item.FullPath == firstTrack.FullPath));
        }

        public void InitializeSavedManager(SavedAlbumsManager manager)
        {
            _savedAlbumsManager = manager;
            _savedAlbumsManager.SavedAlbums.CollectionChanged += (s, e) => UpdateHeartState();
        }


        public event Action<string>? PlayRequested;

        public void MarkAsPlaying(string path)
        {
            CurrentlyPlayingPath = path;   
            foreach (var file in Files)
            {
                file.IsPlaying = (file.FullPath == path);
            }
        }


        public void PlayNext()
        {
            if (Files.Count == 0 || string.IsNullOrEmpty(CurrentlyPlayingPath)) return;

            var current = Files.FirstOrDefault(f => f.FullPath == CurrentlyPlayingPath);
            if (current == null) return;

            int index = Files.IndexOf(current);
            for (int i = index + 1; i < Files.Count; i++)
            {
                if (!Files[i].IsDirectory)
                {
                    PlayRequested?.Invoke(Files[i].FullPath);
                    return;
                }
            }
        }

        public void PlayPrev()
        {
            if (Files.Count == 0 || string.IsNullOrEmpty(CurrentlyPlayingPath)) return;

            var current = Files.FirstOrDefault(f => f.FullPath == CurrentlyPlayingPath);
            if (current == null) return;

            int index = Files.IndexOf(current);
            for (int i = index - 1; i >= 0; i--)
            {
                if (!Files[i].IsDirectory)
                {
                    PlayRequested?.Invoke(Files[i].FullPath);
                    return;
                }
            }
        }

        public int SortFiles(string columnHeader)
        {
            if (Files == null || Files.Count == 0) return 0;

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

            ApplySortAndFilter();

            if (_isSortAscending == null) return 0;
            return _isSortAscending == true ? 1 : 2;
        }

        private bool FilterNodeRecursive(TreeViewNode node, string query)
        {
            bool matches = node.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
            bool hasVisibleChild = false;

            if (node.Name == "Loading...") return false;

            foreach (var child in node.Children)
                if (FilterNodeRecursive(child, query))
                    hasVisibleChild = true;

            node.IsVisible = matches || hasVisibleChild;

            if (!string.IsNullOrWhiteSpace(query) && hasVisibleChild)
                node.IsExpanded = true;       

            return node.IsVisible;
        }

        private void SetVisibilityRecursive(System.Collections.Generic.IEnumerable<TreeViewNode> nodes, bool isVisible)
        {
            foreach (var node in nodes)
            {
                node.IsVisible = isVisible;
                SetVisibilityRecursive(node.Children, isVisible);
            }
        }

        public class ExplorerStringComparer : System.Collections.Generic.IComparer<string>
        {
            [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            private static extern int StrCmpLogicalW(string psz1, string psz2);

            public int Compare(string? x, string? y)
            {
                if (x == null && y == null) return 0;
                if (x == null) return -1;
                if (y == null) return 1;
                return StrCmpLogicalW(x, y);
            }
        }

        private void ApplySortAndFilter()
        {
            var filtered = _rawFiles.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchFileText))
            {
                filtered = filtered.Where(f =>
                    (f.Name != null && f.Name.Contains(SearchFileText, StringComparison.OrdinalIgnoreCase)) ||
                    (f.Artist != null && f.Artist.Contains(SearchFileText, StringComparison.OrdinalIgnoreCase)) ||
                    (f.Title != null && f.Title.Contains(SearchFileText, StringComparison.OrdinalIgnoreCase)));
            }

            if (_isSortAscending == null)
            {
                filtered = filtered.OrderBy(f => f.Name);
            }
            else
            {
                bool asc = _isSortAscending.Value;
                filtered = _currentSortColumn switch
                {
                    "Title" => asc ? filtered.OrderBy(f => f.Title ?? f.Name) : filtered.OrderByDescending(f => f.Title ?? f.Name),
                    "Artist" => asc ? filtered.OrderBy(f => f.Artist) : filtered.OrderByDescending(f => f.Artist),
                    "Album" => asc ? filtered.OrderBy(f => f.Album) : filtered.OrderByDescending(f => f.Album),
                    "Year" => asc ? filtered.OrderBy(f => f.Year) : filtered.OrderByDescending(f => f.Year),
                    "Genre" => asc ? filtered.OrderBy(f => f.Genre) : filtered.OrderByDescending(f => f.Genre),
                    "Size" => asc ? filtered.OrderBy(f => f.Size) : filtered.OrderByDescending(f => f.Size),
                    "Modified" => asc ? filtered.OrderBy(f => f.Modified) : filtered.OrderByDescending(f => f.Modified),
                    "Format" => asc ? filtered.OrderBy(f => f.Extension) : filtered.OrderByDescending(f => f.Extension),
                    "Bitrate" => asc ? filtered.OrderBy(f => f.Bitrate ?? 0) : filtered.OrderByDescending(f => f.Bitrate ?? 0),
                    "Rate" => asc ? filtered.OrderBy(f => f.SampleRate ?? 0) : filtered.OrderByDescending(f => f.SampleRate ?? 0),
                    "Depth" => asc ? filtered.OrderBy(f => f.BitsPerSample ?? 0) : filtered.OrderByDescending(f => f.BitsPerSample ?? 0),
                    "Duration" => asc ? filtered.OrderBy(f => f.Duration) : filtered.OrderByDescending(f => f.Duration),
                    "#" => asc ? filtered.OrderBy(f => f.TrackNumber ?? 0) : filtered.OrderByDescending(f => f.TrackNumber ?? 0),
                    _ => asc ? filtered.OrderBy(f => f.Name) : filtered.OrderByDescending(f => f.Name)
                };
            }

            var finalResult = filtered.GroupBy(f => f.FullPath).Select(g => g.First()).ToList();

            var currentFilesMap = Files.ToDictionary(f => f.FullPath);
            var finalResultPaths = finalResult.Select(f => f.FullPath).ToHashSet();

            var toRemove = Files.Where(f => !finalResultPaths.Contains(f.FullPath)).ToList();
            foreach (var r in toRemove)
            {
                Files.Remove(r);
                currentFilesMap.Remove(r.FullPath);     
            }

            foreach (var fr in finalResult)
            {
                if (currentFilesMap.TryGetValue(fr.FullPath, out var existing))
                {
                    existing.Title = fr.Title;
                    existing.Artist = fr.Artist;
                    existing.Album = fr.Album;
                    existing.Genre = fr.Genre;
                    existing.Year = fr.Year;
                    existing.TrackNumber = fr.TrackNumber;
                    existing.Bitrate = fr.Bitrate;
                    existing.SampleRate = fr.SampleRate;
                    existing.BitsPerSample = fr.BitsPerSample;
                    existing.Duration = fr.Duration;
                    existing.Name = fr.Name;
                    existing.Size = fr.Size;
                    existing.Modified = fr.Modified;
                }
                else
                {
                    Files.Add(fr);
                    currentFilesMap[fr.FullPath] = fr;    
                }
            }

            for (int i = 0; i < finalResult.Count; i++)
            {
                if (currentFilesMap.TryGetValue(finalResult[i].FullPath, out var existingFile))
                {
                    int currentIndex = Files.IndexOf(existingFile);

                    if (currentIndex != i && currentIndex >= 0 && i < Files.Count)
                    {
                        Files.Move(currentIndex, i);
                    }
                }
            }

            foreach (var file in Files)
                file.IsPlaying = (file.FullPath == CurrentlyPlayingPath);

            IsShowingFoldersOnly = Files.Count > 0 && Files.All(f => f.IsDirectory);

            CalculateFolderStats();

            UpdateAlbumInfo(CurrentFolderPath);
        }

        public void UpdateItemMetadata(string filePath)
        {
            var rawItem = _rawFiles.FirstOrDefault(x => x.FullPath == filePath);
            rawItem?.ReloadMetadata();

            var displayedItem = Files.FirstOrDefault(x => x.FullPath == filePath);
            displayedItem?.ReloadMetadata();

            if (IsAlbumViewEnabled)
            {
                UpdateAlbumInfo(CurrentFolderPath);
            }
        }

        public PlaylistManager PlaylistManager { get; }

        public FileBrowserViewModel(PlaylistManager playlistManager)
        {
            PlaylistManager = playlistManager;

            _drivesLoadingTask = LoadDrivesAsync();

            _debounceTimer = new System.Timers.Timer(1000);
            _debounceTimer.AutoReset = false;
            _debounceTimer.Elapsed += (s, e) => {
                App.Current.Dispatcher.BeginInvoke(new Action(() => Refresh()));
            };
        }

        private bool _isStateRestored = false;

        public async System.Threading.Tasks.Task RestoreTreeState()
        {
            if (_drivesLoadingTask != null)
            {
                await _drivesLoadingTask;
            }

            if (_isStateRestored) return;
            _isStateRestored = true;

            _isRestoringTree = true;

            if (JLS.Properties.Settings.Default.BrowserSortActive)
            {
                _currentSortColumn = JLS.Properties.Settings.Default.BrowserSortColumn;
                _isSortAscending = JLS.Properties.Settings.Default.BrowserSortAscending;
            }

            if (string.IsNullOrEmpty(CurrentFolderPath) && !string.IsNullOrEmpty(JLS.Properties.Settings.Default.LastBrowserPath))
            {
                CurrentFolderPath = JLS.Properties.Settings.Default.LastBrowserPath;
                await LoadFiles(CurrentFolderPath);
            }

            if (!string.IsNullOrEmpty(JLS.Properties.Settings.Default.ExpandedFoldersJson))
            {
                try
                {
                    var paths = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.HashSet<string>>(JLS.Properties.Settings.Default.ExpandedFoldersJson);
                    if (paths != null)
                    {
                        foreach (var path in paths)
                        {
                            await ExpandPathAsync(path);
                        }
                    }
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(CurrentFolderPath))
            {
                await ExpandPathAsync(CurrentFolderPath, selectLastNode: true);
            }

            _isRestoringTree = false;
            ManageWatchers();
        }

        private async System.Threading.Tasks.Task ExpandPathAsync(string path, bool selectLastNode = false)
        {
            var parts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            string rootPath = parts[0] + Path.DirectorySeparatorChar;
            var currentNode = DriveNodes.FirstOrDefault(d => d.FullPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase));

            for (int i = 0; i < parts.Length; i++)
            {
                if (currentNode == null) break;

                if (i < parts.Length - 1 || !selectLastNode)
                {
                    currentNode.IsExpanded = true;
                    await ExpandNode(currentNode);    
                }

                if (i < parts.Length - 1)
                {
                    string nextPath = Path.Combine(currentNode.FullPath, parts[i + 1]);
                    currentNode = currentNode.Children.FirstOrDefault(c => c.FullPath.Equals(nextPath, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    if (selectLastNode || currentNode.FullPath == CurrentFolderPath)
                    {
                        currentNode.IsSelected = true;
                        SelectedNode = currentNode;
                    }
                }
            }
        }

        public void SaveTreeState()
        {
            if (!_isStateRestored) return;

            var expandedPaths = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in DriveNodes)
            {
                GatherExpandedPaths(node, expandedPaths);
            }

            JLS.Properties.Settings.Default.ExpandedFoldersJson = System.Text.Json.JsonSerializer.Serialize(expandedPaths);
            JLS.Properties.Settings.Default.LastBrowserPath = CurrentFolderPath;

            JLS.Properties.Settings.Default.BrowserSortColumn = _currentSortColumn;
            JLS.Properties.Settings.Default.BrowserSortAscending = _isSortAscending ?? false;
            JLS.Properties.Settings.Default.BrowserSortActive = _isSortAscending != null;

            JLS.Properties.Settings.Default.Save();
        }

        private void ClearSelection(ObservableCollection<TreeViewNode> nodes)
        {
            foreach (var node in nodes)
            {
                node.IsSelected = false;
                ClearSelection(node.Children);
            }
        }

        partial void OnCurrentFolderPathChanged(string value)
        {
            if (!string.IsNullOrEmpty(value) && Directory.Exists(value))
            {
                ManageWatchers();     

                JLS.Properties.Settings.Default.LastBrowserPath = value;
                JLS.Properties.Settings.Default.Save();
            }
        }

        partial void OnSelectedNodeChanged(TreeViewNode? value)
        {
            if (value != null && value.IsDirectory)
            {
                ClearSelection(DriveNodes);

                value.IsSelected = true;

                CurrentFolderPath = value.FullPath;
                _ = LoadFiles(value.FullPath);
            }
        }

        private void ManageWatchers()
        {
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var pathsToWatch = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (!string.IsNullOrEmpty(CurrentFolderPath) && Directory.Exists(CurrentFolderPath))
                {
                    pathsToWatch.Add(CurrentFolderPath);
                }

                foreach (var driveNode in DriveNodes)
                {
                    GatherExpandedPaths(driveNode, pathsToWatch);
                }

                var currentWatchedPaths = _activeWatchers.Keys.ToList();
                foreach (var path in currentWatchedPaths)
                {
                    if (!pathsToWatch.Contains(path))
                    {
                        _activeWatchers[path].Dispose();
                        _activeWatchers.Remove(path);
                    }
                }

                foreach (var path in pathsToWatch)
                {
                    if (!_activeWatchers.ContainsKey(path))
                    {
                        try
                        {
                            var watcher = new FileSystemWatcher(path)
                            {
                                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                                EnableRaisingEvents = true
                            };

                            watcher.Changed += (s, e) => OnFolderChanged();
                            watcher.Created += (s, e) => OnFolderChanged();
                            watcher.Deleted += (s, e) => OnFolderChanged();
                            watcher.Renamed += (s, e) => OnFolderChanged();

                            _activeWatchers[path] = watcher;
                        }
                        catch {      }
                    }
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void GatherExpandedPaths(TreeViewNode node, System.Collections.Generic.HashSet<string> paths)
        {
            if (node.IsExpanded && node.Children.Count > 0 && node.Children[0].Name != "Loading...")
            {
                if (Directory.Exists(node.FullPath))
                {
                    paths.Add(node.FullPath);
                }

                foreach (var child in node.Children.Where(c => c.IsDirectory))
                {
                    GatherExpandedPaths(child, paths);
                }
            }
        }

        private void OnFolderChanged()
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private async Task LoadDrivesAsync()
        {
            DriveNodes.Clear();

            var drives = await Task.Run(() =>
            {
                return DriveInfo.GetDrives()
                                .Where(d => d.IsReady)
                                .Select(d => new
                                {
                                    Name = d.Name.TrimEnd('\\'),
                                    FullPath = d.RootDirectory.FullName
                                })
                                .ToList();
            });

            foreach (var drive in drives)
            {
                var node = new TreeViewNode
                {
                    Name = drive.Name,
                    FullPath = drive.FullPath,
                    IsDrive = true,
                    IsDirectory = true
                };
                node.Children.Add(new TreeViewNode { Name = "Loading..." });
                DriveNodes.Add(node);
            }
        }

        [RelayCommand]
        private async Task ExpandNode(TreeViewNode? node)
        {
            if (node == null || !node.IsDirectory) return;
            if (node.Children.Count != 1 || node.Children[0].Name != "Loading...") return;

            var dirs = await Task.Run(() =>
            {
                try
                {
                    var explorerComparer = new ExplorerStringComparer();
                    return Directory.GetDirectories(node.FullPath)
                                    .Select(d => new DirectoryInfo(d))
                                    .Where(info => (info.Attributes & FileAttributes.Hidden) == 0)
                                    .OrderBy(info => info.Name, explorerComparer)
                                    .ToList();
                }
                catch { return new System.Collections.Generic.List<DirectoryInfo>(); }
            });

            node.Children.Clear();

            foreach (var info in dirs)
            {
                var childNode = new TreeViewNode
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    IsDirectory = true
                };

                try
                {
                    if (Directory.EnumerateDirectories(info.FullName).Any())
                    {
                        childNode.Children.Add(new TreeViewNode { Name = "Loading..." });
                    }
                }
                catch { }

                node.Children.Add(childNode);
            }

            if (!_isRestoringTree)
            {
                ManageWatchers();
            }
        }



        public async System.Threading.Tasks.Task LoadFiles(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                _rawFiles.Clear();
                ApplySortAndFilter();
                return;
            }

            Files.Clear();
            if (IsAlbumViewEnabled)
            {
                AlbumTitle = string.Empty;
                AlbumArtist = string.Empty;
                AlbumYear = string.Empty;
                AlbumDurationInfo = string.Empty;
                AlbumCover = null;
                FullAlbumCover = null;
                DominantColor = Color.FromRgb(30, 30, 30);
                LightColor = Color.FromRgb(100, 100, 120);
            }

            try
            {
                var allItems = await System.Threading.Tasks.Task.Run(() =>
                {
                    var items = FileSystemService.GetDirectoryContents(path);
                    if (items == null) return null;

                    var audioFiles = items.Where(i => !i.IsDirectory).ToList();

                    if (audioFiles.Count == 0)
                    {
                        var dirs = items.Where(i => i.IsDirectory).ToList();

                        System.Threading.Tasks.Parallel.ForEach(dirs, item =>
                        {
                            string? extractedAlbum = GetAlbumNameFromFolder(item.FullPath);
                            item.Title = string.IsNullOrWhiteSpace(extractedAlbum) ? item.Name : extractedAlbum;

                            var meta = GetFolderMetadata(item.FullPath);
                            if (meta.TrackCount > 0)
                            {
                                string durStr = meta.TotalDuration.TotalHours >= 1 ?
                                    $"{(int)meta.TotalDuration.TotalHours:D2}:{meta.TotalDuration.Minutes:D2}:{meta.TotalDuration.Seconds:D2}" :
                                    $"{meta.TotalDuration.Minutes:D2}:{meta.TotalDuration.Seconds:D2}";

                                string sizeStr = FormatSize(meta.TotalSize);

                                item.Duration = $"{meta.TrackCount} tracks  •  {durStr}  •  {sizeStr}";
                                item.Size = meta.TotalSize;
                            }
                            else
                            {
                                item.Duration = string.Empty;
                                item.Size = 0;
                            }
                        });
                    }
                    return items;
                });

                if (CurrentFolderPath != path) return;

                _rawFiles.Clear();
                if (allItems != null)
                {
                    var audioFiles = allItems.Where(i => !i.IsDirectory).ToList();
                    if (audioFiles.Count > 0)
                    {
                        foreach (var item in audioFiles) _rawFiles.Add(item);
                    }
                    else
                    {
                        foreach (var item in allItems.Where(i => i.IsDirectory)) _rawFiles.Add(item);
                    }
                }
            }
            catch (Exception)
            {
            }

            ApplySortAndFilter();
        }

        [RelayCommand]
        private void OpenInExplorer(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            JLS.Services.ExplorerHelper.OpenFolderAndSelectFile(path);
        }

        [RelayCommand]
        private void Refresh()
        {
            if (string.IsNullOrEmpty(CurrentFolderPath)) return;

            _ = LoadFilesSoft(CurrentFolderPath);

            UpdateTreeSoft();
        }

        private void UpdateNodeChildren(TreeViewNode node)
        {
            try
            {
                if (!Directory.Exists(node.FullPath)) return;

                var currentItems = Directory.EnumerateDirectories(node.FullPath)
                    .Select(d => new DirectoryInfo(d))
                    .Where(d => (d.Attributes & FileAttributes.Hidden) == 0)
                    .ToList();

                var toRemove = node.Children
                    .Where(child => child.Name != "Loading..." && currentItems.All(i => i.FullName != child.FullPath))
                    .ToList();

                foreach (var r in toRemove)
                {
                    node.Children.Remove(r);
                }

                foreach (var item in currentItems)
                {
                    var existingNode = node.Children.FirstOrDefault(c => c.FullPath == item.FullName);

                    if (existingNode == null)
                    {
                        var newNode = new TreeViewNode
                        {
                            Name = item.Name,
                            FullPath = item.FullName,
                            IsDirectory = true
                        };

                        try
                        {
                            if (Directory.EnumerateDirectories(item.FullName).Any())
                                newNode.Children.Add(new TreeViewNode { Name = "Loading..." });
                        }
                        catch { }

                        node.Children.Add(newNode);
                    }
                    else
                    {
                        if (existingNode.Children.Count == 0)
                        {
                            try
                            {
                                if (Directory.EnumerateDirectories(item.FullName).Any())
                                {
                                    existingNode.Children.Add(new TreeViewNode { Name = "Loading..." });
                                }
                            }
                            catch { }
                        }
                        else if (existingNode.Children.Count == 1 && existingNode.Children[0].Name == "Loading...")
                        {
                            try
                            {
                                if (!Directory.EnumerateDirectories(item.FullName).Any())
                                {
                                    existingNode.Children.Clear();
                                }
                            }
                            catch { }
                        }
                    }
                }

                var explorerComparer = new ExplorerStringComparer();
                var sorted = node.Children.OrderBy(c => c.Name, explorerComparer).ToList();

                for (int i = 0; i < sorted.Count; i++)
                {
                    int currentIndex = node.Children.IndexOf(sorted[i]);
                    if (currentIndex != i)
                    {
                        node.Children.Move(currentIndex, i);
                    }
                }
            }
            catch {          }
        }

        public async Task LoadFilesSoft(string path)
        {
            if (!Directory.Exists(path))
            {
                _rawFiles.Clear();
                ApplySortAndFilter();
                return;
            }

            var allItems = await Task.Run(() =>
            {
                var items = FileSystemService.GetDirectoryContents(path);
                if (items == null) return new System.Collections.Generic.List<FileSystemItem>();

                var audioFiles = items.Where(i => !i.IsDirectory).ToList();
                if (audioFiles.Count == 0)
                {
                    var dirs = items.Where(i => i.IsDirectory).ToList();

                    System.Threading.Tasks.Parallel.ForEach(dirs, dir =>
                    {
                        string? extractedAlbum = GetAlbumNameFromFolder(dir.FullPath);
                        dir.Title = string.IsNullOrWhiteSpace(extractedAlbum) ? dir.Name : extractedAlbum;

                        var meta = GetFolderMetadata(dir.FullPath);
                        if (meta.TrackCount > 0)
                        {
                            string durStr = meta.TotalDuration.TotalHours >= 1 ?
                                $"{(int)meta.TotalDuration.TotalHours:D2}:{meta.TotalDuration.Minutes:D2}:{meta.TotalDuration.Seconds:D2}" :
                                $"{meta.TotalDuration.Minutes:D2}:{meta.TotalDuration.Seconds:D2}";

                            string sizeStr = FormatSize(meta.TotalSize);
                            dir.Duration = $"{meta.TrackCount} tracks  •  {durStr}  •  {sizeStr}";
                            dir.Size = meta.TotalSize;
                        }
                        else
                        {
                            dir.Duration = string.Empty;
                            dir.Size = 0;
                        }
                    });
                }
                return items;
            });

            if (CurrentFolderPath != path)
                return;

            var mainAudioFiles = allItems.Where(i => !i.IsDirectory).ToList();
            if (mainAudioFiles.Count > 0)
            {
                _rawFiles = mainAudioFiles;
            }
            else
            {
                _rawFiles = allItems.Where(i => i.IsDirectory).ToList();
            }

            ApplySortAndFilter();
        }

        [RelayCommand]
        private void RefreshAll()
        {
            if (!string.IsNullOrEmpty(CurrentFolderPath))
            {
                _ = LoadFiles(CurrentFolderPath);
            }
            UpdateTreeSoft();
        }

        private void UpdateTreeSoft()
        {
            var currentDrives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();

            var drivesToRemove = DriveNodes.Where(dn => currentDrives.All(d => d.RootDirectory.FullName != dn.FullPath)).ToList();
            foreach (var r in drivesToRemove) DriveNodes.Remove(r);

            foreach (var drive in currentDrives)
            {
                if (DriveNodes.All(dn => dn.FullPath != drive.RootDirectory.FullName))
                {
                    var node = new TreeViewNode
                    {
                        Name = drive.Name.TrimEnd('\\'),
                        FullPath = drive.RootDirectory.FullName,
                        IsDrive = true,
                        IsDirectory = true
                    };
                    node.Children.Add(new TreeViewNode { Name = "Loading..." });
                    DriveNodes.Add(node);
                }
            }

            foreach (var driveNode in DriveNodes)
            {
                RefreshNodeRecursive(driveNode);
            }
            ManageWatchers();
        }

        private void RefreshNodeRecursive(TreeViewNode node)
        {
            if (node.Children.Count > 0 && node.Children[0].Name != "Loading...")
            {
                UpdateNodeChildren(node);

                foreach (var child in node.Children.Where(c => c.IsDirectory))
                {
                    RefreshNodeRecursive(child);
                }
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

        [RelayCommand]
        private void FileDoubleClick(FileSystemItem? file)
        {
            if (file == null) return;

            if (file.IsDirectory)
            {
                _ = ExpandPathAsync(file.FullPath, selectLastNode: true);
            }
            else
            {
                PlayRequested?.Invoke(file.FullPath);
            }
        }

        private void CalculateFolderStats()
        {
            if (Files == null || Files.Count == 0)
            {
                FolderStats = "0 tracks  •  00:00  •  0 MB";
                return;
            }

            long totalBytes = 0;
            TimeSpan totalDuration = TimeSpan.Zero;

            foreach (var f in Files)
            {
                totalBytes += f.Size;
                if (!string.IsNullOrEmpty(f.Duration))
                {
                    string timeStr = f.Duration;

                    if (f.IsDirectory && timeStr.Contains("•"))
                    {
                        var segments = timeStr.Split('•');
                        var timeSegment = segments.FirstOrDefault(s => s.Contains(":"));
                        if (timeSegment != null) timeStr = timeSegment.Trim();
                    }

                    var parts = timeStr.Split(':');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int m) && int.TryParse(parts[1], out int s))
                        totalDuration += new TimeSpan(0, m, s);
                    else if (parts.Length == 3 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m2) && int.TryParse(parts[2], out int s2))
                        totalDuration += new TimeSpan(h, m2, s2);
                }
            }

            string sizeStr = totalBytes >= 1073741824 ? $"{totalBytes / 1073741824.0:F2} GB" :
                             totalBytes >= 1048576 ? $"{totalBytes / 1048576.0:F2} MB" :
                             $"{totalBytes / 1024.0:F2} KB";

            string durStr = totalDuration.TotalHours >= 1 ?
                $"{(int)totalDuration.TotalHours:D2}:{totalDuration.Minutes:D2}:{totalDuration.Seconds:D2}" :
                $"{totalDuration.Minutes:D2}:{totalDuration.Seconds:D2}";

            string itemWord = IsShowingFoldersOnly ? "folders" : "tracks";
            FolderStats = $"{Files.Count} {itemWord}  •  {durStr}  •  {sizeStr}";
        }

        partial void OnIsAlbumViewEnabledChanged(bool value)
        {
            UpdateAlbumInfo(CurrentFolderPath);
        }

        private void UpdateAlbumInfo(string path)
        {
            AlbumTitle = string.Empty;
            AlbumArtist = string.Empty;
            AlbumYear = string.Empty;
            AlbumCover = null;
            DominantColor = Color.FromRgb(30, 30, 30);

            AlbumFormat = string.Empty;
            IsAlbumFormatVisible = false;
            AlbumSampleRate = string.Empty;
            IsAlbumSampleRateVisible = false;
            AlbumBitDepth = string.Empty;
            IsAlbumBitDepthVisible = false;

            if (!IsAlbumViewEnabled || string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return;

            var audioFiles = _rawFiles.Where(f => !f.IsDirectory).ToList();

            if (audioFiles.Count == 0)
            {
                AlbumTitle = new DirectoryInfo(path).Name;
                AlbumDurationInfo = string.Empty;
                IsHeartVisible = false;

                LoadAlbumCover(path, audioFiles);
                UpdateHeartState();
                return;
            }

            var tracksToCheck = audioFiles.Take(50).ToList();

            var formats = tracksToCheck.Select(f => f.Extension?.Trim().ToLowerInvariant()).Distinct().ToList();
            if (formats.Count == 1 && !string.IsNullOrWhiteSpace(formats[0]))
            {
                AlbumFormat = formats[0]!.Replace(".", "").ToUpperInvariant();
                IsAlbumFormatVisible = true;
            }

            var rates = tracksToCheck.Select(f => f.SampleRate).Distinct().ToList();
            if (rates.Count == 1 && rates[0] is { } rate && rate > 0)
            {
                AlbumSampleRate = $"{rate / 1000.0:0.#} kHz";
                IsAlbumSampleRateVisible = true;
            }

            var depths = tracksToCheck.Select(f => f.BitsPerSample).Distinct().ToList();
            if (depths.Count == 1 && depths[0] is { } depth && depth > 0)
            {
                bool isLossy = formats.Count == 1 && (formats[0] == ".mp3" || formats[0] == ".aac" || formats[0] == ".ogg" || formats[0] == ".wma");
                if (!isLossy)
                {
                    AlbumBitDepth = $"{depth} Bit";
                    IsAlbumBitDepthVisible = true;
                }
            }

            IsHeartVisible = true;          

            IEnumerable<string> ExtractAllArtists(string? fullArtist)
            {
                if (string.IsNullOrWhiteSpace(fullArtist)) yield break;

                string[] separators = { " feat. ", " ft. ", " featuring ", " feat ", " ft ", " & ", " and ", " vs. ", ",", ";" };

                var parts = fullArtist.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    string cleaned = part.Trim();
                    if (!string.IsNullOrEmpty(cleaned))
                    {
                        yield return cleaned;
                    }
                }
            }

            if (audioFiles.Count > 0)
            {
                int threshold60 = (int)Math.Ceiling(audioFiles.Count * 0.6);

                var dominantAlbum = audioFiles.Where(f => !string.IsNullOrWhiteSpace(f.Album))
                                              .GroupBy(f => f.Album)
                                              .OrderByDescending(g => g.Count())
                                              .FirstOrDefault();
                AlbumTitle = (dominantAlbum != null && dominantAlbum.Count() >= threshold60) ? (dominantAlbum.Key ?? "") : "";

                var dominantYear = audioFiles.Where(f => !string.IsNullOrWhiteSpace(f.Year))
                                             .GroupBy(f => f.Year)
                                             .OrderByDescending(g => g.Count())
                                             .FirstOrDefault();
                AlbumYear = (dominantYear != null && dominantYear.Count() >= threshold60) ? (dominantYear.Key ?? "") : "";

                var dominantArtist = audioFiles
                    .Where(f => !string.IsNullOrWhiteSpace(f.Artist))
                    .SelectMany(track => ExtractAllArtists(track.Artist)
                        .Select(artist => new {
                            Original = artist,
                            Cleaned = artist.ToLowerInvariant(),
                            Track = track
                        }))
                    .GroupBy(x => x.Cleaned)
                    .Select(g => new
                    {
                        OriginalName = g.First().Original,
                        TrackCount = g.Select(x => x.Track).Distinct().Count()
                    })
                    .OrderByDescending(a => a.TrackCount)
                    .FirstOrDefault();

                if (dominantArtist != null && dominantArtist.TrackCount >= threshold60)
                {
                    AlbumArtist = dominantArtist.OriginalName;
                }
                else
                {
                    AlbumArtist = "Mixed Tracks";
                }
            }

            if (string.IsNullOrWhiteSpace(AlbumTitle))
            {
                AlbumTitle = new DirectoryInfo(path).Name;
                AlbumYear = string.Empty;
            }

            TimeSpan totalDuration = TimeSpan.Zero;
            foreach (var f in audioFiles)
            {
                if (!string.IsNullOrEmpty(f.Duration))
                {
                    var parts = f.Duration.Split(':');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int m) && int.TryParse(parts[1], out int s))
                        totalDuration += new TimeSpan(0, m, s);
                    else if (parts.Length == 3 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m2) && int.TryParse(parts[2], out int s2))
                        totalDuration += new TimeSpan(h, m2, s2);
                }
            }
            string durStr = totalDuration.TotalHours >= 1 ?
                $"{(int)totalDuration.TotalHours:D2}:{totalDuration.Minutes:D2}:{totalDuration.Seconds:D2}" :
                $"{totalDuration.Minutes:D2}:{totalDuration.Seconds:D2}";

            AlbumDurationInfo = $"{audioFiles.Count} tracks • {durStr}";

            LoadAlbumCover(path, audioFiles);

            UpdateHeartState();
        }

        private void LoadAlbumCover(string folderPath, System.Collections.Generic.List<FileSystemItem> audioFiles)
        {
            try
            {
                _cachedCoverPath = null;
                _cachedCoverBytes = null;
                FullAlbumCover = null;
                BitmapImage? bitmap = null;

                string[] possibleNames = { "cover.jpg", "folder.jpg", "album.jpg", "cover.png", "folder.png", "front.jpg", "front.png" };
                foreach (var name in possibleNames)
                {
                    string fullPath = Path.Combine(folderPath, name);
                    if (File.Exists(fullPath)) { _cachedCoverPath = fullPath; break; }
                }

                if (_cachedCoverPath != null)
                {
                    bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(_cachedCoverPath);
                    bitmap.DecodePixelWidth = 400;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                }
                else if (audioFiles != null && audioFiles.Count > 0)
                {
                    var covers = new System.Collections.Generic.List<BitmapImage>();
                    var seenSizes = new System.Collections.Generic.HashSet<long>();

                    int limit = Math.Min(audioFiles.Count, 30);
                    for (int i = 0; i < limit; i++)
                    {
                        if (covers.Count >= 4) break;      

                        try
                        {
                            using (var tagFile = TagLib.File.Create(new JLS.Services.SafeFileAbstraction(audioFiles[i].FullPath)))
                            {
                                if (tagFile.Tag.Pictures.Length > 0)
                                {
                                    var picData = tagFile.Tag.Pictures[0].Data.Data;
                                    long size = picData.Length;

                                    if (seenSizes.Add(size))
                                    {
                                        if (covers.Count == 0) _cachedCoverBytes = picData;

                                        using (var ms = new MemoryStream(picData))
                                        {
                                            var bmp = new BitmapImage();
                                            bmp.BeginInit();
                                            bmp.StreamSource = ms;
                                            bmp.DecodePixelWidth = 400;
                                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                                            bmp.EndInit();
                                            bmp.Freeze();
                                            covers.Add(bmp);
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    if (covers.Count >= 4)
                    {
                        bitmap = CreateCollage(covers);
                    }
                    else if (covers.Count > 0 && _cachedCoverBytes != null)
                    {
                        using (var ms = new MemoryStream(_cachedCoverBytes))
                        {
                            bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.StreamSource = ms;
                            bitmap.DecodePixelWidth = 400;
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                            bitmap.Freeze();
                        }
                    }
                    else
                    {
                        bitmap = SearchSubfoldersForCover(folderPath);
                    }
                }
                else
                {
                    bitmap = SearchSubfoldersForCover(folderPath);
                }

                if (bitmap != null)
                {
                    AlbumCover = bitmap;
                    var palette = ColorExtractor.GetPaletteFromImage(bitmap);
                    DominantColor = palette.Primary;
                    LightColor = palette.Light;
                }
            }
            catch { }
        }

        public void RefreshAlbumColors()
        {
            if (AlbumCover is BitmapSource bitmapSource)
            {
                var palette = ColorExtractor.GetPaletteFromImage(bitmapSource);
                DominantColor = palette.Primary;
                LightColor = palette.Light;
            }
        }

        public void BulkAddToPlaylist(Playlist playlist, System.Collections.Generic.List<FileSystemItem> tracks)
        {
            foreach (var track in tracks)
            {
                if (!track.IsDirectory && !playlist.TrackPaths.Contains(track.FullPath))
                {
                    PlaylistManager.AddTrackToPlaylist(playlist, track);
                }
            }
        }

        private (int TrackCount, TimeSpan TotalDuration, long TotalSize) GetFolderMetadata(string folderPath)
        {
            int trackCount = 0;
            TimeSpan totalDuration = TimeSpan.Zero;
            long totalSize = 0;

            try
            {
                var extensions = new[] { ".mp3", ".flac", ".wav", ".m4a", ".ogg", ".aac", ".wma", ".alac", ".ape", ".dsf", ".dff" };

                var files = Directory.EnumerateFiles(folderPath)
                                     .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                     .ToList();

                if (files.Count == 0)
                {
                    var subdirs = Directory.EnumerateDirectories(folderPath);
                    foreach (var subdir in subdirs)
                    {
                        try
                        {
                            files.AddRange(Directory.EnumerateFiles(subdir)
                                                    .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant())));
                        }
                        catch { }
                    }
                }

                trackCount = files.Count;

                foreach (var file in files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        totalSize += fileInfo.Length;

                        using (var tagFile = TagLib.File.Create(new JLS.Services.SafeFileAbstraction(file)))
                        {
                            totalDuration += tagFile.Properties.Duration;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return (trackCount, totalDuration, totalSize);
        }

        private string FormatSize(long bytes)
        {
            if (bytes >= 1073741824) return $"{bytes / 1073741824.0:F2} GB";
            if (bytes >= 1048576) return $"{bytes / 1048576.0:F2} MB";
            return $"{bytes / 1024.0:F2} KB";
        }

        private string? GetAlbumNameFromFolder(string folderPath)
        {
            try
            {
                var extensions = new[] { ".mp3", ".flac", ".wav", ".m4a", ".ogg", ".aac", ".wma", ".alac", ".ape", ".dsf", ".dff" };
                var files = Directory.EnumerateFiles(folderPath)
                                     .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                     .Take(2)
                                     .ToList();

                if (files.Count == 0) return null;

                string? album1 = null, album2 = null;
                try { using (var t1 = TagLib.File.Create(new JLS.Services.SafeFileAbstraction(files[0]))) { album1 = t1.Tag.Album; } } catch { }

                if (files.Count > 1)
                {
                    try { using (var t2 = TagLib.File.Create(new JLS.Services.SafeFileAbstraction(files[1]))) { album2 = t2.Tag.Album; } } catch { }

                    if (album1 == album2 && !string.IsNullOrWhiteSpace(album1))
                        return album1;
                    return null;
                }

                return string.IsNullOrWhiteSpace(album1) ? null : album1;
            }
            catch { }
            return null;
        }

        private BitmapImage? SearchSubfoldersForCover(string folderPath)
        {
            var covers = new System.Collections.Generic.List<BitmapImage>();
            string[] possibleNames = { "cover.jpg", "folder.jpg", "album.jpg", "cover.png", "folder.png", "front.jpg", "front.png" };
            var extensions = new[] { ".mp3", ".flac", ".wav", ".m4a", ".ogg", ".aac", ".wma", ".alac", ".ape", ".dsf", ".dff" };

            try
            {
                var subdirs = Directory.EnumerateDirectories(folderPath).OrderBy(d => d).ToList();
                foreach (var subdir in subdirs)
                {
                    if (covers.Count >= 4) break;

                    try
                    {
                        byte[]? foundBytes = null;
                        string? foundPath = null;

                        void CheckFolder(string targetPath)
                        {
                            if (foundPath != null || foundBytes != null) return;

                            foreach (var name in possibleNames)
                            {
                                string path = Path.Combine(targetPath, name);
                                if (File.Exists(path)) { foundPath = path; return; }
                            }

                            var firstAudio = Directory.EnumerateFiles(targetPath).FirstOrDefault(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                            if (firstAudio != null)
                            {
                                using (var tagFile = TagLib.File.Create(new JLS.Services.SafeFileAbstraction(firstAudio)))
                                {
                                    if (tagFile.Tag.Pictures.Length > 0)
                                        foundBytes = tagFile.Tag.Pictures[0].Data.Data;
                                }
                            }
                        }

                        CheckFolder(subdir);

                        if (foundPath == null && foundBytes == null)
                        {
                            var innerDirs = Directory.EnumerateDirectories(subdir).OrderBy(d => d);
                            foreach (var innerDir in innerDirs)
                            {
                                CheckFolder(innerDir);
                                if (foundPath != null || foundBytes != null) break;
                            }
                        }

                        if (foundPath != null) foundBytes = File.ReadAllBytes(foundPath);

                        if (foundBytes != null)
                        {
                            if (covers.Count == 0) _cachedCoverBytes = foundBytes;

                            using (var ms = new MemoryStream(foundBytes))
                            {
                                var bmp = new BitmapImage();
                                bmp.BeginInit();
                                bmp.StreamSource = ms;
                                bmp.DecodePixelWidth = 400;
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.EndInit();
                                bmp.Freeze();
                                covers.Add(bmp);
                            }
                        }
                    }
                    catch { continue; }
                }

                if (covers.Count >= 4) return CreateCollage(covers);

                if (covers.Count > 0 && _cachedCoverBytes != null)
                {
                    using (var ms = new MemoryStream(_cachedCoverBytes))
                    {
                        var fullBmp = new BitmapImage();
                        fullBmp.BeginInit();
                        fullBmp.StreamSource = ms;
                        fullBmp.DecodePixelWidth = 800;
                        fullBmp.CacheOption = BitmapCacheOption.OnLoad;
                        fullBmp.EndInit();
                        fullBmp.Freeze();
                        return fullBmp;
                    }
                }
            }
            catch { }
            return null;
        }

        private BitmapImage CreateCollage(System.Collections.Generic.List<BitmapImage> images)
        {
            int width = 800; int height = 800;
            var renderTarget = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            var drawingVisual = new System.Windows.Media.DrawingVisual();

            using (var drawingContext = drawingVisual.RenderOpen())
            {
                drawingContext.DrawImage(images[0], new Rect(0, 0, width / 2, height / 2));
                drawingContext.DrawImage(images[1], new Rect(width / 2, 0, width / 2, height / 2));
                drawingContext.DrawImage(images[2], new Rect(0, height / 2, width / 2, height / 2));
                drawingContext.DrawImage(images[3], new Rect(width / 2, height / 2, width / 2, height / 2));
            }
            renderTarget.Render(drawingVisual);

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(renderTarget));

            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                _cachedCoverBytes = ms.ToArray();
                _cachedCoverPath = null;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(_cachedCoverBytes);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
        }

    }

    public partial class TreeViewNode : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _fullPath = string.Empty;

        [ObservableProperty]
        private bool _isDirectory;

        [ObservableProperty]
        private bool _isDrive;

        [ObservableProperty]
        private bool _isExpanded;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _isVisible = true;

        public ObservableCollection<TreeViewNode> Children { get; } = new();
    }
}