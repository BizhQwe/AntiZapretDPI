using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace AntiZapretDPI.Helpers.Extensions
{
    public static class TitleBarBehavior
    {
        public static readonly DependencyProperty DisableMaximizeButtonProperty =
            DependencyProperty.RegisterAttached(
                "DisableMaximizeButton",
                typeof(bool),
                typeof(TitleBarBehavior),
                new PropertyMetadata(false, OnDisableMaximizeButtonChanged));

        public static void SetDisableMaximizeButton(DependencyObject element, bool value)
        {
            element.SetValue(DisableMaximizeButtonProperty, value);
        }

        public static bool GetDisableMaximizeButton(DependencyObject element)
        {
            return (bool)element.GetValue(DisableMaximizeButtonProperty);
        }

        private static void OnDisableMaximizeButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TitleBar titleBar && (bool)e.NewValue)
            {
                titleBar.ApplyTemplate();

                if (FindAndModifyMaximizeButton(titleBar))
                {
                    return;
                }

                void layoutHandler(object? s, EventArgs args)
                {
                    titleBar.ApplyTemplate();
                    if (FindAndModifyMaximizeButton(titleBar))
                    {
                        titleBar.LayoutUpdated -= layoutHandler;
                    }
                }

                titleBar.LayoutUpdated += layoutHandler;
            }
        }

        private static bool FindAndModifyMaximizeButton(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is TitleBarButton button && button.ButtonType == TitleBarButtonType.Maximize)
                {
                    button.Opacity = 0.3;
                    button.IsHitTestVisible = false;
                    return true;
                }

                if (FindAndModifyMaximizeButton(child))
                {
                    return true;
                }
            }
            return false;
        }
    }
}