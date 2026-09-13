using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using JLS.Models;
using JLS.ViewModels;
using JLS.Properties;
using System.Text.Json;

namespace JLS.Views
{
    public partial class FileBrowserView : UserControl
    {
        private bool _wasOpened = false;

        private readonly Dictionary<GridViewColumn, double> _originalColumnWidths = new();
        private ContextMenu? _columnContextMenu;

        private bool _isSidebarCollapsed = false;
        private double _savedSidebarWidth = 220;

        private readonly Dictionary<string, double> _classicFolderScrollCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _albumFolderScrollCache = new(StringComparer.OrdinalIgnoreCase);

        private string _lastRecordedPath = string.Empty;
        private bool _pendingScrollRestoration = false;

        public FileBrowserView()
        {
            InitializeComponent();
            _columnContextMenu = (ContextMenu)FindResource("ColumnHeaderContextMenu");

            this.IsVisibleChanged += FileBrowserView_IsVisibleChanged;

            this.DataContextChanged += FileBrowserView_DataContextChanged;

            try
            {
                if (!string.IsNullOrEmpty(Settings.Default.ClassicFolderScrollJson))
                {
                    var classicCache = JsonSerializer.Deserialize<Dictionary<string, double>>(Settings.Default.ClassicFolderScrollJson);
                    if (classicCache != null)
                    {
                        foreach (var kvp in classicCache) _classicFolderScrollCache[kvp.Key] = kvp.Value;
                    }
                }

                if (!string.IsNullOrEmpty(Settings.Default.AlbumFolderScrollJson))
                {
                    var albumCache = JsonSerializer.Deserialize<Dictionary<string, double>>(Settings.Default.AlbumFolderScrollJson);
                    if (albumCache != null)
                    {
                        foreach (var kvp in albumCache) _albumFolderScrollCache[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch {        }

            Application.Current.Exit += (s, e) => {
                try
                {
                    if (_wasOpened) FileBrowserView_Unloaded(null!, null!);
                }
                catch { }
            };
        }

        private async void FileBrowserView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
            {
                _wasOpened = true;

                RestoreColumnWidths();

                if (DataContext is FileBrowserViewModel vm)
                {
                    _lastRecordedPath = vm.CurrentFolderPath;
                    await vm.RestoreTreeState();
                }

                await System.Threading.Tasks.Task.Delay(50);

                _ = Dispatcher.BeginInvoke(new System.Action(async () =>
                {
                    var treeScroll = GetScrollViewer(FoldersTreeView);
                    if (treeScroll != null)
                    {
                        treeScroll.ScrollToVerticalOffset(Settings.Default.TreeScrollPos);
                    }

                    if (FilesListView.Visibility == Visibility.Visible)
                    {
                        FilesListView.ApplyTemplate();
                        var fileScroll = GetScrollViewer(FilesListView);
                        if (fileScroll != null)
                        {
                            for (int i = 0; i < 5; i++) { if (fileScroll.ScrollableHeight > 0) break; await System.Threading.Tasks.Task.Delay(50); }
                            fileScroll.ScrollToVerticalOffset(Settings.Default.FileScrollPos);
                        }
                    }

                    if (AlbumListView.Visibility == Visibility.Visible)
                    {
                        AlbumListView.ApplyTemplate();
                        var albumScroll = GetScrollViewer(AlbumListView);
                        if (albumScroll != null)
                        {
                            for (int i = 0; i < 5; i++) { if (albumScroll.ScrollableHeight > 0) break; await System.Threading.Tasks.Task.Delay(50); }
                            albumScroll.ScrollToVerticalOffset(Settings.Default.AlbumScrollPos);
                        }
                    }

                }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
            else
            {
                SaveBrowserState();
            }
        }

        private void FileBrowserView_Unloaded(object sender, RoutedEventArgs e)
        {
            SaveBrowserState();
        }

        private void SaveBrowserState()
        {
            var fileScroll = GetScrollViewer(FilesListView);
            if (fileScroll != null && FilesListView.Visibility == Visibility.Visible)
                Settings.Default.FileScrollPos = fileScroll.VerticalOffset;

            var albumScroll = GetScrollViewer(AlbumListView);
            if (albumScroll != null && AlbumListView.Visibility == Visibility.Visible)
                Settings.Default.AlbumScrollPos = albumScroll.VerticalOffset;

            var treeScroll = GetScrollViewer(FoldersTreeView);
            if (treeScroll != null)
                Settings.Default.TreeScrollPos = treeScroll.VerticalOffset;

            Settings.Default.ClassicFolderScrollJson = JsonSerializer.Serialize(
                _classicFolderScrollCache.Take(50).ToDictionary(p => p.Key, p => p.Value));

            Settings.Default.AlbumFolderScrollJson = JsonSerializer.Serialize(
                _albumFolderScrollCache.Take(50).ToDictionary(p => p.Key, p => p.Value));

            Settings.Default.BrowserSplitterWidth = _isSidebarCollapsed ? _savedSidebarWidth : LeftColumn.ActualWidth;
            Settings.Default.IsBrowserSidebarCollapsed = _isSidebarCollapsed;

            if (DataContext is FileBrowserViewModel vm)
                vm.SaveTreeState();

            if (FilesListView?.View is GridView gridView)
            {
                var widthsToSave = new Dictionary<string, double>();
                foreach (var column in gridView.Columns)
                {
                    string header = column.Header?.ToString() ?? "";
                    string cleanName = header.Replace(" ▲", "").Replace(" ▼", "");
                    widthsToSave[cleanName] = double.IsNaN(column.Width) ? column.ActualWidth : column.Width;
                }
                Settings.Default.BrowserColumns = JsonSerializer.Serialize(widthsToSave);
            }

            Settings.Default.Save();
        }


        private void RestoreColumnWidths()
        {
            double savedWidth = Settings.Default.BrowserSplitterWidth;
            if (savedWidth < 150) savedWidth = 220;     

            _savedSidebarWidth = savedWidth;

            if (Settings.Default.IsBrowserSidebarCollapsed)
            {
                _isSidebarCollapsed = true;
                LeftColumn.MinWidth = 0;
                LeftColumn.MaxWidth = 0;
                LeftColumn.Width = new GridLength(0);   
                BrowserSplitter.Visibility = Visibility.Collapsed;

                if (ToggleSidebarIcon?.RenderTransform is RotateTransform rotate) rotate.Angle = 180;
                if (AlbumToggleSidebarIcon?.RenderTransform is RotateTransform albumRotate) albumRotate.Angle = 180;
            }
            else
            {
                _isSidebarCollapsed = false;
                LeftColumn.ClearValue(ColumnDefinition.MaxWidthProperty);
                LeftColumn.MinWidth = 150;
                LeftColumn.Width = new GridLength(_savedSidebarWidth);
                BrowserSplitter.Visibility = Visibility.Visible;

                if (ToggleSidebarIcon?.RenderTransform is RotateTransform rotate) rotate.Angle = 0;
                if (AlbumToggleSidebarIcon?.RenderTransform is RotateTransform albumRotate) albumRotate.Angle = 0;
            }

            if (FilesListView?.View is GridView gridView && !string.IsNullOrEmpty(Settings.Default.BrowserColumns))
            {
                try
                {
                    var savedWidths = JsonSerializer.Deserialize<Dictionary<string, double>>(Settings.Default.BrowserColumns);
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
        }

        
        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewNode node)
            {
                if (DataContext is FileBrowserViewModel vm)
                {
                    vm.SelectedNode = node;
                }
            }
        }

        private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem item && item.DataContext is TreeViewNode node)
            {
                if (DataContext is FileBrowserViewModel vm)
                {
                    vm.ExpandNodeCommand.Execute(node);
                }
                e.Handled = true;    
            }
        }

        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
{
    if (DataContext is FileBrowserViewModel vm)
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
                    vm.FileDoubleClickCommand.Execute(fileItem);
                }
            }
        }
        
        
    }
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

        private void BulkEditTagsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var activeListView = FilesListView.IsVisible ? FilesListView : AlbumListView;

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
                var activeListView = FilesListView.IsVisible ? FilesListView : AlbumListView;
                var selectedFiles = activeListView.SelectedItems.Cast<FileSystemItem>().ToList();

                if (DataContext is FileBrowserViewModel vm)
                {
                    vm.BulkAddToPlaylist(playlist, selectedFiles);
                }
            }
        }

