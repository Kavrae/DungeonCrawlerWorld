using Engine.ECS.Context;
using Engine.Math;

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

    public static void PopulateFloor(Game.World.World world, EcsContext ecsContext, MathUtility mathUtility) =>
        new TestMapBuilder(ecsContext.EntityManager, ecsContext.ComponentManager, mathUtility).Populate(world);
}