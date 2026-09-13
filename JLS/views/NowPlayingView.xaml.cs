using JLS.Models;
using JLS.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace JLS.Views
{
    public partial class NowPlayingView : UserControl
    {
        private EventHandler? _currentScrollAnimator = null;
        private bool _isAutoScrollPaused = false;
        private bool _isLyricsOpenState = false;
        private bool _isQueueOpenState = false;
        private bool _justOpenedQueue = false;
        private bool _wasDragged = false;
        private Point _dragStartPoint;
        private System.Windows.Threading.DispatcherTimer _mouseIdleTimer;
        private bool _isCloseButtonVisible = true;
        private Point _lastMousePosition;
        private bool _isQueueAutoScrollPaused = false;

        public NowPlayingView()
        {
            InitializeComponent();

            _mouseIdleTimer = new System.Windows.Threading.DispatcherTimer();
            _mouseIdleTimer.Interval = TimeSpan.FromSeconds(2);
            _mouseIdleTimer.Tick += MouseIdleTimer_Tick;

            this.MouseMove += NowPlayingView_MouseMove;

            SyncedLyricsListBox.AddHandler(System.Windows.Controls.Primitives.ScrollBar.ScrollEvent, new System.Windows.Controls.Primitives.ScrollEventHandler(OnUserScroll));
            UnifiedQueueListBox.AddHandler(System.Windows.Controls.Primitives.ScrollBar.ScrollEvent, new System.Windows.Controls.Primitives.ScrollEventHandler(OnQueueUserScroll));

            this.DataContextChanged += (s, e) =>
            {
                if (e.OldValue is MainViewModel oldVm)
                {
                    oldVm.PropertyChanged -= OnVmPropertyChanged;
                    oldVm.ActiveLyricChanged -= OnActiveLyricChanged;
                }

                if (e.NewValue is MainViewModel newVm)
                {
                    newVm.PropertyChanged += OnVmPropertyChanged;
                    newVm.ActiveLyricChanged += OnActiveLyricChanged;
                }
            };
        }

        private void SyncedLyricsListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = GetScrollViewer(SyncedLyricsListBox);
            if (scrollViewer != null)
            {
                bool canScrollUp = scrollViewer.VerticalOffset > 0;
                bool canScrollDown = scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight;

                if ((e.Delta > 0 && canScrollUp) || (e.Delta < 0 && canScrollDown))
                {
                    PauseAutoScroll();
                }
            }
        }

        private void SyncedLyricsListBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                DependencyObject parent = source;
                while (parent != null)
                {
                    if (parent is System.Windows.Controls.Primitives.ScrollBar)
                    {
                        PauseAutoScroll();
                        break;
                    }
                    parent = VisualTreeHelper.GetParent(parent);
                }
            }
        }

        private void OnUserScroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e)
        {
            PauseAutoScroll();
        }

        private void PauseAutoScroll()
        {
            if (this.DataContext is MainViewModel vm && vm.IsSyncedLyricsMode)
            {
                if (!_isAutoScrollPaused)
                {
                    _isAutoScrollPaused = true;
                    ResumeAutoScrollBtn.Visibility = Visibility.Visible;
                }
            }
        }

        private void ResumeAutoScrollBtn_Click(object sender, RoutedEventArgs e)
        {
            _isAutoScrollPaused = false;
            ResumeAutoScrollBtn.Visibility = Visibility.Collapsed;

            if (this.DataContext is MainViewModel vm)
            {
                var activeLine = vm.SyncedLyrics.FirstOrDefault(l => l.IsActive);
                if (activeLine != null)
                {
                    ScrollToActiveLyric(activeLine);
                }
            }
        }

        private void UnifiedQueueListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = GetScrollViewer(UnifiedQueueListBox);
            if (scrollViewer != null)
            {
                bool canScrollUp = scrollViewer.VerticalOffset > 0;
                bool canScrollDown = scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight;

                if ((e.Delta > 0 && canScrollUp) || (e.Delta < 0 && canScrollDown))
                {
                    PauseQueueAutoScroll();
                }
            }
        }

        private void UnifiedQueueListBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                DependencyObject parent = source;
                while (parent != null)
                {
                    if (parent is System.Windows.Controls.Primitives.ScrollBar)
                    {
                        PauseQueueAutoScroll();
                        break;
                    }
                    parent = VisualTreeHelper.GetParent(parent);
                }
            }
        }

        private void OnQueueUserScroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e)
        {
            PauseQueueAutoScroll();
        }

        private void PauseQueueAutoScroll()
        {
            if (this.DataContext is MainViewModel vm && vm.IsQueueOpen)
            {
                if (!_isQueueAutoScrollPaused)
                {
                    _isQueueAutoScrollPaused = true;
                    ResumeQueueAutoScrollBtn.Visibility = Visibility.Visible;
                }
            }
        }

        private void ResumeQueueAutoScrollBtn_Click(object sender, RoutedEventArgs e)
        {
            _isQueueAutoScrollPaused = false;
            ResumeQueueAutoScrollBtn.Visibility = Visibility.Collapsed;

            ScrollToActiveItem(true);
        }

        private void OnActiveLyricChanged(object? sender, LyricLine activeLine)
        {
            ScrollToActiveLyric(activeLine);
        }

        private void ScrollToActiveLyric(LyricLine line)
        {
            if (SyncedLyricsListBox.Visibility != Visibility.Visible) return;

            if (_isAutoScrollPaused) return;

            Dispatcher.InvokeAsync(async () =>
            {
                var scrollViewer = GetScrollViewer(SyncedLyricsListBox);
                if (scrollViewer == null) return;

                await Task.Delay(50);
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

                var container = SyncedLyricsListBox.ItemContainerGenerator.ContainerFromItem(line) as FrameworkElement;

                if (container == null)
                {
                    SyncedLyricsListBox.ScrollIntoView(line);

                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

                    container = SyncedLyricsListBox.ItemContainerGenerator.ContainerFromItem(line) as FrameworkElement;
                }

                if (container != null && container.ActualHeight > 0)
                {
                    Point relativePoint = container.TransformToAncestor(scrollViewer).Transform(new Point(0, 0));
                    double elementCenterY = relativePoint.Y + (container.ActualHeight / 2);
                    double viewportCenterY = (scrollViewer.ActualHeight / 2) + 25;
                    double delta = elementCenterY - viewportCenterY;

                    if (Math.Abs(delta) < 2) return;

                    double targetOffset = scrollViewer.VerticalOffset + delta;

                    AnimateScroll(scrollViewer, targetOffset);
                }
            }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsCleanModeEnabled) || e.PropertyName == nameof(MainViewModel.IsNowPlayingVisible))
            {
                if (sender is MainViewModel vm)
                {
                    if (vm.IsNowPlayingVisible && vm.IsCleanModeEnabled)
                    {
                        _mouseIdleTimer.Start();
                    }
                    else
                    {
                        _mouseIdleTimer.Stop();
                        ShowCloseButton();
                    }
                }
            }

            if (e.PropertyName == nameof(MainViewModel.CurrentTrackName))
            {
                Dispatcher.Invoke(() =>
                {
                    _isAutoScrollPaused = false;
                    if (ResumeAutoScrollBtn != null)
                    {
                        ResumeAutoScrollBtn.Visibility = Visibility.Collapsed;
                    }

                    var scrollViewer = GetScrollViewer(SyncedLyricsListBox);
                    if (scrollViewer != null)
                    {
                        AnimateScroll(scrollViewer, 0);
                    }
                });
            }

            if (e.PropertyName == nameof(MainViewModel.IsLyricsOpen))
            {
                if (sender is MainViewModel vm)
                {
                    bool wasQueueOpen = _isQueueOpenState;
                    _isLyricsOpenState = vm.IsLyricsOpen;

                    if (_isLyricsOpenState)
                    {
                        LyricsPanelContainer.IsHitTestVisible = true;
                        Storyboard sb = (Storyboard)this.Resources[wasQueueOpen ? "PanelSlideInDelayed" : "PanelSlideInNormal"];
                        sb.Begin(LyricsPanelContainer);
                    }
                    else
                    {
                        LyricsPanelContainer.IsHitTestVisible = false;
                        Storyboard sb = (Storyboard)this.Resources["PanelDropOut"];
                        sb.Begin(LyricsPanelContainer);
                    }
                }
            }

            if (e.PropertyName == nameof(MainViewModel.IsQueueOpen))
            {
                if (sender is MainViewModel vm)
                {
                    bool wasLyricsOpen = _isLyricsOpenState;
                    _isQueueOpenState = vm.IsQueueOpen;

                    if (_isQueueOpenState)
                    {
                        QueuePanelContainer.IsHitTestVisible = true;
                        Storyboard sb = (Storyboard)this.Resources[wasLyricsOpen ? "PanelSlideInDelayed" : "PanelSlideInNormal"];
                        sb.Begin(QueuePanelContainer);

                        _isQueueAutoScrollPaused = false;
                        if (ResumeQueueAutoScrollBtn != null)
                        {
                            ResumeQueueAutoScrollBtn.Visibility = Visibility.Collapsed;
                        }

                        _justOpenedQueue = true;
                        ScrollToActiveItem(false);

                        Task.Delay(600).ContinueWith(_ =>
                        {
                            Dispatcher.Invoke(() => _justOpenedQueue = false);
                        });
                    }
                    else
                    {
                        QueuePanelContainer.IsHitTestVisible = false;
                        Storyboard sb = (Storyboard)this.Resources["PanelDropOut"];
                        sb.Begin(QueuePanelContainer);

                        _justOpenedQueue = false;
                    }
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.UnifiedQueue))
            {
                if (sender is MainViewModel vm && vm.IsQueueOpen)
                {
                    ScrollToActiveItem(true);
                }
            }
        }

        public void ScrollToActiveItem(bool animate = true)
        {
            if (_isQueueAutoScrollPaused) return;

            if (_justOpenedQueue)
            {
                animate = false;
            }

            Dispatcher.InvokeAsync(async () =>
            {
                if (this.DataContext is MainViewModel vm && UnifiedQueueListBox != null)
                {
                    var playingItem = vm.UnifiedQueue.FirstOrDefault(x => x.IsPlaying);
                    if (playingItem == null) return;

                    var scrollViewer = GetScrollViewer(UnifiedQueueListBox);
                    if (scrollViewer == null) return;

                    
                    if (_justOpenedQueue)
                    {
                        await Task.Delay(50);    
                    }

                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

                    double startOffsetBeforeJump = scrollViewer.VerticalOffset;
                    var container = UnifiedQueueListBox.ItemContainerGenerator.ContainerFromItem(playingItem) as FrameworkElement;

                    if (container == null)
                    {
                        UnifiedQueueListBox.ScrollIntoView(playingItem);
                        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                        container = UnifiedQueueListBox.ItemContainerGenerator.ContainerFromItem(playingItem) as FrameworkElement;
                    }

                    if (container != null && container.ActualHeight > 0)
                    {
                        Point relativePoint = container.TransformToAncestor(scrollViewer).Transform(new Point(0, 0));
                        double elementCenterY = relativePoint.Y + (container.ActualHeight / 2);
                        double viewportCenterY = (scrollViewer.ActualHeight / 2) + 25;
                        double delta = elementCenterY - viewportCenterY;

                        if (Math.Abs(delta) < 2) return;

                        double targetOffset = scrollViewer.VerticalOffset + delta;

                        if (animate)
                        {
                            AnimateScroll(scrollViewer, targetOffset, startOffsetBeforeJump);
                        }
                        else
                        {
                            scrollViewer.ScrollToVerticalOffset(targetOffset);
                        }
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private ScrollViewer? GetScrollViewer(DependencyObject? depObj)
        {
            if (depObj == null) return null;

            if (depObj is ScrollViewer scrollViewer) return scrollViewer;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                var result = GetScrollViewer(child);

                if (result != null) return result;
            }

            return null;
        }

        private void AnimateScroll(ScrollViewer scrollViewer, double targetOffset, double? customFrom = null)
        {
            targetOffset = Math.Max(0, Math.Min(targetOffset, scrollViewer.ScrollableHeight));

            if (_currentScrollAnimator != null)
            {
                CompositionTarget.Rendering -= _currentScrollAnimator;
                _currentScrollAnimator = null;
            }

            DateTime start = DateTime.Now;

            double from = customFrom ?? scrollViewer.VerticalOffset;

            _currentScrollAnimator = (s, e) =>
            {
                double t = (DateTime.Now - start).TotalMilliseconds / 700.0;

                if (t >= 1.0)
                {
                    scrollViewer.ScrollToVerticalOffset(targetOffset);

                    if (_currentScrollAnimator != null)
                    {
                        CompositionTarget.Rendering -= _currentScrollAnimator;
                        _currentScrollAnimator = null;
                    }
                    return;
                }

                double easedT = 1 - Math.Pow(1 - t, 4);
                scrollViewer.ScrollToVerticalOffset(from + (targetOffset - from) * easedT);
            };

            CompositionTarget.Rendering += _currentScrollAnimator;
        }

        private double _previousVolume = 0.5;
        private int _volumePopupToken = 0;

        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is JLS.ViewModels.MainViewModel vm)
            {
                if (vm.Volume > 0)
                {
                    _previousVolume = vm.Volume;
                    vm.Volume = 0;
                }
                else
                {
                    vm.Volume = _previousVolume > 0 ? _previousVolume : 0.1;
                }
            }
        }

        private void ShowVolumePopup()
        {
            if (this.DataContext is JLS.ViewModels.MainViewModel vm && !vm.IsNowPlayingVisible) return;

            if (VolumePopup != null)
            {
                VolumePopup.IsOpen = true;
            }
        }

        private void HideVolumePopup()
        {
            if (VolumePopup != null)
            {
                VolumePopup.IsOpen = false;
            }
        }

        private void VolumeSlider_MouseEnter(object sender, MouseEventArgs e)
        {
            ShowVolumePopup();
        }

        private void VolumeSlider_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isDraggingVolume)
            {
                HideVolumePopup();
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ShowVolumePopup();

            int currentToken = ++_volumePopupToken;

            Task.Delay(1600).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (currentToken == _volumePopupToken && OverlayVolumeSlider != null &&
                        !OverlayVolumeSlider.IsMouseOver && !CoverSpeakerIcon.IsMouseOver && !_isDraggingVolume)
                    {
                        HideVolumePopup();
                    }
                });
            });
        }

        private void Marquee_TargetUpdated(object sender, System.Windows.Data.DataTransferEventArgs e)
        {
            if (sender is TextBlock trimmedText && VisualTreeHelper.GetParent(trimmedText) is Grid container)
            {
                if (container.Children.Count > 1 &&
                    container.Children[1] is Canvas canvas &&
                    canvas.Children.Count > 0 &&
                    canvas.Children[0] is TextBlock fullText)
                {
                    if (fullText.RenderTransform is TranslateTransform transform)
                    {
                        transform.BeginAnimation(TranslateTransform.XProperty, null);
                        transform.X = 0;
                    }

                    trimmedText.Visibility = Visibility.Visible;
                    canvas.Visibility = Visibility.Hidden;
                }
            }
        }

        private void Marquee_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Grid container &&
                container.Children.Count > 1 &&
                container.Children[0] is TextBlock trimmedText &&
                container.Children[1] is Canvas canvas &&
                canvas.Children.Count > 0 &&
                canvas.Children[0] is TextBlock fullText)
            {
                if (fullText.RenderTransform is TranslateTransform transform)
                {
                    double textWidth = fullText.ActualWidth;
                    double containerWidth = container.ActualWidth;

                    if (textWidth > containerWidth + 1)
                    {
                        trimmedText.Visibility = Visibility.Hidden;
                        canvas.Visibility = Visibility.Visible;

                        double extraSpace = 50;
                        double scrollDistance = textWidth - containerWidth + extraSpace;

                        double pixelsPerSecond = 45;
                        TimeSpan duration = TimeSpan.FromSeconds(scrollDistance / pixelsPerSecond);

                        DoubleAnimation marqueeAnimation = new DoubleAnimation
                        {
                            To = -scrollDistance,
                            Duration = duration,
                            BeginTime = TimeSpan.FromSeconds(0.3),    
                            AutoReverse = true,
                            RepeatBehavior = RepeatBehavior.Forever
                        };

                        transform.BeginAnimation(TranslateTransform.XProperty, marqueeAnimation);
                    }
                }
            }
        }

        private async void Marquee_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Grid grid && grid.Children.Count >= 2)
            {
                var staticCtrl = grid.Children[0] as FrameworkElement;
                var canvas = grid.Children[1] as Canvas;

                if (canvas == null || canvas.Children.Count == 0) return;
                var movingCtrl = canvas.Children[0] as FrameworkElement;

                if (staticCtrl != null && movingCtrl != null)
                {
                    if (movingCtrl.RenderTransform is TranslateTransform tt && !tt.IsFrozen)
                    {
                        DoubleAnimation returnAnimation = new DoubleAnimation
                        {
                            To = 0,
                            Duration = TimeSpan.FromSeconds(0.4),
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        };

                        tt.BeginAnimation(TranslateTransform.XProperty, returnAnimation);

                        await Task.Delay(400);

                        if (!grid.IsMouseOver)
                        {
                            staticCtrl.Visibility = Visibility.Visible;
                            canvas.Visibility = Visibility.Hidden;
                        }
                    }
                }
            }
        }

        private void TimestampTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (this.DataContext is MainViewModel vm)
            {
                var sorted = vm.EditorSyncedLyrics.OrderBy(l => l.Timestamp).ToList();

                for (int i = 0; i < sorted.Count; i++)
                {
                    int oldIndex = vm.EditorSyncedLyrics.IndexOf(sorted[i]);
                    if (oldIndex != i)
                    {
                        vm.EditorSyncedLyrics.Move(oldIndex, i);
                    }
                }
            }
        }

        private void TimestampTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (sender is TextBox textBox)
                {
                    textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                }
            }
        }

        private void TrackSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider slider && DataContext is MainViewModel vm)
            {
                slider.CaptureMouse();
                _wasDragged = false;
                _dragStartPoint = e.GetPosition(slider);

                double clickPosition = CalculateSliderPosition(slider, e, vm.TotalDuration);
                vm.StartDragging(clickPosition);

                e.Handled = true;
            }
        }

        private void TrackSlider_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (sender is Slider slider && slider.IsMouseCaptured && DataContext is MainViewModel vm)
            {
                if (vm.IsDraggingTrack)
                {
                    Point currentPoint = e.GetPosition(slider);

                    if (!_wasDragged)
                    {
                        if (Math.Abs(currentPoint.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                            Math.Abs(currentPoint.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                        {
                            return;
                        }
                    }

                    _wasDragged = true;

                    double newPosition = CalculateSliderPosition(slider, e, vm.TotalDuration);
                    vm.UpdateDragPosition(newPosition);
                }
            }
        }

        private void TrackSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider slider && slider.IsMouseCaptured && DataContext is MainViewModel vm)
            {
                slider.ReleaseMouseCapture();

                double finalPosition = CalculateSliderPosition(slider, e, vm.TotalDuration);
                vm.IsDraggingTrack = false;

                vm.ApplySeek(finalPosition, _wasDragged);

                e.Handled = true;
            }
        }

        private double CalculateSliderPosition(Slider slider, MouseEventArgs e, double totalDuration)
        {
            if (slider.ActualWidth <= 0 || totalDuration <= 0) return 0;

            double mouseX = e.GetPosition(slider).X;

            if (slider.Name == "ClassicView")
            {
                double trackWidth = Math.Max(1, slider.ActualWidth - 14);
                double trackX = Math.Max(0, Math.Min(mouseX - 7, trackWidth));

                return (trackX / trackWidth) * totalDuration;
            }

            mouseX = Math.Max(0, Math.Min(mouseX, slider.ActualWidth));
            return (mouseX / slider.ActualWidth) * totalDuration;
        }

        private bool _isDraggingVolume = false;

        private void SetVolumeSmoothly(Slider slider, double newValue, int durationMs = 150)
        {
            double oldValue = slider.Value;

            if (Math.Abs(oldValue - newValue) < 0.001) return;

            slider.Value = newValue;

            DoubleAnimation anim = new DoubleAnimation(oldValue, newValue, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };

            slider.BeginAnimation(Slider.ValueProperty, anim);
        }

        private void VolumeSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider slider)
            {
                slider.BeginAnimation(Slider.ValueProperty, null);

                if (e.OriginalSource is FrameworkElement fe && fe.TemplatedParent is System.Windows.Controls.Primitives.Thumb)
                    return;

                if (slider.Template.FindName("PART_Track", slider) is System.Windows.Controls.Primitives.Track track)
                {
                    _isDraggingVolume = true;
                    slider.CaptureMouse();

                    double targetValue = track.ValueFromPoint(e.GetPosition(track));

                    SetVolumeSmoothly(slider, targetValue, 250);

                    e.Handled = true;
                }
            }
        }

        private void VolumeSlider_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingVolume && sender is Slider slider)
            {
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    _isDraggingVolume = false;
                    slider.ReleaseMouseCapture();
                    return;
                }

                if (slider.Template.FindName("PART_Track", slider) is System.Windows.Controls.Primitives.Track track)
                {
                    slider.BeginAnimation(Slider.ValueProperty, null);
                    slider.Value = track.ValueFromPoint(e.GetPosition(track));
                }
            }
        }

        private void VolumeSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingVolume && sender is Slider slider)
            {
                _isDraggingVolume = false;
                slider.ReleaseMouseCapture();
                e.Handled = true;

                if (!slider.IsMouseOver && !CoverSpeakerIcon.IsMouseOver)
                {
                    HideVolumePopup();
                }
            }
        }

        private void VolumeSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is Slider slider)
            {
                double step = 0.05;
                double targetValue = slider.Value;

                if (e.Delta > 0)
                    targetValue = Math.Min(slider.Maximum, slider.Value + step);
                else if (e.Delta < 0)
                    targetValue = Math.Max(slider.Minimum, slider.Value - step);

                SetVolumeSmoothly(slider, targetValue, 150);

                e.Handled = true;
            }
        }

        private void NowPlayingView_MouseMove(object sender, MouseEventArgs e)
        {
            Point currentPosition = e.GetPosition(this);

            if (Math.Abs(currentPosition.X - _lastMousePosition.X) < 1 &&
                Math.Abs(currentPosition.Y - _lastMousePosition.Y) < 1)
            {
                return;       
            }

            _lastMousePosition = currentPosition;

            if (this.DataContext is MainViewModel vm && vm.IsCleanModeEnabled && vm.IsNowPlayingVisible)
            {
                ShowCloseButton();
                _mouseIdleTimer.Stop();
                _mouseIdleTimer.Start();       
            }
            else
            {
                _mouseIdleTimer.Stop();
                ShowCloseButton();
            }
        }

        private void MouseIdleTimer_Tick(object? sender, EventArgs e)
        {
            _mouseIdleTimer.Stop();
            if (this.DataContext is MainViewModel vm && vm.IsCleanModeEnabled && vm.IsNowPlayingVisible)
            {
                HideCloseButton();
            }
        }

        private void ShowCloseButton()
        {
            if (_isCloseButtonVisible) return;
            _isCloseButtonVisible = true;
            
            this.Cursor = null;
            
            DoubleAnimation fade = new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.3));
            CloseNowPlayingBtn.BeginAnimation(UIElement.OpacityProperty, fade);
            CloseNowPlayingBtn.IsHitTestVisible = true;
        }

        private void HideCloseButton()
        {
            if (!_isCloseButtonVisible) return;
            _isCloseButtonVisible = false;

            this.Cursor = Cursors.None;

            DoubleAnimation fade = new DoubleAnimation(0.0, TimeSpan.FromSeconds(0.5));
            CloseNowPlayingBtn.BeginAnimation(UIElement.OpacityProperty, fade);
            CloseNowPlayingBtn.IsHitTestVisible = false;
        }

        private bool _isWaveformHoverVisible = false;
        private bool _isClassicHoverVisible = false;

        private void AnimateOpacity(UIElement element, double toOpacity, int durationMs = 150)
        {
            DoubleAnimation anim = new DoubleAnimation
            {
                To = toOpacity,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            element.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private string GetFormattedHoverTime(TimeSpan t, bool showTenths)
        {
            if (showTenths)
                return t.TotalHours >= 1 ? t.ToString(@"hh\:mm\:ss\.f") : t.ToString(@"mm\:ss\.f");
            else
                return t.TotalHours >= 1 ? t.ToString(@"hh\:mm\:ss") : t.ToString(@"mm\:ss");
        }

        private void WaveformArea_MouseMove(object sender, MouseEventArgs e)
        {
            if (DataContext is JLS.ViewModels.MainViewModel vm && vm.TotalDuration > 0)
            {
                double mouseX = e.GetPosition(WaveformView).X;
                double clampedX = Math.Max(0, Math.Min(mouseX, WaveformView.ActualWidth));

                TimeSpan t = TimeSpan.FromSeconds((clampedX / WaveformView.ActualWidth) * vm.TotalDuration);
                WaveformHoverText.Text = GetFormattedHoverTime(t, vm.IsShowTenthsEnabled);

                WaveformHoverTransform.X = clampedX;

                if (!_isWaveformHoverVisible)
                {
                    _isWaveformHoverVisible = true;
                    AnimateOpacity(WaveformHoverBubble, 1.0);
                }
            }
        }

        private void WaveformArea_MouseLeave(object sender, MouseEventArgs e)
        {
            _isWaveformHoverVisible = false;
            AnimateOpacity(WaveformHoverBubble, 0.0, 200);
        }

        private void ClassicArea_MouseMove(object sender, MouseEventArgs e)
        {
            if (DataContext is JLS.ViewModels.MainViewModel vm && vm.TotalDuration > 0)
            {
                double mouseX = e.GetPosition(ClassicView).X;
                double width = ClassicView.ActualWidth;
                double clampedX = Math.Max(0, Math.Min(mouseX, width));

                double trackWidth = Math.Max(1, width - 14);
                double trackX = Math.Max(0, Math.Min(clampedX - 7, trackWidth));

                TimeSpan t = TimeSpan.FromSeconds((trackX / trackWidth) * vm.TotalDuration);
                ClassicHoverText.Text = GetFormattedHoverTime(t, vm.IsShowTenthsEnabled);

                ClassicHoverTransform.X = clampedX;

                if (!_isClassicHoverVisible)
                {
                    _isClassicHoverVisible = true;
                    AnimateOpacity(ClassicHoverBubble, 1.0);
                }
            }
        }

        private void ClassicArea_MouseLeave(object sender, MouseEventArgs e)
        {
            _isClassicHoverVisible = false;
            AnimateOpacity(ClassicHoverBubble, 0.0, 200);
        }

        private void MoreOptionsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                DependencyObject? current = btn;
                FrameworkElement? elementWithMenu = null;

                while (current != null)
                {
                    if (current is FrameworkElement fe && fe.ContextMenu != null)
                    {
                        elementWithMenu = fe;
                        break;
                    }
                    current = VisualTreeHelper.GetParent(current);
                }

                if (elementWithMenu?.ContextMenu != null)
                {
                    btn.Tag = elementWithMenu.Tag;

                    elementWithMenu.ContextMenu.PlacementTarget = btn;
                    elementWithMenu.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Left;
                    elementWithMenu.ContextMenu.VerticalOffset = 4;
                    elementWithMenu.ContextMenu.HorizontalOffset = -4;
                    elementWithMenu.ContextMenu.IsOpen = true;
                }

                e.Handled = true;
            }
        }

    }
}