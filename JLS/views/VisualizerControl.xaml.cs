using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using JLS.ViewModels;

namespace JLS.Views
{
    public partial class VisualizerControl : UserControl
    {
        private Shape?[] _shapes = Array.Empty<Shape?>();
        private ContextMenu _contextMenu;
        private Point _lastMousePosition;

        private DispatcherTimer _idleTimer;
        private bool _isUiHidden = false;

        private double[] _smoothedData = new double[512];
        private double[] _displayData = new double[512];
        private double[] _tempData = new double[512];

        private double[] _smoothedDataLeft = new double[256];
        private double[] _smoothedDataRight = new double[256];
        private double[] _displayDataLeft = new double[256];
        private double[] _displayDataRight = new double[256];
        private double[] _tempDataLeft = new double[256];
        private double[] _tempDataRight = new double[256];

        private Point[] _topPoints = new Point[512];
        private Point[] _bottomPoints = new Point[512];
        private Point[] _roadLeftPoints = new Point[512];
        private Point[] _roadRightPoints = new Point[512];
        private Point[] _barPoints = new Point[513];

        private readonly float[] _emptyFft = new float[1024];
        private readonly float[] _emptyStereoFft = new float[2048];
        private readonly float[] _emptyWave = new float[2048];

        private VisualizerStyle _currentStyle = VisualizerStyle.ClassicBars;
        private bool _isRendering = false;

        public VisualizerControl()
        {
            InitializeComponent();

            _contextMenu = new ContextMenu();
            _contextMenu.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            _contextMenu.Foreground = Brushes.White;
            _contextMenu.BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));

            _idleTimer = new DispatcherTimer();
            _idleTimer.Interval = TimeSpan.FromSeconds(3);    
            _idleTimer.Tick += IdleTimer_Tick;
            _idleTimer.Start();

