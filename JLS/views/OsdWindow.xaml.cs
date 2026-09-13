using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace JLS.Views
{
    public partial class OsdWindow : Window
    {
        private DispatcherTimer _closeTimer;
        private DispatcherTimer _volumeTimer;
        private bool _isAnimatingIn = false;

        public static readonly DependencyProperty IsVolumeChangingProperty =
            DependencyProperty.Register("IsVolumeChanging", typeof(bool), typeof(OsdWindow), new PropertyMetadata(false));

        public bool IsVolumeChanging
        {
            get { return (bool)GetValue(IsVolumeChangingProperty); }
            set { SetValue(IsVolumeChangingProperty, value); }
        }

        public OsdWindow()
        {
            InitializeComponent();

            _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _closeTimer.Tick += CloseTimer_Tick;

            _volumeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.1) };
            _volumeTimer.Tick += VolumeTimer_Tick;

            this.Loaded += OsdWindow_Loaded;

            this.DataContextChanged += OsdWindow_DataContextChanged;
        }

        private void OsdWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is INotifyPropertyChanged oldVm)
            {
                oldVm.PropertyChanged -= Vm_PropertyChanged;
            }
            if (e.NewValue is INotifyPropertyChanged newVm)
            {
                newVm.PropertyChanged += Vm_PropertyChanged;
            }
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Volume")
            {
                this.Dispatcher.Invoke(() =>
                {
                    IsVolumeChanging = true;
                    _volumeTimer.Stop();
                    _volumeTimer.Start();
                });
            }
        }

        private void VolumeTimer_Tick(object? sender, EventArgs e)
        {
            _volumeTimer.Stop();
            IsVolumeChanging = false;     
        }

        private void OsdWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdatePosition();
            ResetTimer();
        }

        public void ResetTimer()
        {
            this.Show();

            if (this.Opacity < 1.0 && !_isAnimatingIn)
            {
                _isAnimatingIn = true;

                var fadeIn = new DoubleAnimation
                {
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(150)
                };

                fadeIn.Completed += (s, ev) => _isAnimatingIn = false;
                this.BeginAnimation(Window.OpacityProperty, fadeIn);
            }

            _closeTimer.Stop();
            _closeTimer.Start();
        }

        private void CloseTimer_Tick(object? sender, EventArgs e)
        {
            _closeTimer.Stop();
            _isAnimatingIn = false;

            var fadeOut = new DoubleAnimation
            {
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(300)
            };

            fadeOut.Completed += (s, ev) => this.Hide();
            this.BeginAnimation(Window.OpacityProperty, fadeOut);
        }

        public void UpdatePosition()
        {
            string position = JLS.Properties.Settings.Default.OsdPosition;
            var workArea = SystemParameters.WorkArea;
            double margin = 10;

            switch (position)
            {
                case "Top-Left":
                    this.Left = workArea.Left + margin;
                    this.Top = workArea.Top + margin;
                    break;
                case "Top-Right":
                    this.Left = workArea.Right - this.Width - margin;
                    this.Top = workArea.Top + margin;
                    break;
                case "Bottom-Left":
                    this.Left = workArea.Left + margin;
                    this.Top = workArea.Bottom - this.Height - margin;
                    break;
                case "Bottom-Right":
                    this.Left = workArea.Right - this.Width - margin;
                    this.Top = workArea.Bottom - this.Height - margin;
                    break;
            }
        }
    }
}