using Engine.Math;
using Game.Modules.Core.Components;
using SpriteFormat;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.Rendering;

namespace Presentation.MapRendering;

/// <summary>What a MapDrawItem draws.</summary>
public enum MapDrawItemKind : byte
{
    /// <summary>A corpse lying on the floor, at its full footprint; drawn in its top row, under whatever stands on its lower tiles.</summary>
    Corpse,

    /// <summary>A living non-blocking occupant that is neither Tiny nor Phasing (an item pile), lying on the floor at its full footprint like a corpse, without the corpse marker.</summary>
    FloorObject,

    /// <summary>One of a tile's Tiny occupants, drawn into the tile's sub-grid in the order they come.</summary>
    Tiny,

    /// <summary>The tile's Blocking occupant, drawn from the bottom-left tile of its footprint.</summary>
    Blocking,

    /// <summary>A Phasing occupant, drawn translucently over the Blocking one, from the bottom-left tile of its footprint.</summary>
    Phasing,

    /// <summary>A tile's up/down arrows for occupied layers above and below the one shown; always after every occupant.</summary>
    LayerBadge,

    /// <summary>A wall's face or top that covers a tile something can stand on, drawn at the start of its wall's row, ahead of that row's occupants.</summary>
    Overhang,
}

/// <summary>One thing the map draws, in the order it must be drawn.</summary>
/// <param name="MapX">The footprint's top-left tile for an entity; the tile itself for a Tiny occupant or a layer badge; the tile it is drawn on for an overhang.</param>
/// <param name="EntityId">-1 for a layer badge; the overhang's index in the overhang list for an overhang.</param>
/// <param name="Size">The footprint in tiles; 1x1 for a Tiny occupant, a layer badge or an overhang.</param>
public readonly record struct MapDrawItem(MapDrawItemKind Kind, int EntityId, int MapX, int MapY, Vector2Byte Size, bool HasHigherLayer = false, bool HasLowerLayer = false);

/// <summary>A wall's face or top drawn live, in the row walk, because it covers a tile something can stand on.</summary>
/// <param name="WallX">The wall's own tile, whose row the piece is drawn in.</param>
/// <param name="AnchorX">The tile the piece sits on: the wall's own for a face, RaisedRows above it for a top.</param>
public readonly record struct MapOverhang(int WallX, int WallY, int AnchorX, int AnchorY, MapVisual Visual, bool IsTop)
{
    /// <summary>Row by row, then faces before tops (a top sits on its face), then left to right -- the order the walk draws them in.</summary>
    public static int CompareDrawOrder(MapOverhang first, MapOverhang second)
    {
        var byRow = first.WallY.CompareTo(second.WallY);
        if (byRow != 0)
        {
            return byRow;
        }

        var byPart = first.IsTop.CompareTo(second.IsTop);
        return byPart != 0 ? byPart : first.WallX.CompareTo(second.WallX);
    }
}

/// <summary>
/// Decides what the map's occupant pass draws and in what order, so MapRenderer only renders it and the order can be
/// tested without a window.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rows top to bottom, each row left to right.</b> Anything that stands up is drawn in the row of the lowest tile of
/// its footprint, and its sprite may reach up (and sideways) past it, so a later row overlaps an earlier one and never
/// the reverse: a goblin south of the player draws over the player's feet, and the player's head over the row above.
/// Blocking and Phasing occupants are anchored at the bottom-left tile of their footprint; a corpse lies on the floor
/// and keeps its top-left anchor, so it is drawn ahead of anything standing on its lower tiles. The Tiny sub-grid is
/// single-tile.
/// </para>
/// <para>
/// <b>Overscan.</b> The walk covers SpriteReach.MaximumRowsAbove rows below the grid and
/// SpriteReach.MaximumColumnsBeside columns either side of it, the farthest anything may draw from its anchor tile
/// (the reach box, which a Huge 3x3 footprint fills), so an entity anchored just outside the grid still draws the part
/// that reaches into view. Tiny occupants and layer badges, which stay inside their tile, are only planned for the
/// grid itself.
/// </para>
/// <para>
/// <b>Walls' overhangs first in their row.</b> A wall's face or top that covers a tile something can stand on (see
/// MapRenderer's structure pass) is drawn at the start of the wall's own row: over everything standing behind it in the
/// rows above, under anything standing beside it in its own row.
/// </para>
/// <para>
/// <b>Layer badges last.</b> They describe a tile, and a later row's tall sprite would paint over them, so they are
/// planned after every occupant, from a second scan of the grid over the other layers.
/// </para>
/// <para>
/// <b>Only occupied tiles are visited.</b> The walk reads the map's occupied cells (IMapViewQuery.GetOccupiedCellsIn),
/// whose order is the walk's own, so an empty or off-map tile costs nothing but its bit in a column mask.
/// </para>
/// </remarks>
public sealed class MapDrawOrder
{
    private readonly List<MapDrawItem> _items = [];
    private readonly List<MapDrawItem> _phasingOccupants = [];

    /// <summary>The last Plan's items, in draw order. Reused by the next Plan.</summary>
    public IReadOnlyList<MapDrawItem> Items => _items;

