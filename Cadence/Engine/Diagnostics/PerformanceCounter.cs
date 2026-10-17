namespace Engine.Diagnostics;

/// <summary>Generic rolling once-per-second rate counter.</summary>
/// <cleanupVersion>1</cleanupVersion>
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

    /// <summary>Updates the performance counter since the last sample</summary>
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