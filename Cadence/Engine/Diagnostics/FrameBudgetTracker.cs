using System.Diagnostics;

namespace Engine.Diagnostics;

/// <summary>Rolling once-per-second wall-clock cost tracker, keyed by (category, group, item).</summary>
/// <remarks>
/// Record(category, groupName, itemName, elapsed) once per occurrence, pairing it with
/// Stopwatch.GetTimestamp()/GetElapsedTime() at the call site so the measurement itself
/// allocates nothing.
///
/// Snapshot exposes the last full second's per-entry totals, sorted descending by cost --
/// DiagnosticsReportWriter regroups this flat list into "Update vs Draw, then per group" for
/// file output. TopEntries exposes the same ranking collapsed to a single "GroupName.ItemName"
/// label per entry, for callers (a live readout) that just want the single largest
/// overall contributor.
///
/// Intended as an opt-in diagnostic (see EngineHooks.FrameCosts, DiagnosticsEngine) for
/// tracking frame budgets. Rows are found by their names' references (see FrameCostRows), and the
/// second rolls over on a Stopwatch timestamp, so a Record on the hottest paths costs little of
/// what it measures.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class FrameBudgetTracker : IFrameCostRecorder
{
    private static readonly long TimestampTicksBetweenSamples = Stopwatch.Frequency;

    private readonly FrameCostRows<EntryTotals> _entriesSinceLastSample = new();
    private readonly List<FrameCostEntry> _snapshot = [];
    private readonly List<(string Name, double MillisecondsPerSecond)> _topEntries = [];

    /// <summary>Each row's "GroupName.ItemName" label, built the first time the row reaches a snapshot.</summary>
    private readonly List<string> _labelsByRowIndex = [];

    private long _lastSampleTimestamp = Stopwatch.GetTimestamp();

    /// <summary>Every entry recorded over the last full second, descending by wall-clock cost.</summary>
    /// <remarks>Empty until the first full second elapses.</remarks>
    public IReadOnlyList<FrameCostEntry> Snapshot => _snapshot;

    /// <summary>Same ranking as Snapshot, collapsed to a single "GroupName.ItemName" label per entry.</summary>
    /// <remarks>[0] is always the single largest contributor. Empty until the first full second elapses.</remarks>
    public IReadOnlyList<(string Name, double MillisecondsPerSecond)> TopEntries => _topEntries;

    /// <summary>Records the elapsed time for one item.</summary>
    public void Record(FrameCostCategory category, string groupName, string itemName, TimeSpan elapsed)
    {
        ref var entryTotals = ref _entriesSinceLastSample.GetOrAdd(category, groupName, itemName);
        entryTotals.Milliseconds += elapsed.TotalMilliseconds;
        entryTotals.RecordCount++;

        var currentTimestamp = Stopwatch.GetTimestamp();
        if (currentTimestamp - _lastSampleTimestamp < TimestampTicksBetweenSamples)
        {
            return;
        }

        TakeSample();
        _lastSampleTimestamp = currentTimestamp;
    }

    private void TakeSample()
    {
        _snapshot.Clear();
        _topEntries.Clear();
        for (var rowIndex = 0; rowIndex < _entriesSinceLastSample.Count; rowIndex++)
        {
            var entryTotals = _entriesSinceLastSample.TotalsAt(rowIndex);
            if (entryTotals.RecordCount == 0)
            {
                continue;
            }

            var name = _entriesSinceLastSample.NameAt(rowIndex);
            _snapshot.Add(new FrameCostEntry(name.Category, name.GroupName, name.ItemName, entryTotals.Milliseconds));
            _topEntries.Add((LabelOf(rowIndex, name), entryTotals.Milliseconds));
        }

        _snapshot.Sort(static (a, b) => b.MillisecondsPerSecond.CompareTo(a.MillisecondsPerSecond));
        _topEntries.Sort(static (a, b) => b.MillisecondsPerSecond.CompareTo(a.MillisecondsPerSecond));

        _entriesSinceLastSample.ClearTotals();
    }

    private string LabelOf(int rowIndex, FrameCostRowName name)
    {
        while (_labelsByRowIndex.Count <= rowIndex)
        {
            _labelsByRowIndex.Add(string.Empty);
        }

        if (_labelsByRowIndex[rowIndex].Length == 0)
        {
            _labelsByRowIndex[rowIndex] = $"{name.GroupName}.{name.ItemName}";
        }

        return _labelsByRowIndex[rowIndex];
    }

    private struct EntryTotals
    {
        public double Milliseconds;
        public int RecordCount;
    }
}
