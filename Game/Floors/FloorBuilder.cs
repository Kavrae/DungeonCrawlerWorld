using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints;
using Game.Blueprints.Composites;
using Game.Blueprints.NPCs.Generic;
using Game.Modules.AbilityScores;
using Game.Modules.Core.Components;
using Game.Modules.Poison;
using Game.Modules.ProcessingTier;
using Game.Modules.StatModifiers;
using Game.Spawning;
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
    /// <summary>Neighborhoods per side of the default map: the fixed 3x3.</summary>
    private const int DefaultNeighborhoodsPerSide = 3;

    private const int LayerCount = 3;

    /// <summary>The map a floor starts on, centred on neighborhood (0, 0), across every MapLayer.</summary>
    /// <remarks>
    /// The default is the unbounded sliding window: the 3x3 of
    /// neighborhoods around (0, 0) loaded, and more loaded and dropped as the player walks. Any other
    /// size is a fixed, bounded square, for scaling measurements, still centred so the neighborhoods west
    /// and north of the spawn have negative coordinates.
    /// </remarks>
    /// <param name="squareSizeOverride">Width and height in tiles for a fixed map instead of the default window -- the "--map-size=" argument. Null, or the default window's own 3072, for the window.</param>
    public static Game.World.Map CreateMap(int floorNumber, int? squareSizeOverride = null)
    {
        if (squareSizeOverride is null or DefaultNeighborhoodsPerSide * Game.World.Neighborhoods.SizeTiles)
        {
            var window = Game.World.Map.Unbounded(LayerCount);
            for (var cellY = -1; cellY <= 1; cellY++)
            {
                for (var cellX = -1; cellX <= 1; cellX++)
                {
                    window.LoadNeighborhood(cellX, cellY);
                }
            }

            window.CenterLookupOn(0, 0);
            return window;
        }

        var tiles = squareSizeOverride.Value;
        var neighborhoodsPerSide = (tiles + Game.World.Neighborhoods.SizeTiles - 1) >> Game.World.Neighborhoods.SizeShift;
        var min = -Game.World.Neighborhoods.OriginOf(neighborhoodsPerSide / 2);
        return new Game.World.Map(new MapBounds(min, min, min + tiles, min + tiles, LayerCount));
    }

    /// <summary>Fills the floor's loaded neighborhoods: terrain, walls and everything spawned on them.</summary>
    /// <remarks>The tier resolver the factory was built with must already have its reference position set to <see cref="PlayerSpawnOrigin"/>, so every entity is born with its processing tier as its first component instead of being tiered and then migrated.</remarks>
    /// <param name="terrain">The session's terrain definitions -- population writes cells of them.</param>
    /// <param name="definitions">The session's blueprints -- population spawns them.</param>
    /// <param name="factory">The one spawn path (see EntityFactory), which carries the tier resolver, the skeletons, the move buffer and the crawler numbers population used to take one by one.</param>
    /// <param name="records">The session's neighborhood records: each neighborhood the map covers is assigned one if it has none, and generated from it.</param>
    public static void PopulateFloor(Game.World.World world, EcsContext ecsContext, NeighborhoodRecords records, EntityFactory factory, Terrain.TerrainRegistry terrain, Blueprints.BlueprintRegistry definitions) =>
        new TestMapBuilder(ecsContext.EntityManager, factory, terrain, definitions).Populate(world, records);

    /// <summary>
    /// Where the player is aimed at spawning -- the actual cell is the nearest free Ground cell to
    /// this (see CreatePlayer). Exposed separately so the spawn sequence can set the tier reference
    /// position to it <b>before</b> population, and terrain and NPCs are born correctly tiered
    /// rather than fixed up afterwards. If the player lands a few cells away, ProcessingTierSystem's
    /// first update treats that as an ordinary player move from here to there and walks the Local
    /// boundary, so the small difference reconciles itself.
    /// </summary>
    /// <remarks>
    /// In TestMapBuilder's starting neighborhood, inside the gap its hallway cross leaves in its wall
    /// corridors.
    /// TEMPORARY: in the corridor gap rather than at the neighborhood's centre, so the Wall sprite is
    /// visible on spawn and the walk west to the neighborhood border (17 tiles) is unobstructed, which
    /// is what makes a tier crossing testable by hand. Revert once neither is needed.
    /// </remarks>
    public static Vector3Int PlayerSpawnOrigin() =>
        new(Game.World.Neighborhoods.OriginOf(TestMapBuilder.StartingCellX) + TestMapBuilder.SpawnColumn, Game.World.Neighborhoods.OriginOf(TestMapBuilder.StartingCellY) + TestMapBuilder.SpawnRow, (int)MapLayer.Ground);

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

    /// <summary>Mints the Player's entity id.</summary>
    /// <remarks>
    /// Called before PopulateFloor, not inside CreatePlayer -- reserving it first, before any of
    /// PopulateFloor's NPC and wall entities exist, deterministically lands the Player on
    /// entity id 0 (see FreeIdPool.Rent: the first Rent() call against a fresh pool always
    /// returns 0) instead of a high id assigned after population. That's what lets the
    /// Player-only component pool capacity overrides (AchievementUnlockedComponent,
    /// ActionHotkeyBindingComponent, ItemHotkeyBindingComponent, HotkeyExpansionUnlockComponent --
    /// see TODO.md's "Per-pool entity capacity for rare component types") actually stay near
    /// their small seed size instead of growing to cover a population-scale id the moment the Player is
    /// created. The reserved id carries no components until CreatePlayer runs -- safe, since
    /// every component pool gates access on its own presence-tracking, never a raw id-range scan.
    /// </remarks>
    public static int ReservePlayerEntity(EcsContext ecsContext) => ecsContext.EntityManager.CreateEntity();

    /// <summary>
    /// Mints the two trade-offer entities' ids. Reserved once, right alongside the player (before PopulateFloor, for the same
    /// low-id reasoning ReservePlayerEntity's own doc comment gives), and reused for the life of
    /// the game -- a trade never destroys/recreates these, it only ever moves stacks/currency into
    /// and back out of them. Neither entity is ever given a TransformComponent or placed on the
    /// map; both stay bare until InventoryActions/CurrencyActions lazily provision an inventory/
    /// currency component on first write, same as any other entity.
    /// </summary>
    public static ReservedEntityIds ReserveTradeOfferEntities(EcsContext ecsContext) =>
        new(ecsContext.EntityManager.CreateEntity(), ecsContext.EntityManager.CreateEntity());

    /// <summary>Builds and places the Player entity, using an id already reserved via ReservePlayerEntity.</summary>
    /// <remarks>
    /// Still runs after PopulateFloor, unlike the id reservation above -- the free-cell search
    /// below (FindFreeGroundCellNear) reads live map occupancy, so it genuinely needs the floor
    /// already populated, not just the id already minted.
    /// </remarks>
    /// <param name="tierResolver">When supplied, the player is pinned Local before its blueprint is built, so every tiered pool it joins sees Local from the start and the player is never recomputed afterwards -- including while off the map. Optional for the same reason as PopulateFloor's: ProcessingTierSystem pins the player on its first update if nothing did here.</param>
    /// <param name="factory">The one spawn path (see EntityFactory) -- the player is a blueprint and a seed like every other entity, so it too carries a spawn record.</param>
    public static void CreatePlayer(Game.World.World world, EcsContext ecsContext, MathUtility mathUtility, EntityFactory factory, BlueprintRegistry definitions, int entityId, ProcessingTierResolver? tierResolver = null)
    {
        tierResolver?.PinLocalAndNotify(entityId);

        var spawnPosition = FindFreeGroundCellNear(world, PlayerSpawnOrigin());
        factory.Spawn(SpawnRequest.At(definitions.GetId(Player.Id), spawnPosition) with { Seed = mathUtility.NextSeed(), Crawler = true, ReservedEntityId = entityId });

        for (var i = 0; i < TestPoisonStackCount; i++)
        {
            PoisonEffects.ApplyStack(ecsContext.ComponentManager, ecsContext.EntityManager.Keys, entityId, ActionSource.Admin, TestPoisonDurationTicks, ecsContext.SystemManager.Clock.CurrentFrame, ecsContext.EventBus, world);
        }

        foreach (var seed in TestAbilityScoreModifierSeeds)
        {
            AbilityScoreEffects.GrantModifier(ecsContext.ComponentManager, entityId, seed.Type, StatModifierOperation.Additive, StatModifierPolarity.Buff,
                canModify: true, seed.PositiveFlat, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin);
            AbilityScoreEffects.GrantModifier(ecsContext.ComponentManager, entityId, seed.Type, StatModifierOperation.Additive, StatModifierPolarity.Debuff,
                canModify: true, seed.NegativeFlat, expiresAtFrame: FrameDeadline.Never, ActionSource.AI);
            AbilityScoreEffects.GrantModifier(ecsContext.ComponentManager, entityId, seed.Type, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
                canModify: true, seed.PositiveMultiplier, expiresAtFrame: FrameDeadline.Never, ActionSource.FromEntity(ecsContext.ComponentManager, ecsContext.EntityManager.Keys, entityId, definitions));
            AbilityScoreEffects.GrantModifier(ecsContext.ComponentManager, entityId, seed.Type, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff,
                canModify: true, seed.NegativeMultiplier, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin);
        }

        // The spawn move itself is EntityFactory's (see Spawn), so hazard/aura detection
        // (ContactDamageSystem, StatusEffectAuraSystem) sees the player immediately if spawned
        // onto/next to one, rather than only on their first real move. Published on the bus here as
        // well, purely so PlayerActivityLog's existing spawn-time log line is preserved unchanged.
        var size = ecsContext.ComponentManager.GetDirectPool<TransformComponent>().GetReadonly(entityId).Size;
        ecsContext.EventBus.Publish(new EntityMovedEvent(entityId, spawnPosition, spawnPosition, size));

        SpawnTestDummy(world, factory, mathUtility, definitions, spawnPosition);
    }

    /// <summary>TEMPORARY test seeding, alongside the Poison/ability-score seeding above -- a dedicated Dodge-practice target a few tiles from the player's own spawn. See TestDummyBlueprint/TestDummyAttackSystem.</summary>
    private const int TestDummySpawnOffsetColumns = 3;

    private static void SpawnTestDummy(Game.World.World world, EntityFactory factory, MathUtility mathUtility, BlueprintRegistry definitions, Vector3Int playerSpawnPosition)
    {
        var origin = new Vector3Int(playerSpawnPosition.X + TestDummySpawnOffsetColumns, playerSpawnPosition.Y, playerSpawnPosition.Z);
        var blueprintId = definitions.GetId(TestDummyBlueprint.Id);

        factory.Spawn(SpawnRequest.At(blueprintId, FindFreeGroundCellNear(world, origin)) with { Seed = mathUtility.NextSeed() });
    }

    /// <summary>Scans outward from origin in expanding square rings for the first on-map, unoccupied, unblocked Ground cell, falling back to origin itself if the whole map is full.</summary>
    public static Vector3Int FindFreeGroundCellNear(Game.World.World world, Vector3Int origin)
    {
        if (IsFreeGroundCell(world, origin))
        {
            return origin;
        }

        var bounds = world.Map.Bounds;
        var maxRadius = Math.Max(bounds.Width, bounds.Height);
        for (var radius = 1; radius <= maxRadius; radius++)
        {
            for (var deltaX = -radius; deltaX <= radius; deltaX++)
            {
                for (var deltaY = -radius; deltaY <= radius; deltaY++)
                {
                    var candidate = new Vector3Int(origin.X + deltaX, origin.Y + deltaY, origin.Z);

                    // Ring only -- interior offsets were already checked at a smaller radius.
                    if (GridDistance.ChebyshevDistance(origin, candidate) != radius)
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
        world.IsOnMap(position) && world.GetEntityIdAt(position) == -1 && !world.IsCellBlocked(position);
}
