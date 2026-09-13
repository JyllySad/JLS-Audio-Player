using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace JLS.Controls
{
    public class MasonryPanel : Panel
    {
        public double ColumnWidth
        {
            get { return (double)GetValue(ColumnWidthProperty); }
            set { SetValue(ColumnWidthProperty, value); }
        }

        public static readonly DependencyProperty ColumnWidthProperty =
            DependencyProperty.Register("ColumnWidth", typeof(double), typeof(MasonryPanel), new FrameworkPropertyMetadata(340.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        protected override Size MeasureOverride(Size availableSize)
        {
            if (InternalChildren.Count == 0) return new Size(0, 0);

            int columns = Math.Max(1, (int)(availableSize.Width / ColumnWidth));
            double[] columnHeights = new double[columns];

            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(ColumnWidth, double.PositiveInfinity));
                int minCol = Array.IndexOf(columnHeights, columnHeights.Min());
                columnHeights[minCol] += child.DesiredSize.Height;
            }

            return new Size(columns * ColumnWidth, columnHeights.Max());
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            int columns = Math.Max(1, (int)(finalSize.Width / ColumnWidth));
            double[] columnHeights = new double[columns];

            foreach (UIElement child in InternalChildren)
            {
                int minCol = Array.IndexOf(columnHeights, columnHeights.Min());
                child.Arrange(new Rect(minCol * ColumnWidth, columnHeights[minCol], child.DesiredSize.Width, child.DesiredSize.Height));
                columnHeights[minCol] += child.DesiredSize.Height;
            }

            return finalSize;
        }
    }
}