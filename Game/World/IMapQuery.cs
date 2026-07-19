using Engine.Math;

namespace Game.World;

/// <summary>
/// Narrow, position-only read contract for whatever movement-style decision-making needs to
/// know about the map, without depending on the full World/Map/MapNode object graph. World
/// implements this directly. Splitting it out (decision #11's follow-up in the plan) is what
/// lets a modded replacement for MovementSystem satisfy just these three members instead of
/// understanding World's internals, and is what let World stop being a MovementModule
/// constructor dependency at all -- MovementModule now only needs an IMapQuery, supplied via
/// IGameModule.Configure.
/// </summary>
public interface IMapQuery
{
    Vector3Int MapSize { get; }

    bool IsOnMap(Vector3Int position);

    /// <summary>The entity occupying position, or -1 if empty.</summary>
    int GetEntityIdAt(Vector3Int position);
}
