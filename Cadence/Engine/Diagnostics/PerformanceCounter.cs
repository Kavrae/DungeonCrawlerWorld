using System.Diagnostics;

namespace Engine.Diagnostics;

/// <summary>Generic rolling rate counter: ticks per second over the last window of about a second.</summary>
/// <remarks>The window closes on the first tick at least a second after it opened, so its rate is divided by the window's real length -- a late tick never reads as a faster second. Timed on Stopwatch, which a clock change can't move.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class PerformanceCounter
{
    private static readonly long WindowTimestampTicks = Stopwatch.Frequency;

    private long _windowStartTimestamp = Stopwatch.GetTimestamp();
    private long _countSinceWindowStart;

    public double RatePerSecond { get; private set; }

    /// <summary>Counts one occurrence, closing the window once a second has passed.</summary>
    public void Tick()
    {
        _countSinceWindowStart++;

        var now = Stopwatch.GetTimestamp();
        var windowTimestampTicks = now - _windowStartTimestamp;
        if (windowTimestampTicks < WindowTimestampTicks)
        {
            return;
        }

        RatePerSecond = _countSinceWindowStart * (double)Stopwatch.Frequency / windowTimestampTicks;
        _windowStartTimestamp = now;
        _countSinceWindowStart = 0;
    }
}
