namespace Game.Sprites;

/// <summary>The neighbour masks of each SpriteLinking, and which piece stands in for one a set leaves empty.</summary>
public static class SpriteLinks
{
    public const int North = 1;
    public const int East = 2;
    public const int South = 4;
    public const int West = 8;

    public const int HorizontalEast = 1;
    public const int HorizontalWest = 2;

    /// <summary>The Horizontal piece with no wall either side, the one every set must hold.</summary>
    public const int Standalone = 0;

    /// <summary>Cells per variant set: 16 for Cardinal, 4 for Horizontal, 1 (each option its own set) for None.</summary>
    public static int CellsPerSet(SpriteLinking linking) => linking switch
    {
        SpriteLinking.Cardinal => 16,
        SpriteLinking.Horizontal => 4,
        _ => 1,
    };

    /// <summary>The piece an empty slot falls back to: Horizontal's standalone piece, Cardinal's fully joined one.</summary>
    public static int FallbackMask(SpriteLinking linking) => linking switch
    {
        SpriteLinking.Horizontal => Standalone,
        SpriteLinking.Cardinal => North | East | South | West,
        _ => 0,
    };

    /// <summary>The percentile a cell rolls (0..99) when its sprites pick variant sets by chance (VariantWeights) rather than evenly.</summary>
    public const int PercentileRange = 100;

    /// <summary>
    /// Which of a part's setCount variant sets a cell's variant picks: with VariantWeights, the variant is a percentile (0..99) and the
    /// set is the one whose cumulative chance it falls under; without, each set is equally likely, variant modulo the set count.
    /// </summary>
    public static int SetIndexFor(SpriteManifestPart part, int variant, int setCount)
    {
        if (part.VariantWeights is not { Count: > 0 } weights || weights.Count != setCount)
        {
            return variant % setCount;
        }

        var percentile = variant % PercentileRange;
        var cumulative = 0;
        for (var setIndex = 0; setIndex < weights.Count; setIndex++)
        {
            cumulative += weights[setIndex];
            if (percentile < cumulative)
            {
                return setIndex;
            }
        }

        return setCount - 1;
    }

    /// <summary>The index in Cells of the piece a cell of the given variant uses for mask -- its set picked by SetIndexFor -- falling back when that slot is empty, or -1 when the fallback is empty too.</summary>
    public static int CellIndexFor(SpriteManifestPart part, int variant, int mask)
    {
        var cellsPerSet = CellsPerSet(part.Linking);
        var setCount = part.Cells.Count / cellsPerSet;
        if (setCount == 0)
        {
            return -1;
        }

        var setStart = SetIndexFor(part, variant, setCount) * cellsPerSet;
        if (part.Cells[setStart + mask] is not null)
        {
            return setStart + mask;
        }

        var fallbackIndex = setStart + FallbackMask(part.Linking);
        return part.Cells[fallbackIndex] is not null ? fallbackIndex : -1;
    }
}
