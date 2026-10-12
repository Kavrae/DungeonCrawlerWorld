using Engine.Math;
using Game.Terrain;
using Game.World;

namespace Game.Views;

/// <summary>Everything the map window reads about the world, in presentation-shaped values rather than component pools.</summary>
/// <remarks>
/// PLAN-presentation-data-layer.md, option D (a pull query, no cache) and Stage 2 (the map draw path
/// first). Every answer is read live on each call, so there is nothing to invalidate and nothing to
/// go stale, and input resolved through it sees the same state gameplay does.
///
/// Terrain and structures are cells, not entities: the map reads them a rectangle at a time (CopyCells)
/// as terrain type ids and seeds against Terrain, the inspector one cell at a time as TerrainViews --
/// never as an id anything could look components up by. Nothing here says which picture anything
/// draws as: sprites are named, with the seed their variant is picked from, and Presentation resolves them.
/// </remarks>
public interface IMapViewQuery
{
    MapBounds Bounds { get; }

    int PlayerEntityId { get; }

    bool IsOnMap(Vector3Int position);

    /// <summary>The tile's Blocking occupant, or -1.</summary>
    int GetBlockingEntityId(Vector3Int position);

    /// <summary>Every occupant of the tile, Blocking included. Valid until the tile's occupants next change.</summary>
    ReadOnlySpan<int> GetOccupants(Vector3Int position);

    /// <summary>Bit N set when MapLayer N holds an occupant at (x, y).</summary>
    byte GetOccupiedLayerMask(int x, int y);

    /// <summary>The terrain under MapLayer layer at (x, y) -- name, description and look -- if that layer has a floor and anything is there.</summary>
    bool TryGetTerrain(int x, int y, int layer, out TerrainView terrain);

    /// <summary>The structure standing on MapLayer layer at (x, y) -- name, description and look -- if there is one.</summary>
    bool TryGetStructure(int x, int y, int layer, out TerrainView structure);

    /// <summary>The terrain definitions every type id CopyCells hands out refers to.</summary>
    TerrainRegistry Terrain { get; }

    /// <summary>Copies a rectangle of mapLayer into cells: per cell its terrain and structure type ids, their seeds, and whether it is on the map.</summary>
    void CopyCells(int minX, int minY, int width, int height, int mapLayer, MapCellBuffer cells);

    bool TryGetVisual(int entityId, out EntityVisualView visual);

    bool TryGetOccupant(int entityId, out OccupantView occupant);

    EntityStatusView GetStatus(int entityId);

    /// <summary>The action entityId is currently winding up, if any.</summary>
    bool TryGetChargingAction(int entityId, out ChargingActionView action);

    /// <summary>How far entityId is through its current windup, 0 at the start to 1 on the frame it resolves; 0 when it isn't winding up.</summary>
    float GetChargeFraction(int entityId);

    EntityInteractionView GetInteraction(int entityId);
}
