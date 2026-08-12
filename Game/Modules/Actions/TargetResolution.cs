using Engine.Math;
using Game.World;

namespace Game.Modules.Actions;

/// <summary>
/// Yields the blocking occupant (if any) then every non-blocking occupant of one tile --
/// replaces the GetEntityIdAt+GetNonBlockingEntityIdsAt pair every per-tile target loop
/// (ActionEffectResolver, ConsumableActivationSystem) used to duplicate inline. Tiny/Phasing
/// entities never occupy the Blocking slot GetEntityIdAt reports, and any number of them can
/// share a tile, so a caller wanting "everyone standing here" needs both halves.
/// </summary>
public static class TargetResolution
{
    public static IEnumerable<int> EnumerateTargets(Vector3Int tile, IMapQuery mapQuery)
    {
        var blockingEntityId = mapQuery.GetEntityIdAt(tile);
        if (blockingEntityId != -1)
        {
            yield return blockingEntityId;
        }

        foreach (var nonBlockingEntityId in mapQuery.GetNonBlockingEntityIdsAt(tile))
        {
            yield return nonBlockingEntityId;
        }
    }
}
