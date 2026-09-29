namespace Engine.Diagnostics;

/// <summary>Samples a fixed set of gauges once per simulation frame and summarizes them per report interval.</summary>
/// <remarks>
/// A cumulative gauge is stored as its change since the previous sample, starting from its value when
/// the tracker was constructed, so the first frame's delta is not everything counted since launch.
/// Every array is sized at construction; Sample allocates nothing.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class GaugeTracker
{
    /// <summary>The process-wide GC gauges, sampled beside every session's own.</summary>
    public static readonly IReadOnlyList<Gauge> ProcessGauges =
    [
        new("Process", "HeapBytes", GaugeKind.Level, static () => GC.GetTotalMemory(forceFullCollection: false)),
        new("Process", "AllocatedBytes", GaugeKind.Cumulative, static () => GC.GetTotalAllocatedBytes(precise: false)),
        new("Process", "Gen0Collections", GaugeKind.Cumulative, static () => GC.CollectionCount(0)),
        new("Process", "Gen1Collections", GaugeKind.Cumulative, static () => GC.CollectionCount(1)),
        new("Process", "Gen2Collections", GaugeKind.Cumulative, static () => GC.CollectionCount(2)),
        new("Process", "GcPauseMilliseconds", GaugeKind.Cumulative, static () => GC.GetTotalPauseDuration().TotalMilliseconds),
    ];

    private readonly Gauge[] _gauges;
    private readonly double[] _previousCumulativeReadings;
    private readonly double[] _latestValues;
    private readonly double[] _intervalMinimums;
    private readonly double[] _intervalMaximums;
    private readonly double[] _intervalTotals;
    private readonly int[] _intervalFramesNonzero;
    private readonly List<GaugeIntervalSummary> _intervalSummaries;
    private int _intervalSampleCount;

    public GaugeTracker(IReadOnlyList<Gauge> gauges)
    {
        _gauges = [.. gauges];
        _previousCumulativeReadings = new double[_gauges.Length];
        _latestValues = new double[_gauges.Length];
        _intervalMinimums = new double[_gauges.Length];
        _intervalMaximums = new double[_gauges.Length];
        _intervalTotals = new double[_gauges.Length];
        _intervalFramesNonzero = new int[_gauges.Length];
        _intervalSummaries = new List<GaugeIntervalSummary>(_gauges.Length);

        for (var gaugeIndex = 0; gaugeIndex < _gauges.Length; gaugeIndex++)
        {
            if (_gauges[gaugeIndex].Kind is GaugeKind.Cumulative)
            {
                _previousCumulativeReadings[gaugeIndex] = _gauges[gaugeIndex].Read();
            }
        }
    }

    /// <summary>Every sampled gauge, in sampling order.</summary>
    public IReadOnlyList<Gauge> Gauges => _gauges;

    /// <summary>Reads every gauge once, for the frame that just ended.</summary>
    public void Sample()
    {
        var isFirstSampleOfInterval = _intervalSampleCount == 0;
        _intervalSampleCount++;

        for (var gaugeIndex = 0; gaugeIndex < _gauges.Length; gaugeIndex++)
        {
            var gauge = _gauges[gaugeIndex];
            var reading = gauge.Read();
            var value = reading;
            if (gauge.Kind is GaugeKind.Cumulative)
            {
                value = reading - _previousCumulativeReadings[gaugeIndex];
                _previousCumulativeReadings[gaugeIndex] = reading;
            }

            _latestValues[gaugeIndex] = value;

            if (isFirstSampleOfInterval)
            {
                _intervalMinimums[gaugeIndex] = value;
                _intervalMaximums[gaugeIndex] = value;
                _intervalTotals[gaugeIndex] = 0;
                _intervalFramesNonzero[gaugeIndex] = 0;
            }
            else
            {
                _intervalMinimums[gaugeIndex] = System.Math.Min(_intervalMinimums[gaugeIndex], value);
                _intervalMaximums[gaugeIndex] = System.Math.Max(_intervalMaximums[gaugeIndex], value);
            }

            _intervalTotals[gaugeIndex] += value;
            if (value != 0)
            {
                _intervalFramesNonzero[gaugeIndex]++;
            }
        }
    }

    /// <summary>The value gaugeIndex had on the last sampled frame.</summary>
    public double GetLatestValue(int gaugeIndex) => _latestValues[gaugeIndex];

    /// <summary>Summarizes every gauge since the previous call and starts a new interval; empty when nothing was sampled since.</summary>
    /// <remarks>The returned list is reused by the next call.</remarks>
    public IReadOnlyList<GaugeIntervalSummary> TakeIntervalSummaries()
    {
        _intervalSummaries.Clear();
        if (_intervalSampleCount == 0)
        {
            return _intervalSummaries;
        }

        for (var gaugeIndex = 0; gaugeIndex < _gauges.Length; gaugeIndex++)
        {
            var gauge = _gauges[gaugeIndex];
            _intervalSummaries.Add(new GaugeIntervalSummary(
                gauge.GroupName,
                gauge.GaugeName,
                gauge.Kind,
                _latestValues[gaugeIndex],
                _intervalMinimums[gaugeIndex],
                _intervalMaximums[gaugeIndex],
                _intervalTotals[gaugeIndex],
                _intervalFramesNonzero[gaugeIndex]));
        }

        _intervalSampleCount = 0;
        return _intervalSummaries;
    }
}