    /// <param name="scroll">The map tile at the grid's top-left.</param>
    /// <param name="layerCount">How many map layers there are, for which layers count as above and below mapLayer.</param>
    /// <param name="overhangs">The walls' live faces and tops, sorted by MapOverhang.CompareDrawOrder; each row's are planned ahead of its occupants.</param>
    public void Plan(IMapViewQuery map, Point scroll, int tileColumns, int tileRows, int mapLayer, int layerCount, IReadOnlyList<MapOverhang> overhangs)
    {
        _items.Clear();

        var allLayersMask = (1 << layerCount) - 1;
        var higherLayerMask = allLayersMask & ~((1 << (mapLayer + 1)) - 1);
        var lowerLayerMask = (1 << mapLayer) - 1;
        var nextOverhang = 0;
        var gridMaxXExclusive = scroll.X + tileColumns;
        var occupiedCells = map.GetOccupiedCellsIn(
            scroll.X - SpriteReach.MaximumColumnsBeside,
            scroll.Y,
            gridMaxXExclusive + SpriteReach.MaximumColumnsBeside,
            scroll.Y + tileRows + SpriteReach.MaximumRowsAbove,
            mapLayer);
        var hasOccupiedCell = occupiedCells.MoveNext();

        for (var rowIndex = 0; rowIndex < tileRows + SpriteReach.MaximumRowsAbove; rowIndex++)
        {
            var rowY = scroll.Y + rowIndex;
            while (nextOverhang < overhangs.Count && overhangs[nextOverhang].WallY <= rowY)
            {
                var overhang = overhangs[nextOverhang];
                if (overhang.WallY == rowY)
                {
                    _items.Add(new MapDrawItem(MapDrawItemKind.Overhang, nextOverhang, overhang.AnchorX, overhang.AnchorY, new Vector2Byte(1, 1)));
                }

                nextOverhang++;
            }

            var isInGridRow = rowIndex < tileRows;
            while (hasOccupiedCell && occupiedCells.Current.Position.Y == rowY)
            {
                var cell = occupiedCells.Current;
                var mapX = cell.Position.X;
                PlanTile(map, cell.Position, cell.Occupants, isInGrid: isInGridRow && mapX >= scroll.X && mapX < gridMaxXExclusive);
                hasOccupiedCell = occupiedCells.MoveNext();
            }
        }

        foreach (var column in map.GetOccupiedColumnsIn(scroll.X, scroll.Y, gridMaxXExclusive, scroll.Y + tileRows, (byte)(higherLayerMask | lowerLayerMask)))
        {
            _items.Add(new MapDrawItem(MapDrawItemKind.LayerBadge, -1, column.X, column.Y, new Vector2Byte(1, 1), (column.LayerMask & higherLayerMask) != 0, (column.LayerMask & lowerLayerMask) != 0));
        }
    }

    /// <summary>The tile's floor-level occupants (corpses, Tiny), then its Blocking occupant, then its Phasing ones -- each only from its anchor tile.</summary>
    private void PlanTile(IMapViewQuery map, Vector3Int tile, ReadOnlySpan<int> occupants, bool isInGrid)
    {
        var mapX = tile.X;
        var mapY = tile.Y;
        var blockingEntityId = map.GetBlockingEntityId(tile);
        _phasingOccupants.Clear();

        foreach (var entityId in occupants)
        {
            if (entityId == blockingEntityId || !map.TryGetOccupant(entityId, out var occupant))
            {
                continue;
            }

            if ((occupant.Kind & NonBlockingKind.Phasing) != 0)
            {
                if (IsBottomLeftAnchor(occupant, mapX, mapY))
                {
                    _phasingOccupants.Add(new MapDrawItem(MapDrawItemKind.Phasing, entityId, occupant.Position.X, occupant.Position.Y, occupant.Size));
                }

                continue;
            }

            if ((occupant.Kind & NonBlockingKind.Tiny) != 0)
            {
                if (isInGrid)
                {
                    _items.Add(new MapDrawItem(MapDrawItemKind.Tiny, entityId, mapX, mapY, new Vector2Byte(1, 1)));
                }

                continue;
            }

            if (!occupant.IsDead && occupant.Position.X == mapX && occupant.Position.Y == mapY)
            {
                _items.Add(new MapDrawItem(MapDrawItemKind.FloorObject, entityId, occupant.Position.X, occupant.Position.Y, occupant.Size));
                continue;
            }

            if (occupant.IsDead && occupant.Position.X == mapX && occupant.Position.Y == mapY)
            {
                _items.Add(new MapDrawItem(MapDrawItemKind.Corpse, entityId, occupant.Position.X, occupant.Position.Y, occupant.Size));
            }
        }

        if (blockingEntityId != -1 && map.TryGetOccupant(blockingEntityId, out var blocking) && IsBottomLeftAnchor(blocking, mapX, mapY))
        {
            _items.Add(new MapDrawItem(MapDrawItemKind.Blocking, blockingEntityId, blocking.Position.X, blocking.Position.Y, blocking.Size));
        }

        _items.AddRange(_phasingOccupants);
    }

    private static bool IsBottomLeftAnchor(OccupantView occupant, int mapX, int mapY) =>
        occupant.Position.X == mapX && occupant.Position.Y + occupant.Size.Y - 1 == mapY;
}
