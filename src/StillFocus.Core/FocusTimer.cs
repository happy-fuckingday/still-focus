using System.Diagnostics;

namespace StillFocus.Core;

public enum TimerPhase { Ready, Focus, Rest, Finished }
public enum Completion { Focus, Rest }
public interface IMonotonicClock { double Seconds { get; } }
public sealed class MonotonicClock : IMonotonicClock
{
    private readonly Stopwatch watch = Stopwatch.StartNew();
    public double Seconds => watch.Elapsed.TotalSeconds;
}

/// <summary>Single-threaded state machine; wall-clock changes never change elapsed focus time.</summary>
public sealed class FocusTimer(IMonotonicClock clock, int minutes = 25, int count = 0)
{
    public TimerPhase Phase { get; private set; } = TimerPhase.Ready;
    public bool Running { get; private set; }
    public int Minutes { get; private set; } = Math.Clamp(minutes, 1, 99);
    public int Count { get; private set; } = Math.Clamp(count, 0, 99);
    public double InitialSeconds { get; private set; } = Math.Clamp(minutes, 1, 99) * 60;
    private double remaining = Math.Clamp(minutes, 1, 99) * 60;
    private double started;
    public double Remaining => Math.Max(0, remaining - (Running ? Math.Max(0, clock.Seconds - started) : 0));
    public string Display
    {
        get { int seconds = (int)Math.Ceiling(Remaining); return $"{seconds / 60:D2}:{seconds % 60:D2}"; }
    }
    public event Action<Completion>? Completed;

    public void Toggle()
    {
        Tick();
        if (Running) { Pause(); return; }
        if (Phase is TimerPhase.Ready or TimerPhase.Finished)
        {
            Phase = TimerPhase.Focus;
            remaining = InitialSeconds = Minutes * 60;
        }
        started = clock.Seconds;
        Running = true;
    }

    public void Pause()
    {
        // Lock/suspend freezes first: it must never count an expired stage as completed.
        if (!Running) return;
        remaining = Remaining;
        Running = false;
    }

    public void Reset()
    {
        if (Phase is TimerPhase.Ready or TimerPhase.Finished)
            InitialSeconds = Minutes * 60;
        Phase = Phase == TimerPhase.Rest ? TimerPhase.Rest : TimerPhase.Focus;
        Running = false;
        remaining = InitialSeconds;
    }

    public void SetMinutes(int minutes)
    {
        Minutes = Math.Clamp(minutes, 1, 99);
        if (Phase == TimerPhase.Ready) remaining = InitialSeconds = Minutes * 60;
    }

    public void ClearCount() => Count = 0;

    public void SkipRest()
    {
        if (Phase != TimerPhase.Rest) return;
        Phase = TimerPhase.Finished;
        Running = false;
        remaining = 0;
    }

    public void Tick()
    {
        if (!Running || Remaining > 0) return;
        if (Phase == TimerPhase.Focus)
        {
            Count = (Count + 1) % 100;
            Phase = TimerPhase.Rest;
            remaining = InitialSeconds = 300;
            started = clock.Seconds;
            Completed?.Invoke(Completion.Focus);
        }
        else if (Phase == TimerPhase.Rest)
        {
            Phase = TimerPhase.Finished;
            Running = false;
            remaining = 0;
            Completed?.Invoke(Completion.Rest);
        }
    }
}
