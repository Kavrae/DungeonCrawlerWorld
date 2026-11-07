using Engine.ECS.Components;

namespace Engine.ECS.Systems;

/// <summary>Moves a periodic timer past every period owed up to now without firing them, as a pool updater (see ScheduledTimerExtensions.SkipOwedPeriods).</summary>
public readonly struct SkipOwedPeriodsUpdate<T>(int periodFrames, long now) : IComponentUpdater<T> where T : struct, IScheduledTimer
{
    public void Update(ref T timer) => timer.SkipOwedPeriods(periodFrames, now);
}

/// <summary>Sets a timer's next deadline outright, as a pool updater: for a deadline that isn't one period on from the last.</summary>
public readonly struct SetNextTickUpdate<T>(uint nextTickFrame) : IComponentUpdater<T> where T : struct, IScheduledTimer
{
    public void Update(ref T timer) => timer.NextTickFrame = nextTickFrame;
}

/// <summary>Picks out the instance of a keyed timer a MultiTimerWheel names, by its TimerKey.</summary>
public readonly struct TimerKeyMatcher<T>(int timerKey) : IComponentMatcher<T> where T : struct, IKeyedScheduledTimer
{
    public bool Matches(in T timer) => timer.TimerKey == timerKey;
}
