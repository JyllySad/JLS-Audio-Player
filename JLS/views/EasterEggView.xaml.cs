using JLS.ViewModels;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace JLS.Views
{
    public partial class EasterEggView : UserControl
    {
        private double _vx = 0;
        private double _vy = 0;
        private double _x = 0;
        private double _y = 0;
        private bool _isMoving = false;
        private bool _isHudVisible = false;

        private readonly Random _rand = new Random();
        private readonly DispatcherTimer _hudTimer;
        private readonly List<DateTime> _exitClicks = new List<DateTime>();

        private Point _lastMousePos;      
        private double _lastHue = -1;        

        public EasterEggView()
        {
            InitializeComponent();

            _hudTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _hudTimer.Tick += HudTimer_Tick;

            PlayArea.SizeChanged += PlayArea_SizeChanged;
            this.IsVisibleChanged += EasterEggView_IsVisibleChanged;
        }

        private void EasterEggView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue == false)
            {
                CompositionTarget.Rendering -= PhysicsLoop;
                _isMoving = false;
                _hudTimer.Stop();
                HudPanel.Opacity = 0;
                UpdateCursors(false);     
            }
            else
            {
                if (DataContext is EasterEggViewModel vm) vm.ResetStats();
                _exitClicks.Clear();
                ChangeColor();    
            }
        }

        private void PlayArea_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_isMoving && LogoPath.ActualWidth > 0)
            {
                if (_x == 0 && _y == 0)
                {
                    _x = (e.NewSize.Width - LogoPath.ActualWidth) / 2;
                    _y = (e.NewSize.Height - LogoPath.ActualHeight) / 2;
                }
                else
                {
                    double maxX = Math.Max(0, e.NewSize.Width - LogoPath.ActualWidth);
                    double maxY = Math.Max(0, e.NewSize.Height - LogoPath.ActualHeight);
                    _x = Math.Max(0, Math.Min(_x, maxX));
                    _y = Math.Max(0, Math.Min(_y, maxY));
                }

                Canvas.SetLeft(LogoPath, _x);
                Canvas.SetTop(LogoPath, _y);
            }
        }

        private void LogoPath_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!_isMoving) AnimateScale(1.15, 150);
        }

        private void LogoPath_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isMoving) AnimateScale(1.0, 250);
        }

        private void AnimateScale(double targetScale, int durationMs)
        {
            LogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(targetScale, TimeSpan.FromMilliseconds(durationMs)));
            LogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(targetScale, TimeSpan.FromMilliseconds(durationMs)));
        }

        private void LogoPath_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            if (!_isMoving)
            {
                _isMoving = true;
                AnimateScale(1.0, 150);        
                UpdateCursors(false);

                double angle = _rand.NextDouble() * Math.PI * 2;
                double speed = 4.0;
                _vx = Math.Cos(angle) * speed;
                _vy = Math.Sin(angle) * speed;

                CompositionTarget.Rendering -= PhysicsLoop;
                CompositionTarget.Rendering += PhysicsLoop;
            }
            else
            {
                _isMoving = false;
                UpdateCursors(false);
                AnimateScale(1.15, 150);          
                CompositionTarget.Rendering -= PhysicsLoop;
            }
        }

        private void PhysicsLoop(object? sender, EventArgs e)
        {
            if (PlayArea.ActualWidth == 0 || PlayArea.ActualHeight == 0) return;

            double maxX = PlayArea.ActualWidth - LogoPath.ActualWidth;
            double maxY = PlayArea.ActualHeight - LogoPath.ActualHeight;

            _x += _vx;
            _y += _vy;

            bool bounced = false;

            if (_x <= 0 && _vx < 0) { _vx *= -1; bounced = true; }
            else if (_x >= maxX && _vx > 0) { _vx *= -1; bounced = true; }

            if (_y <= 0 && _vy < 0) { _vy *= -1; bounced = true; }
            else if (_y >= maxY && _vy > 0) { _vy *= -1; bounced = true; }

            _x = Math.Max(0, Math.Min(_x, maxX));
            _y = Math.Max(0, Math.Min(_y, maxY));

            if (bounced && DataContext is EasterEggViewModel vm)
            {
                ChangeColor();

                bool nearXEdge = _x <= 3 || _x >= maxX - 3;
                bool nearYEdge = _y <= 3 || _y >= maxY - 3;

                if (nearXEdge && nearYEdge)
                {
                    vm.CornerHits++;
                    TriggerGlowAndPulse();     
                }
                else
                {
                    vm.WallHits++;
                }
            }

            Canvas.SetLeft(LogoPath, _x);
            Canvas.SetTop(LogoPath, _y);
        }

        private void ChangeColor()
        {
            double hue;

            if (_lastHue == -1)
            {
                hue = _rand.NextDouble() * 360;
            }
            else
            {
                do
                {
                    hue = _rand.NextDouble() * 360;

                    double diff = Math.Abs(hue - _lastHue);
                    double circularDiff = Math.Min(diff, 360 - diff);

                    if (circularDiff >= 60) break;          

                } while (true);
            }

            _lastHue = hue;

            double saturation = 0.8 + (_rand.NextDouble() * 0.2);

            double lightness = 0.4 + (_rand.NextDouble() * 0.2);

            Color newColor = HslToRgb(hue, saturation, lightness);
            LogoPath.Fill = new SolidColorBrush(newColor);

            GlowEffect.Color = newColor;
        }

        private void TriggerGlowAndPulse()
        {
            DoubleAnimation glowAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromSeconds(1))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            GlowEffect.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnim);

            DoubleAnimation pulseAnim = new DoubleAnimation(1.0, 1.25, TimeSpan.FromMilliseconds(166))
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(3)
            };
            LogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnim);
            LogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnim);
        }

        private void UpdateCursors(bool hide)
        {
            if (hide)
            {
                this.Cursor = Cursors.None;
                LogoPath.Cursor = Cursors.None;      
            }
            else
            {
                this.Cursor = Cursors.Arrow;
                LogoPath.Cursor = _isMoving ? Cursors.Arrow : Cursors.Hand;
            }
        }

        private void UserControl_MouseMove(object sender, MouseEventArgs e)
        {
            Point currentPos = e.GetPosition(this);
            if (Math.Abs(currentPos.X - _lastMousePos.X) > 2 || Math.Abs(currentPos.Y - _lastMousePos.Y) > 2)
            {
                _lastMousePos = currentPos;
                UpdateCursors(false);   

                if (_isMoving)
                {
                    if (!_isHudVisible)
                    {
                        _isHudVisible = true;
                        HudPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200)));
                    }
                    _hudTimer.Stop();
                    _hudTimer.Start();
                }
            }
        }

        private void HudTimer_Tick(object? sender, EventArgs e)
        {
            _hudTimer.Stop();
            _isHudVisible = false;
            HudPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(500)));

            if (_isMoving) UpdateCursors(true);
        }

        private void UserControl_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _exitClicks.Add(DateTime.Now);
            _exitClicks.RemoveAll(time => (DateTime.Now - time).TotalSeconds > 5);

            if (_exitClicks.Count >= 10)
            {
                _exitClicks.Clear();
                if (DataContext is EasterEggViewModel vm) vm.IsEasterEggVisible = false;
            }
        }

        private Color HslToRgb(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = l - c / 2;
            double r = 0, g = 0, b = 0;
            if (0 <= h && h < 60) { r = c; g = x; b = 0; }
            else if (60 <= h && h < 120) { r = x; g = c; b = 0; }
            else if (120 <= h && h < 180) { r = 0; g = c; b = x; }
            else if (180 <= h && h < 240) { r = 0; g = x; b = c; }
            else if (240 <= h && h < 300) { r = x; g = 0; b = c; }
            else if (300 <= h && h < 360) { r = c; g = 0; b = x; }
            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }
    }
}