using System.Windows;

namespace JLS.Controls
{
    public static class SidebarHelper
    {
        public static readonly DependencyProperty DefaultIconProperty =
            DependencyProperty.RegisterAttached("DefaultIcon", typeof(object), typeof(SidebarHelper), new PropertyMetadata(null));
        public static void SetDefaultIcon(DependencyObject element, object value) => element.SetValue(DefaultIconProperty, value);
        public static object GetDefaultIcon(DependencyObject element) => element.GetValue(DefaultIconProperty);

        public static readonly DependencyProperty ActiveIconProperty =
            DependencyProperty.RegisterAttached("ActiveIcon", typeof(object), typeof(SidebarHelper), new PropertyMetadata(null));
        public static void SetActiveIcon(DependencyObject element, object value) => element.SetValue(ActiveIconProperty, value);
        public static object GetActiveIcon(DependencyObject element) => element.GetValue(ActiveIconProperty);

        public static readonly DependencyProperty ActiveValueProperty =
            DependencyProperty.RegisterAttached("ActiveValue", typeof(object), typeof(SidebarHelper), 
                new PropertyMetadata(null, OnValueChanged));
        public static void SetActiveValue(DependencyObject element, object value) => element.SetValue(ActiveValueProperty, value);
        public static object GetActiveValue(DependencyObject element) => element.GetValue(ActiveValueProperty);

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.RegisterAttached("Value", typeof(object), typeof(SidebarHelper), 
                new PropertyMetadata(null, OnValueChanged));
        public static void SetValue(DependencyObject element, object value) => element.SetValue(ValueProperty, value);
        public static object GetValue(DependencyObject element) => element.GetValue(ValueProperty);

        private static readonly DependencyPropertyKey IsActivePropertyKey =
            DependencyProperty.RegisterAttachedReadOnly("IsActive", typeof(bool), typeof(SidebarHelper), 
                new PropertyMetadata(false));
        public static readonly DependencyProperty IsActiveProperty = IsActivePropertyKey.DependencyProperty;
        public static bool GetIsActive(DependencyObject element) => (bool)element.GetValue(IsActiveProperty);

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var active = GetActiveValue(d);
            var val = GetValue(d);
            
            bool isActive = active != null && val != null && active.ToString() == val.ToString();
            d.SetValue(IsActivePropertyKey, isActive);
        }
    }
}