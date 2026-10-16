using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace StillFocus.App;

internal sealed class ToastWindow : Window
{
    private readonly DispatcherTimer dismiss = new() { Interval = TimeSpan.FromSeconds(3) };
    public ToastWindow(string message, IntPtr mainHandle)
    {
        Width = 340; Height = 94; WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = Brushes.Transparent; Topmost = true; ShowActivated = false; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
        Title = "静时 · 完成提醒";
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 15, 0) };
        stack.Children.Add(new TextBlock { Text = "✓  " + message, Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock { Text = "静时 · StillFocus    /    3 秒后自动消失", Foreground = new SolidColorBrush(Color.FromRgb(160, 180, 200)), FontSize = 11, Margin = new Thickness(23, 8, 0, 0) });
        Content = new Border { CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(245, 29, 42, 57)), BorderBrush = new SolidColorBrush(Color.FromRgb(71, 102, 134)), BorderThickness = new Thickness(1), Child = stack };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW: mouse clicks must not steal foreground focus.
            var style = GetWindowLongPtr(hwnd, -20).ToInt64();
            SetWindowLongPtr(hwnd, -20, new IntPtr(style | 0x08000000 | 0x80));
        };
        Loaded += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var work = System.Windows.Forms.Screen.FromHandle(mainHandle).WorkingArea;
            var rect = NativeMethods.Rect(hwnd);
            NativeMethods.Move(hwnd, work.Left + (work.Width - rect.Width) / 2, work.Top + (work.Height - rect.Height) / 2, rect.Width, rect.Height);
            dismiss.Start();
        };
        dismiss.Tick += (_, _) => Close();
        Closed += (_, _) => dismiss.Stop();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
}
