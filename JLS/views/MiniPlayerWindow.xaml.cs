using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace JLS
{
    public partial class MiniPlayerWindow : Window
    {
        

        private bool _isDraggingVolume = false;
        private bool _wasDragged = false;
        private Point _dragStartPoint;

        public MiniPlayerWindow()
        {
            InitializeComponent();
            LoadPosition();
        }

        private void LoadPosition()
        {
            double left = JLS.Properties.Settings.Default.MiniPlayerLeft;
            double top = JLS.Properties.Settings.Default.MiniPlayerTop;

            this.WindowStartupLocation = WindowStartupLocation.Manual;

            if (left != -1 && top != -1 &&
                left >= SystemParameters.VirtualScreenLeft && left + this.Width <= SystemParameters.VirtualScreenWidth + SystemParameters.VirtualScreenLeft &&
                top >= SystemParameters.VirtualScreenTop && top + this.Height <= SystemParameters.VirtualScreenHeight + SystemParameters.VirtualScreenTop)
            {
                this.Left = left;
                this.Top = top;
            }
            else
            {
                this.Left = SystemParameters.WorkArea.Left + 20;
                this.Top = SystemParameters.WorkArea.Top + 20;
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();

                Point finalPos = SnapToEdges();

                JLS.Properties.Settings.Default.MiniPlayerLeft = finalPos.X;
                JLS.Properties.Settings.Default.MiniPlayerTop = finalPos.Y;
                JLS.Properties.Settings.Default.Save();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public class MONITORINFO
        {
            public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            public RECT rcMonitor = new RECT();
            public RECT rcWork = new RECT();
            public int dwFlags = 0;
        }

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        private Point SnapToEdges()
        {
            double targetLeft = this.Left;
            double targetTop = this.Top;

            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            IntPtr monitor = MonitorFromWindow(hwnd, 2);    

            if (monitor != IntPtr.Zero)
            {
                MONITORINFO info = new MONITORINFO();
                GetMonitorInfo(monitor, info);

                var source = PresentationSource.FromVisual(this);
                double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                double workLeft = info.rcWork.Left / dpiX;
                double workTop = info.rcWork.Top / dpiY;
                double workRight = info.rcWork.Right / dpiX;
                double workBottom = info.rcWork.Bottom / dpiY;

                double margin = 12;         
                double threshold = 35;         

                if (Math.Abs(this.Left - workLeft) < threshold)
                    targetLeft = workLeft + margin;
                else if (Math.Abs((this.Left + this.Width) - workRight) < threshold)
                    targetLeft = workRight - this.Width - margin;

                if (Math.Abs(this.Top - workTop) < threshold)
                    targetTop = workTop + margin;
                else if (Math.Abs((this.Top + this.Height) - workBottom) < threshold)
                    targetTop = workBottom - this.Height - margin;

                if (targetLeft != this.Left || targetTop != this.Top)
                {
                    AnimateWindowPosition(targetLeft, targetTop);
                }
            }

            return new Point(targetLeft, targetTop);
        }

        private void AnimateWindowPosition(double targetLeft, double targetTop)
        {
            var animX = new DoubleAnimation(this.Left, targetLeft, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            var animY = new DoubleAnimation(this.Top, targetTop, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };

            animX.Completed += (s, e) => { this.Left = targetLeft; };
            animY.Completed += (s, e) => { this.Top = targetTop; };

            this.BeginAnimation(Window.LeftProperty, animX);
            this.BeginAnimation(Window.TopProperty, animY);
        }

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
    }
}