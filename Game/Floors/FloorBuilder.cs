using Engine.ECS.Context;
using Engine.Math;
using Game.Blueprints;
using Game.Modules.Core.Components;

namespace Game.Floors;

/// <summary>
/// Builds a single floor's content. Split into two phases because of a real ordering
/// constraint, not style: CreateMap must run before GameBootstrapper.Build (MovementModule's
/// Configure step needs an IMapQuery -- i.e. a World wrapping this Map -- to configure
/// itself), while PopulateFloor needs the EntityManager/ComponentManager that
/// GameBootstrapper.Build is what produces. See TestMapBuilder's own doc comment for the same
/// constraint from the population side.
///
/// floorNumber is accepted but currently unused -- every floor is built identically via
/// TestMapBuilder today. Once real floor generation exists (predetermined maps for floors
/// divisible by 3, procedural otherwise), it branches here without callers changing.
/// </summary>
public static class FloorBuilder
{
    private static readonly Vector3Int TestMapSize = new(1000, 1000, 3);

    public static Game.World.Map CreateMap(int floorNumber) => new(TestMapSize);

    public static void PopulateFloor(Game.World.World world, EcsContext ecsContext, MathUtility mathUtility)
    {
        new TestMapBuilder(ecsContext.EntityManager, ecsContext.ComponentManager, mathUtility).Populate(world);

        world.PlayerEntityId = CreatePlayer(world, ecsContext, mathUtility);
    }

    private static int CreatePlayer(Game.World.World world, EcsContext ecsContext, MathUtility mathUtility)
    {
        var entityId = ecsContext.EntityManager.CreateEntity();
        new PlayerBlueprint(mathUtility).Build(ecsContext.ComponentManager, entityId);

        var spawnPosition = FindFreeGroundCellNearCenter(world);
        ref var transform = ref ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Get(entityId);
        world.PlaceEntityOnMap(entityId, spawnPosition, ref transform);

        return entityId;
    }

    /// <summary>
    /// Scans outward in expanding square rings from the map's Ground-layer center for the
    /// first on-map, unoccupied cell -- deliberately not a hardcoded coordinate, since that
    /// would couple player spawning to TestMapBuilder's own deterministic (and, per its doc
    /// comment, placeholder) wall/population pattern. Falls back to the exact center if
    /// somehow nothing else is found within the map's bounds.
    /// </summary>
    private static Vector3Int FindFreeGroundCellNearCenter(Game.World.World world)
    {
        var mapSize = world.Map.Size;
        var center = new Vector3Int(mapSize.X / 2, mapSize.Y / 2, (int)MapLayer.Ground);

        if (IsFreeGroundCell(world, center))
        {
            return center;
        }

        var maxRadius = Math.Max(mapSize.X, mapSize.Y);
        for (var radius = 1; radius <= maxRadius; radius++)
        {
            for (var deltaX = -radius; deltaX <= radius; deltaX++)
            {
                for (var deltaY = -radius; deltaY <= radius; deltaY++)
                {
                    // Ring only -- interior offsets were already checked at a smaller radius.
                    if (Math.Max(Math.Abs(deltaX), Math.Abs(deltaY)) != radius)
                    {
                        continue;
                    }

                    var candidate = new Vector3Int(center.X + deltaX, center.Y + deltaY, center.Z);
                    if (IsFreeGroundCell(world, candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return center;
    }

    private static bool IsFreeGroundCell(Game.World.World world, Vector3Int position) =>
        world.IsOnMap(position) && world.GetEntityIdAt(position) == -1;
}
