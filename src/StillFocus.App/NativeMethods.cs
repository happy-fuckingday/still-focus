using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using Microsoft.Win32;

namespace StillFocus.App;

internal static class NativeMethods
{
    public static readonly IntPtr HWND_BROADCAST = new(0xffff);
    public static readonly uint ShowMessage = RegisterWindowMessage("StillFocus.Show.v1");
    public static readonly uint SettingsMessage = RegisterWindowMessage("StillFocus.Settings.v1");
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] public static extern uint GetDoubleClickTime();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string text);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    public static void DarkTitle(Window window)
    {
        window.SourceInitialized += (_, _) => { int enabled = 1; DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref enabled, sizeof(int)); };
    }
    public static double Scale(IntPtr hwnd) => Math.Max(96, GetDpiForWindow(hwnd)) / 96.0;
    public static RECT Rect(IntPtr hwnd) { GetWindowRect(hwnd, out var r); return r; }
    public static void Move(IntPtr hwnd, int x, int y, int width, int height) => SetWindowPos(hwnd, IntPtr.Zero, x, y, Math.Max(1, width), Math.Max(1, height), 0x0014); // no activate, no z-order change

    // Conservative fullscreen detection includes borderless fullscreen, and never activates anything.
    public static bool Fullscreen(IntPtr ownWindow)
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == ownWindow || IsIconic(foreground)) return false;
        GetWindowThreadProcessId(foreground, out uint pid);
        if (pid == Environment.ProcessId) return false;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            if (process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return false;
        }
        catch (ArgumentException) { return false; }
        var r = Rect(foreground);
        var b = Forms.Screen.FromHandle(foreground).Bounds;
        return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
    }

    public static bool ReadStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("StillFocus") is string;
    }

    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("StillFocus", $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue("StillFocus", false);
    }
}
