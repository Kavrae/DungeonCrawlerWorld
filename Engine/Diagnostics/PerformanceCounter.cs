namespace Engine.Diagnostics;

/// <summary>
/// Generic rolling once-per-second rate counter. Call Tick() once per occurrence (e.g. once
/// per Draw for FPS, once per Update for UPS) and read RatePerSecond; one instance per metric
/// tracked, so drawing/update rates -- or any other per-second count -- don't duplicate the
/// tick-window bookkeeping.
/// </summary>
public sealed class PerformanceCounter
{
    private static readonly long TicksBetweenSamples = TimeSpan.TicksPerSecond;

    private long _lastSampleTicks;
    private long _countSinceLastSample;

    public PerformanceCounter()
    {
        _lastSampleTicks = DateTime.UtcNow.Ticks;
    }

    public double RatePerSecond { get; private set; }

    public void Tick()
    {
        _countSinceLastSample++;

        var currentTicks = DateTime.UtcNow.Ticks;
        var elapsed = currentTicks - _lastSampleTicks;

        if (elapsed >= TicksBetweenSamples)
        {
            RatePerSecond = _countSinceLastSample;
            _lastSampleTicks = currentTicks;
            _countSinceLastSample = 0;
        }
    }
}
