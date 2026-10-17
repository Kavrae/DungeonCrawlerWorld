using Microsoft.Xna.Framework;
using SpriteFormat;

namespace Presentation.MapRendering;

/// <summary>
/// Decides, for every wall around the visible grid, which faces and tops go into the cached terrain image and which are
/// drawn live by the row walk -- so MapRenderer only draws the result and the decision can be tested without a window.
/// </summary>
/// <remarks>
/// <para>
/// A face stands on its wall's own tile. Under a top it shows only where the tile south of it isn't a wall -- so a thick
/// wall shows tops with faces along its exposed south edge -- while a wall with no top (glyph-only, or a mod's) draws its
/// face on every tile, as walls always did. A face may be up to two rows tall by its placement. A top sits on every wall,
/// TopRaisedRows above it (directly above its face's highest row).
/// </para>
/// <para>
/// A piece whose picture touches only walls and off-map tiles can't hide anything standing, so it is cached: faces in
/// CachedPieces first, then tops, so a top always lands on the faces under it whatever order the walk found them in. A
/// piece over a tile something can stand on must occlude whoever stands there, so it is an overhang, drawn live in its
/// wall's row (MapDrawOrder). A one-row face covers nothing but its own wall and is always cached.
/// </para>
/// <para>
/// Walks the same overscan as the row walk -- SpriteReach.MaximumRowsAbove rows below the grid and
/// SpriteReach.MaximumColumnsBeside columns either side -- since a wall just outside the grid can reach into it. Off the
/// map counts as a wall: nothing stands there, and a wall running off the loaded map shows no face at its edge that would
/// pop when the neighbour loads.
/// </para>
/// </remarks>
public sealed class MapWallPlan
{
    private readonly List<MapOverhang> _cachedPieces = [];
    private readonly List<MapOverhang> _cachedTops = [];
    private readonly List<MapOverhang> _overhangs = [];

    /// <summary>The last Plan's cached faces, then its cached tops -- the order to draw them into the cached image.</summary>
    public IReadOnlyList<MapOverhang> CachedPieces => _cachedPieces;

    /// <summary>The last Plan's live pieces, sorted by MapOverhang.CompareDrawOrder for the row walk.</summary>
    public IReadOnlyList<MapOverhang> Overhangs => _overhangs;

    /// <param name="cells">The terrain around the grid, read (MapTerrainCells.Read) for the same scroll and size.</param>
    /// <param name="scroll">The map tile at the grid's top-left.</param>
    public void Plan(MapTerrainCells cells, Point scroll, int tileColumns, int tileRows)
    {
        _cachedPieces.Clear();
        _cachedTops.Clear();
        _overhangs.Clear();

        for (var rowIndex = 0; rowIndex < tileRows + SpriteReach.MaximumRowsAbove; rowIndex++)
        {
            for (var columnIndex = -SpriteReach.MaximumColumnsBeside; columnIndex < tileColumns + SpriteReach.MaximumColumnsBeside; columnIndex++)
            {
                var mapX = scroll.X + columnIndex;
                var mapY = scroll.Y + rowIndex;
                if (!cells.HasStructure(mapX, mapY))
                {
                    continue;
                }

                var hasTop = cells.TryGetTop(mapX, mapY, out var top, out var raisedRows);
                if ((!hasTop || !IsWallOrOffMap(cells, mapX, mapY + 1)) && cells.TryGetFace(mapX, mapY, out var face))
                {
                    Place(cells, new MapOverhang(mapX, mapY, mapX, mapY, face, IsTop: false));
                }

                if (hasTop)
                {
                    Place(cells, new MapOverhang(mapX, mapY, mapX, mapY - raisedRows, top, IsTop: true));
                }
            }
        }

        _cachedPieces.AddRange(_cachedTops);
        _overhangs.Sort(MapOverhang.CompareDrawOrder);
    }

    /// <summary>Which tiles piece's drawn picture touches, relative to the tile it sits on.</summary>
    public static SpriteTileArea AreaOf(MapOverhang piece) =>
        piece.Visual.Sprite is { } sprite
            ? SpritePlacementTiles.DrawnTiles(1, 1, sprite.SourceRectangle.Width, sprite.SourceRectangle.Height, sprite.Placement)
            : new SpriteTileArea(0, 0, 1, 1);

    private void Place(MapTerrainCells cells, MapOverhang piece)
    {
        if (!CoversOnlyWalls(cells, piece))
        {
            _overhangs.Add(piece);
        }
        else if (piece.IsTop)
        {
            _cachedTops.Add(piece);
        }
        else
        {
            _cachedPieces.Add(piece);
        }
    }

    private static bool CoversOnlyWalls(MapTerrainCells cells, MapOverhang piece)
    {
        var area = AreaOf(piece);
        for (var row = (int)MathF.Floor(area.Top); row < (int)MathF.Ceiling(area.Bottom); row++)
        {
            for (var column = (int)MathF.Floor(area.Left); column < (int)MathF.Ceiling(area.Right); column++)
            {
                if (!IsWallOrOffMap(cells, piece.AnchorX + column, piece.AnchorY + row))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsWallOrOffMap(MapTerrainCells cells, int mapX, int mapY) =>
        !cells.IsOnMap(mapX, mapY) || cells.HasStructure(mapX, mapY);
}
