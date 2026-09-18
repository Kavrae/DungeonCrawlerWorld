using Engine.Math;
using Microsoft.Xna.Framework;

namespace Game.Views;

/// <summary>Everything the map window reads about the world, in presentation-shaped values rather than component pools.</summary>
/// <remarks>
/// PLAN-presentation-data-layer.md, option D (a pull query, no cache) and Stage 2 (the map draw path
/// first). Every answer is read live on each call, so there is nothing to invalidate and nothing to
/// go stale, and input resolved through it sees the same state gameplay does.
///
/// Terrain and structures are cells, not entities, so they are
/// exposed only as TerrainViews and a background colour -- never as an id anything could look
/// components up by.
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

    /// <summary>How the terrain under MapLayer layer at (x, y) looks, if that layer has a floor and anything is there.</summary>
    bool TryGetTerrainVisual(int x, int y, int layer, out EntityVisualView visual);

    /// <summary>The terrain under MapLayer layer at (x, y) -- name, description and look -- if that layer has a floor and anything is there.</summary>
    bool TryGetTerrain(int x, int y, int layer, out TerrainView terrain);

    /// <summary>How the structure standing on MapLayer layer at (x, y) looks, if there is one.</summary>
    bool TryGetStructureVisual(int x, int y, int layer, out EntityVisualView visual);

    /// <summary>The structure standing on MapLayer layer at (x, y) -- name, description and look -- if there is one.</summary>
    bool TryGetStructure(int x, int y, int layer, out TerrainView structure);

    /// <summary>The background wash for (x, y) on layer: the Blocking occupant's own background if it has one, else the structure's, else the terrain's, else white; black off the map.</summary>
    Color GetBackgroundColor(int x, int y, int layer);

    bool TryGetVisual(int entityId, out EntityVisualView visual);

    bool TryGetOccupant(int entityId, out OccupantView occupant);

    EntityStatusView GetStatus(int entityId);

    /// <summary>The action entityId is currently winding up, if any.</summary>
    bool TryGetChargingAction(int entityId, out ChargingActionView action);

    /// <summary>The length of entityId's current action lock, or 0 when it has none.</summary>
    int GetActionLockTotalFrames(int entityId);

    EntityInteractionView GetInteraction(int entityId);
}
