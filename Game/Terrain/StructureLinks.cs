using Game.Sprites;

namespace Game.Terrain;

/// <summary>Whether the cell at (x, y) counts as joined to the wall whose mask is being worked out.</summary>
public interface INeighbourLinks
{
    bool LinksTo(int x, int y);
}

/// <summary>
/// Which linked-set piece a wall cell draws, from its own neighbours: the top's 4-bit mask (north 1, east 2, south 4,
/// west 8) and the face's 2-bit one (east 1, west 2). DevTools/SpriteManifestBuilder's link preview uses the same rule.
/// </summary>
/// <remarks>
/// The mask is computed, never stored in TerrainCell: neighbours change after a cell's variant is rolled (SetStructure,
/// streaming, Admin Regenerate), and a stored mask would need fixing at every one of those writes. It is worked out only when
/// the cached terrain image is rebuilt, at four reads per top and two per face. It is always read from the wall's own
/// neighbours, not from those of the tile its top is drawn on.
/// </remarks>
public static class StructureLinks
{
    public static int CardinalMask<TLinks>(in TLinks links, int x, int y)
        where TLinks : struct, INeighbourLinks =>
        (links.LinksTo(x, y - 1) ? SpriteLinks.North : 0) |
        (links.LinksTo(x + 1, y) ? SpriteLinks.East : 0) |
        (links.LinksTo(x, y + 1) ? SpriteLinks.South : 0) |
        (links.LinksTo(x - 1, y) ? SpriteLinks.West : 0);

    public static int HorizontalMask<TLinks>(in TLinks links, int x, int y)
        where TLinks : struct, INeighbourLinks =>
        (links.LinksTo(x + 1, y) ? SpriteLinks.HorizontalEast : 0) |
        (links.LinksTo(x - 1, y) ? SpriteLinks.HorizontalWest : 0);

    /// <summary>The mask a part with linking draws by, or null for a plain (options) part, which has no pieces to choose between.</summary>
    public static int? MaskFor<TLinks>(SpriteLinking linking, in TLinks links, int x, int y)
        where TLinks : struct, INeighbourLinks => linking switch
        {
            SpriteLinking.Cardinal => CardinalMask(links, x, y),
            SpriteLinking.Horizontal => HorizontalMask(links, x, y),
            _ => null,
        };
}
