using System.ComponentModel;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using StillFocus.Core;
using Forms = System.Windows.Forms;

namespace StillFocus.App;

public partial class MainWindow : Window
{
    private readonly SettingsStore store;
    private readonly Settings settings;
    private readonly FocusTimer timer;
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer click = new();
    private readonly DispatcherTimer edgeAnimation = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly System.Diagnostics.Stopwatch edgeWatch = new();
    private readonly Forms.NotifyIcon tray;
    private readonly SoundPlayer sound;
    private readonly Stream soundStream;
    private IntPtr handle;
    private bool hidden, dragging, dialogOpen, menuOpen, closing, fullScreen, saveErrorShown;
    private bool animationTarget;
    private NativeMethods.RECT animationFrom, animationTo;
    private DateTime? hoverSince, leaveSince;
    private Point? down;
    private int expandedX, expandedY;
    private double normalHeight = 120;
    private ToastWindow? toast;

    public MainWindow(SettingsStore store, IMonotonicClock? clock = null)
    {
        this.store = store;
        settings = store.Load();
        InitializeComponent();
        timer = new FocusTimer(clock ?? new MonotonicClock(), settings.Minutes, settings.Count);
        Opacity = settings.Opacity;
        soundStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/StillFocus;component/Assets/complete.wav"))!.Stream;
        sound = new SoundPlayer(soundStream);
        tray = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/StillFocus;component/Assets/app.ico"))!.Stream),
            Text = "静时 · StillFocus", Visible = true
        };
        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("显示主窗口", null, (_, _) => Dispatcher.Invoke(Reveal));
        trayMenu.Items.Add("开始 / 暂停 / 继续", null, (_, _) => Dispatcher.Invoke(Toggle));
        trayMenu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        trayMenu.Items.Add("倒数日管理", null, (_, _) => Dispatcher.Invoke(OpenEvents));
        trayMenu.Items.Add("关于静时", null, (_, _) => Dispatcher.Invoke(About));
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Close));
        trayMenu.Opening += (_, _) => menuOpen = true;
        trayMenu.Closed += (_, _) => { menuOpen = false; leaveSince = DateTime.UtcNow; };
        tray.ContextMenuStrip = trayMenu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(Reveal);
        click.Interval = TimeSpan.FromMilliseconds(NativeMethods.GetDoubleClickTime());
        click.Tick += (_, _) => { click.Stop(); Toggle(); };
        timer.Completed += Completed;
        refresh.Tick += (_, _) => Tick();
        edgeAnimation.Tick += (_, _) => AnimateEdge();
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(Hook);
        };
        Loaded += (_, _) =>
        {
            Render();
            RestorePosition();
            refresh.Start();
            if (store.Recovered) tray.ShowBalloonTip(4000, "设置已恢复", "发现不完整配置，已尝试从备份恢复。", Forms.ToolTipIcon.Info);
        };
        ContextMenuOpening += (_, _) => BuildMenu();
        ContextMenu = new ContextMenu();
        SystemEvents.SessionSwitch += SessionSwitch;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        Closing += OnClosing;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if ((uint)msg == NativeMethods.ShowMessage) { Reveal(); handled = true; }
        if ((uint)msg == NativeMethods.SettingsMessage) { Dispatcher.BeginInvoke(ShowSettings); handled = true; }
        return IntPtr.Zero;
    }

    private void Tick()
    {
        timer.Tick();
        Render();
        bool nextFull = !dialogOpen && !menuOpen && NativeMethods.Fullscreen(handle);
        if (nextFull != fullScreen)
        {
            fullScreen = nextFull;
            if (fullScreen) { Topmost = false; Visibility = Visibility.Hidden; toast?.Close(); toast = null; }
            else { Show(); Topmost = true; }
        }
        if (settings.Dock.Length == 0 || !settings.AutoHide || dialogOpen || menuOpen || dragging || fullScreen || edgeAnimation.IsEnabled) return;
        if (IsMouseOver)
        {
            leaveSince = null;
            hoverSince ??= DateTime.UtcNow;
            if (hidden && (DateTime.UtcNow - hoverSince.Value).TotalMilliseconds >= 150) SetHidden(false);
        }
        else
        {
            hoverSince = null;
            leaveSince ??= DateTime.UtcNow;
            if (!hidden && (DateTime.UtcNow - leaveSince.Value).TotalMilliseconds >= 500) SetHidden(true);
        }
    }

    private void Render()
    {
        TimerText.Text = timer.Display;
        TimerText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(!timer.Running && timer.Phase is TimerPhase.Focus or TimerPhase.Rest ? "#929BA9" : timer.Phase == TimerPhase.Rest ? "#82DCA5" : "#F5F7FC"));
        ClockText.Text = DateTime.Now.ToString("HH:mm");
        CountText.Text = timer.Count.ToString("D2");
        var day = settings.ShowDays ? settings.Current : null;
        DayText.Text = day?.Label(DateOnly.FromDateTime(DateTime.Today)) ?? "";
        DayText.ToolTip = day == null ? null : $"{day.Name}\n{day.Date:yyyy-MM-dd}\n单击切换事件";
        DayRow.Height = new GridLength(day == null ? 0 : 23);
        double nextHeight = day == null ? 98 : 120;
        if (normalHeight != nextHeight)
        {
            normalHeight = nextHeight;
            if (!hidden && !edgeAnimation.IsEnabled) Height = normalHeight;
        }
        string phase = timer.Phase == TimerPhase.Rest ? "休息" : "专注";
        tray.Text = $"静时 · {phase} {timer.Display} · 已完成 {timer.Count:D2}";
    }

    private void Toggle() { click.Stop(); timer.Toggle(); Render(); }
    private void TimerDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ClickCount == 2) { click.Stop(); timer.Reset(); Render(); }
        else if (e.ClickCount == 1) click.Start();
    }
    private void DayClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var events = settings.Events.Where(x => x.Enabled).ToList();
        if (events.Count == 0) return;
        int index = events.FindIndex(x => x.Id == settings.Current?.Id);
        settings.Selected = events[(index + 1) % events.Count].Id;
        Save(); Render();
    }
    private void Wheel(object sender, MouseWheelEventArgs e)
    {
        if (timer.Phase != TimerPhase.Ready) return;
        timer.SetMinutes(timer.Minutes + Math.Sign(e.Delta)); settings.Minutes = timer.Minutes;
        Save(); Render();
    }
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space) { Toggle(); e.Handled = true; }
    }
    private void BackgroundDown(object sender, MouseButtonEventArgs e)
    {
        if (hidden) { SetHidden(false); return; }
        down = e.GetPosition(this);
    }
    private void BackgroundMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (down is not Point origin || e.LeftButton != MouseButtonState.Pressed || hidden) return;
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        down = null; dragging = true;
        try { DragMove(); }
        catch (InvalidOperationException) { }
        finally { dragging = false; DockAfterDrag(); }
    }
    private void BackgroundUp(object sender, MouseButtonEventArgs e) => down = null;
    private void PointerEnter(object sender, System.Windows.Input.MouseEventArgs e) { hoverSince = DateTime.UtcNow; leaveSince = null; }
    private void PointerLeave(object sender, System.Windows.Input.MouseEventArgs e) { leaveSince = DateTime.UtcNow; hoverSince = null; }

    private void DockAfterDrag()
    {
        var r = NativeMethods.Rect(handle);
        var work = Forms.Screen.FromHandle(handle).WorkingArea;
        double threshold = 10 * NativeMethods.Scale(handle);
        var candidates = new[] { ("Left", Math.Abs(r.Left - work.Left)), ("Right", Math.Abs(r.Right - work.Right)), ("Top", Math.Abs(r.Top - work.Top)), ("Bottom", Math.Abs(r.Bottom - work.Bottom)) };
        var nearest = candidates.OrderBy(x => x.Item2).First();
        settings.Dock = settings.AutoHide && nearest.Item2 <= threshold ? nearest.Item1 : "";
        expandedX = Math.Clamp(r.Left, work.Left, Math.Max(work.Left, work.Right - r.Width));
        expandedY = Math.Clamp(r.Top, work.Top, Math.Max(work.Top, work.Bottom - r.Height));
        if (settings.Dock == "Left") expandedX = work.Left;
        if (settings.Dock == "Right") expandedX = work.Right - r.Width;
        if (settings.Dock == "Top") expandedY = work.Top;
        if (settings.Dock == "Bottom") expandedY = work.Bottom - r.Height;
        NativeMethods.Move(handle, expandedX, expandedY, r.Width, r.Height);
        settings.X = expandedX; settings.Y = expandedY;
        Save(); leaveSince = DateTime.UtcNow;
    }

    private void RestorePosition()
    {
        var r = NativeMethods.Rect(handle);
        var screen = settings.X.HasValue && settings.Y.HasValue
            ? Forms.Screen.FromPoint(new System.Drawing.Point(settings.X.Value, settings.Y.Value)) : Forms.Screen.PrimaryScreen!;
        var work = screen.WorkingArea;
        expandedX = Math.Clamp(settings.X ?? work.Right - r.Width - 28, work.Left, Math.Max(work.Left, work.Right - r.Width));
        expandedY = Math.Clamp(settings.Y ?? work.Top + 36, work.Top, Math.Max(work.Top, work.Bottom - r.Height));
        NativeMethods.Move(handle, expandedX, expandedY, r.Width, r.Height);
        // Reapply docking on this monitor, including a changed work area.
        if (settings.Dock == "Left") expandedX = work.Left;
        if (settings.Dock == "Right") expandedX = work.Right - r.Width;
        if (settings.Dock == "Top") expandedY = work.Top;
        if (settings.Dock == "Bottom") expandedY = work.Bottom - r.Height;
        NativeMethods.Move(handle, expandedX, expandedY, r.Width, r.Height);
        leaveSince = DateTime.UtcNow;
    }

    private void SetHidden(bool value, bool animate = true)
    {
        if (dragging) return;
        edgeAnimation.Stop();
        var r = NativeMethods.Rect(handle);
        if (!hidden && !value) return;
        if (!hidden) { expandedX = r.Left; expandedY = r.Top; }
        var work = Forms.Screen.FromPoint(new System.Drawing.Point(expandedX + 10, expandedY + 10)).WorkingArea;
        double scale = NativeMethods.Scale(handle);
        int width = (int)Math.Round(240 * scale), height = (int)Math.Round(normalHeight * scale), strip = (int)Math.Round(6 * scale);
        expandedX = Math.Clamp(expandedX, work.Left, Math.Max(work.Left, work.Right - width));
        expandedY = Math.Clamp(expandedY, work.Top, Math.Max(work.Top, work.Bottom - height));
        if (settings.Dock == "Right") expandedX = work.Right - width;
        if (settings.Dock == "Bottom") expandedY = work.Bottom - height;
        int x = expandedX, y = expandedY;
        if (value)
        {
            if (settings.Dock == "Left") width = strip;
            if (settings.Dock == "Right") { x = work.Right - strip; width = strip; }
            if (settings.Dock == "Top") height = strip;
            if (settings.Dock == "Bottom") { y = work.Bottom - strip; height = strip; }
        }
        animationTarget = value;
        animationFrom = r;
        animationTo = new NativeMethods.RECT { Left = x, Top = y, Right = x + width, Bottom = y + height };
        hidden = value;
        ContentGrid.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        Grip.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        if (animate) { edgeWatch.Restart(); edgeAnimation.Start(); }
        else NativeMethods.Move(handle, x, y, width, height);
    }

    private void AnimateEdge()
    {
        double t = Math.Clamp(edgeWatch.Elapsed.TotalMilliseconds / 200, 0, 1);
        double ease = 1 - Math.Pow(1 - t, 3);
        int Lerp(int a, int b) => (int)Math.Round(a + (b - a) * ease);
        NativeMethods.Move(handle, Lerp(animationFrom.Left, animationTo.Left), Lerp(animationFrom.Top, animationTo.Top), Lerp(animationFrom.Width, animationTo.Width), Lerp(animationFrom.Height, animationTo.Height));
        if (t >= 1) { edgeAnimation.Stop(); hidden = animationTarget; }
    }

    private void Reveal()
    {
        edgeAnimation.Stop();
        if (hidden) SetHidden(false, false);
        settings.Dock = "";
        Show(); fullScreen = false; Topmost = true;
        RestorePosition(); Save();
    }

    private void DefaultPosition()
    {
        if (hidden) SetHidden(false, false);
        settings.X = settings.Y = null; settings.Dock = "";
        RestorePosition(); Save();
    }

    private void Completed(Completion completion)
    {
        settings.Count = timer.Count;
        Save(); // Commit count before presenting completion; no active stage is restored on restart.
        string text = completion == Completion.Focus ? "专注完成，开始休息" : "休息结束，准备下一轮";
        if (settings.Sound) { try { sound.Play(); } catch (InvalidOperationException) { } }
        if (settings.Shake && !hidden && !fullScreen)
        {
            var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(600) };
            for (int i = 0; i <= 12; i++) animation.KeyFrames.Add(new LinearDoubleKeyFrame(i == 12 ? 0 : Math.Sin(i * Math.PI / 2) * 4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * 50))));
            var transform = new TranslateTransform(); Card.RenderTransform = transform;
            transform.BeginAnimation(TranslateTransform.XProperty, animation);
        }
        if (settings.Toast && !fullScreen && !NativeMethods.Fullscreen(handle))
        { toast?.Close(); toast = new ToastWindow(text, handle); toast.Show(); }
    }

    private void PauseForSystem()
    {
        click.Stop(); timer.Pause();
        toast?.Close(); toast = null;
        Card.RenderTransform = Transform.Identity;
        Render();
    }
    private void SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.ConsoleDisconnect)
            Dispatcher.Invoke(PauseForSystem);
    }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is PowerModes.Suspend or PowerModes.Resume) Dispatcher.Invoke(PauseForSystem);
    }
    private void DisplaysChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { if (hidden) SetHidden(false, false); RestorePosition(); });

    private void Save()
    {
        settings.Count = timer.Count;
        try { store.Save(settings); saveErrorShown = false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!saveErrorShown) tray.ShowBalloonTip(5000, "设置暂未保存", "本地配置目录不可写，请检查磁盘空间和目录权限。", Forms.ToolTipIcon.Warning);
            saveErrorShown = true;
        }
    }

    private MenuItem Item(string label, Action action, bool enabled = true, bool? check = null)
    {
        var item = new MenuItem { Header = label, IsEnabled = enabled, IsCheckable = check.HasValue, IsChecked = check == true };
        item.Click += (_, _) => action();
        return item;
    }
    private void BuildMenu()
    {
        click.Stop();
        var menu = ContextMenu!; menu.Items.Clear();
        menu.Items.Add(Item(timer.Running ? "Ⅱ  暂停" : timer.Phase is TimerPhase.Ready or TimerPhase.Finished ? "▶  开始专注" : "▶  继续", Toggle));
        menu.Items.Add(Item("↺  重置当前阶段", () => { timer.Reset(); Render(); }));
        if (timer.Phase == TimerPhase.Rest) menu.Items.Add(Item("»  跳过休息", () => { timer.SkipRest(); Render(); }));
        menu.Items.Add(Item("专注时长…", OpenSettings));
        menu.Items.Add(new Separator());
        var days = new MenuItem { Header = "倒数日" };
        days.Items.Add(Item("显示顶部倒数日", () => { settings.ShowDays = !settings.ShowDays; Save(); Render(); }, check: settings.ShowDays));
        foreach (var day in settings.Events.Where(e => e.Enabled))
            days.Items.Add(Item(day.Name, () => { settings.Selected = day.Id; Save(); Render(); }, check: day.Id == settings.Current?.Id));
        days.Items.Add(new Separator()); days.Items.Add(Item("管理事件…", OpenEvents)); menu.Items.Add(days);
        var reminders = new MenuItem { Header = "提醒" };
        reminders.Items.Add(Item("声音", () => { settings.Sound = !settings.Sound; Save(); }, check: settings.Sound));
        reminders.Items.Add(Item("窗口摆动", () => { settings.Shake = !settings.Shake; Save(); }, check: settings.Shake));
        reminders.Items.Add(Item("屏幕中央提示", () => { settings.Toast = !settings.Toast; Save(); }, check: settings.Toast));
        menu.Items.Add(reminders);
        var window = new MenuItem { Header = "窗口" };
        window.Items.Add(Item("自动贴边隐藏", () => { if (hidden) SetHidden(false, false); settings.AutoHide = !settings.AutoHide; if (!settings.AutoHide) settings.Dock = ""; Save(); }, check: settings.AutoHide));
        window.Items.Add(Item("透明度与窗口设置…", OpenSettings));
        window.Items.Add(Item("恢复默认位置", DefaultPosition)); menu.Items.Add(window);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("清零专注次数…", () => { dialogOpen = true; try { if (MessageBox.Show(this, "将累计专注次数清零？此操作无法撤销。", "清零确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) { timer.ClearCount(); Save(); Render(); } } finally { dialogOpen = false; } }));
        menu.Items.Add(Item("开机自启动", () => ChangeStartup(!NativeMethods.ReadStartup()), check: NativeMethods.ReadStartup()));
        menu.Items.Add(Item("设置…", OpenSettings));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("关于静时", About)); menu.Items.Add(Item("退出", Close));
        menu.Opened -= MenuOpened; menu.Closed -= MenuClosed;
        menu.Opened += MenuOpened; menu.Closed += MenuClosed;
    }
    private void MenuOpened(object sender, RoutedEventArgs e) => menuOpen = true;
    private void MenuClosed(object sender, RoutedEventArgs e) { menuOpen = false; leaveSince = DateTime.UtcNow; }

    private bool ChangeStartup(bool enabled)
    {
        try { NativeMethods.SetStartup(enabled); settings.Startup = enabled; Save(); return true; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { MessageBox.Show(this, "无法修改开机启动设置，请检查当前用户的注册表权限。", "开机启动"); return false; }
    }
    private void OpenSettings()
    {
        if (dialogOpen) return;
        click.Stop(); dialogOpen = true;
        try
        {
            var dialog = new SettingsWindow(settings, NativeMethods.ReadStartup()) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                if (dialog.Startup != NativeMethods.ReadStartup() && !ChangeStartup(dialog.Startup)) return;
                settings.Minutes = dialog.Minutes; timer.SetMinutes(settings.Minutes);
                settings.ShowDays = dialog.ShowDays; settings.Sound = dialog.Sound; settings.Shake = dialog.Shake; settings.Toast = dialog.Toast;
                settings.Opacity = dialog.WindowOpacity; Opacity = settings.Opacity;
                settings.AutoHide = dialog.AutoHide;
                if (!settings.AutoHide) { if (hidden) SetHidden(false, false); settings.Dock = ""; }
                if (dialog.ResetPosition) DefaultPosition();
                Save(); Render();
            }
        }
        finally { dialogOpen = false; leaveSince = DateTime.UtcNow; }
    }
    public void ShowSettings() { Reveal(); OpenSettings(); }
    private void OpenEvents()
    {
        if (dialogOpen) return;
        click.Stop(); dialogOpen = true;
        try
        {
            var dialog = new EventsWindow(settings) { Owner = this };
            if (dialog.ShowDialog() == true) { settings.Events = dialog.Events; settings.Selected = dialog.Selected; settings.Normalize(); Save(); Render(); }
        }
        finally { dialogOpen = false; leaveSince = DateTime.UtcNow; }
    }
    private void About()
    {
        dialogOpen = true;
        try { MessageBox.Show(this, "静时 · StillFocus 1.0.0\n\n极简悬浮番茄钟与倒数日\n作者：Codex\n许可证：MIT\n\n单击计时 · 双击重置 · 右键打开菜单\n本地运行，无账号、无遥测。", "关于静时", MessageBoxButton.OK, MessageBoxImage.Information); }
        finally { dialogOpen = false; }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closing) return;
        closing = true; timer.Pause(); click.Stop(); refresh.Stop(); edgeAnimation.Stop();
        if (!hidden) { var r = NativeMethods.Rect(handle); settings.X = r.Left; settings.Y = r.Top; }
        else { settings.X = expandedX; settings.Y = expandedY; }
        Save(); toast?.Close(); sound.Stop(); sound.Dispose(); soundStream.Dispose(); tray.Visible = false; tray.Dispose();
        SystemEvents.SessionSwitch -= SessionSwitch; SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
    }
}
