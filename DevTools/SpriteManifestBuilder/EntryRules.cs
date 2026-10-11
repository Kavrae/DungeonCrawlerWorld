namespace SpriteManifestBuilder;

/// <summary>The checks behind the sheet gestures and the warnings: option hit-testing, duplicates, and whether a cell fits its entry's span.</summary>
public static class EntryRules
{
    /// <summary>The topmost (most recently added) option on sheetPath containing sourcePixel, or -1.</summary>
    public static int FindOptionAt(IReadOnlyList<ManifestCell> options, string sheetPath, Point sourcePixel)
    {
        for (var index = options.Count - 1; index >= 0; index--)
        {
            if (options[index].SheetPath == sheetPath && options[index].SourceRectangle.Contains(sourcePixel))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Whether an option already draws exactly the same picture -- the same pixels, flipped, turned and tinted the same way. Adding it again would silently double its odds.</summary>
    public static bool HasIdenticalOption(IReadOnlyList<ManifestCell> options, ManifestCell candidate) =>
        FindIdenticalOption(options, candidate) >= 0;

    public static int FindIdenticalOption(IReadOnlyList<ManifestCell> options, ManifestCell candidate)
    {
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            if (option.SheetPath == candidate.SheetPath && option.SourceRectangle == candidate.SourceRectangle &&
                option.Rotation == candidate.Rotation && option.FlipHorizontal == candidate.FlipHorizontal && option.FlipVertical == candidate.FlipVertical &&
                option.Tint == candidate.Tint)
            {
                return index;
            }
        }

        return -1;
    }

    public static bool Fits(ManifestCell cell, Size span) =>
        cell.EffectiveColumnSpan == span.Width && cell.EffectiveRowSpan == span.Height;

    public static int CountMisfits(IEnumerable<ManifestCell?> cells, Size span) =>
        cells.Count(cell => cell is not null && !Fits(cell, span));

    /// <summary>The span a part was authored at: the one saved with it, else (one saved before spans were) its most common cell span, else 1x1.</summary>
    public static Size SpanOf(IEnumerable<ManifestCell?> cells, int savedColumnSpan, int savedRowSpan)
    {
        if (savedColumnSpan > 0 || savedRowSpan > 0)
        {
            return new Size(Math.Max(1, savedColumnSpan), Math.Max(1, savedRowSpan));
        }

        return cells
            .OfType<ManifestCell>()
            .GroupBy(cell => new Size(cell.EffectiveColumnSpan, cell.EffectiveRowSpan))
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .DefaultIfEmpty(new Size(1, 1))
            .First();
    }

    /// <summary>Whether any cell of the entry, or of a wall's top, doesn't fit the span its part was authored at.</summary>
    public static bool HasMisfit(ManifestEntry entry) =>
        CountMisfits(entry.Cells, SpanOf(entry.Cells, entry.ColumnSpan, entry.RowSpan)) > 0 ||
        entry.Top is { } top && CountMisfits(top.Cells, SpanOf(top.Cells, top.ColumnSpan, top.RowSpan)) > 0;

    /// <summary>The span-sized option whose top-left grid cell is topLeft, or null when it would run off the sheet.</summary>
    public static ManifestCell? SpanningFrom(string sheetPath, Point topLeft, Size span, int cellWidth, int cellHeight, Size sheetSize)
    {
        var bottomRight = new Point(topLeft.X + span.Width - 1, topLeft.Y + span.Height - 1);
        if ((bottomRight.X + 1) * cellWidth > sheetSize.Width || (bottomRight.Y + 1) * cellHeight > sheetSize.Height)
        {
            return null;
        }

        return ManifestCell.Spanning(sheetPath, topLeft, bottomRight, cellWidth, cellHeight);
    }
}
