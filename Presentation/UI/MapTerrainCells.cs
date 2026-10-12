using Game.Terrain;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Rendering;
using SpriteFormat;

namespace Presentation.UI;

/// <summary>The terrain and structures around the visible grid, read once per rebuild of the terrain image and resolved for drawing: each cell's floor, a wall's face and top, and which neighbours a wall joins.</summary>
/// <remarks>
/// <para>
/// Read copies the grid plus a margin wide enough for everything the image and MapWallPlan look at -- the row walk's overscan, a wall's
/// picture reaching out of it, and one more tile all round for join masks -- into one MapCellBuffer (IMapViewQuery.CopyCells), so a rebuild
/// asks Game once rather than several times per tile. Everything after that is arrays.
/// </para>
/// <para>
/// What each terrain type looks like is held per type id (its sprites, glyph and link group), resolved the first time the id is seen and
/// dropped when its definition is replaced (TerrainRegistry.DefinitionChanged), so no tile costs a string lookup.
/// </para>
/// <para>
/// Joining is a display rule, decided here: a neighbour joins a wall when it is a structure in the same link group (the definition's,
/// TerrainRegistry.GetLinkGroup), or when it is off the map -- a wall running off the loaded map keeps a straight run instead of showing an
/// end cap that would pop when the neighbour loads. A top's mask reads all four neighbours, a face's only east and west. Masks are always
/// worked out from the wall's own neighbours, never from those of the tile its top is drawn on.
/// </para>
/// </remarks>
public sealed class MapTerrainCells
{
    private const int MarginColumns = 2 * SpriteReach.MaximumColumnsBeside + 1;
    private const int MarginRowsAbove = SpriteReach.MaximumRowsAbove + 1;
    private const int MarginRowsBelow = SpriteReach.MaximumRowsAbove + 2;

    private readonly IMapViewQuery _mapView;
    private readonly SpriteManifestResolver _sprites;
    private readonly MapCellBuffer _cells = new();
    private TerrainLook?[] _looksByTypeId = [];

    public MapTerrainCells(IMapViewQuery mapView, SpriteManifestResolver sprites)
    {
        _mapView = mapView;
        _sprites = sprites;
        _mapView.Terrain.DefinitionChanged += (typeId, _, _) => ForgetLook(typeId);
    }

    /// <summary>What every cell of one terrain type looks like.</summary>
    /// <param name="Sprites">Its manifest entry resolved, or null when it has none (or the manifest lacks it): it draws its glyph.</param>
    private sealed record TerrainLook(TerrainSpriteSet? Sprites, string Glyph, Color GlyphColor, int LinkGroup);

    /// <summary>Copies the cells around the grid whose top-left map tile is scroll, on mapLayer.</summary>
    public void Read(Point scroll, int tileColumns, int tileRows, int mapLayer) =>
        _mapView.CopyCells(
            scroll.X - MarginColumns,
            scroll.Y - MarginRowsAbove,
            tileColumns + 2 * MarginColumns,
            tileRows + MarginRowsAbove + MarginRowsBelow,
            mapLayer,
            _cells);

    public bool IsOnMap(int x, int y) => _cells.IsOnMap(x, y);

    /// <summary>Whether a structure stands at (x, y) on the read layer; false off the map.</summary>
    public bool HasStructure(int x, int y) => _cells.StructureTypeIdAt(x, y) != TerrainRegistry.None;

    /// <summary>How the floor at (x, y) draws: its sprite for the cell's seed, else its glyph; false where the layer has no floor or nothing is there.</summary>
    public bool TryGetFloor(int x, int y, out MapVisual floor)
    {
        if (LookOf(_cells.TerrainTypeIdAt(x, y)) is not { } look)
        {
            floor = default;
            return false;
        }

        floor = PlainPicture(look, _cells.TerrainSeedAt(x, y));
        return true;
    }

    /// <summary>How the face of the wall at (x, y) draws: a linked face's piece for its east and west neighbours (its standalone piece when that slot is empty), a plain one's option for the seed, else its glyph.</summary>
    public bool TryGetFace(int x, int y, out MapVisual face)
    {
        if (LookOf(_cells.StructureTypeIdAt(x, y)) is not { } look)
        {
            face = default;
            return false;
        }

        var seed = _cells.StructureSeedAt(x, y);
        if (look.Sprites is { FaceLinking: not SpriteLinking.None } sprites && sprites.TryGetFace(seed, HorizontalMaskAt(x, y, look.LinkGroup), out var joined))
        {
            face = new MapVisual(joined, string.Empty, default);
            return true;
        }

        face = PlainPicture(look, seed);
        return true;
    }

