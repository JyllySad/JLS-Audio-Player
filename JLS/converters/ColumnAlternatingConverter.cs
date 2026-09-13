using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace JLS.Converters
{
    public class ColumnAlternatingConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2) return Brushes.Transparent;
            
            var header = values[0] as GridViewColumnHeader;
            var styleStr = values[1]?.ToString();
            
            if (header == null || header.Column == null) return Brushes.Transparent;
            
            if (styleStr != "Columns" && styleStr != "Both") return Brushes.Transparent;

            var gridView = FindParentGridView(header);
            if (gridView == null) return Brushes.Transparent;

            int visibleIndex = 0;
            foreach (var col in gridView.Columns)
            {
                if (col == header.Column) break;

                if (double.IsNaN(col.Width) || col.Width > 0) visibleIndex++;
            }


            if (visibleIndex % 2 == 1)
                return new SolidColorBrush(Color.FromArgb(8, 255, 255, 255)); 

            return Brushes.Transparent;
        }

        private GridView? FindParentGridView(DependencyObject child)
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is ListView))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            return (parent as ListView)?.View as GridView;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}