using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace JLS.Services
{
    public static class SmoothScrollBehavior
    {
        public static readonly DependencyProperty EnableProperty =
            DependencyProperty.RegisterAttached("Enable", typeof(bool), typeof(SmoothScrollBehavior), new PropertyMetadata(false, OnEnableChanged));

        public static bool GetEnable(DependencyObject obj) => (bool)obj.GetValue(EnableProperty);
        public static void SetEnable(DependencyObject obj, bool value) => obj.SetValue(EnableProperty, value);

        private static readonly Dictionary<ScrollViewer, ScrollData> ActiveScrolls = new Dictionary<ScrollViewer, ScrollData>();
        private static bool _isRendering = false;

        private static TimeSpan _lastRenderTime;

        class ScrollData
        {
            public double TargetOffset;
            public double CurrentOffset;
        }

        private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element)
            {
                if ((bool)e.NewValue)
                {
                    element.PreviewMouseWheel += Element_PreviewMouseWheel;
                    element.Unloaded += Element_Unloaded;
                }
                else
                {
                    element.PreviewMouseWheel -= Element_PreviewMouseWheel;
                    element.Unloaded -= Element_Unloaded;
                }
            }
        }

        private static void Element_Unloaded(object sender, RoutedEventArgs e)
        {
            var sv = GetScrollViewer(sender as DependencyObject);
            if (sv != null && ActiveScrolls.ContainsKey(sv))
            {
                ActiveScrolls.Remove(sv);
            }
        }

        private static void Element_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = GetScrollViewer(sender as DependencyObject);
            if (scrollViewer == null || scrollViewer.ScrollableHeight == 0) return;

            if (IsInsidePopupOrMenu(scrollViewer)) return;

            e.Handled = true;

            if (!ActiveScrolls.TryGetValue(scrollViewer, out var data))
            {
                data = new ScrollData
                {
                    CurrentOffset = scrollViewer.VerticalOffset,
                    TargetOffset = scrollViewer.VerticalOffset
                };
                ActiveScrolls[scrollViewer] = data;
            }

            if (Math.Abs(scrollViewer.VerticalOffset - data.CurrentOffset) > 1)
            {
                data.CurrentOffset = scrollViewer.VerticalOffset;
                data.TargetOffset = scrollViewer.VerticalOffset;
            }

            data.TargetOffset -= e.Delta * 0.6;
            data.TargetOffset = Math.Max(0, Math.Min(data.TargetOffset, scrollViewer.ScrollableHeight));

            if (!_isRendering)
            {
                _lastRenderTime = TimeSpan.Zero;      
                CompositionTarget.Rendering += OnRendering;
                _isRendering = true;
            }
        }

        private static void OnRendering(object? sender, EventArgs e)
        {
            var renderingArgs = (RenderingEventArgs)e;

            if (_lastRenderTime == TimeSpan.Zero)
            {
                _lastRenderTime = renderingArgs.RenderingTime;
                return;
            }

            double deltaTime = (renderingArgs.RenderingTime - _lastRenderTime).TotalSeconds;
            _lastRenderTime = renderingArgs.RenderingTime;

            if (deltaTime > 0.1) deltaTime = 0.016;

            bool needsMoreRendering = false;
            var keysToRemove = new List<ScrollViewer>();

            foreach (var kvp in ActiveScrolls)
            {
                var sv = kvp.Key;
                var data = kvp.Value;

                if (Math.Abs(data.TargetOffset - data.CurrentOffset) < 0.5)
                {
                    data.CurrentOffset = data.TargetOffset;
                    sv.ScrollToVerticalOffset(data.CurrentOffset);
                    keysToRemove.Add(sv);
                    continue;
                }

                double lerpFactor = 1 - Math.Exp(-10 * deltaTime);
                data.CurrentOffset += (data.TargetOffset - data.CurrentOffset) * lerpFactor;

                sv.ScrollToVerticalOffset(data.CurrentOffset);
                needsMoreRendering = true;
            }

            foreach (var sv in keysToRemove)
            {
                ActiveScrolls.Remove(sv);
            }

            if (!needsMoreRendering && ActiveScrolls.Count == 0)
            {
                CompositionTarget.Rendering -= OnRendering;
                _isRendering = false;
                _lastRenderTime = TimeSpan.Zero;
            }
        }

        private static ScrollViewer? GetScrollViewer(DependencyObject? depObj)
        {
            if (depObj == null) return null;
            if (depObj is ScrollViewer viewer) return viewer;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        private static bool IsInsidePopupOrMenu(DependencyObject obj)
        {
            DependencyObject current = obj;
            while (current != null)
            {
                string typeName = current.GetType().Name;
                if (typeName.Contains("Popup") || typeName.Contains("Menu") || typeName.Contains("ComboBox"))
                {
                    return true;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

    }
}