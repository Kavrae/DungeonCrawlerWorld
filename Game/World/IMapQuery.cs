using Engine.Math;

namespace Game.World;

/// <summary>
/// Narrow, position-only read contract for whatever movement-style decision-making needs to
/// know about the map, without depending on the full World/Map object graph. World
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

    /// <summary>
    /// Whether every cell of an X/Y footprint at position is on the map (Z fixed -- a
    /// footprint never spans more than one MapLayer). Checked via just the footprint's
    /// top-left and (for anything larger than 1x1) bottom-right corners, sufficient since the
    /// map is a plain rectangular volume: no cell in between can be off-map if both corners
    /// are on it. Default-implemented purely in terms of the single-point overload above, so
    /// no implementer (World, or a test's fake) has to do anything to pick it up.
    /// </summary>
    bool IsOnMap(Vector3Int position, Vector2Byte size)
    {
        if (!IsOnMap(position))
        {
            return false;
        }

        if (size.X == 1 && size.Y == 1)
        {
            return true;
        }

        return IsOnMap(new Vector3Int(position.X + size.X - 1, position.Y + size.Y - 1, position.Z));
    }

    /// <summary>The entity occupying position, or -1 if empty.</summary>
    int GetEntityIdAt(Vector3Int position);

    /// <summary>
    /// Whether entityId currently participates in exclusive map occupancy -- not a default
    /// method like the overload above, since it needs pool state each implementer holds
    /// differently (World derives it from NonBlockingComponent/ForceBlockingComponent).
    /// </summary>
    bool IsBlocking(int entityId);
}
