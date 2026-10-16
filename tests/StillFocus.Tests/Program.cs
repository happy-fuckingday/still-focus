using StillFocus.Core;

int passed = 0;
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); Environment.ExitCode = 1; }
}
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}"); }
(FocusTimer, FakeClock) Create(int minutes = 1, int count = 0) { var clock = new FakeClock(); return (new FocusTimer(clock, minutes, count), clock); }
Test("focus -> rest exactly once; rest -> finished", () => {
    var (t,c) = Create(); int completions = 0; t.Completed += _ => completions++;
    t.Toggle(); c.Seconds = 60; t.Tick(); t.Tick(); Equal(1,t.Count); Equal(TimerPhase.Rest,t.Phase); Equal("05:00", t.Display); Equal(1,completions);
    c.Seconds = 360; t.Tick(); t.Tick(); Equal(TimerPhase.Finished,t.Phase); Equal("00:00",t.Display); Equal(2,completions); Equal(false,t.Running);
    t.Toggle(); Equal(TimerPhase.Focus,t.Phase); Equal("01:00", t.Display);
});
Test("pause preserves fractional remaining time", () => { var (t,c) = Create(); t.Toggle(); c.Seconds = 10.25; t.Pause(); c.Seconds = 1000; Equal(49.75,t.Remaining); t.Toggle(); c.Seconds += 9.75; Equal(40.0,t.Remaining); });
Test("reset retains this round's duration after settings changes", () => { var (t,c) = Create(); t.Toggle(); c.Seconds=20; t.SetMinutes(5); t.Reset(); Equal("01:00",t.Display); Equal(false,t.Running); t.Toggle(); c.Seconds+=60; t.Tick(); t.Reset(); Equal("05:00",t.Display); Equal(1,t.Count); t.SkipRest(); t.Toggle(); Equal("05:00",t.Display); });
Test("skip rest keeps count and does not notify", () => { var (t,c)=Create(); int n=0; t.Completed+=_=>n++; t.Toggle(); c.Seconds=60; t.Tick(); t.SkipRest(); Equal(1,t.Count); Equal(1,n); Equal("00:00",t.Display); });
Test("counter wraps 99 -> 00", () => { var (t,c)=Create(count:99); t.Toggle(); c.Seconds=60; t.Tick(); Equal(0,t.Count); });
Test("UI stall completes only one focus and starts a full rest", () => { var (t,c)=Create(); t.Toggle(); c.Seconds=10000; t.Tick(); Equal(1,t.Count); Equal("05:00",t.Display); });
Test("lock/suspend pause never completes an overdue stage", () => { var (t,c)=Create(); t.Toggle(); c.Seconds=600; t.Pause(); t.Tick(); Equal(0,t.Count); Equal(false,t.Running); Equal(TimerPhase.Focus,t.Phase); });
Test("minute bounds and ready edit", () => { var(t,c)=Create(); t.SetMinutes(0); Equal(1,t.Minutes); t.SetMinutes(100); Equal("99:00",t.Display); });
Test("reset ready and finished is a paused focus", () => { var(t,c)=Create(); t.Reset(); Equal(TimerPhase.Focus,t.Phase); Equal(false,t.Running); t.SetMinutes(8); Equal("01:00",t.Display); });
Test("calendar future / today / past / leap day / huge range", () => {
    var e = new DayEvent {Name="事件",Date=new DateOnly(2028,3,1)};
    Equal("事件 · D-2",e.Label(new DateOnly(2028,2,28))); Equal("事件 · 今天",e.Label(e.Date)); Equal("事件 · 已过 3 天",e.Label(e.Date.AddDays(3)));
    e.Date = new DateOnly(9999,12,31); Equal(true, e.Label(new DateOnly(2026,1,1)).Contains("D-"));
});
Test("enabled event selection and deletion fallback", () => { var a=new DayEvent(); var b=new DayEvent(); var s=new Settings {Events=[a,b],Selected=a.Id}; a.Enabled=false; Equal(b.Id,s.Current!.Id); s.Events.Clear(); Equal<DayEvent?>(null,s.Current); });
Test("atomic save, backup recovery, restart never restores active countdown", () => {
    string path=Path.Combine(Path.GetTempPath(),"StillFocus-tests-"+Guid.NewGuid());
    try { var store=new SettingsStore(path); var s=new Settings {Count=9,Minutes=12,Events=[new DayEvent {Name="测试"}]}; store.Save(s); s.Count=10; store.Save(s); Equal(10,store.Load().Count); File.WriteAllText(store.FilePath,"{broken"); var restored=store.Load(); Equal(9,restored.Count); Equal(true,store.Recovered); Equal("测试",restored.Events[0].Name); var t=new FocusTimer(new FakeClock(),restored.Minutes,restored.Count); Equal(TimerPhase.Ready,t.Phase); Equal(false,t.Running); }
    finally { Directory.Delete(path,true); }
});
Test("normalization handles malformed user values", () => { var s=new Settings {Count=999,Minutes=-2,Opacity=double.NaN,Dock="bogus",Events=null!}; s.Normalize(); Equal(99,s.Count); Equal(1,s.Minutes); Equal(.94,s.Opacity); Equal("",s.Dock); Equal(0,s.Events.Count); });
Console.WriteLine($"{passed}/13 tests passed.");
sealed class FakeClock : IMonotonicClock { public double Seconds {get;set;} }
