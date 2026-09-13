using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace JLS.Controls
{
    public class WaveformControl : Grid
    {
        public static readonly DependencyProperty WaveformRenderStyleProperty =
            DependencyProperty.Register("WaveformRenderStyle", typeof(JLS.ViewModels.WaveformStyle), typeof(WaveformControl), new PropertyMetadata(JLS.ViewModels.WaveformStyle.Dj, OnGeometryDataChanged));

        public JLS.ViewModels.WaveformStyle WaveformRenderStyle
        {
            get => (JLS.ViewModels.WaveformStyle)GetValue(WaveformRenderStyleProperty);
            set => SetValue(WaveformRenderStyleProperty, value);
        }

        public static readonly DependencyProperty LeftPeaksProperty =
            DependencyProperty.Register("LeftPeaks", typeof(float[]), typeof(WaveformControl), new PropertyMetadata(null, OnGeometryDataChanged));

        public static readonly DependencyProperty RightPeaksProperty =
            DependencyProperty.Register("RightPeaks", typeof(float[]), typeof(WaveformControl), new PropertyMetadata(null, OnGeometryDataChanged));

        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register("Progress", typeof(double), typeof(WaveformControl), new PropertyMetadata(0.0, OnProgressChanged));

        public static readonly DependencyProperty PlayedBrushProperty =
            DependencyProperty.Register("PlayedBrush", typeof(Brush), typeof(WaveformControl), new PropertyMetadata(Brushes.White, OnColorChanged));

        public static readonly DependencyProperty UnplayedBrushProperty =
            DependencyProperty.Register("UnplayedBrush", typeof(Brush), typeof(WaveformControl), new PropertyMetadata(Brushes.DarkGray, OnColorChanged));

        public float[] LeftPeaks { get => (float[])GetValue(LeftPeaksProperty); set => SetValue(LeftPeaksProperty, value); }
        public float[] RightPeaks { get => (float[])GetValue(RightPeaksProperty); set => SetValue(RightPeaksProperty, value); }
        public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
        public Brush PlayedBrush { get => (Brush)GetValue(PlayedBrushProperty); set => SetValue(PlayedBrushProperty, value); }
        public Brush UnplayedBrush { get => (Brush)GetValue(UnplayedBrushProperty); set => SetValue(UnplayedBrushProperty, value); }

        private readonly Grid _innerGrid;
        private readonly Rectangle _unplayedRect;
        private readonly Rectangle _playedRect;
        private readonly ScaleTransform _progressTransform;

        public WaveformControl()
        {
            _innerGrid = new Grid();
            _unplayedRect = new Rectangle();
            _playedRect = new Rectangle { HorizontalAlignment = HorizontalAlignment.Left };

            _progressTransform = new ScaleTransform(0, 1);
            _playedRect.RenderTransform = _progressTransform;
            _playedRect.RenderTransformOrigin = new Point(0, 0);

            _innerGrid.Children.Add(_unplayedRect);
            _innerGrid.Children.Add(_playedRect);

            this.Children.Add(_innerGrid);

            RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
            UpdateColors();
        }

        private static void OnGeometryDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((WaveformControl)d).RebuildMask();
        private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((WaveformControl)d).UpdateProgress();
        private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((WaveformControl)d).UpdateColors();

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            _playedRect.Width = sizeInfo.NewSize.Width;
            RebuildMask();
        }

        private void UpdateColors()
        {
            _unplayedRect.Fill = UnplayedBrush;
            _playedRect.Fill = PlayedBrush;
        }

        private void UpdateProgress()
        {
            _progressTransform.ScaleX = double.IsNaN(Progress) ? 0 : Math.Max(0, Math.Min(1, Progress));
        }

        private void RebuildMask()
        {
            if (LeftPeaks == null || RightPeaks == null || LeftPeaks.Length == 0 || ActualWidth <= 0 || ActualHeight <= 0)
            {
                _innerGrid.OpacityMask = null;
                return;
            }

            double width = ActualWidth;
            double height = ActualHeight;

            double scaleX = 1.0;
            double scaleY = 1.0;
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                scaleX = source.CompositionTarget.TransformToDevice.M11;
                scaleY = source.CompositionTarget.TransformToDevice.M22;
            }

            double dpiX = 96.0 * scaleX;
            double dpiY = 96.0 * scaleY;

            int pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scaleX));
            int pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scaleY));

            int rawCount = LeftPeaks.Length;
            int renderPoints = Math.Min(rawCount, pixelWidth);

            float[] drawLeft = LeftPeaks;
            float[] drawRight = RightPeaks;

            if (rawCount > renderPoints && renderPoints > 0)
            {
                drawLeft = new float[renderPoints];
                drawRight = new float[renderPoints];
                double chunk = (double)rawCount / renderPoints;

                for (int i = 0; i < renderPoints; i++)
                {
                    int start = (int)(i * chunk);
                    int end = (int)((i + 1) * chunk);
                    if (end > rawCount) end = rawCount;

                    float maxL = 0f;
                    float maxR = 0f;
                    for (int j = start; j < end; j++)
                    {
                        if (LeftPeaks[j] > maxL) maxL = LeftPeaks[j];
                        if (j < RightPeaks.Length && RightPeaks[j] > maxR) maxR = RightPeaks[j];
                    }
                    drawLeft[i] = maxL;
                    drawRight[i] = maxR;
                }
            }

            double stepX = width / (renderPoints > 1 ? renderPoints - 1 : 1);

            var visual = new DrawingVisual();
            RenderOptions.SetEdgeMode(visual, EdgeMode.Unspecified);

            using (var dc = visual.RenderOpen())
            {
                if (WaveformRenderStyle == JLS.ViewModels.WaveformStyle.MountainBars ||
                    WaveformRenderStyle == JLS.ViewModels.WaveformStyle.SolidBars ||
                    WaveformRenderStyle == JLS.ViewModels.WaveformStyle.StereoBars)
                {
                    double barWidth = 3.0;
                    double barSpacing = 5.0;
                    int barCount = (int)(width / barSpacing);
                    double cornerRadius = 1.2;
                    double halfHeight = height / 2.0;

                    if (WaveformRenderStyle == JLS.ViewModels.WaveformStyle.MountainBars)
                    {
                        double maxH = height * 0.95;
                        double baseLine = height - 2;

                        for (int i = 0; i < barCount; i++)
                        {
                            double x = i * barSpacing;
                            int dataIndex = Math.Max(0, Math.Min((int)((x / width) * (renderPoints - 1)), renderPoints - 1));

                            float mixedPeak = Math.Max(drawLeft[dataIndex], drawRight[dataIndex]);
                            double h = Math.Max(3, mixedPeak * maxH);

                            dc.DrawRoundedRectangle(Brushes.White, null, new Rect(x, baseLine - h, barWidth, h), cornerRadius, cornerRadius);
                        }
                    }
                    else if (WaveformRenderStyle == JLS.ViewModels.WaveformStyle.SolidBars)
                    {
                        double maxH = halfHeight * 0.95;

                        for (int i = 0; i < barCount; i++)
                        {
                            double x = i * barSpacing;
                            int dataIndex = Math.Max(0, Math.Min((int)((x / width) * (renderPoints - 1)), renderPoints - 1));

                            float mixedPeak = Math.Max(drawLeft[dataIndex], drawRight[dataIndex]);
                            double h = Math.Max(2, mixedPeak * maxH);

                            dc.DrawRoundedRectangle(Brushes.White, null, new Rect(x, halfHeight - h, barWidth, h * 2), cornerRadius, cornerRadius);
                        }
                    }
                    else
                    {
                        double maxH = halfHeight * 0.95;

                        for (int i = 0; i < barCount; i++)
                        {
                            double x = i * barSpacing;
                            int dataIndex = Math.Max(0, Math.Min((int)((x / width) * (renderPoints - 1)), renderPoints - 1));

                            float leftPeak = drawLeft[dataIndex];
                            float rightPeak = drawRight[dataIndex];

                            double hTop = Math.Max(2, leftPeak * maxH);
                            double hBot = Math.Max(2, rightPeak * maxH);

                            dc.DrawRoundedRectangle(Brushes.White, null, new Rect(x, halfHeight - 1 - hTop, barWidth, hTop), cornerRadius, cornerRadius);
                            dc.DrawRoundedRectangle(Brushes.White, null, new Rect(x, halfHeight + 1, barWidth, hBot), cornerRadius, cornerRadius);
                        }
                    }
                }
                else
                {
                    var geometry = new StreamGeometry();
                    using (var ctx = geometry.Open())
                    {
                        double halfHeight = height / 2.0;

                        switch (WaveformRenderStyle)
                        {
                            case JLS.ViewModels.WaveformStyle.Solid:
                                double maxHSolid = halfHeight * 0.95;

                                ctx.BeginFigure(new Point(0, halfHeight), true, true);
                                for (int i = 0; i < renderPoints; i++)
                                {
                                    float mixedPeak = Math.Max(drawLeft[i], drawRight[i]);
                                    ctx.LineTo(new Point(i * stepX, halfHeight - Math.Max(1, mixedPeak * maxHSolid)), true, false);
                                }
                                for (int i = renderPoints - 1; i >= 0; i--)
                                {
                                    float mixedPeak = Math.Max(drawLeft[i], drawRight[i]);
                                    ctx.LineTo(new Point(i * stepX, halfHeight + Math.Max(1, mixedPeak * maxHSolid)), true, false);
                                }
                                break;

                            case JLS.ViewModels.WaveformStyle.Stereo:
                                double qH = height / 4.0;
                                double tqH = height * 0.75;
                                double maxH = qH * 0.90;

                                ctx.BeginFigure(new Point(0, qH - Math.Max(1, drawLeft[0] * maxH)), true, true);
                                for (int i = 1; i < renderPoints; i++)
                                    ctx.LineTo(new Point(i * stepX, qH - Math.Max(1, drawLeft[i] * maxH)), true, false);
                                for (int i = renderPoints - 1; i >= 0; i--)
                                    ctx.LineTo(new Point(i * stepX, qH + Math.Max(1, drawLeft[i] * maxH)), true, false);

                                ctx.BeginFigure(new Point(0, tqH - Math.Max(1, drawRight[0] * maxH)), true, true);
                                for (int i = 1; i < renderPoints; i++)
                                    ctx.LineTo(new Point(i * stepX, tqH - Math.Max(1, drawRight[i] * maxH)), true, false);
                                for (int i = renderPoints - 1; i >= 0; i--)
                                    ctx.LineTo(new Point(i * stepX, tqH + Math.Max(1, drawRight[i] * maxH)), true, false);
                                break;

                            case JLS.ViewModels.WaveformStyle.Dj:
                                double maxHDj = halfHeight * 0.95;

                                ctx.BeginFigure(new Point(0, halfHeight - 1), true, true);
                                for (int i = 0; i < renderPoints; i++)
                                    ctx.LineTo(new Point(i * stepX, halfHeight - 1 - Math.Max(1, drawLeft[i] * maxHDj)), true, false);
                                ctx.LineTo(new Point(width, halfHeight - 1), true, false);

                                ctx.BeginFigure(new Point(0, halfHeight + 1), true, true);
                                for (int i = 0; i < renderPoints; i++)
                                    ctx.LineTo(new Point(i * stepX, halfHeight + 1 + Math.Max(1, drawRight[i] * maxHDj)), true, false);
                                ctx.LineTo(new Point(width, halfHeight + 1), true, false);
                                break;

                            case JLS.ViewModels.WaveformStyle.Mountains:
                                double maxHMount = height * 0.95;
                                double baseLineMount = height - 0;

                                ctx.BeginFigure(new Point(0, baseLineMount), true, true);
                                for (int i = 0; i < renderPoints; i++)
                                {
                                    float mixedPeak = Math.Max(drawLeft[i], drawRight[i]);
                                    ctx.LineTo(new Point(i * stepX, baseLineMount - Math.Max(3, mixedPeak * maxHMount)), true, false);
                                }
                                ctx.LineTo(new Point(width, baseLineMount), true, false);
                                break;
                        }
                    }
                    geometry.Freeze();
                    dc.DrawGeometry(Brushes.White, null, geometry);
                }
            }

            var renderTarget = new RenderTargetBitmap(pixelWidth, pixelHeight, dpiX, dpiY, PixelFormats.Pbgra32);
            renderTarget.Render(visual);

            var imageBrush = new ImageBrush(renderTarget)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };
            imageBrush.Freeze();

            _innerGrid.OpacityMask = imageBrush;
        }
    }
}