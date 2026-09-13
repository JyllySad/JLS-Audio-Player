using System;
using System.Windows;
using System.Windows.Media;

namespace JLS.Controls
{
    public class AnimatedProgressBar : FrameworkElement
    {
        public static readonly DependencyProperty LeftPeaksProperty =
            DependencyProperty.Register("LeftPeaks", typeof(float[]), typeof(AnimatedProgressBar), new PropertyMetadata(null, OnDataChanged));

        public static readonly DependencyProperty RightPeaksProperty =
            DependencyProperty.Register("RightPeaks", typeof(float[]), typeof(AnimatedProgressBar), new PropertyMetadata(null, OnDataChanged));

        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register("Progress", typeof(double), typeof(AnimatedProgressBar), new PropertyMetadata(0.0, (d, e) => ((AnimatedProgressBar)d).InvalidateVisual()));

        public static readonly DependencyProperty IsPlayingProperty =
            DependencyProperty.Register("IsPlaying", typeof(bool), typeof(AnimatedProgressBar), new PropertyMetadata(false, (d, e) => ((AnimatedProgressBar)d).InvalidateVisual()));

        public static readonly DependencyProperty PlayedBrushProperty =
            DependencyProperty.Register("PlayedBrush", typeof(Brush), typeof(AnimatedProgressBar), new PropertyMetadata(Brushes.White, (d, e) => ((AnimatedProgressBar)d).InvalidateVisual()));

        public static readonly DependencyProperty UnplayedBrushProperty =
            DependencyProperty.Register("UnplayedBrush", typeof(Brush), typeof(AnimatedProgressBar), new PropertyMetadata(Brushes.DarkGray, (d, e) => ((AnimatedProgressBar)d).InvalidateVisual()));

        public static readonly DependencyProperty IsFrozenProperty =
            DependencyProperty.Register("IsFrozen", typeof(bool), typeof(AnimatedProgressBar), new PropertyMetadata(false));

        public static readonly DependencyProperty FftDataProperty =
    DependencyProperty.Register("FftData", typeof(float[]), typeof(AnimatedProgressBar), new PropertyMetadata(null));

        public float[] FftData
        {
            get => (float[])GetValue(FftDataProperty);
            set => SetValue(FftDataProperty, value);
        }

        public bool IsFrozen { get => (bool)GetValue(IsFrozenProperty); set => SetValue(IsFrozenProperty, value); }

        public float[] LeftPeaks { get => (float[])GetValue(LeftPeaksProperty); set => SetValue(LeftPeaksProperty, value); }
        public float[] RightPeaks { get => (float[])GetValue(RightPeaksProperty); set => SetValue(RightPeaksProperty, value); }
        public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
        public bool IsPlaying { get => (bool)GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }
        public Brush PlayedBrush { get => (Brush)GetValue(PlayedBrushProperty); set => SetValue(PlayedBrushProperty, value); }
        public Brush UnplayedBrush { get => (Brush)GetValue(UnplayedBrushProperty); set => SetValue(UnplayedBrushProperty, value); }

        private double[] _baseHeights = Array.Empty<double>();
        private double[] _currentHeights = Array.Empty<double>();
        private double[] _currentWidths = Array.Empty<double>();
        private double[] _targetTwitch = Array.Empty<double>();
        private double[] _transitions = Array.Empty<double>();
        private DateTime[] _lastTwitchTimes = Array.Empty<DateTime>();

        private int _barsCount = 0;
        private Random _rnd = new Random();

        public AnimatedProgressBar()
        {
            SnapsToDevicePixels = true;

            Loaded += (s, e) =>
            {
                CompositionTarget.Rendering -= OnRendering;
                CompositionTarget.Rendering += OnRendering;
                RebuildData();
            };

            Unloaded += (s, e) =>
            {
                CompositionTarget.Rendering -= OnRendering;
            };

            IsVisibleChanged += (s, e) =>
            {
                if ((bool)e.NewValue)
                {
                    RebuildData();
                    InvalidateVisual();
                }
            };
        }

        private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((AnimatedProgressBar)d).RebuildData();

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            RebuildData();
        }

        private static bool IsValidDouble(double val)
        {
            return !double.IsNaN(val) && !double.IsInfinity(val);
        }

