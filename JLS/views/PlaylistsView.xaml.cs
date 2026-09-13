using System;
using System.Windows;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using JLS.Models;
using JLS.ViewModels;
using JLS.Properties;
using System.Text.Json;

namespace JLS.Views
{
    public partial class PlaylistsView : UserControl
    {
        private bool _wasOpened = false;

        private Dictionary<string, double> _tracksScrollCache = new();
        private string _currentPlaylistName = string.Empty;
        private readonly Dictionary<GridViewColumn, double> _originalColumnWidths = new();
        private ContextMenu? _columnContextMenu;
        private GridViewColumnHeader? _lastHeaderClicked = null;
        private string _originalHeaderString = string.Empty;

        public PlaylistsView()
        {
            InitializeComponent();
            _columnContextMenu = (ContextMenu)FindResource("ColumnHeaderContextMenu");

            this.IsVisibleChanged += PlaylistsView_IsVisibleChanged;

            this.DataContextChanged += PlaylistsView_DataContextChanged;

            Application.Current.Exit += (s, e) => {
                try
                {
                    if (_wasOpened) PlaylistsView_Unloaded(null!, null!);
                }
                catch { }
            };
        }

        private ScrollViewer? GetActiveTracksScrollViewer()
        {
            if (MinimalPlaylistTracks.IsVisible)
                return GetScrollViewer(MinimalPlaylistTracks);

            if (PlaylistTracksLines.IsVisible)
                return GetScrollViewer(PlaylistTracksLines);

            return GetScrollViewer(PlaylistTracksLines) ?? GetScrollViewer(MinimalPlaylistTracks);
        }

        private void PlaylistsView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
            {
                _wasOpened = true;   
                PlaylistsView_Loaded(this, new RoutedEventArgs());
            }
            else
            {
                if (_wasOpened) PlaylistsView_Unloaded(this, new RoutedEventArgs());
            }
        }

