namespace SpriteManifestBuilder;

/// <summary>
/// Which linked-set piece a wall cell uses: one bit per cardinal neighbour holding a structure in the same
/// link group -- north 1, east 2, south 4, west 8 -- so a set's 16 cells are saved in this mask order. The
/// game's wall-top linking uses the same rule; keep the two in sync by hand.
/// </summary>
public static class LinkMask
{
    public const int North = 1;
    public const int East = 2;
    public const int South = 4;
    public const int West = 8;
    public const int SlotCount = 16;

    /// <summary>Masks laid out as the slot grid shows them: a 3x3 room top-left, a vertical run down the right, a horizontal run along the bottom, and the lone pillar in the corner.</summary>
    public static readonly int[,] DisplayLayout =
    {
        { East | South, East | South | West, South | West, South },
        { North | East | South, North | East | South | West, North | South | West, North | South },
        { North | East, North | East | West, North | West, North },
        { East, East | West, West, 0 },
    };

    public static int Compute(Func<int, int, bool> isLinked, int x, int y) =>
        (isLinked(x, y - 1) ? North : 0) |
        (isLinked(x + 1, y) ? East : 0) |
        (isLinked(x, y + 1) ? South : 0) |
        (isLinked(x - 1, y) ? West : 0);

    public static string Describe(int mask)
    {
        if (mask == 0)
        {
            return "joined nowhere";
        }

        var sides = new List<string>(4);
        if ((mask & North) != 0)
        {
            sides.Add("north");
        }
        if ((mask & East) != 0)
        {
            sides.Add("east");
        }
        if ((mask & South) != 0)
        {
            sides.Add("south");
        }
        if ((mask & West) != 0)
        {
            sides.Add("west");
        }

        return $"joined {string.Join("+", sides)}";
    }
}
