namespace SpriteFormat;

/// <summary>
/// One kind of linked set as a tool shows it: how a slot grid lays out its slots, what each mask is called, which
/// piece is required, and how its masks change when a piece is mirrored or turned (SpriteLinks).
/// </summary>
public sealed class SpriteLinkLayout
{
    /// <summary>A wall's top: 16 slots, one per combination of joined sides, laid out as a 3x3 room top-left, a vertical run down the right, a horizontal run along the bottom, and the lone pillar in the corner. An empty slot falls back to the fully joined piece.</summary>
    public static readonly SpriteLinkLayout Cardinal = new(
        SpriteLinking.Cardinal,
        new[,]
        {
            { SpriteLinks.East | SpriteLinks.South, SpriteLinks.East | SpriteLinks.South | SpriteLinks.West, SpriteLinks.South | SpriteLinks.West, SpriteLinks.South },
            { SpriteLinks.North | SpriteLinks.East | SpriteLinks.South, SpriteLinks.North | SpriteLinks.East | SpriteLinks.South | SpriteLinks.West, SpriteLinks.North | SpriteLinks.South | SpriteLinks.West, SpriteLinks.North | SpriteLinks.South },
            { SpriteLinks.North | SpriteLinks.East, SpriteLinks.North | SpriteLinks.East | SpriteLinks.West, SpriteLinks.North | SpriteLinks.West, SpriteLinks.North },
            { SpriteLinks.East, SpriteLinks.East | SpriteLinks.West, SpriteLinks.West, 0 },
        },
        requiredMask: null,
        DescribeCardinal,
        static mask => ((mask & SpriteLinks.North) != 0, (mask & SpriteLinks.East) != 0, (mask & SpriteLinks.South) != 0, (mask & SpriteLinks.West) != 0),
        static (isLinked, x, y) =>
            (isLinked(x, y - 1) ? SpriteLinks.North : 0) |
            (isLinked(x + 1, y) ? SpriteLinks.East : 0) |
            (isLinked(x, y + 1) ? SpriteLinks.South : 0) |
            (isLinked(x - 1, y) ? SpriteLinks.West : 0),
        mirrorTopBottom: SpriteLinks.MirroredTopBottom,
        turnClockwise: SpriteLinks.TurnedClockwise);

    /// <summary>A wall's face: standalone (no wall either side), left end (a wall only to the east), right end (only to the west) and middle. Only the standalone piece is required; the others fall back to it.</summary>
    public static readonly SpriteLinkLayout Horizontal = new(
        SpriteLinking.Horizontal,
        new[,] { { SpriteLinks.HorizontalEast, SpriteLinks.HorizontalEast | SpriteLinks.HorizontalWest, SpriteLinks.HorizontalWest, SpriteLinks.Standalone } },
        requiredMask: SpriteLinks.Standalone,
        static mask => mask switch
        {
            SpriteLinks.HorizontalEast => "left end",
            SpriteLinks.HorizontalWest => "right end",
            SpriteLinks.HorizontalEast | SpriteLinks.HorizontalWest => "middle",
            _ => "standalone",
        },
        static mask => (false, (mask & SpriteLinks.HorizontalEast) != 0, false, (mask & SpriteLinks.HorizontalWest) != 0),
        static (isLinked, x, y) => (isLinked(x + 1, y) ? SpriteLinks.HorizontalEast : 0) | (isLinked(x - 1, y) ? SpriteLinks.HorizontalWest : 0),
        mirrorTopBottom: null,
        turnClockwise: null);

    private readonly Func<int, string> _describe;
    private readonly Func<int, (bool North, bool East, bool South, bool West)> _sidesOf;
    private readonly Func<Func<int, int, bool>, int, int, int> _compute;

    private SpriteLinkLayout(
        SpriteLinking linking,
        int[,] displayLayout,
        int? requiredMask,
        Func<int, string> describe,
        Func<int, (bool North, bool East, bool South, bool West)> sidesOf,
        Func<Func<int, int, bool>, int, int, int> compute,
        Func<int, int>? mirrorTopBottom,
        Func<int, int>? turnClockwise)
    {
        Linking = linking;
        SlotCount = SpriteLinks.CellsPerSet(linking);
        DisplayLayout = displayLayout;
        FallbackMask = SpriteLinks.FallbackMask(linking);
        RequiredMask = requiredMask;
        _describe = describe;
        _sidesOf = sidesOf;
        _compute = compute;
        MirrorLeftRight = mask => SpriteLinks.MirroredLeftRight(linking, mask);
        MirrorTopBottom = mirrorTopBottom;
        TurnClockwise = turnClockwise;
    }

    public SpriteLinking Linking { get; }

    public int SlotCount { get; }

    /// <summary>The masks in the order the slot grid shows them, rows by columns.</summary>
    public int[,] DisplayLayout { get; }

    /// <summary>The piece drawn for an empty slot.</summary>
    public int FallbackMask { get; }

    /// <summary>The one slot every variant set must fill before it can be saved, if any.</summary>
    public int? RequiredMask { get; }

    public Func<int, int> MirrorLeftRight { get; }

    /// <summary>Null when this kind of set has no vertical joins to swap.</summary>
    public Func<int, int>? MirrorTopBottom { get; }

    /// <summary>Null when a turn would join sides this kind of set doesn't have.</summary>
    public Func<int, int>? TurnClockwise { get; }

    /// <summary>The mask selected by default: the required slot, else the fallback.</summary>
    public int DefaultSelectedMask => RequiredMask ?? FallbackMask;

    public string Describe(int mask) => _describe(mask);

    public (bool North, bool East, bool South, bool West) SidesOf(int mask) => _sidesOf(mask);

    /// <summary>The mask of the piece drawn at (x, y): one bit per side whose neighbour isLinked says joins it.</summary>
    public int Compute(Func<int, int, bool> isLinked, int x, int y) => _compute(isLinked, x, y);

    /// <summary>The piece a set draws for mask: its own slot, else the fallback's, else null.</summary>
    public SpriteManifestCell? PieceFor(SpriteManifestCell?[] slots, int mask) => slots[mask] ?? slots[FallbackMask];

    public SpriteManifestCell?[] NewVariantSet() => new SpriteManifestCell?[SlotCount];

    private static string DescribeCardinal(int mask)
    {
        if (mask == 0)
        {
            return "joined nowhere";
        }

        var sides = new List<string>(4);
        if ((mask & SpriteLinks.North) != 0)
        {
            sides.Add("north");
        }
        if ((mask & SpriteLinks.East) != 0)
        {
            sides.Add("east");
        }
        if ((mask & SpriteLinks.South) != 0)
        {
            sides.Add("south");
        }
        if ((mask & SpriteLinks.West) != 0)
        {
            sides.Add("west");
        }

        return $"joined {string.Join("+", sides)}";
    }
}