        private void PlaylistsView_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is PlaylistsViewModel vm)
            {
                vm.SearchPlaylistText = string.Empty;
            }


            double savedWidth = Settings.Default.PlaylistSplitterWidth;
            if (savedWidth < 200) savedWidth = 250;           

            _savedSidebarWidth = savedWidth;

            if (Settings.Default.IsPlaylistSidebarCollapsed)
            {
                _isSidebarCollapsed = true;
                LeftColumn.MinWidth = 0;
                LeftColumn.MaxWidth = 0;
                LeftColumn.Width = new GridLength(0);    
                PlaylistSplitter.Visibility = Visibility.Collapsed;

                if (ToggleSidebarIcon.RenderTransform is RotateTransform rotate1) rotate1.Angle = 180;
                if (MinimalToggleSidebarIcon?.RenderTransform is RotateTransform rotate2) rotate2.Angle = 180;
            }
            else
            {
                _isSidebarCollapsed = false;
                LeftColumn.ClearValue(ColumnDefinition.MaxWidthProperty);     
                LeftColumn.MinWidth = 200;
                LeftColumn.Width = new GridLength(_savedSidebarWidth);
                PlaylistSplitter.Visibility = Visibility.Visible;

                if (ToggleSidebarIcon.RenderTransform is RotateTransform rotate1) rotate1.Angle = 0;
                if (MinimalToggleSidebarIcon?.RenderTransform is RotateTransform rotate2) rotate2.Angle = 0;
            }

            if (PlaylistTracksLines?.View is GridView gridView && !string.IsNullOrEmpty(Settings.Default.PlaylistColumns))
            {
                try
                {
                    var savedWidths = JsonSerializer.Deserialize<Dictionary<string, double>>(Settings.Default.PlaylistColumns);
                    if (savedWidths != null)
                    {
                        foreach (var column in gridView.Columns)
                        {
                            string header = column.Header?.ToString() ?? "";
                            string cleanName = header.Replace(" ▲", "").Replace(" ▼", "");

                            if (savedWidths.TryGetValue(cleanName, out double width))
                            {
                                column.Width = width;
                                if (width > 0) _originalColumnWidths[column] = width;
                            }
                        }
                    }
                }
                catch { }
            }

            try
            {
                if (!string.IsNullOrEmpty(Settings.Default.PlaylistTracksScrollJson))
                {
                    _tracksScrollCache = JsonSerializer.Deserialize<Dictionary<string, double>>(Settings.Default.PlaylistTracksScrollJson) ?? new();
                }

                Dispatcher.BeginInvoke(new Action(async () => {
                    if (DataContext is PlaylistsViewModel vm)
                    {
                        if (vm.SelectedPlaylist == null && !string.IsNullOrEmpty(Settings.Default.LastSelectedPlaylistName))
                        {
                            var savedPlaylist = vm.Manager.Playlists.FirstOrDefault(p => p.Name == Settings.Default.LastSelectedPlaylistName);
                            if (savedPlaylist != null)
                            {
                                vm.SelectedPlaylist = savedPlaylist;
                            }
                        }

                        if (vm.SelectedPlaylist != null)
                        {
                            _currentPlaylistName = vm.SelectedPlaylist.Name;
                        }

                        await System.Threading.Tasks.Task.Delay(50);

                        var leftScroll = GetScrollViewer(PlaylistsListView);
                        if (leftScroll != null) leftScroll.ScrollToVerticalOffset(Settings.Default.PlaylistsListScrollPos);

                        var rightScroll = GetActiveTracksScrollViewer();
                        if (rightScroll != null && _tracksScrollCache.TryGetValue(_currentPlaylistName, out double savedOffset))
                        {
                            rightScroll.ScrollToVerticalOffset(savedOffset);
                        }
                    }
                }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
            catch { }
        }

        private void PlaylistsView_Unloaded(object sender, RoutedEventArgs e)
        {
            Settings.Default.PlaylistSplitterWidth = _isSidebarCollapsed ? _savedSidebarWidth : LeftColumn.ActualWidth;

            Settings.Default.IsPlaylistSidebarCollapsed = _isSidebarCollapsed;

            var leftScroll = GetScrollViewer(PlaylistsListView);
            if (leftScroll != null) Settings.Default.PlaylistsListScrollPos = leftScroll.VerticalOffset;

            var rightScroll = GetActiveTracksScrollViewer();
            if (rightScroll != null && !string.IsNullOrEmpty(_currentPlaylistName))
            {
                _tracksScrollCache[_currentPlaylistName] = rightScroll.VerticalOffset;
            }

            Settings.Default.PlaylistTracksScrollJson = JsonSerializer.Serialize(_tracksScrollCache);

            if (PlaylistTracksLines?.View is GridView gridView)
            {
                var widthsToSave = new Dictionary<string, double>();
                foreach (var column in gridView.Columns)
                {
                    string header = column.Header?.ToString() ?? "";
                    string cleanName = header.Replace(" ▲", "").Replace(" ▼", "");
                    widthsToSave[cleanName] = double.IsNaN(column.Width) ? column.ActualWidth : column.Width;
                }
                Settings.Default.PlaylistColumns = JsonSerializer.Serialize(widthsToSave);
            }
            Settings.Default.Save();
        }

        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is PlaylistsViewModel vm)
            {
                var listView = sender as ListView;
                if (listView == null) return;

                var hitTestResult = VisualTreeHelper.HitTest(listView, e.GetPosition(listView));
                if (hitTestResult?.VisualHit != null)
                {
                    DependencyObject current = hitTestResult.VisualHit;
                    while (current != null && !(current is ListViewItem))
                    {
                        current = VisualTreeHelper.GetParent(current);
                    }

                    if (current is ListViewItem item)
                    {
                        var fileItem = item.Content as FileSystemItem;
                        if (fileItem != null)
                        {
                            vm.TrackDoubleClickCommand.Execute(fileItem);
                        }
                    }
                }
            }
        }

        private void MoreOptionsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                DependencyObject current = btn;
                while (current != null && !(current is ListViewItem))
                {
                    current = VisualTreeHelper.GetParent(current);
                }

                if (current is ListViewItem listViewItem)
                {
                    listViewItem.IsSelected = true;

                    if (listViewItem.ContextMenu != null)
                    {
                        btn.Tag = listViewItem.Tag;

                        listViewItem.ContextMenu.PlacementTarget = btn;
                        listViewItem.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Left;
                        listViewItem.ContextMenu.VerticalOffset = 4;
                        listViewItem.ContextMenu.HorizontalOffset = -4;
                        listViewItem.ContextMenu.IsOpen = true;
                    }
                }

                e.Handled = true;
            }
        }

        private Point _dragStartPoint;
        private Point _mouseStartPosInItem;
        private bool _isDraggingPlaylist = false;
        private Playlist? _draggedPlaylist = null;
        private ListViewItem? _draggedPlaylistLVI = null;

        private DragAdorner? _dragAdorner;
        private AdornerLayer? _adornerLayer;

        private void CleanupAdorner()
        {
            if (_dragAdorner != null && _adornerLayer != null)
            {
                _adornerLayer.Remove(_dragAdorner);
                _dragAdorner = null;
                _adornerLayer = null;
            }
            if (_draggedPlaylistLVI != null) _draggedPlaylistLVI.Opacity = 1.0;
            if (_draggedTrackLVI != null) _draggedTrackLVI.Opacity = 1.0;
        }

        private void PlaylistsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _draggedPlaylistLVI = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);

            if (_draggedPlaylistLVI != null)
            {
                _draggedPlaylist = _draggedPlaylistLVI.DataContext as Playlist;
                _mouseStartPosInItem = e.GetPosition(_draggedPlaylistLVI);       
            }
        }

        private void PlaylistsList_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                if (_isDraggingPlaylist) FinishDrag();
                return;
            }

            if (_draggedPlaylist == null || _draggedPlaylistLVI == null) return;

            Point mousePos = e.GetPosition(null);
            Vector diff = _dragStartPoint - mousePos;

            if (!_isDraggingPlaylist &&
                (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                 Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
            {
                _isDraggingPlaylist = true;
                PlaylistsListView.CaptureMouse();   

                _draggedPlaylistLVI.Opacity = 0.3;    

                _adornerLayer = AdornerLayer.GetAdornerLayer(PlaylistsListView);
                if (_adornerLayer != null)
                {
                    _dragAdorner = new DragAdorner(PlaylistsListView, _draggedPlaylistLVI, 0.85);
                    _adornerLayer.Add(_dragAdorner);
                }
            }

            if (_isDraggingPlaylist)
            {
                Point listPos = e.GetPosition(PlaylistsListView);
                _dragAdorner?.UpdatePosition(listPos.X - _mouseStartPosInItem.X, listPos.Y - _mouseStartPosInItem.Y);

                var hitResult = VisualTreeHelper.HitTest(PlaylistsListView, listPos);
                var hoveredItem = FindAncestor<ListViewItem>(hitResult?.VisualHit);

                if (hoveredItem != null && hoveredItem != _draggedPlaylistLVI && hoveredItem.DataContext is Playlist hoveredPlaylist)
                {
                    if (DataContext is PlaylistsViewModel vm)
                    {
                        int oldIdx = vm.Manager.Playlists.IndexOf(_draggedPlaylist);
                        int newIdx = vm.Manager.Playlists.IndexOf(hoveredPlaylist);

                        if (oldIdx != -1 && newIdx != -1)
                        {
                            Point posInHovered = e.GetPosition(hoveredItem);
                            double itemHalfHeight = hoveredItem.ActualHeight / 2;
                            bool isDraggingDown = newIdx > oldIdx;

                            if ((isDraggingDown && posInHovered.Y > itemHalfHeight) ||
                                (!isDraggingDown && posInHovered.Y < itemHalfHeight))
                            {
                                vm.Manager.Playlists.Move(oldIdx, newIdx);
                            }
                        }
                    }
                }
            }
        }

        private void PlaylistsList_MouseUp(object sender, MouseButtonEventArgs e)
        {
            FinishDrag();
        }

        private void PlaylistsList_MouseLeave(object sender, MouseEventArgs e)
        {
        }

        private void FinishDrag()
        {
            if (_isDraggingPlaylist)
            {
                _isDraggingPlaylist = false;
                PlaylistsListView.ReleaseMouseCapture();

                CleanupAdorner();       

                if (DataContext is PlaylistsViewModel vm)
                {
                    vm.Manager.SavePlaylists();
                }
            }

            _draggedPlaylist = null;
            _draggedPlaylistLVI = null;
        }

        private Point _dragTrackStartPoint;
        private Point _mouseStartPosInTrackItem;
        private bool _isDraggingTrack = false;
        private FileSystemItem? _draggedTrack = null;
        private ListViewItem? _draggedTrackLVI = null;
        private ListView? _activeTrackListView = null;

        private void TracksList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _activeTrackListView = sender as ListView;       

            _dragTrackStartPoint = e.GetPosition(null);
            _draggedTrackLVI = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);

            if (_draggedTrackLVI != null)
            {
                _draggedTrack = _draggedTrackLVI.DataContext as FileSystemItem;
                _mouseStartPosInTrackItem = e.GetPosition(_draggedTrackLVI);
            }
        }

        private void TracksList_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                if (_isDraggingTrack) FinishTrackDrag();
                return;
            }

            if (_draggedTrack == null || _draggedTrackLVI == null || _activeTrackListView == null) return;

            Point mousePos = e.GetPosition(null);
            Vector diff = _dragTrackStartPoint - mousePos;

            if (!_isDraggingTrack &&
                (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                 Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
            {
                _isDraggingTrack = true;
                _activeTrackListView.CaptureMouse();    

                _draggedTrackLVI.Opacity = 0.3;

                _adornerLayer = AdornerLayer.GetAdornerLayer(_activeTrackListView);    
                if (_adornerLayer != null)
                {
                    _dragAdorner = new DragAdorner(_activeTrackListView, _draggedTrackLVI, 0.85);
                    _adornerLayer.Add(_dragAdorner);
                }
            }

            if (_isDraggingTrack)
            {
                Point listPos = e.GetPosition(_activeTrackListView);    
                _dragAdorner?.UpdatePosition(listPos.X - _mouseStartPosInTrackItem.X, listPos.Y - _mouseStartPosInTrackItem.Y);

                var hitResult = VisualTreeHelper.HitTest(_activeTrackListView, listPos);
                var hoveredItem = FindAncestor<ListViewItem>(hitResult?.VisualHit);

                if (hoveredItem != null && hoveredItem != _draggedTrackLVI && hoveredItem.DataContext is FileSystemItem hoveredTrack)
                {
                    if (DataContext is PlaylistsViewModel vm && vm.SelectedPlaylist != null)
                    {
                        int oldIdx = vm.SelectedPlaylist.Items.IndexOf(_draggedTrack);
                        int newIdx = vm.SelectedPlaylist.Items.IndexOf(hoveredTrack);

                        if (oldIdx != -1 && newIdx != -1)
                        {
                            Point posInHovered = e.GetPosition(hoveredItem);
                            double itemHalfHeight = hoveredItem.ActualHeight / 2;
                            bool isDraggingDown = newIdx > oldIdx;

                            if ((isDraggingDown && posInHovered.Y > itemHalfHeight) ||
                                (!isDraggingDown && posInHovered.Y < itemHalfHeight))
                            {
                                vm.SelectedPlaylist.Items.Move(oldIdx, newIdx);
                            }
                        }
                    }
                }
            }
        }

        private void TracksList_MouseUp(object sender, MouseButtonEventArgs e)
        {
            FinishTrackDrag();
        }

        private void TracksList_MouseLeave(object sender, MouseEventArgs e)
        {
        }

        private void FinishTrackDrag()
        {
            if (_isDraggingTrack)
            {
                _isDraggingTrack = false;

                if (_activeTrackListView != null)
                {
                    _activeTrackListView.ReleaseMouseCapture();    
                }

                CleanupAdorner();

                if (DataContext is PlaylistsViewModel vm)
                {
                    vm.Manager.SavePlaylists();
                }
            }

            _draggedTrack = null;
            _draggedTrackLVI = null;
            _activeTrackListView = null;   
        }

        private void TracksList_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

                if (DataContext is PlaylistsViewModel vm)
                {
                    vm.AddFiles(files);
                }
            }
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            if (current == null) return null;

            do
            {
                if (current is T ancestor) return ancestor;
                current = VisualTreeHelper.GetParent(current);
            }
            while (current != null);
            return null;
        }



        private void ListView_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var listView = sender as ListView;

            if (listView != null && listView.SelectedItems.Count > 1)
            {
                var bulkMenu = (ContextMenu)FindResource("BulkContextMenu");
                bulkMenu.PlacementTarget = listView;
                bulkMenu.IsOpen = true;
                e.Handled = true;
                return;
            }

            var element = e.OriginalSource as DependencyObject;
            while (element != null && !(element is GridViewColumnHeader))
                element = VisualTreeHelper.GetParent(element);

            if (element is GridViewColumnHeader header && header.Column != null)
            {
                BuildColumnMenu();
                _columnContextMenu!.PlacementTarget = header;
                _columnContextMenu.IsOpen = true;
                e.Handled = true;
            }
        }

        private void BulkEditTagsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var activeListView = PlaylistTracksLines.IsVisible ? PlaylistTracksLines : MinimalPlaylistTracks;
            var selectedFiles = activeListView.SelectedItems.Cast<FileSystemItem>().Select(f => f.FullPath).ToList();

            if (Window.GetWindow(this)?.DataContext is MainViewModel mainVm)
            {
                mainVm.BulkTrackInfo.Open(selectedFiles);
            }
        }

        private void BulkAddToPlaylistMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is Playlist playlist)
            {
                var activeListView = PlaylistTracksLines.IsVisible ? PlaylistTracksLines : MinimalPlaylistTracks;
                var selectedFiles = activeListView.SelectedItems.Cast<FileSystemItem>().ToList();

                if (DataContext is PlaylistsViewModel vm)
                {
                    vm.BulkAddToPlaylist(playlist, selectedFiles);
                }
            }
        }

        private void BulkRemoveTracksMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var activeListView = PlaylistTracksLines.IsVisible ? PlaylistTracksLines : MinimalPlaylistTracks;
            var selectedFiles = activeListView.SelectedItems.Cast<FileSystemItem>().ToList();

            if (DataContext is PlaylistsViewModel vm)
            {
                vm.BulkRemoveFromPlaylist(selectedFiles);
            }
        }

        private void BuildColumnMenu()
        {
            if (PlaylistTracksLines?.View is not GridView gridView) return;

            while (_columnContextMenu!.Items.Count > 2)
                _columnContextMenu.Items.RemoveAt(2);

            foreach (var column in gridView.Columns)
            {
                var rawHeaderText = column.Header?.ToString() ?? "Column";
                var cleanHeaderText = rawHeaderText.Replace(" ▲", "").Replace(" ▼", "");

                var menuItem = new MenuItem
                {
                    Header = cleanHeaderText,
                    IsCheckable = true,
                    StaysOpenOnClick = true, 
                    IsChecked = double.IsNaN(column.Width) || column.Width > 0
                };
                menuItem.Tag = column;
                menuItem.Checked += ColumnMenuItem_Checked;
                menuItem.Unchecked += ColumnMenuItem_Unchecked;
                _columnContextMenu.Items.Add(menuItem);
            }
        }

        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is GridViewColumnHeader header && header.Column != null)
            {
                string currentHeader = header.Column.Header.ToString() ?? "";

                if (_lastHeaderClicked != null && _lastHeaderClicked != header)
                {
                    _lastHeaderClicked.Column.Header = _originalHeaderString;
                }

                string cleanName = currentHeader.Replace(" ▲", "").Replace(" ▼", "");

                if (DataContext is PlaylistsViewModel vm)
                {
                    int sortState = vm.SortTracks(cleanName);

                    if (sortState == 1)
                    {
                        if (_lastHeaderClicked != header) _originalHeaderString = cleanName;
                        header.Column.Header = cleanName + " ▲";
                        _lastHeaderClicked = header;
                    }
                    else if (sortState == 2)
                    {
                        header.Column.Header = cleanName + " ▼";
                    }
                    else
                    {
                        header.Column.Header = cleanName;
                        _lastHeaderClicked = null;
                    }
                }
            }
        }

        private void ColumnMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is GridViewColumn column)
            {
                if (_originalColumnWidths.TryGetValue(column, out double width))
                    column.Width = width;
                else
                    column.Width = double.NaN;
            }
        }

        private void ColumnMenuItem_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is GridViewColumn column)
            {
                if (double.IsNaN(column.Width) || column.Width > 0)
                {
                    _originalColumnWidths[column] = double.IsNaN(column.Width) ? column.ActualWidth : column.Width;
                }
                column.Width = 0;
            }
        }

        private void PlaylistsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CloseSearchBar();
            CloseMinimalSearchBar();

            var rightScroll = GetActiveTracksScrollViewer();  
            if (rightScroll != null && !string.IsNullOrEmpty(_currentPlaylistName))
            {
                _tracksScrollCache[_currentPlaylistName] = rightScroll.VerticalOffset;
            }

            if (PlaylistsListView.SelectedItem is Playlist selectedPlaylist)
            {
                _currentPlaylistName = selectedPlaylist.Name;
                Settings.Default.LastSelectedPlaylistName = _currentPlaylistName;

                if (_tracksScrollCache.TryGetValue(_currentPlaylistName, out double savedOffset))
                {
                    Dispatcher.BeginInvoke(new Action(async () => {
                        await System.Threading.Tasks.Task.Delay(50);
                        var scroll = GetActiveTracksScrollViewer();
                        if (scroll != null) scroll.ScrollToVerticalOffset(savedOffset);
                    }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
            }
        }

        private ScrollViewer? GetScrollViewer(DependencyObject depObj)
        {
            if (depObj is ScrollViewer scroll) return scroll;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        private bool _isSearchOpen = false;

        private void OpenSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isSearchOpen) return;
            _isSearchOpen = true;

            SearchOverlay.Visibility = Visibility.Visible;

            OpenSearchBtn.IsHitTestVisible = false;
            OpenSearchBtn.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150)));

            double targetWidth = NormalHeader.ActualWidth - 84;
            if (targetWidth < 100) targetWidth = 100;

            var widthAnim = new DoubleAnimation(32, targetWidth, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));

            SearchOverlay.BeginAnimation(WidthProperty, widthAnim);
            SearchOverlay.BeginAnimation(OpacityProperty, opacityAnim);

            PlaylistTitleText.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150)));

            SearchTrackBox.Focus();
        }

        private void CloseSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            CloseSearchBar();
        }

        private void SearchTrackBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchTrackBox.Text))
            {
                CloseSearchBar();
            }
        }

        private void CloseSearchBar()
        {
            if (!_isSearchOpen) return;
            _isSearchOpen = false;

            SearchTrackBox.Text = string.Empty;

            var widthAnim = new DoubleAnimation(SearchOverlay.ActualWidth, 32, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var opacityAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));

            widthAnim.Completed += (s, e) => { SearchOverlay.Visibility = Visibility.Collapsed; };

            SearchOverlay.BeginAnimation(WidthProperty, widthAnim);
            SearchOverlay.BeginAnimation(OpacityProperty, opacityAnim);

            PlaylistTitleText.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));

            OpenSearchBtn.IsHitTestVisible = true;
            OpenSearchBtn.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        }

        private void PopupBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void ClosePlaylistSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            SearchPlaylistBox.Text = string.Empty;
        }

        private bool _isMinimalSearchOpen = false;

        private void MinimalOpenSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isMinimalSearchOpen) return;
            _isMinimalSearchOpen = true;

            MinimalSearchOverlay.Visibility = Visibility.Visible;

            MinimalOpenSearchBtn.IsHitTestVisible = false;
            MinimalOpenSearchBtn.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150)));

            double targetWidth = MinimalHeaderGrid.ActualWidth - 84;
            if (targetWidth < 100) targetWidth = 100;

            var widthAnim = new DoubleAnimation(32, targetWidth, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));

            MinimalSearchOverlay.BeginAnimation(WidthProperty, widthAnim);
            MinimalSearchOverlay.BeginAnimation(OpacityProperty, opacityAnim);

            MinimalSearchTrackBox.Focus();
        }

        private void MinimalCloseSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            CloseMinimalSearchBar();
        }

        private void MinimalSearchTrackBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(MinimalSearchTrackBox.Text))
            {
                CloseMinimalSearchBar();
            }
        }

        private void CloseMinimalSearchBar()
        {
            if (!_isMinimalSearchOpen) return;
            _isMinimalSearchOpen = false;

            MinimalSearchTrackBox.Text = string.Empty;

            var widthAnim = new DoubleAnimation(MinimalSearchOverlay.ActualWidth, 32, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var opacityAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));

            widthAnim.Completed += (s, e) => { MinimalSearchOverlay.Visibility = Visibility.Collapsed; };

            MinimalSearchOverlay.BeginAnimation(WidthProperty, widthAnim);
            MinimalSearchOverlay.BeginAnimation(OpacityProperty, opacityAnim);

            MinimalOpenSearchBtn.IsHitTestVisible = true;
            MinimalOpenSearchBtn.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        }

        private void TrackInfoMenuItem_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.CommandParameter is string filePath)
            {
                if (System.Windows.Window.GetWindow(this)?.DataContext is JLS.ViewModels.MainViewModel mainVm)
                {
                    mainVm.ShowTrackInfoCommand.Execute(filePath);
                }
            }
        }

        private bool _isSidebarCollapsed = false;
        private double _savedSidebarWidth = 250;   

        private void ToggleSidebarBtn_Click(object sender, RoutedEventArgs e)
        {
            LeftColumn.BeginAnimation(ColumnDefinition.MaxWidthProperty, null);
            SidebarContainer.CacheMode = null;

            if (!_isSidebarCollapsed)
            {
                _savedSidebarWidth = LeftColumn.ActualWidth > 0 ? LeftColumn.ActualWidth : 250;
                LeftColumn.MinWidth = 0;

                SidebarContainer.Width = _savedSidebarWidth;
                SidebarContainer.HorizontalAlignment = HorizontalAlignment.Left;

                SidebarContainer.CacheMode = new BitmapCache();

                var widthAnim = new DoubleAnimation(_savedSidebarWidth, 0, TimeSpan.FromMilliseconds(250))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                widthAnim.Completed += (s, args) =>
                {
                    LeftColumn.BeginAnimation(ColumnDefinition.MaxWidthProperty, null);
                    LeftColumn.MaxWidth = 0;
                    LeftColumn.Width = new GridLength(0);

                    SidebarContainer.CacheMode = null;
                };

                LeftColumn.BeginAnimation(ColumnDefinition.MaxWidthProperty, widthAnim);
                PlaylistSplitter.Visibility = Visibility.Collapsed;

                var rotateAnim = new DoubleAnimation(0, 180, TimeSpan.FromMilliseconds(250)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                ToggleSidebarIcon.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);
                if (MinimalToggleSidebarIcon != null)
                    MinimalToggleSidebarIcon.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);

                _isSidebarCollapsed = true;
            }
            else
            {
                PlaylistSplitter.Visibility = Visibility.Visible;

                LeftColumn.Width = new GridLength(_savedSidebarWidth);

                SidebarContainer.Width = _savedSidebarWidth;
                SidebarContainer.HorizontalAlignment = HorizontalAlignment.Left;
                SidebarContainer.CacheMode = new BitmapCache();

                var widthAnim = new DoubleAnimation(0, _savedSidebarWidth, TimeSpan.FromMilliseconds(250))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                widthAnim.Completed += (s, args) =>
                {
                    LeftColumn.BeginAnimation(ColumnDefinition.MaxWidthProperty, null);
                    LeftColumn.ClearValue(ColumnDefinition.MaxWidthProperty);
                    LeftColumn.Width = new GridLength(_savedSidebarWidth);
                    LeftColumn.MinWidth = 200;

                    SidebarContainer.Width = double.NaN;
                    SidebarContainer.HorizontalAlignment = HorizontalAlignment.Stretch;

                    SidebarContainer.CacheMode = null;
                };

                LeftColumn.BeginAnimation(ColumnDefinition.MaxWidthProperty, widthAnim);

                var rotateAnim = new DoubleAnimation(180, 0, TimeSpan.FromMilliseconds(250)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                ToggleSidebarIcon.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);
                if (MinimalToggleSidebarIcon != null)
                    MinimalToggleSidebarIcon.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);

                _isSidebarCollapsed = false;
            }
        }

        private void PlaylistsView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is System.ComponentModel.INotifyPropertyChanged oldVm)
            {
                oldVm.PropertyChanged -= Vm_PropertyChanged;
            }

            if (e.NewValue is System.ComponentModel.INotifyPropertyChanged newVm)
            {
                newVm.PropertyChanged += Vm_PropertyChanged;
            }
        }

        private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (DataContext is not PlaylistsViewModel vm) return;

            if (e.PropertyName == nameof(PlaylistsViewModel.SearchTrackText))
            {
                if (string.IsNullOrEmpty(vm.SearchTrackText))
                {
                    Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        await System.Threading.Tasks.Task.Delay(50);   

                        foreach (var listView in FindVisualChildren<ListView>(this))
                        {
                            if (listView.IsVisible && listView.SelectedItem is FileSystemItem selectedTrack)
                            {
                                listView.ScrollIntoView(selectedTrack);
                            }
                        }
                    }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
            }

            if (e.PropertyName == nameof(PlaylistsViewModel.SearchPlaylistText))
            {
                if (string.IsNullOrEmpty(vm.SearchPlaylistText) && PlaylistsListView.SelectedItem != null)
                {
                    Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        await System.Threading.Tasks.Task.Delay(50);   
                        if (PlaylistsListView.IsVisible)
                        {
                            PlaylistsListView.ScrollIntoView(PlaylistsListView.SelectedItem);
                        }
                    }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject? child = VisualTreeHelper.GetChild(depObj, i);

                if (child != null && child is T t)
                {
                    yield return t;
                }

                foreach (T childOfChild in FindVisualChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }

    }

    public class DragAdorner : Adorner
    {
        private readonly Brush _visualBrush;
        private readonly Size _elementSize;
        private double _leftOffset;
        private double _topOffset;

        public DragAdorner(UIElement adornedElement, UIElement visualToDrag, double opacity = 0.75)
            : base(adornedElement)
        {
            IsHitTestVisible = false;      

            _elementSize = visualToDrag.RenderSize;
            _visualBrush = new VisualBrush(visualToDrag) { Opacity = opacity };
        }

        public void UpdatePosition(double left, double top)
        {
            _leftOffset = left;
            _topOffset = top;

            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            Rect rect = new Rect(_leftOffset, _topOffset, _elementSize.Width, _elementSize.Height);
            drawingContext.DrawRoundedRectangle(_visualBrush, null, rect, 6, 6);
        }
    }

}