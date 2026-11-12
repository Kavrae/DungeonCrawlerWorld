using System.Diagnostics;

namespace Engine.Diagnostics;

/// <summary>Rolling wall-clock cost tracker by frame-cost row: each row's milliseconds per second over the last window of about a second, and over a longer report interval.</summary>
/// <remarks>
/// Snapshot holds the last closed window, sorted descending by cost, for a live readout (TopEntries collapses it to one
/// "GroupName.ItemName" label per row). TakeIntervalSnapshot holds every window closed since the previous call, for a
/// periodic report that must not miss what happened between its reports. Both are per second of the time they cover:
/// a window closes on the first moment no measurement is open at least a second after it opened, so its length varies,
/// and dividing by that length keeps a hitch or a long frame from reading as a costlier second. The first window opens
/// at the first measurement, not at construction, so the time before anything was measured never dilutes it.
///
/// A window closes only while no measurement is open (between frames, after a draw), so closing it -- sorting and
/// copying the rows -- lands in no row, and a window never splits a measurement from the ones nested in it. A Record
/// otherwise costs a few array writes; a row's time is added only by its outermost open measurement (see
/// FrameCostOpenMeasurements), and its parents are what it was recorded directly inside.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class FrameBudgetTracker : IFrameCostRecorder
{
    private static readonly long WindowTimestampTicks = Stopwatch.Frequency;

    private readonly FrameCostOpenMeasurements _openMeasurements = new();
    private readonly List<FrameCostEntry> _snapshot = [];
    private readonly List<(string Name, double MillisecondsPerSecond)> _topEntries = [];

    private RowTotals[] _windowTotalsByRow = new RowTotals[64];
    private RowTotals[] _intervalTotalsByRow = new RowTotals[64];
    private int[] _lastParentRowIndexPlusOneByRow = new int[64];
    private List<int>?[] _parentRowIndexesByRow = new List<int>?[64];
    private FrameCostRowName[]?[] _parentNamesByRow = new FrameCostRowName[]?[64];
    private string?[] _labelsByRow = new string?[64];
    private int _rowCountSeen;

    private bool _isWindowOpen;
    private long _windowStartTimestamp;
    private long _intervalTimestampTicks;

    /// <summary>Every row recorded in the last closed window, per second of it, descending by cost.</summary>
    /// <remarks>Empty until the first window closes.</remarks>
    public IReadOnlyList<FrameCostEntry> Snapshot => _snapshot;

    /// <summary>Same ranking as Snapshot, collapsed to a single "GroupName.ItemName" label per entry.</summary>
    /// <remarks>[0] is always the single largest contributor. Empty until the first window closes.</remarks>
    public IReadOnlyList<(string Name, double MillisecondsPerSecond)> TopEntries => _topEntries;

    /// <summary>Opens a measurement of row: anything recorded before its Record ran inside it.</summary>
    public void Begin(FrameCostRowId row)
    {
        if (!_isWindowOpen)
        {
            OpenWindow(Stopwatch.GetTimestamp());
        }

        EnsureRow(row.Index);
        _openMeasurements.Open(row.Index);
    }

    /// <summary>Records the elapsed time for row, closing its measurement, and notes the measurement still open around it as its parent.</summary>
    public void Record(FrameCostRowId row, TimeSpan elapsed)
    {
        var rowIndex = row.Index;
        EnsureRow(rowIndex);
        var isOutermost = _openMeasurements.Close(rowIndex);

        ref var windowTotals = ref _windowTotalsByRow[rowIndex];
        windowTotals.RecordCount++;
        if (isOutermost)
        {
            windowTotals.Milliseconds += elapsed.TotalMilliseconds;
        }

        var parentRowIndex = _openMeasurements.InnermostOpenRowIndexOtherThan(rowIndex);
        if (parentRowIndex >= 0 && _lastParentRowIndexPlusOneByRow[rowIndex] != parentRowIndex + 1)
        {
            _lastParentRowIndexPlusOneByRow[rowIndex] = parentRowIndex + 1;
            AddParent(rowIndex, parentRowIndex);
        }

        if (_openMeasurements.IsEmpty)
        {
            CloseWindowIfDue();
        }
    }

    /// <summary>Every row recorded in the windows closed since the previous call, per second of them, descending by cost; empty when none closed.</summary>
    /// <param name="intervalSeconds">How long those windows lasted together.</param>
    public FrameCostEntry[] TakeIntervalSnapshot(out double intervalSeconds)
    {
        intervalSeconds = _intervalTimestampTicks / (double)Stopwatch.Frequency;
        if (_intervalTimestampTicks == 0)
        {
            return [];
        }

        var entries = new List<FrameCostEntry>();
        for (var rowIndex = 0; rowIndex < _rowCountSeen; rowIndex++)
        {
            var intervalTotals = _intervalTotalsByRow[rowIndex];
            if (intervalTotals.RecordCount == 0)
            {
                continue;
            }

            var name = FrameCostRowIds.NameOf(new FrameCostRowId(rowIndex));
            entries.Add(new FrameCostEntry(name.Category, name.GroupName, name.ItemName, intervalTotals.Milliseconds / intervalSeconds, ParentNamesOf(rowIndex)));
        }

        entries.Sort(static (first, second) => second.MillisecondsPerSecond.CompareTo(first.MillisecondsPerSecond));
        Array.Clear(_intervalTotalsByRow, 0, _rowCountSeen);
        _intervalTimestampTicks = 0;
        return [.. entries];
    }

    private void OpenWindow(long timestamp)
    {
        _isWindowOpen = true;
        _windowStartTimestamp = timestamp;
    }

    private void CloseWindowIfDue()
    {
        var now = Stopwatch.GetTimestamp();
        if (!_isWindowOpen)
        {
            OpenWindow(now);
            return;
        }

        var windowTimestampTicks = now - _windowStartTimestamp;
        if (windowTimestampTicks < WindowTimestampTicks)
        {
            return;
        }

        CloseWindow(windowTimestampTicks);
        OpenWindow(now);
    }

    private void CloseWindow(long windowTimestampTicks)
    {
        var windowSeconds = windowTimestampTicks / (double)Stopwatch.Frequency;
        _snapshot.Clear();
        _topEntries.Clear();
        for (var rowIndex = 0; rowIndex < _rowCountSeen; rowIndex++)
        {
            var windowTotals = _windowTotalsByRow[rowIndex];
            if (windowTotals.RecordCount == 0)
            {
                continue;
            }

            ref var intervalTotals = ref _intervalTotalsByRow[rowIndex];
            intervalTotals.Milliseconds += windowTotals.Milliseconds;
            intervalTotals.RecordCount += windowTotals.RecordCount;

            var name = FrameCostRowIds.NameOf(new FrameCostRowId(rowIndex));
            var millisecondsPerSecond = windowTotals.Milliseconds / windowSeconds;
            _snapshot.Add(new FrameCostEntry(name.Category, name.GroupName, name.ItemName, millisecondsPerSecond, ParentNamesOf(rowIndex)));
            _topEntries.Add((LabelOf(rowIndex, name), millisecondsPerSecond));
        }

        _snapshot.Sort(static (first, second) => second.MillisecondsPerSecond.CompareTo(first.MillisecondsPerSecond));
        _topEntries.Sort(static (first, second) => second.MillisecondsPerSecond.CompareTo(first.MillisecondsPerSecond));
        Array.Clear(_windowTotalsByRow, 0, _rowCountSeen);
        Array.Clear(_lastParentRowIndexPlusOneByRow, 0, _rowCountSeen);
        _intervalTimestampTicks += windowTimestampTicks;
    }

    private void EnsureRow(int rowIndex)
    {
        if (rowIndex < _rowCountSeen)
        {
            return;
        }

        _rowCountSeen = rowIndex + 1;
        if (rowIndex < _windowTotalsByRow.Length)
        {
            return;
        }

        var rowCapacity = System.Math.Max(rowIndex + 1, _windowTotalsByRow.Length * 2);
        Array.Resize(ref _windowTotalsByRow, rowCapacity);
        Array.Resize(ref _intervalTotalsByRow, rowCapacity);
        Array.Resize(ref _lastParentRowIndexPlusOneByRow, rowCapacity);
        Array.Resize(ref _parentRowIndexesByRow, rowCapacity);
        Array.Resize(ref _parentNamesByRow, rowCapacity);
        Array.Resize(ref _labelsByRow, rowCapacity);
    }

    private void AddParent(int rowIndex, int parentRowIndex)
    {
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
        if (_parentRowIndexesByRow[rowIndex] is not { } parents)
        {
            return null;
        }

        return _parentNamesByRow[rowIndex] ??= [.. parents.Select(static parentRowIndex => FrameCostRowIds.NameOf(new FrameCostRowId(parentRowIndex)))];
    }

    private string LabelOf(int rowIndex, FrameCostRowName name) =>
        _labelsByRow[rowIndex] ??= $"{name.GroupName}.{name.ItemName}";

    private struct RowTotals
    {
        public double Milliseconds;
        public int RecordCount;
    }
}