        private void RebuildData()
        {
            try
            {
                var peaks = LeftPeaks;
                var rPeaks = RightPeaks;

                if (peaks == null || peaks.Length == 0 || !IsValidDouble(ActualWidth) || !IsValidDouble(ActualHeight) || ActualWidth <= 0 || ActualHeight <= 0)
                {
                    _barsCount = 0;
                    InvalidateVisual();
                    return;
                }

                double barTotalWidth = 9.0;

                int newBarsCount = Math.Max(1, Math.Min(2000, (int)(ActualWidth / barTotalWidth)));

                var newBaseHeights = new double[newBarsCount];
                var newCurrentHeights = new double[newBarsCount];
                var newCurrentWidths = new double[newBarsCount];
                var newTargetTwitch = new double[newBarsCount];
                var newTransitions = new double[newBarsCount];
                var newLastTwitchTimes = new DateTime[newBarsCount];

                int rawCount = peaks.Length;
                double chunk = (double)rawCount / newBarsCount;

                double maxH = (ActualHeight / 2.0) * 0.80;
                double minH = Math.Max(2, maxH * 0.1);

                for (int i = 0; i < newBarsCount; i++)
                {
                    int start = (int)(i * chunk);
                    int end = (int)((i + 1) * chunk);
                    if (end > rawCount) end = rawCount;

                    float maxPeak = 0f;
                    if (start < end)
                    {
                        for (int j = start; j < end; j++)
                        {
                            float p = peaks[j];
                            if (float.IsNaN(p) || float.IsInfinity(p)) p = 0f;

                            if (rPeaks != null && j < rPeaks.Length)
                            {
                                float rp = rPeaks[j];
                                if (!float.IsNaN(rp) && !float.IsInfinity(rp) && rp > p) p = rp;
                            }
                            if (p > maxPeak) maxPeak = p;
                        }
                    }

                    double compressed = Math.Pow(Math.Max(0, maxPeak), 0.80);
                    if (!IsValidDouble(compressed)) compressed = 0;

                    newBaseHeights[i] = minH + (maxH - minH) * compressed;
                    newCurrentHeights[i] = minH + (newBaseHeights[i] * 0.5 - minH) * 0.2;
                    newCurrentWidths[i] = barTotalWidth * 0.30;
                    newTargetTwitch[i] = 1.0;
                    newTransitions[i] = 0.0;
                    newLastTwitchTimes[i] = DateTime.Now.AddMilliseconds(-_rnd.Next(0, 400));
                }

                _baseHeights = newBaseHeights;
                _currentHeights = newCurrentHeights;
                _currentWidths = newCurrentWidths;
                _targetTwitch = newTargetTwitch;
                _transitions = newTransitions;
                _lastTwitchTimes = newLastTwitchTimes;

                _barsCount = newBarsCount;
                InvalidateVisual();
            }
            catch
            {
                _barsCount = 0;
            }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            try
            {
                if (_barsCount == 0 && ActualWidth > 0 && ActualHeight > 0 && LeftPeaks != null && LeftPeaks.Length > 0)
                {
                    RebuildData();
                }

                int count = _barsCount;
                if (count == 0 || !IsValidDouble(ActualHeight) || !IsValidDouble(ActualWidth) || ActualHeight <= 0 || ActualWidth <= 0) return;

                if (_transitions.Length < count || _currentWidths.Length < count || _currentHeights.Length < count ||
                    _baseHeights.Length < count || _targetTwitch.Length < count || _lastTwitchTimes.Length < count)
                {
                    return;
                }

                bool needsRedraw = false;
                double validProgress = IsValidDouble(Progress) ? Math.Max(0, Math.Min(1, Progress)) : 0;
                double barTotalWidth = ActualWidth / count;

                double maxH = (ActualHeight / 2.0) * 0.80;
                double minH = Math.Max(2, maxH * 0.1);
                DateTime now = DateTime.Now;

                float[] fft = FftData;
                double overallVolume = 0;
                if (fft != null && fft.Length >= 1024)
                {
                    for (int v = 0; v < 100; v++)
                    {
                        if (fft[v] > overallVolume) overallVolume = fft[v];
                    }
                }

                for (int i = 0; i < count; i++)
                {
                    double barStartThreshold = i / (double)count;
                    double barEndThreshold = (i + 1) / (double)count;

                    double targetTransition = 0.0;
                    if (validProgress >= barEndThreshold)
                    {
                        targetTransition = 1.0;
                    }
                    else if (validProgress > barStartThreshold)
                    {
                        double localProgress = (validProgress - barStartThreshold) / (barEndThreshold - barStartThreshold);
                        double delayFactor = 0.80;

                        if (localProgress > delayFactor)
                        {
                            targetTransition = (localProgress - delayFactor) / (1.0 - delayFactor);
                        }
                    }

                    double diffT = targetTransition - _transitions[i];
                    if (Math.Abs(diffT) > 0.005)
                    {
                        _transitions[i] += diffT * 0.30;
                        needsRedraw = true;
                    }
                    else if (_transitions[i] != targetTransition)
                    {
                        _transitions[i] = targetTransition;
                        needsRedraw = true;
                    }

                    double t = _transitions[i];
                    if (!IsValidDouble(t)) t = 0;

                    double targetW = barTotalWidth * (0.30 + (0.35 * t));
                    if (!IsValidDouble(targetW)) targetW = barTotalWidth * 0.30;

                    double diffW = targetW - _currentWidths[i];
                    if (Math.Abs(diffW) > 0.05)
                    {
                        _currentWidths[i] += diffW * 0.30;
                        needsRedraw = true;
                    }
                    else if (_currentWidths[i] != targetW)
                    {
                        _currentWidths[i] = targetW;
                        needsRedraw = true;
                    }

                    double scaleTarget = 0.60 + (0.55 * t);
                    double targetH;

                    if (IsPlaying && !IsFrozen)
                    {
                        double positionPercent = i / (double)count;
                        double twitchInterval = 100 + (positionPercent * 300);

                        double heightDiff = maxH - minH;
                        double heightFactor = heightDiff > 0.001 ? (_baseHeights[i] - minH) / heightDiff : 0;
                        if (!IsValidDouble(heightFactor)) heightFactor = 0;

                        double unplayedAmp = 0.14;
                        double playedAmp = 0.12 + (heightFactor * 0.16);
                        double amplitude = unplayedAmp + ((playedAmp - unplayedAmp) * t);

                        if (fft != null && fft.Length >= 1024)
                        {
                            double playedReactivity = 0;
                            if (t > 0.01)
                            {
                                int playedCount = Math.Max(1, (int)(validProgress * count));
                                double relativePos = Math.Min(1.0, (double)i / playedCount);
                                double relativeNext = Math.Min(1.0, (double)(i + 1) / playedCount);

                                double stretchedPos = Math.Pow(relativePos, 0.75);
                                double stretchedNext = Math.Pow(relativeNext, 0.75);

                                double startBinExact = Math.Max(0, Math.Pow(2, stretchedPos * 9.0) - 1.0);
                                double endBinExact = Math.Max(0, Math.Pow(2, stretchedNext * 9.0) - 1.0);

                                int lowBin = (int)startBinExact;
                                int highBin = (int)Math.Ceiling(endBinExact);

                                float maxMono = 0;

                                if (highBin - lowBin <= 1)
                                {
                                    double centerBin = (startBinExact + endBinExact) / 2.0;
                                    int b1 = Math.Max(0, Math.Min(1023, (int)centerBin));
                                    int b2 = Math.Min(1023, b1 + 1);
                                    double fraction = centerBin - b1;

                                    maxMono = (float)((fft[b1] * (1.0 - fraction)) + (fft[b2] * fraction));
                                }
                                else
                                {
                                    for (int j = lowBin; j < highBin && j < 1024; j++)
                                    {
                                        if (fft[j] > maxMono) maxMono = fft[j];
                                    }
                                }

                                double freqBoost = 1.0 + (relativePos * 3.2);

                                double reactivityMultiplier = 2.6;
                                playedReactivity = Math.Sqrt(maxMono) * freqBoost * reactivityMultiplier;

                                double reactivityCeiling = 2.2;
                                if (playedReactivity > reactivityCeiling)
                                {
                                    playedReactivity = reactivityCeiling + Math.Pow(playedReactivity - reactivityCeiling, 0.4);
                                }
                            }

                            double unplayedReactivity = 0;
                            if (t < 0.99)
                            {
                                double randomWobble = 0.8 + 0.2 * Math.Sin((now.TimeOfDay.TotalMilliseconds * 0.006) + (i * 0.4));
                                unplayedReactivity = Math.Sqrt(overallVolume) * 2.4 * randomWobble;
                            }

                            double finalAudioReactivity = (unplayedReactivity * (1.0 - t)) + (playedReactivity * t);

                            _targetTwitch[i] = 1.0 + (amplitude * finalAudioReactivity);
                            _lastTwitchTimes[i] = now;
                        }
                        else
                        {
                            if ((now - _lastTwitchTimes[i]).TotalMilliseconds > twitchInterval)
                            {
                                double newTwitch = (1.0 - amplitude) + _rnd.NextDouble() * (amplitude * 2);
                                _targetTwitch[i] = IsValidDouble(newTwitch) ? newTwitch : 1.0;
                                _lastTwitchTimes[i] = now;
                            }
                        }

                        targetH = (_baseHeights[i] * scaleTarget) * _targetTwitch[i];
                        needsRedraw = true;
                    }
                    else
                    {
                        targetH = minH + ((_baseHeights[i] * scaleTarget) - minH) * 0.50;
                    }

                    if (!IsValidDouble(targetH)) targetH = minH;

                    double diffH = targetH - _currentHeights[i];
                    if (Math.Abs(diffH) > 0.05)
                    {
                        double motionSpeed = IsPlaying ? (0.09 + 0.15 * t) : 0.1;
                        _currentHeights[i] += diffH * motionSpeed;
                        needsRedraw = true;
                    }
                    else if (_currentHeights[i] != targetH)
                    {
                        _currentHeights[i] = targetH;
                        needsRedraw = true;
                    }
                }

                if (needsRedraw)
                {
                    InvalidateVisual();
                }
            }
            catch
            {

            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            try
            {
                int count = _barsCount;
                if (count == 0 || !IsValidDouble(ActualHeight) || !IsValidDouble(ActualWidth) || ActualHeight <= 0 || ActualWidth <= 0) return;

                double centerY = ActualHeight / 2.0;
                double barTotalWidth = ActualWidth / count;

                Brush safePlayed = PlayedBrush;
                if (safePlayed is SolidColorBrush spb && !spb.IsFrozen)
                {
                    safePlayed = new SolidColorBrush(spb.Color);
                    safePlayed.Freeze();
                }

                Brush safeUnplayed = UnplayedBrush;
                if (safeUnplayed is SolidColorBrush sub && !sub.IsFrozen)
                {
                    safeUnplayed = new SolidColorBrush(sub.Color);
                    safeUnplayed.Freeze();
                }

                for (int i = 0; i < count; i++)
                {
                    if (i >= _transitions.Length || i >= _currentHeights.Length || i >= _currentWidths.Length) break;

                    double t = _transitions[i];
                    if (!IsValidDouble(t)) t = 0;

                    double currentH = _currentHeights[i];
                    if (!IsValidDouble(currentH) || currentH < 2) currentH = 2;

                    double w = _currentWidths[i];
                    if (!IsValidDouble(w) || w < 1) w = 1;

                    double x = i * barTotalWidth + (barTotalWidth - w) / 2.0;
                    double y = centerY - currentH;
                    double cornerRadius = w / 2.0;

                    if (!IsValidDouble(x) || !IsValidDouble(y) || !IsValidDouble(cornerRadius) || cornerRadius < 0) continue;
                    if (w <= 0 || currentH <= 0 || w > 10000 || currentH > 10000 || x < -10000 || x > 10000) continue;

                    Rect rect = new Rect(x, y, w, currentH * 2);

                    if (t >= 0.99)
                    {
                        dc.DrawRoundedRectangle(safePlayed, null, rect, cornerRadius, cornerRadius);
                    }
                    else if (t <= 0.01)
                    {
                        dc.DrawRoundedRectangle(safeUnplayed, null, rect, cornerRadius, cornerRadius);
                    }
                    else
                    {
                        double safeOpacity = Math.Max(0.0, Math.Min(1.0, t));

                        dc.DrawRoundedRectangle(safeUnplayed, null, rect, cornerRadius, cornerRadius);

                        dc.PushOpacity(safeOpacity);
                        dc.DrawRoundedRectangle(safePlayed, null, rect, cornerRadius, cornerRadius);
                        dc.Pop();
                    }
                }
            }
            catch
            {

            }
        }
    }
}