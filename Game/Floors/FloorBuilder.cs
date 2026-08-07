using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints;
using Game.Modules.AbilityScores;
using Game.Modules.Core.Components;
using Game.Modules.Poison;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

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

    public static void PopulateFloor(Game.World.World world, EcsContext ecsContext, MathUtility mathUtility, UniqueNumberAllocator crawlerNumberAllocator) =>
        new TestMapBuilder(ecsContext.EntityManager, ecsContext.ComponentManager, mathUtility, crawlerNumberAllocator).Populate(world);

    // TEMPORARY test seeding -- exercises Poison until a real in-game source exists. Remove
    // once one does. 10 applications of a 5-tick duration each: since ApplyStack takes the
    // *greater* of the remaining and new duration (not additive), the end result is 10 stacks
    // with a duration of exactly 5 ticks, not 50.
    private const int TestPoisonStackCount = 10;
    private const int TestPoisonDurationTicks = 5;

    // TEMPORARY: exercises the Ability Score window's ordering/formatting (flat before
    // multiplicative, positive before negative) and its right-aligned scrolling list with real
    // modifier data, until real content (equipment, buffs, level-up -- see TODO.md's Stats
    // entry) grants these itself. Remove once one does. One of each shape per Core score --
    // positive/negative additive, positive/negative multiplicative -- with varied magnitude and
    // source so the window shows real variety, not five identical columns.
    private static readonly (AbilityScoreType Type, float PositiveFlat, float NegativeFlat, float PositiveMultiplier, float NegativeMultiplier)[] TestAbilityScoreModifierSeeds =
    [
        (AbilityScoreType.Strength, 3f, -1f, 0.20f, -0.05f),
        (AbilityScoreType.Intelligence, 2f, -2f, 0.10f, -0.15f),
        (AbilityScoreType.Constitution, 5f, -3f, 0.30f, -0.10f),
        (AbilityScoreType.Dexterity, 1f, -4f, 0.15f, -0.20f),
        (AbilityScoreType.Charisma, 4f, -1f, 0.25f, -0.08f),
    ];

    public static int CreatePlayer(Game.World.World world, EcsContext ecsContext, MathUtility mathUtility, FrameEventBuffer<EntityMovedEvent> movedEntities, UniqueNumberAllocator crawlerNumberAllocator)
    {
        var entityId = ecsContext.EntityManager.CreateEntity();
        new PlayerBlueprint(mathUtility, crawlerNumberAllocator).Build(ecsContext.ComponentManager, entityId);

        for (var i = 0; i < TestPoisonStackCount; i++)
        {
            PoisonEffects.ApplyStack(ecsContext.ComponentManager, entityId, StatusEffectSource.Admin, TestPoisonDurationTicks);
        }

        foreach (var seed in TestAbilityScoreModifierSeeds)
        {
            AbilityScoreEffects.GrantModifier(ecsContext.ComponentManager, entityId, seed.Type, StatModifierOperation.Additive, StatModifierPolarity.Buff,
                canModify: true, seed.PositiveFlat, durationFrames: StatModifierComponent.Permanent, StatusEffectSource.Admin);
            AbilityScoreEffects.GrantModifier(ecsContext.ComponentManager, entityId, seed.Type, StatModifierOperation.Additive, StatModifierPolarity.Debuff,
                canModify: true, seed.NegativeFlat, durationFrames: StatModifierComponent.Permanent, StatusEffectSource.AI);
            AbilityScoreEffects.GrantModifier(ecsContext.ComponentManager, entityId, seed.Type, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
                canModify: true, seed.PositiveMultiplier, durationFrames: StatModifierComponent.Permanent, StatusEffectSource.FromEntity(entityId));
            AbilityScoreEffects.GrantModifier(ecsContext.ComponentManager, entityId, seed.Type, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff,
                canModify: true, seed.NegativeMultiplier, durationFrames: StatModifierComponent.Permanent, StatusEffectSource.Admin);
        }

        // TEMPORARY: spawn beside TestMapBuilder's column-16 wall corridor (a fixed column
        // regardless of map size, unlike the map-size-relative exact center below) instead of
        // FindFreeGroundCellNearCenter's usual target, so the sprite migration's Wall sprite
        // (SpriteManifest.Wall) is immediately visible on spawn without scrolling ~480 tiles
        // to the nearest wall. Revert to FindFreeGroundCellNearCenter(world) once that's been
        // visually confirmed in-game.
        var wallAdjacentOrigin = new Vector3Int(17, world.Map.Size.Y / 2, (int)MapLayer.Ground);
        var spawnPosition = FindFreeGroundCellNear(world, wallAdjacentOrigin);
        ref var transform = ref ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Get(entityId);
        world.PlaceEntityOnMap(entityId, spawnPosition, ref transform);

        // Spawning counts as a move (see EntityMovedEvent's own doc comment) so hazard/aura
        // detection (ContactDamageSystem, StatusEffectAuraSystem) sees the player immediately
        // if spawned onto/next to one, rather than only on their first real move. Recorded into
        // the shared buffer those systems actually drain now (see FrameEventBuffer's own doc
        // comment), not published on the bus -- EventBus.Publish is kept alongside it purely so
        // PlayerActivityLog's existing spawn-time log line is preserved unchanged.
        movedEntities.Record(new EntityMovedEvent(entityId, spawnPosition, spawnPosition, transform.Size));
        ecsContext.EventBus.Publish(new EntityMovedEvent(entityId, spawnPosition, spawnPosition, transform.Size));

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
        return FindFreeGroundCellNear(world, new Vector3Int(mapSize.X / 2, mapSize.Y / 2, (int)MapLayer.Ground));
    }

    /// <summary>Same ring-expanding search as FindFreeGroundCellNearCenter, but from an arbitrary origin -- extracted so CreatePlayer's TEMPORARY wall-adjacent override above can reuse the same free-cell-finding robustness without duplicating it.</summary>
    private static Vector3Int FindFreeGroundCellNear(Game.World.World world, Vector3Int origin)
    {
        if (IsFreeGroundCell(world, origin))
        {
            return origin;
        }

        var mapSize = world.Map.Size;
        var maxRadius = Math.Max(mapSize.X, mapSize.Y);
        for (var radius = 1; radius <= maxRadius; radius++)
        {
            for (var deltaX = -radius; deltaX <= radius; deltaX++)
            {
                for (var deltaY = -radius; deltaY <= radius; deltaY++)
                {
                    var candidate = new Vector3Int(origin.X + deltaX, origin.Y + deltaY, origin.Z);

                    // Ring only -- interior offsets were already checked at a smaller radius.
                    if (DistanceFalloff.ChebyshevDistance(origin, candidate) != radius)
                    {
                        continue;
                    }

                    if (IsFreeGroundCell(world, candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return origin;
    }

    private static bool IsFreeGroundCell(Game.World.World world, Vector3Int position) =>
        world.IsOnMap(position) && world.GetEntityIdAt(position) == -1;
}
