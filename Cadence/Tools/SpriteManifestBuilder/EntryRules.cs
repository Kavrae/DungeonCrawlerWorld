namespace SpriteManifestBuilder;

/// <summary>The checks behind the sheet gestures and the warnings: option hit-testing, duplicates, and whether a cell fits its entry's span (SpriteEntryRules, in this tool's Size).</summary>
public static class EntryRules
{
    /// <summary>The topmost (most recently added) option on sheetPath containing sourcePixel, or -1.</summary>
    public static int FindOptionAt(IReadOnlyList<SpriteManifestCell> options, string sheetPath, Point sourcePixel)
    {
        for (var index = options.Count - 1; index >= 0; index--)
        {
            if (options[index].SheetPath == sheetPath && options[index].SourceRectangle.ToDrawingRectangle().Contains(sourcePixel))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Whether an option already draws exactly the same picture -- the same pixels, flipped, turned and tinted the same way. Adding it again would silently double its odds.</summary>
    public static bool HasIdenticalOption(IReadOnlyList<SpriteManifestCell> options, SpriteManifestCell candidate) =>
        FindIdenticalOption(options, candidate) >= 0;

    public static int FindIdenticalOption(IReadOnlyList<SpriteManifestCell> options, SpriteManifestCell candidate)
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

    public static bool Fits(SpriteManifestCell cell, Size span) => SpriteEntryRules.Fits(cell, span.Width, span.Height);

    public static int CountMisfits(IEnumerable<SpriteManifestCell?> cells, Size span) => SpriteEntryRules.CountMisfits(cells, span.Width, span.Height);

    /// <inheritdoc cref="SpriteEntryRules.SpanOf(IEnumerable{SpriteManifestCell?}, int, int)"/>
    public static Size SpanOf(IEnumerable<SpriteManifestCell?> cells, int savedColumnSpan, int savedRowSpan)
    {
        var (columns, rows) = SpriteEntryRules.SpanOf(cells, savedColumnSpan, savedRowSpan);
        return new Size(columns, rows);
    }

    /// <summary>The span-sized option whose top-left grid cell is topLeft, or null when it would run off the sheet.</summary>
    public static SpriteManifestCell? SpanningFrom(string sheetPath, Point topLeft, Size span, int cellWidth, int cellHeight, Size sheetSize)
    {
        var bottomRight = new Point(topLeft.X + span.Width - 1, topLeft.Y + span.Height - 1);
        if ((bottomRight.X + 1) * cellWidth > sheetSize.Width || (bottomRight.Y + 1) * cellHeight > sheetSize.Height)
        {
            return null;
        }

        return SpriteManifestCell.Spanning(sheetPath, topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y, cellWidth, cellHeight);
    }
}
