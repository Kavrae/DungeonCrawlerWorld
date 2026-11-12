namespace Engine.Diagnostics;

/// <summary>The frame-cost measurements open now, innermost last, and how many of each row are open.</summary>
/// <remarks>
/// Shared by every recorder that nests, so each reads a Record the same way. A Record closes its row's innermost open
/// measurement and everything opened inside it, so one emit site that never records can't misplace the rest. A row open
/// inside itself (an event published by its own handler) is outermost only when its last open measurement closes: the
/// outer measurement's elapsed already holds the inner one's, so only the outermost adds time.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
internal sealed class FrameCostOpenMeasurements
{
    private int[] _openRowIndexes = new int[32];
    private int _openCount;
    private int[] _openCountByRowIndex = new int[64];

    public bool IsEmpty => _openCount == 0;

    public void Open(int rowIndex)
    {
        if (_openCount == _openRowIndexes.Length)
        {
            Array.Resize(ref _openRowIndexes, _openRowIndexes.Length * 2);
        }

        _openRowIndexes[_openCount++] = rowIndex;
        EnsureRow(rowIndex);
        _openCountByRowIndex[rowIndex]++;
    }

    /// <summary>Closes rowIndex's innermost open measurement and everything opened inside it.</summary>
    /// <returns>True when no measurement of rowIndex is still open around it -- its time is not already inside another of its own; also true for a Record with no Begin.</returns>
    public bool Close(int rowIndex)
    {
        var openIndex = _openCount - 1;
        while (openIndex >= 0 && _openRowIndexes[openIndex] != rowIndex)
        {
            openIndex--;
        }

        if (openIndex < 0)
        {
            return rowIndex >= _openCountByRowIndex.Length || _openCountByRowIndex[rowIndex] == 0;
        }

        for (var closingIndex = _openCount - 1; closingIndex >= openIndex; closingIndex--)
        {
            _openCountByRowIndex[_openRowIndexes[closingIndex]]--;
        }

        _openCount = openIndex;
        return _openCountByRowIndex[rowIndex] == 0;
    }

    /// <summary>The innermost open measurement of another row, or -1: one nested in a measurement of itself is placed where the outer one is, never under itself.</summary>
    public int InnermostOpenRowIndexOtherThan(int rowIndex)
    {
        for (var openIndex = _openCount - 1; openIndex >= 0; openIndex--)
        {
            if (_openRowIndexes[openIndex] != rowIndex)
            {
                return _openRowIndexes[openIndex];
            }
        }

        return -1;
    }

    private void EnsureRow(int rowIndex)
    {
        if (rowIndex >= _openCountByRowIndex.Length)
        {
            Array.Resize(ref _openCountByRowIndex, System.Math.Max(rowIndex + 1, _openCountByRowIndex.Length * 2));
        }
    }
}
