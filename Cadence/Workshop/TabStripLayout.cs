namespace Workshop;

/// <summary>How many tabs fit on the strip, left to right, with room left for the overflow tile when some don't.</summary>
public static class TabStripLayout
{
    /// <summary>How many leading tabs to show in availableWidth: all of them when they fit, otherwise as many as fit beside an overflow tile of overflowTileWidth.</summary>
    public static int VisibleTabCount(IReadOnlyList<float> tabWidths, float availableWidth, float overflowTileWidth)
    {
        if (tabWidths.Sum() <= availableWidth)
        {
            return tabWidths.Count;
        }

        var used = 0f;
        var visibleCount = 0;
        while (visibleCount < tabWidths.Count && used + tabWidths[visibleCount] <= availableWidth - overflowTileWidth)
        {
            used += tabWidths[visibleCount];
            visibleCount++;
        }

        return visibleCount;
    }
}