    /// <summary>How the top of the wall at (x, y) draws -- the piece for its four neighbours, its fully joined piece when that slot is empty -- and how many rows above (x, y) it sits; false for a wall whose sprite has no top.</summary>
    public bool TryGetTop(int x, int y, out MapVisual top, out int raisedRows)
    {
        if (LookOf(_cells.StructureTypeIdAt(x, y)) is { Sprites: { HasTop: true } sprites } look &&
            sprites.TryGetTop(_cells.StructureSeedAt(x, y), CardinalMaskAt(x, y, look.LinkGroup), out var piece))
        {
            top = new MapVisual(piece, string.Empty, default);
            raisedRows = sprites.TopRaisedRows;
            return true;
        }

        top = default;
        raisedRows = 0;
        return false;
    }

    /// <summary>The wall at (x, y)'s four-neighbour mask (north 1, east 2, south 4, west 8), for its own link group.</summary>
    public int CardinalMaskAt(int x, int y) => CardinalMaskAt(x, y, LinkGroupAt(x, y));

    /// <summary>The wall at (x, y)'s east/west mask (east 1, west 2), for its own link group.</summary>
    public int HorizontalMaskAt(int x, int y) => HorizontalMaskAt(x, y, LinkGroupAt(x, y));

    private int CardinalMaskAt(int x, int y, int linkGroup) =>
        (Joins(x, y - 1, linkGroup) ? SpriteLinks.North : 0) |
        (Joins(x + 1, y, linkGroup) ? SpriteLinks.East : 0) |
        (Joins(x, y + 1, linkGroup) ? SpriteLinks.South : 0) |
        (Joins(x - 1, y, linkGroup) ? SpriteLinks.West : 0);

    private int HorizontalMaskAt(int x, int y, int linkGroup) =>
        (Joins(x + 1, y, linkGroup) ? SpriteLinks.HorizontalEast : 0) |
        (Joins(x - 1, y, linkGroup) ? SpriteLinks.HorizontalWest : 0);

    private bool Joins(int x, int y, int linkGroup)
    {
        if (!_cells.IsOnMap(x, y))
        {
            return true;
        }

        return LookOf(_cells.StructureTypeIdAt(x, y)) is { } neighbour && neighbour.LinkGroup == linkGroup;
    }

    private int LinkGroupAt(int x, int y) => LookOf(_cells.StructureTypeIdAt(x, y))?.LinkGroup ?? 0;

    /// <summary>A floor's option, or a wall face's fallback piece, for seed -- else the glyph.</summary>
    private static MapVisual PlainPicture(TerrainLook look, uint seed) =>
        look.Sprites is { } sprites && sprites.TryGetFace(seed, mask: null, out var picture)
            ? new MapVisual(picture, string.Empty, default)
            : new MapVisual(null, look.Glyph, look.GlyphColor);

    private TerrainLook? LookOf(ushort typeId)
    {
        if (typeId == TerrainRegistry.None)
        {
            return null;
        }

        if (typeId < _looksByTypeId.Length && _looksByTypeId[typeId] is { } known)
        {
            return known;
        }

        if (!_mapView.Terrain.TryGet(typeId, out var definition))
        {
            return null;
        }

        if (typeId >= _looksByTypeId.Length)
        {
            Array.Resize(ref _looksByTypeId, Math.Max(typeId + 1, _looksByTypeId.Length * 2));
        }

        TerrainSpriteSet? sprites = definition.SpriteName is { } spriteName && _sprites.TryGetTerrainSprites(spriteName, out var found) ? found : null;
        return _looksByTypeId[typeId] = new TerrainLook(sprites, definition.Glyph, definition.GlyphColor, _mapView.Terrain.GetLinkGroup(typeId));
    }

    private void ForgetLook(ushort typeId)
    {
        if (typeId < _looksByTypeId.Length)
        {
            _looksByTypeId[typeId] = null;
        }
    }
}
