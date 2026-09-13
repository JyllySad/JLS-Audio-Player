using System; 
using System.Runtime.InteropServices; 
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace JLS
{
    public partial class MainWindow : Window
    {
        public static Window? ActiveVisualizerWindow = null;

        public void RegisterGlobalHotkey(int id, uint modifiers, uint key)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, id);      
            RegisterHotKey(handle, id, modifiers, key);
        }

        public void UnregisterGlobalHotkeys()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            for (int i = 100; i <= 110; i++)
            {
                UnregisterHotKey(handle, i);
            }
            for (int i = 900; i <= 903; i++)
            {
                UnregisterHotKey(handle, i);
            }
        }

        public MainWindow()
        {
            var jsonProvider = new JLS.Services.JsonSettingsProvider();
            jsonProvider.Initialize("JsonSettingsProvider", new System.Collections.Specialized.NameValueCollection());

            JLS.Properties.Settings.Default.Providers.Add(jsonProvider);

            foreach (System.Configuration.SettingsProperty prop in JLS.Properties.Settings.Default.Properties)
            {
                prop.Provider = jsonProvider;
            }

            JLS.Properties.Settings.Default.Reload();

            if (JLS.Properties.Settings.Default.IsOptimizedWindowModeEnabled)
            {
                this.AllowsTransparency = false;

                this.Background = (Brush)new BrushConverter().ConvertFromString("#FF181818")!;
            }
            else
            {
                this.AllowsTransparency = true;
                this.Background = Brushes.Transparent;
            }

            InitializeComponent();

            _hoverCollisionTimer = new System.Windows.Threading.DispatcherTimer();
            _hoverCollisionTimer.Interval = TimeSpan.FromMilliseconds(30);     
            _hoverCollisionTimer.Tick += HoverCollisionTimer_Tick;

            double top = JLS.Properties.Settings.Default.WindowTop;
            double left = JLS.Properties.Settings.Default.WindowLeft;
            string state = JLS.Properties.Settings.Default.WindowState;

            if (left >= SystemParameters.VirtualScreenLeft && left + this.Width <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                top >= SystemParameters.VirtualScreenTop && top + this.Height <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            {
                this.Top = top;
                this.Left = left;
            }

            if (Enum.TryParse(state, out WindowState savedState))
            {
                if (savedState == WindowState.Minimized) savedState = WindowState.Normal;
                this.WindowState = savedState;
            }

            this.SourceInitialized += MainWindow_SourceInitialized;

            
            this.Deactivated += (s, e) => { if (_isAppFullscreen) this.Topmost = false; };
            this.Activated += (s, e) => { if (_isAppFullscreen) this.Topmost = true; };
        }

        private void AppBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (JLS.Properties.Settings.Default.IsOptimizedWindowModeEnabled)
            {
                AppBorder.Clip = null;
                return;
            }

            AppBorder.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 10, 10);
        }

        private bool _isForceClose = false;

        private bool _wasDragged = false;

        private Point _dragStartPoint;

        public static MiniPlayerWindow? ActiveMiniPlayer = null;

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is JLS.ViewModels.MainViewModel vm)
            {
                if (vm.IsMinimizeToTrayEnabled && !_isForceClose)
                {
                    e.Cancel = true;
                    this.Hide();

                    if (TrayOpenMenuItem != null) TrayOpenMenuItem.Visibility = Visibility.Visible;

                    if (vm.IsMiniPlayerEnabled)
                    {
                        if (ActiveMiniPlayer == null)
                        {
                            ActiveMiniPlayer = new MiniPlayerWindow();
                            ActiveMiniPlayer.DataContext = vm;
                        }
                        vm.IsMiniPlayerActive = true;
                        ActiveMiniPlayer.Show();
                    }

                    return;        
                }

                vm.SavePlaybackState();
            }

            if (this.WindowState == WindowState.Normal)
            {
                JLS.Properties.Settings.Default.WindowTop = this.Top;
                JLS.Properties.Settings.Default.WindowLeft = this.Left;
            }
            JLS.Properties.Settings.Default.WindowState = this.WindowState.ToString();

            JLS.Properties.Settings.Default.Save();
            JLS.Services.StatsTracker.Instance.EndSession();

            base.OnClosing(e);
        }

        #region Win32 API для корректного развертывания на весь экран

        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;      
        private const int DWMWA_BORDER_COLOR = 34;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public class MONITORINFO
        {
            public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            public RECT rcMonitor = new RECT();
            public RECT rcWork = new RECT();
            public int dwFlags = 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle).AddHook(new HwndSourceHook(WindowProc));

            var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);

            if (JLS.Properties.Settings.Default.IsOptimizedWindowModeEnabled)
            {
                if (chrome != null) chrome.GlassFrameThickness = new Thickness(1);

                int preference = DWMWCP_ROUND;
                DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));

                int borderColor = 0x00181818;
                DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));

                if (AppBorder != null)
                {
                    AppBorder.CornerRadius = new CornerRadius(0);
                    AppBorder.Margin = new Thickness(0);
                }
            }
            else
            {
                if (chrome != null) chrome.GlassFrameThickness = new Thickness(0);
            }

            try
            {
                double top = JLS.Properties.Settings.Default.WindowTop;
                double left = JLS.Properties.Settings.Default.WindowLeft;
                double width = JLS.Properties.Settings.Default.WindowWidth;
                double height = JLS.Properties.Settings.Default.WindowHeight;
                string state = JLS.Properties.Settings.Default.WindowState;

                if (width > 0 && height > 0 &&
                    left >= SystemParameters.VirtualScreenLeft && left + width <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                    top >= SystemParameters.VirtualScreenTop && top + height <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
                {
                    this.WindowStartupLocation = WindowStartupLocation.Manual;     
                    this.Top = top;
                    this.Left = left;
                }

                if (Enum.TryParse(state, out WindowState savedState))
                {
                    if (savedState == WindowState.Minimized) savedState = WindowState.Normal;
                    this.WindowState = savedState;
                }
            }
            catch { }

            if (DataContext is JLS.ViewModels.MainViewModel vm)
            {
                vm.SetupAllHotkeys();
            }
        }

        private const int WM_APPCOMMAND = 0x0319;
        private const int APPCOMMAND_MEDIA_NEXTTRACK = 11;
        private const int APPCOMMAND_MEDIA_PREVIOUSTRACK = 12;
        private const int APPCOMMAND_MEDIA_STOP = 13;
        private const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0024)  
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            else if (msg == WM_APPCOMMAND)    
            {
                int cmd = (int)((uint)lParam >> 16 & 0xFFFF);     

                if (DataContext is JLS.ViewModels.MainViewModel vm && vm.IsMediaKeysEnabled)
                {
                    switch (cmd)
                    {
                        case APPCOMMAND_MEDIA_PLAY_PAUSE:
                            vm.TogglePlayPauseCommand.Execute(null);
                            handled = true;
                            break;
                        case APPCOMMAND_MEDIA_NEXTTRACK:
                            vm.PlayNextCommand.Execute(null);
                            handled = true;
                            break;
                        case APPCOMMAND_MEDIA_PREVIOUSTRACK:
                            vm.PlayPrevCommand.Execute(null);
                            handled = true;
                            break;
                        case APPCOMMAND_MEDIA_STOP:
                            if (vm.IsPlaying) vm.TogglePlayPauseCommand.Execute(null);
                            handled = true;
                            break;
                    }
                }
            }

            else if (msg == WM_HOTKEY)    
            {
                int id = wParam.ToInt32();
                if (DataContext is JLS.ViewModels.MainViewModel vm)
                {
                    vm.HandleGlobalHotkey(id);
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }

        private void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO))!;

            int MONITOR_DEFAULTTONEAREST = 0x00000002;
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

            if (monitor != IntPtr.Zero)
            {
                MONITORINFO monitorInfo = new MONITORINFO();
                GetMonitorInfo(monitor, monitorInfo);

                RECT rcWorkArea = monitorInfo.rcWork;
                RECT rcMonitorArea = monitorInfo.rcMonitor;

                mmi.ptMaxSize.X = Math.Abs(rcWorkArea.Right - rcWorkArea.Left);
                mmi.ptMaxSize.Y = Math.Abs(rcWorkArea.Bottom - rcWorkArea.Top);

                mmi.ptMaxPosition.X = Math.Abs(rcWorkArea.Left - rcMonitorArea.Left);
                mmi.ptMaxPosition.Y = Math.Abs(rcWorkArea.Top - rcMonitorArea.Top);

                mmi.ptMinTrackSize.X = this.MinWidth > 0 ? (int)this.MinWidth : mmi.ptMinTrackSize.X;
                mmi.ptMinTrackSize.Y = this.MinHeight > 0 ? (int)this.MinHeight : mmi.ptMinTrackSize.Y;
            }

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        #endregion

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);

            if (WindowState == WindowState.Maximized)
            {
                if (MaximizeBtn != null) MaximizeBtn.Content = "\uE923";

                if (AppBorder != null)
                {
                    AppBorder.CornerRadius = new CornerRadius(0);
                    AppBorder.Margin = new Thickness(0);
                }
            }
            else
            {
                if (MaximizeBtn != null) MaximizeBtn.Content = "\uE922";

                if (AppBorder != null && !JLS.Properties.Settings.Default.IsOptimizedWindowModeEnabled)
                {
                    AppBorder.CornerRadius = new CornerRadius(10);
                    AppBorder.Margin = new Thickness(1);
                }
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isAppFullscreen) return;      

            if (e.ClickCount == 2)
            {
                Maximize_Click(sender, e);
            }
            else
            {
                if (WindowState == WindowState.Maximized)
                {
                    var mousePos = e.GetPosition(this);
                    WindowState = WindowState.Normal;
                    Top = mousePos.Y - 20;
                }

                DragMove();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (_isAppFullscreen) return;      

            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            else
                WindowState = WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void WaveformArea_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (this.DataContext is JLS.ViewModels.MainViewModel vm)
            {
                vm.WaveformWidth = e.NewSize.Width;
            }
            UpdateWaveformCaretPosition();
        }

        private void ClassicArea_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateClassicCaretPosition();
        }

        private void TrackSliderWaveform_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateWaveformCaretPosition();
        }

        private void TrackSliderClassic_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateClassicCaretPosition();
        }

        private void UpdateWaveformCaretPosition()
        {
            if (DataContext is JLS.ViewModels.MainViewModel vm && vm.TotalDuration > 0 && TrackSliderWaveform.ActualWidth > 0)
            {
                double progress = TrackSliderWaveform.Value / TrackSliderWaveform.Maximum;
                double currentX = progress * TrackSliderWaveform.ActualWidth;

                double bubbleWidth = WaveformCaretBubble.ActualWidth > 0 ? WaveformCaretBubble.ActualWidth : 45;

                double targetX = currentX - (bubbleWidth / 2.0);
                double minX = 0;
                double maxX = TrackSliderWaveform.ActualWidth - bubbleWidth;

                WaveformCaretTransform.X = Math.Max(minX, Math.Min(targetX, maxX));
            }
        }

        private void UpdateClassicCaretPosition()
        {
            if (DataContext is JLS.ViewModels.MainViewModel vm && vm.TotalDuration > 0 && TrackSliderClassic.ActualWidth > 0)
            {
                double trackWidth = Math.Max(1, TrackSliderClassic.ActualWidth - 14);
                double progress = TrackSliderClassic.Value / TrackSliderClassic.Maximum;
                double currentX = 7 + (progress * trackWidth);

                double bubbleWidth = ClassicCaretBubble.ActualWidth > 0 ? ClassicCaretBubble.ActualWidth : 45;

                double targetX = currentX - (bubbleWidth / 2.0);
                double minX = 0;
                double maxX = TrackSliderClassic.ActualWidth - bubbleWidth;

                ClassicCaretTransform.X = Math.Max(minX, Math.Min(targetX, maxX));
            }
        }

        private int _volumePopupToken = 0;

        private void ShowVolumePopup()
        {
            if (!this.IsLoaded) return;

            var mainWindow = Application.Current.MainWindow;
            if (mainWindow != null && (mainWindow.WindowState == WindowState.Minimized || !mainWindow.IsActive))
                return;

            if (this.DataContext is JLS.ViewModels.MainViewModel vm)
            {
                if (vm.IsNowPlayingVisible) return;

                if (vm.IsMinimalBottomPanelEnabled)
                {
                    if (MinimalVolumePopup != null) MinimalVolumePopup.IsOpen = true;
                    return;
                }

                if (vm.IsHorizontalVolumeEnabled)
                {
                    if (VolumePopupHorizontal != null) VolumePopupHorizontal.IsOpen = true;
                }
                else
                {
                    if (VolumePopup != null) VolumePopup.IsOpen = true;
                }
            }
        }

        private void HideVolumePopup()
        {
            if (VolumePopup != null) VolumePopup.IsOpen = false;
            if (VolumePopupHorizontal != null) VolumePopupHorizontal.IsOpen = false;
            if (MinimalVolumePopup != null) MinimalVolumePopup.IsOpen = false;  
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
                    if (currentToken == _volumePopupToken && !_isDraggingVolume)
                    {
                        bool isVerticalHovered = VolumeSlider != null && VolumeSlider.IsMouseOver;
                        bool isHorizontalHovered = VolumeSliderHorizontal != null && VolumeSliderHorizontal.IsMouseOver;
                        bool isMinimalHovered = MinimalVolumeSlider != null && MinimalVolumeSlider.IsMouseOver;  

                        if (!isVerticalHovered && !isHorizontalHovered && !isMinimalHovered)
                        {
                            HideVolumePopup();
                        }
                    }
                });
            });
        }

        private void MinimalVolume_MouseEnter(object sender, MouseEventArgs e)
        {
            ShowVolumePopup();
        }

        private void MinimalVolume_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isDraggingVolume)
            {
                Task.Delay(100).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        bool isMinimalHovered = MinimalVolumeSlider != null && MinimalVolumeSlider.IsMouseOver;

                        if (!isMinimalHovered && !_isDraggingVolume)
                        {
                            HideVolumePopup();
                        }
                    });
                });
            }
        }

        private double _previousVolume = 0.5;

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

        private bool _isAppFullscreen = false;
        private WindowState _savedWindowState = WindowState.Normal;
        private Rect _savedBounds;

        private void ToggleAppFullscreen()
        {
            if (!_isAppFullscreen)
            {
                _isAppFullscreen = true;
                _savedWindowState = this.WindowState;

                if (this.WindowState == WindowState.Normal)
                {
                    _savedBounds = new Rect(this.Left, this.Top, this.Width, this.Height);
                }

                this.WindowState = WindowState.Normal;

                IntPtr handle = new WindowInteropHelper(this).Handle;
                IntPtr monitor = MonitorFromWindow(handle, 2   );
                if (monitor != IntPtr.Zero)
                {
                    MONITORINFO monitorInfo = new MONITORINFO();
                    GetMonitorInfo(monitor, monitorInfo);

                    PresentationSource source = PresentationSource.FromVisual(this);
                    if (source != null && source.CompositionTarget != null)
                    {
                        Matrix matrix = source.CompositionTarget.TransformFromDevice;
                        Point topLeft = matrix.Transform(new Point(monitorInfo.rcMonitor.Left, monitorInfo.rcMonitor.Top));
                        Point bottomRight = matrix.Transform(new Point(monitorInfo.rcMonitor.Right, monitorInfo.rcMonitor.Bottom));

                        this.Left = topLeft.X;
                        this.Top = topLeft.Y;
                        this.Width = bottomRight.X - topLeft.X;
                        this.Height = bottomRight.Y - topLeft.Y;
                    }
                }

                this.Topmost = true;

                if (AppBorder != null)
                {
                    AppBorder.CornerRadius = new CornerRadius(0);
                    AppBorder.Margin = new Thickness(0);
                }

                if (TitleBarRow != null) TitleBarRow.Height = new GridLength(0);
                if (TitleBarContainer != null) TitleBarContainer.Visibility = Visibility.Collapsed;

                var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
                if (chrome != null) chrome.CaptionHeight = 0;
            }
            else
            {
                _isAppFullscreen = false;
                this.Topmost = false;

                if (_savedWindowState == WindowState.Maximized)
                {
                    this.WindowState = WindowState.Maximized;
                }
                else
                {
                    this.Left = _savedBounds.Left;
                    this.Top = _savedBounds.Top;
                    this.Width = _savedBounds.Width;
                    this.Height = _savedBounds.Height;

                    if (AppBorder != null && !JLS.Properties.Settings.Default.IsOptimizedWindowModeEnabled)
                    {
                        AppBorder.CornerRadius = new CornerRadius(16);
                        AppBorder.Margin = new Thickness(1);
                    }
                }

                if (TitleBarRow != null) TitleBarRow.Height = new GridLength(32);
                if (TitleBarContainer != null) TitleBarContainer.Visibility = Visibility.Visible;

                var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
                if (chrome != null) chrome.CaptionHeight = 32;
            }
        }

        private void ToggleVisualizer()
        {
            if (ActiveVisualizerWindow != null)
            {
                ActiveVisualizerWindow.Close();
                ActiveVisualizerWindow = null;
                return;
            }

            if (DataContext is JLS.ViewModels.MainViewModel vm && !vm.IsFullscreenVisualizerEnabled)
            {
                return;       
            }

            ActiveVisualizerWindow = new Views.VisualizerWindow();
            ActiveVisualizerWindow.DataContext = this.DataContext;
            ActiveVisualizerWindow.Show();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.F10 || (e.Key == Key.System && e.SystemKey == Key.F10))
            {
                if (DataContext is JLS.ViewModels.MainViewModel f10Vm && f10Vm.IsFullscreenF10Enabled)
                {
                    ToggleAppFullscreen();
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.F12)
            {
                if (DataContext is JLS.ViewModels.MainViewModel overlayVm)
                {
                    overlayVm.ToggleOverlayVisualizerCommand.Execute(null);
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F11)
            {
                ToggleVisualizer();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                if (ActiveVisualizerWindow != null)
                {
                    ActiveVisualizerWindow.Close();
                    ActiveVisualizerWindow = null;
                    e.Handled = true;
                    return;
                }

                if (DataContext is JLS.ViewModels.MainViewModel escapeVm && escapeVm.IsOverlayVisualizerVisible)
                {
                    escapeVm.IsOverlayVisualizerVisible = false;
                    e.Handled = true;
                    return;
                }

                if (_isAppFullscreen)
                {
                    ToggleAppFullscreen();
                    e.Handled = true;
                    return;
                }
            }

            if (DataContext is JLS.ViewModels.MainViewModel vm && vm.RecordingItem != null)
            {
                e.Handled = true;      

                if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                    e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                    e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                    e.Key == Key.System)
                {
                    return;
                }

                if (e.Key == Key.Escape)
                {
                    vm.CancelRecording();
                    return;
                }

                uint modifiers = 0;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= 0x0001;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= 0x0002;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= 0x0004;

                Key keyToRecord = (e.Key == Key.System ? e.SystemKey : e.Key);
                uint vk = (uint)KeyInterop.VirtualKeyFromKey(keyToRecord);

                string display = "";
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) display += "Ctrl + ";
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) display += "Alt + ";
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) display += "Shift + ";
                display += keyToRecord.ToString();

                vm.FinishRecording(modifiers, vk, display);
            }
            else
            {
                base.OnPreviewKeyDown(e);
            }
        }

        private void Marquee_TargetUpdated(object sender, DataTransferEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                DependencyObject parent = VisualTreeHelper.GetParent(element);
                while (parent != null && !(parent is Grid))
                {
                    parent = VisualTreeHelper.GetParent(parent);
                }

                if (parent is Grid grid && grid.Children.Count >= 2)
                {
                    var staticCtrl = grid.Children[0] as FrameworkElement;
                    var canvas = grid.Children[1] as Canvas;

                    if (canvas == null || canvas.Children.Count == 0) return;
                    var movingCtrl = canvas.Children[0] as FrameworkElement;

                    if (staticCtrl != null && movingCtrl != null)
                    {
                        if (movingCtrl.RenderTransform is TranslateTransform tt)
                        {
                            if (!tt.IsFrozen)
                            {
                                tt.BeginAnimation(TranslateTransform.XProperty, null);
                            }
                            else
                            {
                                movingCtrl.RenderTransform = new TranslateTransform();
                            }
                        }

                        canvas.Visibility = Visibility.Hidden;
                        staticCtrl.Visibility = Visibility.Visible;
                    }
                }
            }
        }

        private void Marquee_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Grid grid && grid.Children.Count >= 2)
            {
                var staticCtrl = grid.Children[0] as FrameworkElement;
                var canvas = grid.Children[1] as Canvas;

                if (canvas == null || canvas.Children.Count == 0) return;
                var movingCtrl = canvas.Children[0] as FrameworkElement;

                if (staticCtrl != null && movingCtrl != null)
                {
                    double fullWidth = movingCtrl.ActualWidth;
                    double visibleWidth = grid.ActualWidth;

                    if (fullWidth > visibleWidth)
                    {
                        staticCtrl.Visibility = Visibility.Hidden;
                        canvas.Visibility = Visibility.Visible;

                        double duration = (fullWidth - visibleWidth) * 0.025;
                        if (duration < 1.5) duration = 1.5;

                        DoubleAnimation marqueeAnimation = new DoubleAnimation
                        {
                            From = 0,
                            To = -(fullWidth - visibleWidth + 20),
                            Duration = TimeSpan.FromSeconds(duration),
                            RepeatBehavior = RepeatBehavior.Forever,
                            AutoReverse = true
                        };

                        if (movingCtrl.RenderTransform is TranslateTransform tt && !tt.IsFrozen)
                        {
                            tt.BeginAnimation(TranslateTransform.XProperty, marqueeAnimation);
                        }
                        else
                        {
                            var newTransform = new TranslateTransform();
                            movingCtrl.RenderTransform = newTransform;
                            newTransform.BeginAnimation(TranslateTransform.XProperty, marqueeAnimation);
                        }
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

                if (!slider.IsMouseOver)
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

        private void AppTaskbarIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            RestoreWindowFromTray();
        }

        private void TrayOpen_Click(object sender, RoutedEventArgs e)
        {
            RestoreWindowFromTray();
        }

        public void RestoreWindowFromTray()
        {
            this.Show();

            if (this.WindowState == WindowState.Minimized)
            {
                this.WindowState = WindowState.Normal;
            }

            this.Activate();
            this.Focus();

            if (TrayOpenMenuItem != null) TrayOpenMenuItem.Visibility = Visibility.Collapsed;

            if (ActiveMiniPlayer != null && ActiveMiniPlayer.IsVisible)
            {
                ActiveMiniPlayer.Hide();
                if (DataContext is JLS.ViewModels.MainViewModel vm)
                {
                    vm.IsMiniPlayerActive = false;
                }
            }
        }

        private void TrayMiniPlayer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && DataContext is JLS.ViewModels.MainViewModel vm)
            {
                bool isEnabled = menuItem.IsChecked;

                if (isEnabled)
                {
                    if (this.Visibility != Visibility.Visible)
                    {
                        if (ActiveMiniPlayer == null)
                        {
                            ActiveMiniPlayer = new MiniPlayerWindow();
                            ActiveMiniPlayer.DataContext = vm;
                        }
                        vm.IsMiniPlayerActive = true;
                        ActiveMiniPlayer.Show();
                    }
                }
                else
                {
                    if (ActiveMiniPlayer != null && ActiveMiniPlayer.IsVisible)
                    {
                        ActiveMiniPlayer.Hide();
                        vm.IsMiniPlayerActive = false;
                    }
                }
            }
        }

        private void TrayExit_Click(object sender, RoutedEventArgs e)
        {
            _isForceClose = true;   
            this.Close();                
        }

        private System.Windows.Threading.DispatcherTimer _hoverCollisionTimer;
        private bool _isWaveformHoverVisible = false;
        private bool _isWaveformHoverColliding = false;
        private bool _isClassicHoverVisible = false;
        private bool _isClassicHoverColliding = false;

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

        private void HoverCollisionTimer_Tick(object? sender, EventArgs e)
        {
            if (DataContext is JLS.ViewModels.MainViewModel vm && vm.TotalDuration > 0)
            {
                double collisionDistance = 65;

                if (_isWaveformHoverVisible)
                {
                    double currentX = (vm.CurrentPosition / vm.TotalDuration) * WaveformArea.ActualWidth;
                    double hoverX = WaveformHoverTransform.X;
                    bool isColliding = Math.Abs(hoverX - currentX) < collisionDistance;

                    if (isColliding && !_isWaveformHoverColliding)
                    {
                        _isWaveformHoverColliding = true;
                        AnimateOpacity(WaveformHoverBubble, 0.0, 150);
                    }
                    else if (!isColliding && _isWaveformHoverColliding)
                    {
                        _isWaveformHoverColliding = false;
                        AnimateOpacity(WaveformHoverBubble, 1.0, 150);
                    }
                }

                if (_isClassicHoverVisible)
                {
                    double trackWidth = Math.Max(1, TrackSliderClassic.ActualWidth - 14);
                    double currentX = 7 + (vm.CurrentPosition / vm.TotalDuration) * trackWidth;
                    double hoverX = ClassicHoverTransform.X;
                    bool isColliding = Math.Abs(hoverX - currentX) < collisionDistance;

                    if (isColliding && !_isClassicHoverColliding)
                    {
                        _isClassicHoverColliding = true;
                        AnimateOpacity(ClassicHoverBubble, 0.0, 150);
                    }
                    else if (!isColliding && _isClassicHoverColliding)
                    {
                        _isClassicHoverColliding = false;
                        AnimateOpacity(ClassicHoverBubble, 1.0, 150);
                    }
                }
            }
        }

        private void WaveformArea_MouseMove(object sender, MouseEventArgs e)
        {
            if (DataContext is JLS.ViewModels.MainViewModel vm && vm.TotalDuration > 0)
            {
                double mouseX = e.GetPosition(WaveformArea).X;
                double clampedX = Math.Max(0, Math.Min(mouseX, WaveformArea.ActualWidth));

                TimeSpan t = TimeSpan.FromSeconds((clampedX / WaveformArea.ActualWidth) * vm.TotalDuration);
                WaveformHoverText.Text = GetFormattedHoverTime(t, vm.IsShowTenthsEnabled);

                double bubbleWidth = WaveformHoverBubble.ActualWidth > 0 ? WaveformHoverBubble.ActualWidth : 45;
                double minX = 23;      
                double maxX = WaveformArea.ActualWidth + 23 - bubbleWidth;
                WaveformHoverTransform.X = Math.Max(minX, Math.Min(clampedX, maxX));

                if (!_isWaveformHoverVisible)
                {
                    _isWaveformHoverVisible = true;
                    _hoverCollisionTimer.Start();
                    if (!_isWaveformHoverColliding) AnimateOpacity(WaveformHoverBubble, 1.0);
                }
            }
        }

        private void WaveformArea_MouseLeave(object sender, MouseEventArgs e)
        {
            _isWaveformHoverVisible = false;
            _isWaveformHoverColliding = false;
            AnimateOpacity(WaveformHoverBubble, 0.0, 200);

            if (!_isClassicHoverVisible) _hoverCollisionTimer.Stop();
        }

        private void ClassicArea_MouseMove(object sender, MouseEventArgs e)
        {
            if (DataContext is JLS.ViewModels.MainViewModel vm && vm.TotalDuration > 0)
            {
                double mouseX = e.GetPosition(TrackSliderClassic).X;
                double width = TrackSliderClassic.ActualWidth;
                double clampedX = Math.Max(0, Math.Min(mouseX, width));

                double trackWidth = Math.Max(1, width - 14);
                double trackX = Math.Max(0, Math.Min(clampedX - 7, trackWidth));

                TimeSpan t = TimeSpan.FromSeconds((trackX / trackWidth) * vm.TotalDuration);
                ClassicHoverText.Text = GetFormattedHoverTime(t, vm.IsShowTenthsEnabled);

                double bubbleWidth = ClassicHoverBubble.ActualWidth > 0 ? ClassicHoverBubble.ActualWidth : 45;
                double minX = 22;      
                double maxX = width + 22 - bubbleWidth;
                ClassicHoverTransform.X = Math.Max(minX, Math.Min(clampedX, maxX));

                if (!_isClassicHoverVisible)
                {
                    _isClassicHoverVisible = true;
                    _hoverCollisionTimer.Start();
                    if (!_isClassicHoverColliding) AnimateOpacity(ClassicHoverBubble, 1.0);
                }
            }
        }

        private void ClassicArea_MouseLeave(object sender, MouseEventArgs e)
        {
            _isClassicHoverVisible = false;
            _isClassicHoverColliding = false;
            AnimateOpacity(ClassicHoverBubble, 0.0, 200);

            if (!_isWaveformHoverVisible) _hoverCollisionTimer.Stop();
        }

        private bool _isMinimalHoverVisible = false;

        private void MinimalProgress_MouseEnter(object sender, MouseEventArgs e)
        {
            _isMinimalHoverVisible = true;
            UpdateMinimalBubblePosition();
            AnimateOpacity(MinimalHoverBubble, 1.0);
        }

        private void MinimalProgress_MouseLeave(object sender, MouseEventArgs e)
        {
            _isMinimalHoverVisible = false;
            AnimateOpacity(MinimalHoverBubble, 0.0, 200);
        }

        private void TrackSliderMinimal_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isMinimalHoverVisible)
            {
                UpdateMinimalBubblePosition();
            }
        }

        private void UpdateMinimalBubblePosition()
        {
            if (DataContext is JLS.ViewModels.MainViewModel vm && vm.TotalDuration > 0 && MinimalProgressContainer.ActualWidth > 0)
            {
                double progress = vm.CurrentPosition / vm.TotalDuration;
                double currentX = progress * MinimalProgressContainer.ActualWidth;

                double bubbleWidth = MinimalHoverBubble.ActualWidth > 0 ? MinimalHoverBubble.ActualWidth : MinimalHoverBubble.MinWidth;
                double halfWidth = bubbleWidth / 2.0;

                double targetX = currentX - halfWidth;

                double minX = 0;
                double maxX = MinimalProgressContainer.ActualWidth - bubbleWidth;

                if (maxX > minX)
                {
                    targetX = Math.Max(minX, Math.Min(targetX, maxX));
                }
                else
                {
                    targetX = minX;
                }

                MinimalHoverTransform.X = targetX;

                TimeSpan current = TimeSpan.FromSeconds(vm.CurrentPosition);
                TimeSpan total = TimeSpan.FromSeconds(vm.TotalDuration);
                TimeSpan remaining = total - current;    

                string currStr = GetFormattedHoverTime(current, vm.IsShowTenthsEnabled);
                string totalStr = GetFormattedHoverTime(total, vm.IsShowTenthsEnabled);
                string remStr = "-" + GetFormattedHoverTime(remaining, vm.IsShowTenthsEnabled);

                string timeMode = vm.TimeDisplayMode.ToString();

                if (timeMode.Contains("Elapsed"))
                {
                    MinimalHoverText.Text = currStr;
                }
                else if (timeMode.Contains("Remaining"))
                {
                    MinimalHoverText.Text = remStr;
                }
                else    
                {
                    MinimalHoverText.Text = $"{currStr} / {totalStr}";
                }
            }
        }

        private void TrackSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider slider && DataContext is ViewModels.MainViewModel vm)
            {
                slider.CaptureMouse();
                _wasDragged = false;

                _dragStartPoint = e.GetPosition(slider);

                double clickPosition = CalculateSliderPosition(slider, e, vm.TotalDuration);

                vm.StartDragging(clickPosition);

                e.Handled = true;
            }
        }

        private double CalculateSliderPosition(Slider slider, MouseEventArgs e, double totalDuration)
        {
            if (slider.ActualWidth <= 0 || totalDuration <= 0) return 0;

            double mouseX = e.GetPosition(slider).X;

            if (slider.Name == "TrackSliderClassic")
            {
                double trackWidth = Math.Max(1, slider.ActualWidth - 14);
                double trackX = Math.Max(0, Math.Min(mouseX - 7, trackWidth));

                return (trackX / trackWidth) * totalDuration;
            }

            mouseX = Math.Max(0, Math.Min(mouseX, slider.ActualWidth));

            return (mouseX / slider.ActualWidth) * totalDuration;
        }

        private void TrackSlider_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (sender is Slider slider && slider.IsMouseCaptured && DataContext is ViewModels.MainViewModel vm)
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
            if (sender is Slider slider && slider.IsMouseCaptured && DataContext is ViewModels.MainViewModel vm)
            {
                slider.ReleaseMouseCapture();

                double finalPosition = CalculateSliderPosition(slider, e, vm.TotalDuration);
                vm.IsDraggingTrack = false;

                vm.ApplySeek(finalPosition, _wasDragged);

                e.Handled = true;
            }
        }

        private readonly System.Collections.Generic.List<DateTime> _startEggClicks = new System.Collections.Generic.List<DateTime>();

        private void Logo_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (this.DataContext is JLS.ViewModels.MainViewModel viewModel)
            {
                if (viewModel.EasterEggVM.IsEasterEggVisible) return;

                _startEggClicks.Add(DateTime.Now);
                _startEggClicks.RemoveAll(time => (DateTime.Now - time).TotalSeconds > 5);

                if (_startEggClicks.Count >= 10)
                {
                    _startEggClicks.Clear();

                    if (viewModel.IsPlaying && viewModel.TogglePlayPauseCommand != null && viewModel.TogglePlayPauseCommand.CanExecute(null))
                    {
                        viewModel.TogglePlayPauseCommand.Execute(null);
                    }

                    viewModel.EasterEggVM.IsEasterEggVisible = true;
                }
            }
        }

        private void InfoBtn_Click(object sender, RoutedEventArgs e)
        {
            if (AppTaskbarIcon?.ContextMenu != null)
            {
                AppTaskbarIcon.ContextMenu.IsOpen = false;
            }
        }

    }
}