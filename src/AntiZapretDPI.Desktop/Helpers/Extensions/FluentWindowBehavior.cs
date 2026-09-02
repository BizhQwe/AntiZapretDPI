using System.Windows;
using System.Windows.Interop;

namespace AntiZapretDPI.Helpers.Extensions
{
    public static class FluentWindowBehavior
    {
        public static readonly DependencyProperty DisableResizeProperty =
            DependencyProperty.RegisterAttached(
                "DisableResize",
                typeof(bool),
                typeof(FluentWindowBehavior),
                new PropertyMetadata(false, OnDisableResizeChanged));

        public static void SetDisableResize(DependencyObject element, bool value)
        {
            element.SetValue(DisableResizeProperty, value);
        }

        public static bool GetDisableResize(DependencyObject element)
        {
            return (bool)element.GetValue(DisableResizeProperty);
        }

        private static void OnDisableResizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Window window && (bool)e.NewValue)
            {
                if (window.IsLoaded)
                {
                    LockWindowSize(window);
                }
                else
                {
                    window.Loaded += Window_Loaded;
                }
            }
        }

        private static void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window)
            {
                window.Loaded -= Window_Loaded;
                LockWindowSize(window);
            }
        }

        private static void LockWindowSize(Window window)
        {
            window.ResizeMode = ResizeMode.CanMinimize;

            window.MinWidth = window.ActualWidth;
            window.MaxWidth = window.ActualWidth;
            window.MinHeight = window.ActualHeight;
            window.MaxHeight = window.ActualHeight;

            var handle = new WindowInteropHelper(window).Handle;
            var hwndSource = HwndSource.FromHwnd(handle);

            hwndSource?.AddHook(WindowProc);
        }

        private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0020)
            {
                int hitTest = lParam.ToInt32() & 0xFFFF;

                if (hitTest is >= 10 and <= 17)
                {
                    handled = true;
                    return 1;
                }
            }
            return IntPtr.Zero;
        }
    }
}