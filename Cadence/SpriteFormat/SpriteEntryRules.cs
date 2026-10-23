namespace SpriteFormat;

/// <summary>The span a part was authored at, and whether its cells fit it: what a tool marks as a misfit (the game draws each cell at its own span, so a misfit is never refused).</summary>
public static class SpriteEntryRules
{
    public static bool Fits(SpriteManifestCell cell, int columnSpan, int rowSpan) =>
        cell.EffectiveColumnSpan == columnSpan && cell.EffectiveRowSpan == rowSpan;

    public static int CountMisfits(IEnumerable<SpriteManifestCell?> cells, int columnSpan, int rowSpan) =>
        cells.Count(cell => cell is not null && !Fits(cell, columnSpan, rowSpan));

    /// <summary>The span a part was authored at: the one saved with it, else (one saved before spans were) its most common cell span, else 1x1.</summary>
    public static (int Columns, int Rows) SpanOf(IEnumerable<SpriteManifestCell?> cells, int savedColumnSpan, int savedRowSpan)
    {
        if (savedColumnSpan > 0 || savedRowSpan > 0)
        {
            return (Math.Max(1, savedColumnSpan), Math.Max(1, savedRowSpan));
        }

        return cells
            .OfType<SpriteManifestCell>()
            .GroupBy(cell => (cell.EffectiveColumnSpan, cell.EffectiveRowSpan))
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .DefaultIfEmpty((1, 1))
            .First();
    }

    /// <summary>The span part was authored at (SpanOf over its own cells and saved span).</summary>
    public static (int Columns, int Rows) SpanOf(SpriteManifestPart part) => SpanOf(part.Cells, part.ColumnSpan, part.RowSpan);

    /// <summary>How many of part's cells don't fit the span it was authored at.</summary>
    public static int CountMisfits(SpriteManifestPart part)
    {
        var (columns, rows) = SpanOf(part);
        return CountMisfits(part.Cells, columns, rows);
    }

    /// <summary>Whether any cell of the entry, or of a wall's top, doesn't fit the span its part was authored at.</summary>
    public static bool HasMisfit(SpriteManifestEntry entry) =>
        CountMisfits(entry.Face) > 0 || entry.Top is { } top && CountMisfits(top) > 0;
}