        private void BuildColumnMenu()
        {
            if (FilesListView?.View is not GridView gridView) return;

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

        private GridViewColumnHeader? _lastHeaderClicked = null;
        private string _originalHeaderString = string.Empty;

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

                if (DataContext is FileBrowserViewModel vm)
                {
                    int sortState = vm.SortFiles(cleanName);            

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

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.Name == "SearchFolderBox")
            {
                AnimateClearButton(ClearFolderBtn, textBox.Text.Length > 0);
            }
        }

        private void ClearSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Name == "ClearFolderBtn")
            {
                SearchFolderBox.Text = string.Empty;
            }
        }

        private void AnimateClearButton(Button btn, bool show)
        {
            if (show && btn.Visibility == Visibility.Collapsed)
            {
                btn.Visibility = Visibility.Visible;
                var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
                btn.BeginAnimation(UIElement.OpacityProperty, anim);
            }
            else if (!show && btn.Visibility == Visibility.Visible)
            {
                var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
                anim.Completed += (s, e) => {
                    if (btn.Opacity == 0) btn.Visibility = Visibility.Collapsed;
                };
                btn.BeginAnimation(UIElement.OpacityProperty, anim);
            }
        }

        private bool _isFileSearchOpen = false;

        private void OpenSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isFileSearchOpen) return;
            _isFileSearchOpen = true;

            SearchOverlay.Visibility = Visibility.Visible;

            double targetWidth = HideableSearchHeader.ActualWidth;
            if (targetWidth < 100) targetWidth = 100;

            var widthAnim = new DoubleAnimation(32, targetWidth, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));

            SearchOverlay.BeginAnimation(WidthProperty, widthAnim);
            SearchOverlay.BeginAnimation(OpacityProperty, opacityAnim);

            HideableSearchHeader.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150)));

            SearchFileBox.Focus();
        }

        private void CloseSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            CloseSearchBar();
        }

        private void SearchFileBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchFileBox.Text))
            {
                CloseSearchBar();
            }
        }

        private void CloseSearchBar()
        {
            if (!_isFileSearchOpen) return;
            _isFileSearchOpen = false;

            SearchFileBox.Text = string.Empty;

            var widthAnim = new DoubleAnimation(SearchOverlay.ActualWidth, 32, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var opacityAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));

            widthAnim.Completed += (s, e) => { SearchOverlay.Visibility = Visibility.Collapsed; };

            SearchOverlay.BeginAnimation(WidthProperty, widthAnim);
            SearchOverlay.BeginAnimation(OpacityProperty, opacityAnim);

            HideableSearchHeader.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        }

        private void TrackInfoMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is string filePath)
            {
                if (Window.GetWindow(this)?.DataContext is MainViewModel mainVm)
                {
                    mainVm.ShowTrackInfoCommand.Execute(filePath);
                }
            }
        }

        private void FileBrowserView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
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
            if (DataContext is not FileBrowserViewModel vm) return;

            if (e.PropertyName == nameof(FileBrowserViewModel.CurrentFolderPath))
            {
                if (!string.IsNullOrEmpty(_lastRecordedPath))
                {
                    var oldClassicScroll = GetScrollViewer(FilesListView);
                    if (oldClassicScroll != null && FilesListView.IsVisible)
                    {
                        _classicFolderScrollCache[_lastRecordedPath] = oldClassicScroll.VerticalOffset;
                    }

                    var oldAlbumScroll = GetScrollViewer(AlbumListView);
                    if (oldAlbumScroll != null && AlbumListView.IsVisible)
                    {
                        _albumFolderScrollCache[_lastRecordedPath] = oldAlbumScroll.VerticalOffset;
                    }
                }

                _lastRecordedPath = vm.CurrentFolderPath;
                _pendingScrollRestoration = true;
            }

            if (e.PropertyName == nameof(FileBrowserViewModel.FolderStats) && _pendingScrollRestoration)
            {
                _pendingScrollRestoration = false;   
                string currentPath = vm.CurrentFolderPath;

                Dispatcher.BeginInvoke(new System.Action(async () =>
                {
                    if (FilesListView.IsVisible)
                    {
                        var classicScroll = GetScrollViewer(FilesListView);
                        if (classicScroll != null)
                        {
                            for (int i = 0; i < 5; i++) { if (classicScroll.ScrollableHeight > 0 || vm.Files.Count == 0) break; await System.Threading.Tasks.Task.Delay(20); }

                            if (_classicFolderScrollCache.TryGetValue(currentPath, out double savedOffset))
                                classicScroll.ScrollToVerticalOffset(savedOffset);    
                            else
                                classicScroll.ScrollToTop();      
                        }
                    }

                    if (AlbumListView.IsVisible)
                    {
                        var albumScroll = GetScrollViewer(AlbumListView);
                        if (albumScroll != null)
                        {
                            for (int i = 0; i < 5; i++) { if (albumScroll.ScrollableHeight > 0 || vm.Files.Count == 0) break; await System.Threading.Tasks.Task.Delay(20); }

                            if (_albumFolderScrollCache.TryGetValue(currentPath, out double savedOffset))
                                albumScroll.ScrollToVerticalOffset(savedOffset);    
                            else
                                albumScroll.ScrollToTop();      
                        }
                    }

                }), System.Windows.Threading.DispatcherPriority.Background);
            }

            if (e.PropertyName == nameof(FileBrowserViewModel.SearchFileText))
            {
                if (string.IsNullOrEmpty(vm.SearchFileText) && vm.SelectedFile != null)
                {
                    Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        await System.Threading.Tasks.Task.Delay(50);

                        if (FilesListView.IsVisible)
                        {
                            FilesListView.ScrollIntoView(vm.SelectedFile);
                        }
                        if (AlbumListView.IsVisible)
                        {
                            AlbumListView.ScrollIntoView(vm.SelectedFile);
                        }
                    }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
            }

            if (e.PropertyName == nameof(FileBrowserViewModel.SearchFolderText))
            {
                if (string.IsNullOrEmpty(vm.SearchFolderText) && vm.SelectedNode != null)
                {
                    Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        await System.Threading.Tasks.Task.Delay(50);
                        BringSelectedTreeViewItemIntoView(FoldersTreeView, vm.SelectedNode);
                    }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
            }
        }

        private void BringSelectedTreeViewItemIntoView(ItemsControl parent, TreeViewNode targetNode)
        {
            if (parent == null || targetNode == null) return;

            foreach (var item in parent.Items)
            {
                var treeViewItem = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (treeViewItem == null) continue;

                if (item == targetNode)
                {
                    treeViewItem.BringIntoView();
                    return;
                }

                if (treeViewItem.IsExpanded)
                {
                    BringSelectedTreeViewItemIntoView(treeViewItem, targetNode);
                }
            }
        }

        private void TreeViewItem_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            if (Mouse.RightButton == MouseButtonState.Pressed)
            {
                e.Handled = true;
            }
        }

        private void ToggleSidebarBtn_Click(object sender, RoutedEventArgs e)
        {
            LeftColumn.BeginAnimation(ColumnDefinition.MaxWidthProperty, null);
            SidebarContainer.CacheMode = null;
            if (!_isSidebarCollapsed)
            {
                _savedSidebarWidth = LeftColumn.ActualWidth > 0 ? LeftColumn.ActualWidth : 220;
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
                BrowserSplitter.Visibility = Visibility.Collapsed;

                var rotateAnim = new DoubleAnimation(0, 180, TimeSpan.FromMilliseconds(250)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                ToggleSidebarIcon.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);
                if (AlbumToggleSidebarIcon != null)
                    AlbumToggleSidebarIcon.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);

                _isSidebarCollapsed = true;
            }
            else
            {
                BrowserSplitter.Visibility = Visibility.Visible;

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
                    LeftColumn.MinWidth = 150;

                    SidebarContainer.Width = double.NaN;
                    SidebarContainer.HorizontalAlignment = HorizontalAlignment.Stretch;

                    SidebarContainer.CacheMode = null;
                };

                LeftColumn.BeginAnimation(ColumnDefinition.MaxWidthProperty, widthAnim);

                var rotateAnim = new DoubleAnimation(180, 0, TimeSpan.FromMilliseconds(250)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                ToggleSidebarIcon.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);
                if (AlbumToggleSidebarIcon != null)
                    AlbumToggleSidebarIcon.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);

                _isSidebarCollapsed = false;
            }
        }

        private bool _isAlbumSearchOpen = false;

        private void OpenAlbumSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isAlbumSearchOpen) return;
            _isAlbumSearchOpen = true;

            AlbumSearchOverlay.Visibility = Visibility.Visible;

            OpenAlbumSearchBtn.IsHitTestVisible = false;
            OpenAlbumSearchBtn.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150)));

            var relativePoint = OpenAlbumSearchBtn.TranslatePoint(new Point(0, 0), BrowserAlbumHeaderGrid);
            double targetWidth = BrowserAlbumHeaderGrid.ActualWidth - relativePoint.X;

            if (targetWidth < 100) targetWidth = 100;      

            var widthAnim = new DoubleAnimation(32, targetWidth, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));

            AlbumSearchOverlay.BeginAnimation(WidthProperty, widthAnim);
            AlbumSearchOverlay.BeginAnimation(OpacityProperty, opacityAnim);

            AlbumSearchBox.Focus();
        }

        private void CloseAlbumSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            CloseAlbumSearchBar();
        }

        private void AlbumSearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(AlbumSearchBox.Text))
            {
                CloseAlbumSearchBar();
            }
        }

        private void CloseAlbumSearchBar()
        {
            if (!_isAlbumSearchOpen) return;
            _isAlbumSearchOpen = false;

            AlbumSearchBox.Text = string.Empty;

            var widthAnim = new DoubleAnimation(AlbumSearchOverlay.ActualWidth, 32, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var opacityAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));

            widthAnim.Completed += (s, e) => { AlbumSearchOverlay.Visibility = Visibility.Collapsed; };

            AlbumSearchOverlay.BeginAnimation(WidthProperty, widthAnim);
            AlbumSearchOverlay.BeginAnimation(OpacityProperty, opacityAnim);

            OpenAlbumSearchBtn.IsHitTestVisible = true;
            OpenAlbumSearchBtn.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        }

    }
}