            this.IsVisibleChanged += VisualizerControl_IsVisibleChanged;
        }

        private void VisualizerControl_IsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.IsVisible && !_isRendering)
            {
                RebuildShapes();
                CompositionTarget.Rendering += OnRenderFrame;
                _isRendering = true;
            }
            else if (!this.IsVisible && _isRendering)
            {
                CompositionTarget.Rendering -= OnRenderFrame;
                _isRendering = false;
            }
        }

        private void UserControl_MouseMove(object? sender, MouseEventArgs e)
        {
            Point currentPosition = e.GetPosition(this);
            if (currentPosition == _lastMousePosition)
            {
                return;       
            }
            _lastMousePosition = currentPosition;

            if (_idleTimer != null)
            {
                _idleTimer.Stop();
                _idleTimer.Start();
            }

            if (_isUiHidden)
            {
                ShowUIElements();
            }
        }

        private void IdleTimer_Tick(object? sender, EventArgs e)
        {
            _idleTimer.Stop();
            HideUIElements();
        }

        private void HideUIElements()
        {
            if (_isUiHidden) return;
            _isUiHidden = true;

            this.Cursor = Cursors.None;

            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromSeconds(0.5));
            CloseButtonContainer.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        private void ShowUIElements()
        {
            if (!_isUiHidden) return;
            _isUiHidden = false;

            this.Cursor = null;

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.3));
            CloseButtonContainer.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        private void UserControl_MouseRightButtonUp(object? sender, MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                _contextMenu.Items.Clear();

                foreach (var kvp in vm.VisualizerStyles)
                {
                    var style = kvp.Key;
                    var item = new MenuItem
                    {
                        Header = kvp.Value,    
                        IsCheckable = true,
                        IsChecked = vm.SelectedVisualizerStyle == style
                    };
                    item.Click += (s, args) =>
                    {
                        vm.SelectedVisualizerStyle = style;
                        RebuildShapes();
                    };
                    _contextMenu.Items.Add(item);
                }
                _contextMenu.IsOpen = true;
            }
        }

        private void DrawCanvas_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (this.IsVisible) RebuildShapes();
        }

        private void RebuildShapes()
        {
            DrawCanvas.Children.Clear();

            if (DataContext is MainViewModel vm)
            {
                _currentStyle = vm.SelectedVisualizerStyle;
                Brush primary = vm.LightColor;

                int bands = (_currentStyle == VisualizerStyle.MirroredMountains ||
                             _currentStyle == VisualizerStyle.SmoothBars ||
                             _currentStyle == VisualizerStyle.SplitSmoothBars ||
                             _currentStyle == VisualizerStyle.PerspectiveRoad) ? 256 : 64;

                if (_currentStyle == VisualizerStyle.MirroredMountains || _currentStyle == VisualizerStyle.SmoothBars)
                {
                    _shapes = new Shape?[1];
                    _shapes[0] = new Path { Fill = primary, Stroke = null, StrokeThickness = 0 };
                    DrawCanvas.Children.Add(_shapes[0]);
                }
                else if (_currentStyle == VisualizerStyle.SplitSmoothBars)
                {
                    _shapes = new Shape?[2];
                    _shapes[0] = new Path { Fill = primary, Stroke = null, StrokeThickness = 0 };
                    _shapes[1] = new Path { Fill = primary, Stroke = null, StrokeThickness = 0 };
                    DrawCanvas.Children.Add(_shapes[0]);
                    DrawCanvas.Children.Add(_shapes[1]);
                }
                else if (_currentStyle == VisualizerStyle.PerspectiveRoad)
                {
                    _shapes = new Shape?[4];
                    _shapes[0] = new Path { Fill = primary, Stroke = null };
                    _shapes[1] = new Path { Fill = primary, Stroke = null };
                    _shapes[2] = new Line { Stroke = primary, StrokeThickness = 2, Opacity = 0.3 };
                    _shapes[3] = new Line { Stroke = primary, StrokeThickness = 2, Opacity = 0.3 };

                    DrawCanvas.Children.Add(_shapes[0]);
                    DrawCanvas.Children.Add(_shapes[1]);
                    DrawCanvas.Children.Add(_shapes[2]);
                    DrawCanvas.Children.Add(_shapes[3]);
                }
                else if (_currentStyle == VisualizerStyle.Oscilloscope)
                {
                    int pointsCount = 128;       
                    _shapes = new Shape?[pointsCount - 1];

                    for (int i = 0; i < pointsCount - 1; i++)
                    {
                        var line = new Line
                        {
                            Stroke = primary,
                            StrokeThickness = 3.0,      
                            StrokeStartLineCap = PenLineCap.Round,
                            StrokeEndLineCap = PenLineCap.Round
                        };
                        _shapes[i] = line;
                        DrawCanvas.Children.Add(line);
                    }
                }
                else
                {
                    _shapes = new Shape?[bands];
                    for (int i = 0; i < bands; i++)
                    {
                        Shape? shape = null;   
                        if (_currentStyle == VisualizerStyle.ClassicBars)
                        {
                            shape = new Rectangle { Fill = primary, Width = Math.Max(2, (DrawCanvas.ActualWidth / bands) - 4), RadiusX = 4, RadiusY = 4 };
                        }
                        else if (_currentStyle == VisualizerStyle.Radial)
                        {
                            shape = new Line { Stroke = primary, StrokeThickness = 6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                        }
                        _shapes[i] = shape;
                        if (shape != null) DrawCanvas.Children.Add(shape);
                    }
                }
            }
        }

        private System.Diagnostics.Stopwatch _fpsTimer = System.Diagnostics.Stopwatch.StartNew();

        private void OnRenderFrame(object? sender, EventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            if (vm.IsFpsLimited)
            {
                double targetMs = 1000.0 / vm.TargetFps;
                if (_fpsTimer.ElapsedMilliseconds < (targetMs - 1.5)) return;
                _fpsTimer.Restart();
            }

            double targetProgress = double.IsNaN(vm.WaveformProgress) ? 0 : vm.WaveformProgress;
            ProgressScale.ScaleX = Math.Max(0, Math.Min(1, targetProgress));

            if (_shapes.Length > 0 && _shapes[0] != null)
            {
                bool needsUpdate = false;
                if (_shapes[0] is Path p && (p.Fill != vm.LightColor && p.Stroke != vm.LightColor)) needsUpdate = true;
                else if (_shapes[0] is Line l && l.Stroke != vm.LightColor) needsUpdate = true;
                else if (_shapes[0]!.Fill != vm.LightColor) needsUpdate = true;

                if (needsUpdate)
                {
                    foreach (var s in _shapes)
                    {
                        if (s == null) continue;    

                        if (s is Line line) line.Stroke = vm.LightColor;
                        else if (s is Path path)
                        {
                            if (path.Fill != null) path.Fill = vm.LightColor;
                            if (path.Stroke != null) path.Stroke = vm.LightColor;
                        }
                        else s.Fill = vm.LightColor;
                    }
                }
            }

            double centerY = DrawCanvas.ActualHeight * 0.5;
            double bottomY = DrawCanvas.ActualHeight;
            double centerX = DrawCanvas.ActualWidth * 0.5;

            if (_currentStyle == VisualizerStyle.Oscilloscope && _shapes.Length > 0 && _shapes[0] is Line)
            {
                float[] wave = (vm.IsPlaying && vm.WaveData != null && vm.WaveData.Length > 0) ? vm.WaveData : _emptyWave;
                int triggerIndex = 0;
                if (vm.IsPlaying && wave.Length >= 1024)
                {
                    for (int i = 0; i < 1024; i++)
                    {
                        if (wave[i] < 0 && wave[i + 1] >= 0) { triggerIndex = i; break; }
                    }
                }

                int pointsCount = 128;     
                int step = Math.Max(1, (wave.Length - triggerIndex) / pointsCount);
                double stepX = DrawCanvas.ActualWidth / (pointsCount - 1);
                double amp = centerY * 0.24;           

                for (int i = 0; i < pointsCount - 1; i++)
                {
                    int waveIndex = triggerIndex + (i * step);
                    int nextIndex = triggerIndex + ((i + 1) * step);
                    if (nextIndex + 1 >= wave.Length) break;

                    double val1 = (wave[waveIndex] + wave[waveIndex + 1]) * 0.5;
                    double val2 = (wave[nextIndex] + wave[nextIndex + 1]) * 0.5;

                    if (_shapes[i] is Line line)
                    {
                        line.X1 = i * stepX; line.Y1 = centerY - (val1 * amp);
                        line.X2 = (i + 1) * stepX; line.Y2 = centerY - (val2 * amp);
                    }
                }
                return;
            }

            float[] fft = (vm.IsPlaying && vm.FftData != null && vm.FftData.Length >= 1024) ? vm.FftData : _emptyFft;
            float[] stereoFft = (vm.IsPlaying && vm.StereoFftData != null && vm.StereoFftData.Length >= 2048) ? vm.StereoFftData : _emptyStereoFft;

            int bands = (_currentStyle == VisualizerStyle.MirroredMountains || _currentStyle == VisualizerStyle.SmoothBars || _currentStyle == VisualizerStyle.SplitSmoothBars || _currentStyle == VisualizerStyle.PerspectiveRoad) ? 256 : 64;
            double maxHeight = DrawCanvas.ActualHeight * 0.5;
            double splitMaxHeight = DrawCanvas.ActualHeight * 0.46;

            for (int i = 0; i < bands; i++)
            {
                int lowBin = (int)Math.Pow(2, i * 9.0 / bands) + i;
                int highBin = (int)Math.Pow(2, (i + 1) * 9.0 / bands) + i + 1;
                if (highBin <= lowBin) highBin = lowBin + 1;

                float maxMono = 0, maxLeft = 0, maxRight = 0;
                for (int j = lowBin; j < highBin && j < 1024; j++)
                {
                    if (fft[j] > maxMono) maxMono = fft[j];
                    if (stereoFft[j * 2] > maxLeft) maxLeft = stereoFft[j * 2];
                    if (stereoFft[j * 2 + 1] > maxRight) maxRight = stereoFft[j * 2 + 1];
                }

                double rawMono = Math.Min(Math.Sqrt(maxMono) * (1.0 + i * 0.05) * 450, maxHeight);
                _smoothedData[i] += (rawMono - _smoothedData[i]) * (rawMono > _smoothedData[i] ? 0.4 : 0.05);

                double rawLeft = Math.Min(Math.Sqrt(maxLeft) * (1.0 + i * 0.05) * 450, splitMaxHeight);
                _smoothedDataLeft[i] += (rawLeft - _smoothedDataLeft[i]) * (rawLeft > _smoothedDataLeft[i] ? 0.4 : 0.05);

                double rawRight = Math.Min(Math.Sqrt(maxRight) * (1.0 + i * 0.05) * 450, splitMaxHeight);
                _smoothedDataRight[i] += (rawRight - _smoothedDataRight[i]) * (rawRight > _smoothedDataRight[i] ? 0.4 : 0.05);

                if (_currentStyle == VisualizerStyle.ClassicBars || _currentStyle == VisualizerStyle.Radial)
                {
                    if (i >= _shapes.Length) break;
                    var shape = _shapes[i];

                    if (_currentStyle == VisualizerStyle.ClassicBars && shape is Rectangle rectBar)
                    {
                        double h = Math.Max(4, _smoothedData[i]);
                        rectBar.Height = h;
                        Canvas.SetLeft(rectBar, i * (DrawCanvas.ActualWidth / bands) + 2);
                        Canvas.SetTop(rectBar, DrawCanvas.ActualHeight - h - 6);
                    }
                    else if (_currentStyle == VisualizerStyle.Radial && shape is Line line)
                    {
                        double radius = 150;
                        double angle = (i * 360.0 / bands) * (Math.PI / 180.0) - Math.PI / 2;
                        double length = Math.Max(4, _smoothedData[i] * 0.8);
                        line.X1 = centerX + Math.Cos(angle) * radius; line.Y1 = centerY + Math.Sin(angle) * radius;
                        line.X2 = centerX + Math.Cos(angle) * (radius + length); line.Y2 = centerY + Math.Sin(angle) * (radius + length);
                    }
                }
            }

            if ((_currentStyle == VisualizerStyle.MirroredMountains || _currentStyle == VisualizerStyle.SmoothBars || _currentStyle == VisualizerStyle.SplitSmoothBars || _currentStyle == VisualizerStyle.PerspectiveRoad) && _shapes.Length > 0 && _shapes[0] is Path polyPath)
            {
                Array.Copy(_smoothedData, _displayData, bands);
                Array.Copy(_smoothedDataLeft, _displayDataLeft, bands);
                Array.Copy(_smoothedDataRight, _displayDataRight, bands);

                int smoothingPasses = bands == 256 ? 6 : 3;
                for (int pass = 0; pass < smoothingPasses; pass++)
                {
                    for (int i = 0; i < bands; i++)
                    {
                        double prev = i > 0 ? _displayData[i - 1] : _displayData[i];
                        double next = i < bands - 1 ? _displayData[i + 1] : _displayData[i];
                        _tempData[i] = (prev + _displayData[i] + next) / 3.0;

                        double prevL = i > 0 ? _displayDataLeft[i - 1] : _displayDataLeft[i];
                        double nextL = i < bands - 1 ? _displayDataLeft[i + 1] : _displayDataLeft[i];
                        _tempDataLeft[i] = (prevL + _displayDataLeft[i] + nextL) / 3.0;

                        double prevR = i > 0 ? _displayDataRight[i - 1] : _displayDataRight[i];
                        double nextR = i < bands - 1 ? _displayDataRight[i + 1] : _displayDataRight[i];
                        _tempDataRight[i] = (prevR + _displayDataRight[i] + nextR) / 3.0;
                    }
                    Array.Copy(_tempData, _displayData, bands);
                    Array.Copy(_tempDataLeft, _displayDataLeft, bands);
                    Array.Copy(_tempDataRight, _displayDataRight, bands);
                }

                double stepX = DrawCanvas.ActualWidth / (bands - 1);

                if (_currentStyle == VisualizerStyle.PerspectiveRoad && _shapes.Length >= 4)
                {
                    double horizonY = DrawCanvas.ActualHeight * 0.45;

                    if (_shapes[2] is Line leftLine)
                    {
                        leftLine.X1 = 0; leftLine.Y1 = bottomY; leftLine.X2 = centerX; leftLine.Y2 = horizonY;
                    }
                    if (_shapes[3] is Line rightLine)
                    {
                        rightLine.X1 = DrawCanvas.ActualWidth; rightLine.Y1 = bottomY; rightLine.X2 = centerX; rightLine.Y2 = horizonY;
                    }

                    StreamGeometry leftGeom = new StreamGeometry();
                    using (StreamGeometryContext ctx = leftGeom.Open())
                    {
                        for (int i = 0; i < bands; i++)
                        {
                            double t = (double)i / (bands - 1);
                            double perspective = Math.Pow(1.0 - t, 2.5);
                            double currentY = horizonY + (bottomY - horizonY) * perspective;
                            double scale = 0.05 + 0.95 * perspective;
                            double leftX = centerX - (centerX * perspective);
                            double h = _displayDataLeft[i] * scale * 1.5;
                            _roadLeftPoints[i] = new Point(leftX, currentY - h);
                            _roadLeftPoints[bands * 2 - 1 - i] = new Point(leftX, currentY);
                        }
                        ctx.BeginFigure(_roadLeftPoints[0], true, true);
                        ctx.PolyLineTo(new ArraySegment<Point>(_roadLeftPoints, 0, bands * 2), true, false);
                    }
                    leftGeom.Freeze();
                    if (_shapes[0] is Path p0) p0.Data = leftGeom;

                    StreamGeometry rightGeom = new StreamGeometry();
                    using (StreamGeometryContext ctx = rightGeom.Open())
                    {
                        for (int i = 0; i < bands; i++)
                        {
                            double t = (double)i / (bands - 1);
                            double perspective = Math.Pow(1.0 - t, 2.5);
                            double currentY = horizonY + (bottomY - horizonY) * perspective;
                            double scale = 0.05 + 0.95 * perspective;
                            double rightX = centerX + (centerX * perspective);
                            double h = _displayDataRight[i] * scale * 1.5;
                            _roadRightPoints[i] = new Point(rightX, currentY - h);
                            _roadRightPoints[bands * 2 - 1 - i] = new Point(rightX, currentY);
                        }
                        ctx.BeginFigure(_roadRightPoints[0], true, true);
                        ctx.PolyLineTo(new ArraySegment<Point>(_roadRightPoints, 0, bands * 2), true, false);
                    }
                    rightGeom.Freeze();
                    if (_shapes[1] is Path p1) p1.Data = rightGeom;
                }
                else if (_currentStyle == VisualizerStyle.SplitSmoothBars && _shapes.Length > 1)
                {
                    StreamGeometry topGeom = new StreamGeometry();
                    using (StreamGeometryContext ctx = topGeom.Open())
                    {
                        ctx.BeginFigure(new Point(0, 0), true, true);
                        for (int i = 0; i < bands; i++) _barPoints[i] = new Point(i * stepX, Math.Max(2, _displayDataLeft[i]));
                        _barPoints[bands] = new Point(DrawCanvas.ActualWidth, 0);
                        ctx.PolyLineTo(new ArraySegment<Point>(_barPoints, 0, bands + 1), true, false);
                    }
                    topGeom.Freeze();
                    if (_shapes[0] is Path p0) p0.Data = topGeom;

                    StreamGeometry botGeom = new StreamGeometry();
                    using (StreamGeometryContext ctx = botGeom.Open())
                    {
                        ctx.BeginFigure(new Point(0, bottomY), true, true);
                        for (int i = 0; i < bands; i++) _barPoints[i] = new Point(i * stepX, bottomY - Math.Max(2, _displayDataRight[i]));
                        _barPoints[bands] = new Point(DrawCanvas.ActualWidth, bottomY);
                        ctx.PolyLineTo(new ArraySegment<Point>(_barPoints, 0, bands + 1), true, false);
                    }
                    botGeom.Freeze();
                    if (_shapes[1] is Path p1) p1.Data = botGeom;
                }
                else if (_currentStyle == VisualizerStyle.MirroredMountains || _currentStyle == VisualizerStyle.SmoothBars)
                {
                    StreamGeometry geom = new StreamGeometry();
                    using (StreamGeometryContext ctx = geom.Open())
                    {
                        if (_currentStyle == VisualizerStyle.MirroredMountains)
                        {
                            ctx.BeginFigure(new Point(0, centerY), true, true);
                            for (int i = 0; i < bands; i++) _topPoints[i] = new Point(i * stepX, centerY - Math.Max(2, _displayData[i] * 0.8));
                            ctx.PolyLineTo(new ArraySegment<Point>(_topPoints, 0, bands), true, false);
                            for (int i = 0; i < bands; i++) _bottomPoints[i] = new Point((bands - 1 - i) * stepX, centerY + Math.Max(2, _displayData[bands - 1 - i] * 0.8));
                            ctx.PolyLineTo(new ArraySegment<Point>(_bottomPoints, 0, bands), true, false);
                        }
                        else if (_currentStyle == VisualizerStyle.SmoothBars)
                        {
                            ctx.BeginFigure(new Point(0, bottomY), true, true);
                            for (int i = 0; i < bands; i++) _topPoints[i] = new Point(i * stepX, bottomY - Math.Max(2, _displayData[i]));
                            _topPoints[bands] = new Point(DrawCanvas.ActualWidth, bottomY);
                            ctx.PolyLineTo(new ArraySegment<Point>(_topPoints, 0, bands + 1), true, false);
                        }
                    }
                    geom.Freeze();
                    polyPath.Data = geom;         
                }
            }
        }
    }
}