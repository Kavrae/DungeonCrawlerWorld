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

    /// <summary>The rows of the measurements open now, outermost first.</summary>
    private readonly List<int> _openRowIndexes = [];

    /// <summary>Each row's parents, as row indexes, and as names for a snapshot; a row recorded only at the top level has none.</summary>
    private readonly List<List<int>?> _parentRowIndexesByRow = [];
    private readonly List<FrameCostRowName[]?> _parentNamesByRow = [];

    private long _lastSampleTimestamp = Stopwatch.GetTimestamp();

    /// <summary>Every entry recorded over the last full second, descending by wall-clock cost.</summary>
    /// <remarks>Empty until the first full second elapses.</remarks>
    public IReadOnlyList<FrameCostEntry> Snapshot => _snapshot;

    /// <summary>Same ranking as Snapshot, collapsed to a single "GroupName.ItemName" label per entry.</summary>
    /// <remarks>[0] is always the single largest contributor. Empty until the first full second elapses.</remarks>
    public IReadOnlyList<(string Name, double MillisecondsPerSecond)> TopEntries => _topEntries;

    /// <summary>Opens a measurement of one item: anything recorded before its Record ran inside it.</summary>
    public void Begin(FrameCostCategory category, string groupName, string itemName) =>
        _openRowIndexes.Add(_entriesSinceLastSample.GetOrAddRowIndex(category, groupName, itemName));

    /// <summary>Records the elapsed time for one item, closing its measurement, and notes the measurement still open around it as its parent.</summary>
    /// <remarks>A Record whose Begin is not the innermost open measurement closes everything opened inside it too, so one emit site that never records can't misplace the rest; a Record with no Begin closes nothing.</remarks>
    public void Record(FrameCostCategory category, string groupName, string itemName, TimeSpan elapsed)
    {
        var rowIndex = _entriesSinceLastSample.GetOrAddRowIndex(category, groupName, itemName);
        var openIndex = _openRowIndexes.LastIndexOf(rowIndex);
        if (openIndex >= 0)
        {
            _openRowIndexes.RemoveRange(openIndex, _openRowIndexes.Count - openIndex);
        }

        ref var entryTotals = ref _entriesSinceLastSample.TotalsAt(rowIndex);
        entryTotals.Milliseconds += elapsed.TotalMilliseconds;
        entryTotals.RecordCount++;
        if (InnermostOpenRowOtherThan(rowIndex) is { } parentRowIndex)
        {
            if (entryTotals.LastParentRowIndexPlusOne != parentRowIndex + 1)
            {
                entryTotals.LastParentRowIndexPlusOne = parentRowIndex + 1;
                AddParent(rowIndex, parentRowIndex);
            }
        }

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
            _snapshot.Add(new FrameCostEntry(name.Category, name.GroupName, name.ItemName, entryTotals.Milliseconds, ParentNamesOf(rowIndex)));
            _topEntries.Add((LabelOf(rowIndex, name), entryTotals.Milliseconds));
        }

        _snapshot.Sort(static (a, b) => b.MillisecondsPerSecond.CompareTo(a.MillisecondsPerSecond));
        _topEntries.Sort(static (a, b) => b.MillisecondsPerSecond.CompareTo(a.MillisecondsPerSecond));

        _entriesSinceLastSample.ClearTotals();
    }

    /// <summary>The innermost open measurement of another row: one nested in a measurement of itself (an event published by its own handler) is parented where the outer one is, never under itself.</summary>
    private int? InnermostOpenRowOtherThan(int rowIndex)
    {
        for (var openIndex = _openRowIndexes.Count - 1; openIndex >= 0; openIndex--)
        {
            if (_openRowIndexes[openIndex] != rowIndex)
            {
                return _openRowIndexes[openIndex];
            }
        }

        return null;
    }

    private void AddParent(int rowIndex, int parentRowIndex)
    {
        while (_parentRowIndexesByRow.Count <= rowIndex)
        {
            _parentRowIndexesByRow.Add(null);
            _parentNamesByRow.Add(null);
        }

        var parents = _parentRowIndexesByRow[rowIndex] ??= [];
        if (!parents.Contains(parentRowIndex))
        {
            parents.Add(parentRowIndex);
            _parentNamesByRow[rowIndex] = null;
        }
    }

    /// <summary>Every row rowIndex was recorded inside, by name; null for a top-level row. Rebuilt only when a parent is added.</summary>
    private FrameCostRowName[]? ParentNamesOf(int rowIndex)
    {
        if (rowIndex >= _parentRowIndexesByRow.Count || _parentRowIndexesByRow[rowIndex] is not { } parents)
        {
            return null;
        }

        return _parentNamesByRow[rowIndex] ??= [.. parents.Select(_entriesSinceLastSample.NameAt)];
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

        /// <summary>The parent row the last Record found plus one, 0 for none yet this second: a Record under the same parent as the last skips the parent list.</summary>
        public int LastParentRowIndexPlusOne;
    }
}
