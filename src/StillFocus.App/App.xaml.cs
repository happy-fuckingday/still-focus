using System.IO;
using System.Threading;
using System.Windows;
using StillFocus.Core;
using WpfApplication = System.Windows.Application;

namespace StillFocus.App;

public partial class App : WpfApplication
{
    private Mutex? mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        // Local to the current Windows session; launching again asks the running instance to show.
        mutex = new Mutex(true, "Local\\StillFocus.Instance", out bool first);
        if (!first)
        {
            NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, e.Args.Contains("--settings") ? NativeMethods.SettingsMessage : NativeMethods.ShowMessage, IntPtr.Zero, IntPtr.Zero);
            Shutdown(); return;
        }
        var store = new SettingsStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StillFocus"));
        MainWindow = new MainWindow(store);
        MainWindow.Show();
        if (e.Args.Contains("--settings")) Dispatcher.BeginInvoke(((MainWindow)MainWindow).ShowSettings);
    }
    protected override void OnExit(ExitEventArgs e) { mutex?.Dispose(); base.OnExit(e); }
}
