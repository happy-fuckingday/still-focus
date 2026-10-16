using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StillFocus.Core;

/// <summary>Source-level UI smoke test and snapshots, using synthetic data in an isolated temp directory.</summary>
static class VisualTests
{
    [STAThread]
    static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    }
    static void Run(string[] args)
    {
        string output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/visual");
        Directory.CreateDirectory(output);
        string config = Path.Combine(Path.GetTempPath(), "StillFocus-visual-" + Guid.NewGuid());
        var app = new StillFocus.App.App();
        app.InitializeComponent();
        var store = new SettingsStore(config);
        var example = new Settings { Count = 8, Sound = false, Shake = false, Opacity = 1, Events = [new DayEvent { Name = "论文截止", Date = DateOnly.FromDateTime(DateTime.Today.AddDays(15)) }] };
        store.Save(example);
        var clock = new TestClock();
        var main = new StillFocus.App.MainWindow(store, clock);
        try
        {
            main.Show();
            ((DispatcherTimer)typeof(StillFocus.App.MainWindow).GetField("refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!).Stop();
            Pump();
            var timer = (FocusTimer)typeof(StillFocus.App.MainWindow).GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
            Snapshot(main, output, "main-ready");
            timer.Toggle(); clock.Seconds = 633; timer.Pause(); Render(main);
            Snapshot(main, output, "main-paused");
            timer.Toggle(); clock.Seconds += 867; timer.Tick(); Render(main);
            if (timer.Count != 9 || timer.Phase != TimerPhase.Rest) throw new Exception("Visual fixture stage failed.");
            Snapshot(main, output, "main-rest");
            var settings = new StillFocus.App.SettingsWindow(new Settings(), false);
            settings.WindowStyle = WindowStyle.None;
            settings.Show(); Pump(); Snapshot(settings, output, "settings"); settings.Close();
            example.Events.Add(new DayEvent { Name = "项目上线", Date = DateOnly.FromDateTime(DateTime.Today.AddDays(40)) });
            var events = new StillFocus.App.EventsWindow(example);
            events.WindowStyle = WindowStyle.None;
            events.Show(); Pump(); Snapshot(events, output, "events"); events.Close();
            Console.WriteLine("PASS: main / paused / rest / settings / events rendered; no private data used.");
        }
        finally
        {
            main.Close();
            app.Shutdown();
            if (Directory.Exists(config)) Directory.Delete(config, true);
        }
    }
    static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    static void Render(StillFocus.App.MainWindow main)
    {
        typeof(StillFocus.App.MainWindow).GetMethod("Render",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)!.Invoke(main,null);
        Pump();
    }
    static void Snapshot(Window window, string output, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth*2), (int)Math.Ceiling(window.ActualHeight*2),192,192,PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
    }
    private sealed class TestClock : IMonotonicClock { public double Seconds { get; set; } }
}
