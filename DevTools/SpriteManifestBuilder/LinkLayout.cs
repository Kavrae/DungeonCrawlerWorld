namespace SpriteManifestBuilder;

/// <summary>
/// One kind of linked set: how many slots it has, how the slot grid lays them out, what each mask means, which piece an
/// empty slot falls back to, and how a mask changes when its piece is mirrored or turned. Mirrors Game/Sprites/SpriteLinks.cs;
/// keep the two in sync by hand.
/// </summary>
public sealed class LinkLayout
{
    /// <summary>A wall's top: 16 slots, one per combination of joined sides (LinkMask). An empty slot falls back to the fully joined piece.</summary>
    public static readonly LinkLayout Cardinal = new(
        SpriteLinking.Cardinal,
        LinkMask.SlotCount,
        LinkMask.DisplayLayout,
        fallbackMask: LinkMask.North | LinkMask.East | LinkMask.South | LinkMask.West,
        requiredMask: null,
        LinkMask.Describe,
        static mask => ((mask & LinkMask.North) != 0, (mask & LinkMask.East) != 0, (mask & LinkMask.South) != 0, (mask & LinkMask.West) != 0),
        LinkMask.Compute,
        mirrorLeftRight: static mask => SwapBits(mask, LinkMask.East, LinkMask.West),
        mirrorTopBottom: static mask => SwapBits(mask, LinkMask.North, LinkMask.South),
        turnClockwise: static mask =>
            ((mask & LinkMask.North) != 0 ? LinkMask.East : 0) |
            ((mask & LinkMask.East) != 0 ? LinkMask.South : 0) |
            ((mask & LinkMask.South) != 0 ? LinkMask.West : 0) |
            ((mask & LinkMask.West) != 0 ? LinkMask.North : 0));

    public const int HorizontalEast = 1;
    public const int HorizontalWest = 2;
    public const int Standalone = 0;

    /// <summary>A wall's face: standalone (no wall either side), left end (a wall only to the east), right end (only to the west) and middle. Only the standalone piece is required; the others fall back to it.</summary>
    public static readonly LinkLayout Horizontal = new(
        SpriteLinking.Horizontal,
        4,
        new[,] { { HorizontalEast, HorizontalEast | HorizontalWest, HorizontalWest, Standalone } },
        fallbackMask: Standalone,
        requiredMask: Standalone,
        static mask => mask switch
        {
            HorizontalEast => "left end",
            HorizontalWest => "right end",
            HorizontalEast | HorizontalWest => "middle",
            _ => "standalone",
        },
        static mask => (false, (mask & HorizontalEast) != 0, false, (mask & HorizontalWest) != 0),
        static (isLinked, x, y) => (isLinked(x + 1, y) ? HorizontalEast : 0) | (isLinked(x - 1, y) ? HorizontalWest : 0),
        mirrorLeftRight: static mask => SwapBits(mask, HorizontalEast, HorizontalWest),
        mirrorTopBottom: null,
        turnClockwise: null);

    private readonly Func<int, string> _describe;
    private readonly Func<int, (bool North, bool East, bool South, bool West)> _sidesOf;
    private readonly Func<Func<int, int, bool>, int, int, int> _compute;

    private LinkLayout(
        SpriteLinking linking,
        int slotCount,
        int[,] displayLayout,
        int fallbackMask,
        int? requiredMask,
        Func<int, string> describe,
        Func<int, (bool North, bool East, bool South, bool West)> sidesOf,
        Func<Func<int, int, bool>, int, int, int> compute,
        Func<int, int> mirrorLeftRight,
        Func<int, int>? mirrorTopBottom,
        Func<int, int>? turnClockwise)
    {
        Linking = linking;
        SlotCount = slotCount;
        DisplayLayout = displayLayout;
        FallbackMask = fallbackMask;
        RequiredMask = requiredMask;
        _describe = describe;
        _sidesOf = sidesOf;
        _compute = compute;
        MirrorLeftRight = mirrorLeftRight;
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

    public int Compute(Func<int, int, bool> isLinked, int x, int y) => _compute(isLinked, x, y);

    /// <summary>The piece a set draws for mask: its own slot, else the fallback's, else null.</summary>
    public ManifestCell? PieceFor(ManifestCell?[] slots, int mask) => slots[mask] ?? slots[FallbackMask];

    public ManifestCell?[] NewVariantSet() => new ManifestCell?[SlotCount];

    private static int SwapBits(int mask, int first, int second) =>
        (mask & ~(first | second)) |
        ((mask & first) != 0 ? second : 0) |
        ((mask & second) != 0 ? first : 0);
}
