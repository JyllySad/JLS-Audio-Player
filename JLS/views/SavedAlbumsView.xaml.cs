using JLS.Models;
using JLS.Properties;    
using JLS.ViewModels;
using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace JLS.Views
{
    public partial class SavedAlbumsView : UserControl
    {
        private bool _wasOpened = false;

        private Point _dragStartPoint;
        private Point _mouseStartPosInItem;
        private bool _isDraggingAlbum = false;
        private SavedAlbum? _draggedAlbum = null;
        private ListBoxItem? _draggedAlbumLBI = null;

        private bool _isSidebarCollapsed = false;
        private double _savedSidebarWidth = 300;
        private bool _isTrackSearchOpen = false;

        private System.Windows.Documents.AdornerLayer? _adornerLayer;
        private DragAdorner? _dragAdorner;

        private void CleanupAdorner()
        {
            if (_dragAdorner != null && _adornerLayer != null)
            {
                _adornerLayer.Remove(_dragAdorner);
                _dragAdorner = null;
                _adornerLayer = null;
            }
            if (_draggedAlbumLBI != null) _draggedAlbumLBI.Opacity = 1.0;
        }

        private static readonly Dictionary<string, double> _tracksScrollCache = new();

        public SavedAlbumsView()
        {
            InitializeComponent();
            this.IsVisibleChanged += SavedAlbumsView_IsVisibleChanged;
            this.DataContextChanged += SavedAlbumsView_DataContextChanged;

            try
            {
                string json = Settings.Default.SavedAlbumsTracksScrollJson;
                if (!string.IsNullOrEmpty(json))
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, double>>(json);
                    if (dict != null)
                    {
                        foreach (var kvp in dict) _tracksScrollCache[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch {      }
            Application.Current.Exit += OnApplicationExit;
        }

        private void OnApplicationExit(object sender, ExitEventArgs e)
        {
            try
            {
                if (_wasOpened) SavedAlbumsView_Unloaded(null!, null!);
            }
            catch { }
        }

        private void SavedAlbumsView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
            {
                _wasOpened = true;   
                SavedAlbumsView_Loaded(this, new RoutedEventArgs());
            }
            else
            {
                if (_wasOpened) SavedAlbumsView_Unloaded(this, new RoutedEventArgs());
            }
        }

        private void SavedAlbumsView_Loaded(object sender, RoutedEventArgs e)
        {
            double savedWidth = Settings.Default.AlbumSplitterWidth;
            if (savedWidth < 200) savedWidth = 300;
            _savedSidebarWidth = savedWidth;

            if (Settings.Default.IsAlbumSidebarCollapsed)
            {
                _isSidebarCollapsed = true;
                LeftColumn.MinWidth = 0;
                LeftColumn.MaxWidth = 0;
                LeftColumn.Width = new GridLength(0);
                AlbumSplitter.Visibility = Visibility.Collapsed;
                if (ToggleSidebarIcon.RenderTransform is RotateTransform rotate) rotate.Angle = 180;
            }
            else
            {
                _isSidebarCollapsed = false;
                LeftColumn.ClearValue(ColumnDefinition.MaxWidthProperty);
                LeftColumn.MinWidth = 200;
                LeftColumn.Width = new GridLength(_savedSidebarWidth);
                AlbumSplitter.Visibility = Visibility.Visible;
                if (ToggleSidebarIcon.RenderTransform is RotateTransform rotate) rotate.Angle = 0;
            }

            Dispatcher.BeginInvoke(new Action(async () => {
                try
                {
                    if (DataContext is SavedAlbumsViewModel vm)
                    {
                        var existingTitles = vm.Albums.Select(a => a.Title).ToHashSet();
                        var keysToRemove = _tracksScrollCache.Keys.Where(k => !existingTitles.Contains(k)).ToList();
                        foreach (var k in keysToRemove) _tracksScrollCache.Remove(k);
                        if (vm.SelectedAlbum == null && !string.IsNullOrEmpty(Settings.Default.LastSelectedAlbumTitle))
                        {
                            var savedAlbum = vm.Albums.FirstOrDefault(a => a.Title == Settings.Default.LastSelectedAlbumTitle);
                            if (savedAlbum != null) vm.SelectedAlbum = savedAlbum;
                        }

                        var leftScroll = GetScrollViewer(AlbumsListBox);
                        if (leftScroll != null)
                        {
                            for (int i = 0; i < 10; i++)
                            {
                                if (leftScroll.ScrollableHeight > 0 || vm.Albums.Count == 0) break;
                                await System.Threading.Tasks.Task.Delay(20);
                            }
                            leftScroll.ScrollToVerticalOffset(Settings.Default.AlbumsListScrollPos);
                        }
                    }
                }
                catch {           }
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private void SavedAlbumsView_Unloaded(object sender, RoutedEventArgs e)
        {
            Application.Current.Exit -= OnApplicationExit;

            if (DataContext is SavedAlbumsViewModel vm && vm.SelectedAlbum != null)
            {
                var rightScroll = GetScrollViewer(TracksListView);
                if (rightScroll != null && TracksListView.IsVisible)
                {
                    _tracksScrollCache[vm.SelectedAlbum.Title] = rightScroll.VerticalOffset;
                }
            }

            Settings.Default.AlbumSplitterWidth = _isSidebarCollapsed ? _savedSidebarWidth : LeftColumn.ActualWidth;
            Settings.Default.IsAlbumSidebarCollapsed = _isSidebarCollapsed;

            var leftScroll = GetScrollViewer(AlbumsListBox);
            if (leftScroll != null) Settings.Default.AlbumsListScrollPos = leftScroll.VerticalOffset;

            Settings.Default.SavedAlbumsTracksScrollJson = JsonSerializer.Serialize(
                _tracksScrollCache.Take(50).ToDictionary(p => p.Key, p => p.Value));

            Settings.Default.Save();
        }

        private void AlbumsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CloseTrackSearchBar();
            if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is SavedAlbum oldAlbum)
            {
                var scroll = GetScrollViewer(TracksListView);
                if (scroll != null && TracksListView.IsVisible)
                {
                    _tracksScrollCache[oldAlbum.Title] = scroll.VerticalOffset;
                }
            }

            if (e.AddedItems.Count > 0 && e.AddedItems[0] is SavedAlbum newAlbum)
            {
                Settings.Default.LastSelectedAlbumTitle = newAlbum.Title;
                Settings.Default.Save();

                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        var scroll = GetScrollViewer(TracksListView);
                        if (scroll != null)
                        {
                            for (int i = 0; i < 5; i++) { if (scroll.ScrollableHeight > 0 || newAlbum.Items.Count == 0) break; await System.Threading.Tasks.Task.Delay(20); }

                            if (_tracksScrollCache.TryGetValue(newAlbum.Title, out double savedOffset))
                                scroll.ScrollToVerticalOffset(savedOffset);
                            else
                                scroll.ScrollToTop();
                        }
                    }
                    catch {         }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void ThreeDotsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void AlbumsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _draggedAlbumLBI = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);

            if (_draggedAlbumLBI != null)
            {
                _draggedAlbum = _draggedAlbumLBI.DataContext as SavedAlbum;
                _mouseStartPosInItem = e.GetPosition(_draggedAlbumLBI);
            }
        }

        private void AlbumsList_MouseMove(object sender, MouseEventArgs e)
        {
            if (DataContext is SavedAlbumsViewModel searchVm && !string.IsNullOrWhiteSpace(searchVm.SearchText)) return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                if (_isDraggingAlbum) FinishDrag();
                return;
            }

            if (_draggedAlbum == null || _draggedAlbumLBI == null) return;

            Point mousePos = e.GetPosition(null);
            Vector diff = _dragStartPoint - mousePos;

            if (!_isDraggingAlbum &&
                (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                 Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
            {
                _isDraggingAlbum = true;
                AlbumsListBox.CaptureMouse();          

                _draggedAlbumLBI.Opacity = 0.3;    

                _adornerLayer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(AlbumsListBox);
                if (_adornerLayer != null)
                {
                    _dragAdorner = new DragAdorner(AlbumsListBox, _draggedAlbumLBI, 0.85);
                    _adornerLayer.Add(_dragAdorner);
                }
            }

            if (_isDraggingAlbum)
            {
                Point listPos = e.GetPosition(AlbumsListBox);
                _dragAdorner?.UpdatePosition(listPos.X - _mouseStartPosInItem.X, listPos.Y - _mouseStartPosInItem.Y);

                var hitResult = VisualTreeHelper.HitTest(AlbumsListBox, listPos);
                var hoveredItem = FindAncestor<ListBoxItem>(hitResult?.VisualHit);

                if (hoveredItem != null && hoveredItem != _draggedAlbumLBI && hoveredItem.DataContext is SavedAlbum hoveredAlbum)
                {
                    if (DataContext is SavedAlbumsViewModel vm)
                    {
                        int oldIdx = vm.Albums.IndexOf(_draggedAlbum);
                        int newIdx = vm.Albums.IndexOf(hoveredAlbum);

                        if (oldIdx != -1 && newIdx != -1)
                        {
                            Point posInHovered = e.GetPosition(hoveredItem);
                            double itemHalfHeight = hoveredItem.ActualHeight / 2;
                            bool isDraggingDown = newIdx > oldIdx;

                            if ((isDraggingDown && posInHovered.Y > itemHalfHeight) ||
                                (!isDraggingDown && posInHovered.Y < itemHalfHeight))
                            {
                                vm.Albums.Move(oldIdx, newIdx);
                            }
                        }
                    }
                }
            }
        }

        private void AlbumsList_MouseUp(object sender, MouseButtonEventArgs e)
        {
            FinishDrag();
        }

        private void AlbumsList_MouseLeave(object sender, MouseEventArgs e)
        {
        }

        private void FinishDrag()
        {
            if (_isDraggingAlbum)
            {
                _isDraggingAlbum = false;
                AlbumsListBox.ReleaseMouseCapture();

                CleanupAdorner();   

                if (DataContext is SavedAlbumsViewModel vm)
                {
                    vm.SaveAlbumsOrder();
                }
            }

            _draggedAlbum = null;
            _draggedAlbumLBI = null;
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            if (current == null) return null;

            do
            {
                if (current is T ancestor) return ancestor;

                if (current is FrameworkContentElement fce)
                {
                    current = fce.Parent;
                }
                else
                {
                    current = VisualTreeHelper.GetParent(current);
                }
            }
            while (current != null);

            return null;
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
            if (sender is TextBox textBox && ClearSearchBtn != null)
            {
                AnimateClearButton(ClearSearchBtn, textBox.Text.Length > 0);
            }
        }

        private void ClearSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            SearchAlbumBox.Text = string.Empty;
        }

        private void AnimateClearButton(Button btn, bool show)
        {
            if (show && btn.Visibility == Visibility.Collapsed)
            {
                btn.Visibility = Visibility.Visible;
                var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
                btn.BeginAnimation(UIElement.OpacityProperty, anim);
            }
            else if (!show && btn.Visibility == Visibility.Visible)
            {
                var anim = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
                anim.Completed += (s, e) => {
                    if (btn.Opacity == 0) btn.Visibility = Visibility.Collapsed;
                };
                btn.BeginAnimation(UIElement.OpacityProperty, anim);
            }
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

        private void ToggleSidebarBtn_Click(object sender, RoutedEventArgs e)
        {
            LeftColumn.BeginAnimation(ColumnDefinition.MaxWidthProperty, null);
            SidebarContainer.CacheMode = null;

            if (!_isSidebarCollapsed)
            {
                _savedSidebarWidth = LeftColumn.ActualWidth > 0 ? LeftColumn.ActualWidth : 300;
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
                AlbumSplitter.Visibility = Visibility.Collapsed;

                var rotateAnim = new DoubleAnimation(0, 180, TimeSpan.FromMilliseconds(250))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                ToggleSidebarIcon.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);

                _isSidebarCollapsed = true;
            }
            else
            {
                AlbumSplitter.Visibility = Visibility.Visible;

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

                var rotateAnim = new DoubleAnimation(180, 0, TimeSpan.FromMilliseconds(250))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                ToggleSidebarIcon.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);

                _isSidebarCollapsed = false;
            }
        }

        private void OpenTrackSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isTrackSearchOpen) return;
            _isTrackSearchOpen = true;

            TrackSearchOverlay.Visibility = Visibility.Visible;

            OpenTrackSearchBtn.IsHitTestVisible = false;
            OpenTrackSearchBtn.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150)));

            var relativePoint = OpenTrackSearchBtn.TranslatePoint(new Point(0, 0), AlbumTracksHeaderGrid);
            double targetWidth = AlbumTracksHeaderGrid.ActualWidth - relativePoint.X;

            if (targetWidth < 100) targetWidth = 100;

            var widthAnim = new System.Windows.Media.Animation.DoubleAnimation(32, targetWidth, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            var opacityAnim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));

            TrackSearchOverlay.BeginAnimation(WidthProperty, widthAnim);
            TrackSearchOverlay.BeginAnimation(OpacityProperty, opacityAnim);

            TrackSearchBox.Focus();
        }

        private void CloseTrackSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            CloseTrackSearchBar();
        }

        private void TrackSearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TrackSearchBox.Text))
            {
                CloseTrackSearchBar();
            }
        }

        private void CloseTrackSearchBar()
        {
            if (!_isTrackSearchOpen) return;
            _isTrackSearchOpen = false;

            TrackSearchBox.Text = string.Empty;

            var widthAnim = new System.Windows.Media.Animation.DoubleAnimation(TrackSearchOverlay.ActualWidth, 32, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
            };
            var opacityAnim = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));

            widthAnim.Completed += (s, e) => { TrackSearchOverlay.Visibility = Visibility.Collapsed; };

            TrackSearchOverlay.BeginAnimation(WidthProperty, widthAnim);
            TrackSearchOverlay.BeginAnimation(OpacityProperty, opacityAnim);

            OpenTrackSearchBtn.IsHitTestVisible = true;
            OpenTrackSearchBtn.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        }

        private void SavedAlbumsView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
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
            if (DataContext is not SavedAlbumsViewModel vm) return;

            if (e.PropertyName == nameof(SavedAlbumsViewModel.SearchText))
            {
                if (string.IsNullOrEmpty(vm.SearchText) && AlbumsListBox.SelectedItem != null)
                {
                    Dispatcher.BeginInvoke(new Action(async () =>
                    {
                         
                        await System.Threading.Tasks.Task.Delay(200);

                        if (AlbumsListBox.IsVisible)
                        {
                            AlbumsListBox.ScrollIntoView(AlbumsListBox.SelectedItem);
                        }
                    }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
            }

            if (e.PropertyName == nameof(SavedAlbumsViewModel.SearchTrackText))
            {
                if (string.IsNullOrEmpty(vm.SearchTrackText) && TracksListView.SelectedItem != null)
                {
                    Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        await System.Threading.Tasks.Task.Delay(50);    
                        if (TracksListView.IsVisible)
                        {
                            TracksListView.ScrollIntoView(TracksListView.SelectedItem);
                        }
                    }), System.Windows.Threading.DispatcherPriority.ContextIdle);
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
            }
        }

        private void BulkEditTagsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = TracksListView.SelectedItems.Cast<FileSystemItem>().Select(f => f.FullPath).ToList();

            if (Window.GetWindow(this)?.DataContext is MainViewModel mainVm)
            {
                mainVm.BulkTrackInfo.Open(selectedFiles);
            }
        }

        private void BulkAddToPlaylistMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is Playlist playlist)
            {
                var selectedFiles = TracksListView.SelectedItems.Cast<FileSystemItem>().ToList();

                if (DataContext is SavedAlbumsViewModel vm)
                {
                    vm.BulkAddToPlaylist(playlist, selectedFiles);
                }
            }
        }

        private void RenameDialogBackground_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is IInputElement element)
            {
                Keyboard.Focus(element);
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

    }
}