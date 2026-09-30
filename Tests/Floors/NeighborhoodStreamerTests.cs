using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Math;
using Game;
using Game.Spawning;
using Game.Bootstrap;
using Game.Floors;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Terrain;
using Game.World;
using Game.Blueprints;

namespace Tests.Floors;

/// <summary>Unloading and regenerating a neighborhood end to end, through the real bootstrapper's destroy wiring, on a map two neighborhoods wide and a few rows tall.</summary>
[TestClass]
public sealed class NeighborhoodStreamerTests
{
    private const int Rows = 24;

    private static readonly Vector3Int Reference = new(10, 5, (int)MapLayer.Ground);

    private sealed record Session(Game.World.World World, EcsContext Ecs, ProcessingTierResolver Resolver, NeighborhoodStreamer Streamer, FrameEventBuffer<EntityMovedEvent> MovedEntities, BlueprintRegistry Definitions, CreatureSkeletons Skeletons);

    private static Session Build(int budgetPerFrame = NeighborhoodStreamer.DefaultBudgetPerFrame)
    {
        var map = new Map(new MapBounds(0, 0, 2 * Neighborhoods.SizeTiles, Rows, 3));
        var mathUtility = new MathUtility(new Random(1));
        var crawlerNumbers = new UniqueNumberAllocator(1, 1, 24);
        var result = GameBootstrapper.Build(ValidatedMods.None, map, mathUtility, initialEntityCapacity: 1_000, initialComponentCapacity: 100, crawlerNumbers: crawlerNumbers);
        var world = result.World;
        var ecs = result.EcsContext;
        result.Internals.ProcessingTierResolver.SetReferencePosition(Reference);

        var records = new NeighborhoodRecords(mathUtility);
        var factory = result.Internals.Factory;
        FloorBuilder.PopulateFloor(world, ecs, records, factory, result.Catalogs.Terrain, result.Catalogs.Definitions);
        result.Internals.MovedEntities.ClearFrame();

        var builder = new TestMapBuilder(ecs.EntityManager, factory, result.Catalogs.Terrain, result.Catalogs.Definitions);
        var streamer = new NeighborhoodStreamer(world, ecs.EntityManager, ecs.ComponentManager.GetDirectPool<TransformComponent>(), ecs.EventBus, result.Internals.ProcessingTierResolver, records, builder, result.Internals.Skeletons) { BudgetPerFrame = budgetPerFrame };
        return new Session(world, ecs, result.Internals.ProcessingTierResolver, streamer, result.Internals.MovedEntities, result.Catalogs.Definitions, result.Internals.Skeletons);
    }

    /// <summary>Pumps the streamer until it has nothing left to do, returning how many entities each frame created or destroyed.</summary>
    private static List<int> RunUntilIdle(Session session)
    {
        var changes = new List<int>();
        while (session.Streamer.IsBusy)
        {
            var livingBefore = session.Ecs.EntityManager.LivingEntityCount;
            session.Streamer.Update(default, 0);
            session.MovedEntities.ClearFrame();
            changes.Add(System.Math.Abs(session.Ecs.EntityManager.LivingEntityCount - livingBefore));
            Assert.IsLessThan(10_000, changes.Count, "The streamer never went idle.");
        }

        return changes;
    }

    private static List<int> EntitiesIn(Session session, int cellX, int cellY)
    {
        var entityIds = new List<int>();
        for (var z = 0; z < 3; z++)
        {
            session.Resolver.Membership.CopyCell(cellX, cellY, z, entityIds);
        }

        return entityIds;
    }

    private static List<(TerrainCell Ground, TerrainCell UnderGround, TerrainCell Wall)> LayoutOfNeighborhoodOne(Game.World.World world)
    {
        var cells = new List<(TerrainCell, TerrainCell, TerrainCell)>();
        for (var y = 0; y < Rows; y++)
        {
            for (var x = Neighborhoods.SizeTiles; x < 2 * Neighborhoods.SizeTiles; x++)
            {
                cells.Add((world.Map.GetTerrain(x, y, TerrainLayer.Ground), world.Map.GetTerrain(x, y, TerrainLayer.UnderGround), world.GetStructureAt(new Vector3Int(x, y, (int)MapLayer.Ground))));
            }
        }

        return cells;
    }

    private static List<bool> OccupancyOfNeighborhoodOne(Game.World.World world)
    {
        var occupied = new List<bool>();
        for (var z = 0; z < 3; z++)
        {
            for (var y = 0; y < Rows; y++)
            {
                for (var x = Neighborhoods.SizeTiles; x < 2 * Neighborhoods.SizeTiles; x++)
                {
                    occupied.Add(world.GetEntityIdAt(new Vector3Int(x, y, z)) != -1);
                }
            }
        }

        return occupied;
    }

    [TestMethod]
    public void Regenerate_KeepsTheLayout_ReplacesThePopulation_AndLeavesNothingBehind()
    {
        var session = Build();
        var keys = session.Ecs.EntityManager.Keys;
        var layoutBefore = LayoutOfNeighborhoodOne(session.World);
        var occupancyBefore = OccupancyOfNeighborhoodOne(session.World);
        var oldKeys = EntitiesIn(session, 1, 0).Select(keys.GetKey).ToList();
        var livingInNeighborhoodZero = session.Ecs.EntityManager.LivingEntityCount - oldKeys.Count;

        Assert.IsTrue(session.Streamer.TryRequestRegenerate(1, 0));
        RunUntilIdle(session);

        var newEntityIds = EntitiesIn(session, 1, 0);
        var transforms = session.Ecs.ComponentManager.GetDirectPool<TransformComponent>();
        CollectionAssert.AreEqual(layoutBefore, LayoutOfNeighborhoodOne(session.World));
        CollectionAssert.AreNotEqual(occupancyBefore, OccupancyOfNeighborhoodOne(session.World), "A fresh population.");
        Assert.IsNotEmpty(newEntityIds);
        Assert.IsTrue(oldKeys.All(key => !keys.TryGetEntityId(key, out _)), "No unloaded entity is still alive.");
        Assert.AreEqual(livingInNeighborhoodZero + newEntityIds.Count, session.Ecs.EntityManager.LivingEntityCount, "Nothing leaked and nothing extra was created.");
        Assert.AreEqual(session.Ecs.EntityManager.LivingEntityCount, session.Resolver.Membership.Count, "Every living entity is indexed exactly once.");
        foreach (var entityId in newEntityIds)
        {
            var position = transforms.GetReadonly(entityId).Position;
            Assert.IsTrue(session.World.GetEntityIdAt(position) == entityId || session.World.GetOccupantEntityIdsAt(position).Contains(entityId), $"Entity {entityId} isn't on the map at {position}.");
        }
    }

    [TestMethod]
    public void Regenerate_CountsItsJob_AndTheEntitiesItDestroysAndSpawns()
    {
        var session = Build();
        var destroyedCount = EntitiesIn(session, 1, 0).Count;

        Assert.IsTrue(session.Streamer.TryRequestRegenerate(1, 0));
        Assert.AreEqual((1, 0, 0), (session.Streamer.RegenerateJobCount, session.Streamer.LoadJobCount, session.Streamer.UnloadJobCount));
        RunUntilIdle(session);

        Assert.AreEqual(0, session.Streamer.RegenerateJobCount);
        Assert.AreEqual(destroyedCount, session.Streamer.TotalEntitiesDestroyed);
        Assert.AreEqual(EntitiesIn(session, 1, 0).Count, session.Streamer.TotalEntitiesSpawned);
        Assert.IsGreaterThan(0L, session.Streamer.TotalBudgetUnitsSpent);
    }

    [TestMethod]
    public void Regenerate_NoFrameGoesMoreThanARowOverItsBudget()
    {
        var session = Build(budgetPerFrame: 64);

        session.Streamer.TryRequestRegenerate(1, 0);
        var changes = RunUntilIdle(session);

        Assert.IsGreaterThan(10, changes.Count, "Precondition: spread over many frames.");
        Assert.IsLessThanOrEqualTo(64 + 150, changes.Max());
    }

    [TestMethod]
    public void CanRegenerate_RefusesWithinLocalRangeOfThePlayer_WorkAlreadyQueued_AndUnloadedNeighborhoods()
    {
        var session = Build();

        Assert.IsFalse(session.Streamer.CanRegenerate(0, 0, out var ownNeighborhood));
        Assert.AreEqual("The player is inside it", ownNeighborhood);
        Assert.IsFalse(session.Streamer.CanRegenerate(5, 0, out var notLoaded));
        Assert.AreEqual("Not loaded", notLoaded);

        Assert.IsTrue(session.Streamer.TryRequestRegenerate(1, 0));
        Assert.IsFalse(session.Streamer.CanRegenerate(1, 0, out var pending));
        Assert.AreEqual("Already regenerating", pending);

        session.Resolver.SetReferencePosition(new Vector3Int(Neighborhoods.SizeTiles - 50, 5, (int)MapLayer.Ground));
        RunUntilIdle(session);
        Assert.IsFalse(session.Streamer.CanRegenerate(1, 0, out var nearEdge));
        Assert.AreEqual("The player is 50 tiles from its edge; needs more than 96", nearEdge);
    }

    [TestMethod]
    public void Regenerate_PlayerComesWithinReachBeforeItStarts_IsDropped()
    {
        var session = Build();
        var keys = session.Ecs.EntityManager.Keys;
        var oldKeys = EntitiesIn(session, 1, 0).Select(keys.GetKey).ToList();
        Assert.IsTrue(session.Streamer.TryRequestRegenerate(1, 0));

        session.Resolver.SetReferencePosition(new Vector3Int(Neighborhoods.SizeTiles - 50, 5, (int)MapLayer.Ground));
        RunUntilIdle(session);

        Assert.IsTrue(oldKeys.All(key => keys.TryGetEntityId(key, out _)), "The same creatures, still alive.");
    }

    [TestMethod]
    public void Regenerate_CreatureArrivingWhileTerrainIsAnnounced_IsDestroyedBeforeTheStoresDrop()
    {
        var session = Build();
        var keys = session.Ecs.EntityManager.Keys;
        var transforms = session.Ecs.ComponentManager.GetDirectPool<TransformComponent>();
        var arrivalEntityId = -1;
        session.Ecs.EventBus.SubscribeOnce<TerrainUnloadingEvent>(_ =>
        {
            for (var x = Neighborhoods.SizeTiles + 10; arrivalEntityId < 0; x++)
            {
                var position = new Vector3Int(x, 5, (int)MapLayer.Ground);
                var entityId = session.Resolver.CreateEntityAt(session.Ecs.EntityManager, position);
                session.Ecs.ComponentManager.Merge(entityId, new TransformComponent(TransformComponent.UnplacedOn(MapLayer.Ground), new Vector2Byte(1, 1)));
                session.World.PlaceEntityOnMap(entityId, position, ref transforms.Get(entityId));
                if (session.World.IsOnMap(transforms.GetReadonly(entityId).Position))
                {
                    arrivalEntityId = entityId;
                }
                else
                {
                    session.Ecs.EntityManager.DestroyEntity(entityId);
                }
            }
        });

        session.Streamer.TryRequestRegenerate(1, 0);
        while (arrivalEntityId < 0)
        {
            session.Streamer.Update(default, 0);
            session.MovedEntities.ClearFrame();
        }

        var arrivalKey = keys.GetKey(arrivalEntityId);
        RunUntilIdle(session);

        Assert.IsFalse(keys.TryGetEntityId(arrivalKey, out _));
        Assert.AreEqual(session.Ecs.EntityManager.LivingEntityCount, session.Resolver.Membership.Count, "Every living entity is indexed exactly once.");
    }

    /// <summary>The kill is recorded with the killer's name, so the corpse still names it after the killer's neighborhood is unloaded and its id belongs to someone else.</summary>
    [TestMethod]
    public void Regenerate_CorpseKilledByAnUnloadedCreature_StillNamesItsKiller()
    {
        var session = Build();
        var components = session.Ecs.ComponentManager;
        // A creature is named by its race rather than by a component of its own (see EntityNaming).
        var naming = EntityNaming.For(components, session.Definitions);
        var corpseEntityId = EntitiesIn(session, 0, 0).First(entityId => naming.TryGetName(entityId, out _));
        var killerEntityId = EntitiesIn(session, 1, 0).First(entityId => naming.TryGetName(entityId, out _));
        var killerName = naming.NameOf(killerEntityId);
        components.GetPackedPool<DeadComponent>().Add(corpseEntityId, new DeadComponent(ActionSource.FromEntity(components, session.Ecs.EntityManager.Keys, killerEntityId, session.Definitions), DiedAtFrame: 0));

        session.Streamer.TryRequestRegenerate(1, 0);
        RunUntilIdle(session);

        var killedBy = components.GetPackedPool<DeadComponent>().GetReadonly(corpseEntityId).KilledBy;
        Assert.AreEqual(killerName, killedBy.Identity.Name);
        Assert.IsFalse(session.Ecs.EntityManager.Keys.TryGetEntityId(killedBy.Key, out _));
    }

    [TestMethod]
    public void Regenerate_EveryNewEntityIsBornAtTheNeighborhoodsOwnTier()
    {
        var session = Build();
        session.Ecs.SystemManager.Clock.Advance(500);

        session.Streamer.TryRequestRegenerate(1, 0);
        RunUntilIdle(session);

        var tiers = session.Ecs.ComponentManager.GetDirectPool<ProcessingTierComponent>();
        foreach (var entityId in EntitiesIn(session, 1, 0))
        {
            Assert.AreEqual(ProcessingTierLevel.Borough, tiers.GetReadonly(entityId).Tier);
        }
    }

    /// <summary>A strip eleven neighborhoods wide (-5 to 5) and a few rows tall, with only the window around neighborhood 0 loaded and populated, and the window centred there.</summary>
    private static Session BuildWindow()
    {
        var map = new Map(new MapBounds(-5 * Neighborhoods.SizeTiles, 0, 6 * Neighborhoods.SizeTiles, Rows, 3));
        foreach (var cellX in new[] { -5, -4, -3, -2, 2, 3, 4, 5 })
        {
            map.UnloadNeighborhood(cellX, 0);
        }

        var mathUtility = new MathUtility(new Random(1));
        var crawlerNumbers = new UniqueNumberAllocator(1, 1, 24);
        var result = GameBootstrapper.Build(ValidatedMods.None, map, mathUtility, initialEntityCapacity: 1_000, initialComponentCapacity: 100, crawlerNumbers: crawlerNumbers);
        var world = result.World;
        var ecs = result.EcsContext;
        result.Internals.ProcessingTierResolver.SetReferencePosition(Reference);
        result.Internals.ProcessingTierResolver.SetWindowCenter(0, 0);

        var records = new NeighborhoodRecords(mathUtility);
        var skeletons = result.Internals.Skeletons;
        var factory = result.Internals.Factory;
        FloorBuilder.PopulateFloor(world, ecs, records, factory, result.Catalogs.Terrain, result.Catalogs.Definitions);
        result.Internals.MovedEntities.ClearFrame();

        var builder = new TestMapBuilder(ecs.EntityManager, factory, result.Catalogs.Terrain, result.Catalogs.Definitions);
        var streamer = new NeighborhoodStreamer(world, ecs.EntityManager, ecs.ComponentManager.GetDirectPool<TransformComponent>(), ecs.EventBus, result.Internals.ProcessingTierResolver, records, builder, skeletons);
        return new Session(world, ecs, result.Internals.ProcessingTierResolver, streamer, result.Internals.MovedEntities, result.Catalogs.Definitions, skeletons);
    }

    /// <summary>Moves the player a neighborhood along and shifts the window there, the way ProcessingTierSystem does.</summary>
    private static void ShiftTo(Session session, int cellX)
    {
        session.Resolver.SetReferencePosition(new Vector3Int(Neighborhoods.OriginOf(cellX) + 100, 5, (int)MapLayer.Ground));
        session.Resolver.ShiftWindowTo(cellX, 0);
    }

    private static int[] LoadedCells(Session session) =>
        Enumerable.Range(-5, 11).Where(cellX => session.World.Map.IsNeighborhoodLoaded(cellX, 0)).ToArray();

    [TestMethod]
    public void WindowShift_LoadsTheEnteringNeighborhood_AndKeepsTheLeavingOneCached()
    {
        var session = BuildWindow();

        ShiftTo(session, 1);
        RunUntilIdle(session);

        CollectionAssert.AreEqual(new[] { -1, 0, 1, 2 }, LoadedCells(session));
        CollectionAssert.AreEqual(new[] { (-1, 0) }, session.Streamer.Cache.ToArray());
        var tiers = session.Ecs.ComponentManager.GetDirectPool<ProcessingTierComponent>();
        Assert.IsNotEmpty(EntitiesIn(session, 2, 0));
        Assert.IsTrue(EntitiesIn(session, 2, 0).All(entityId => tiers.GetReadonly(entityId).Tier == ProcessingTierLevel.Borough), "Born against the new centre.");
    }

    [TestMethod]
    public void WindowShift_CountsTheLoadAndItsPlanThroughEachStage()
    {
        var session = BuildWindow();
        var livingBefore = session.Ecs.EntityManager.LivingEntityCount;

        ShiftTo(session, 1);
        Assert.AreEqual((1, 1, 0, 0), (session.Streamer.LoadJobCount, session.Streamer.PlansAwaitingStartCount, session.Streamer.PlansRunningCount, session.Streamer.PlansReadyCount));

        session.Streamer.Update(default, 0);
        Assert.AreEqual(0, session.Streamer.PlansAwaitingStartCount);
        Assert.AreEqual(1, session.Streamer.PlansRunningCount + session.Streamer.PlansReadyCount);

        RunUntilIdle(session);
        Assert.AreEqual((0, 0, 0, 0), (session.Streamer.LoadJobCount, session.Streamer.PlansAwaitingStartCount, session.Streamer.PlansRunningCount, session.Streamer.PlansReadyCount));
        Assert.AreEqual(session.Ecs.EntityManager.LivingEntityCount - livingBefore, session.Streamer.TotalEntitiesSpawned);
    }

    /// <summary>A queued load starts exactly StartDelayFrames updates after it was queued -- not earlier, however fast its worker was, and not later, however slow: the main thread waits for the plan instead.</summary>
    [TestMethod]
    public void WindowShift_StartsTheLoadAfterExactlyTheStartDelay()
    {
        var session = BuildWindow();
        session.Streamer.Update(default, 0);

        ShiftTo(session, 1);
        for (var update = 1; update < session.Streamer.StartDelayFrames; update++)
        {
            session.Streamer.Update(default, 0);
            Assert.IsFalse(session.World.Map.IsNeighborhoodLoaded(2, 0), $"Loaded early, on update {update}.");
        }

        session.Streamer.Update(default, 0);
        Assert.IsTrue(session.World.Map.IsNeighborhoodLoaded(2, 0));
    }

    /// <summary>Turning back before a load starts drops it and its worker, and counts no population: when the player does arrive, the neighborhood gets the population it would have had the first time.</summary>
    [TestMethod]
    public void WindowShift_BackBeforeTheLoadStarts_DropsIt_AndTheNextVisitGetsTheSamePopulation()
    {
        var direct = BuildWindow();
        ShiftTo(direct, 1);
        RunUntilIdle(direct);

        var turnedBack = BuildWindow();
        ShiftTo(turnedBack, 1);
        turnedBack.Streamer.Update(default, 0);
        ShiftTo(turnedBack, 0);
        RunUntilIdle(turnedBack);
        Assert.IsFalse(turnedBack.World.Map.IsNeighborhoodLoaded(2, 0), "Precondition: the load was dropped.");

        ShiftTo(turnedBack, 1);
        RunUntilIdle(turnedBack);

        CollectionAssert.AreEqual(PositionsIn(direct, 2), PositionsIn(turnedBack, 2));
    }

    private static List<Vector3Int> PositionsIn(Session session, int cellX)
    {
        var transforms = session.Ecs.ComponentManager.GetDirectPool<TransformComponent>();
        return [.. EntitiesIn(session, cellX, 0).Select(entityId => transforms.GetReadonly(entityId).Position).OrderBy(static p => p.Z).ThenBy(static p => p.Y).ThenBy(static p => p.X)];
    }

    /// <summary>An eviction frees its built creatures before anything else, so promotions (held on IsEvictingBuiltCreatures) can reuse their storage without waiting for the rest of the unload.</summary>
    [TestMethod]
    public void Eviction_DestroysBuiltCreaturesFirst_AndStopsHoldingPromotionsBeforeTheRestUnloads()
    {
        var session = BuildWindow();
        var skeletons = session.Skeletons;
        var evicted = EntitiesIn(session, -1, 0);
        Assert.IsTrue(evicted.Any(entityId => !skeletons.IsSkeleton(entityId)), "Creatures born within Local reach of the spawn are built.");
        Assert.IsTrue(evicted.Any(skeletons.IsSkeleton));

        foreach (var cellX in new[] { 1, 2, 3 })
        {
            ShiftTo(session, cellX);
            RunUntilIdle(session);
        }

        Assert.IsFalse(session.Streamer.IsEvictingBuiltCreatures);

        ShiftTo(session, 4);
        Assert.IsTrue(session.Streamer.IsEvictingBuiltCreatures, "Neighborhood -1 is the oldest cached, evicted by this shift.");

        for (var frame = 0; frame < 1_000 && session.Streamer.IsEvictingBuiltCreatures; frame++)
        {
            session.Streamer.Update(default, 0);
            session.MovedEntities.ClearFrame();
        }

        var remaining = EntitiesIn(session, -1, 0).Where(session.Ecs.EntityManager.EntityExists).ToList();
        Assert.IsFalse(session.Streamer.IsEvictingBuiltCreatures);
        Assert.IsNotEmpty(remaining, "Only the built creatures are gone yet.");
        Assert.IsTrue(remaining.All(skeletons.IsSkeleton));
        Assert.IsTrue(session.Streamer.IsBusy);

        RunUntilIdle(session);
        CollectionAssert.DoesNotContain(LoadedCells(session), -1);
    }

    [TestMethod]
    public void WindowShift_TurningStraightBack_ReloadsNothing()
    {
        var session = BuildWindow();
        ShiftTo(session, 1);
        RunUntilIdle(session);
        var keys = session.Ecs.EntityManager.Keys;
        var westKeys = EntitiesIn(session, -1, 0).Select(keys.GetKey).ToList();

        ShiftTo(session, 0);

        Assert.IsFalse(session.Streamer.IsBusy);
        CollectionAssert.AreEqual(new[] { (2, 0) }, session.Streamer.Cache.ToArray());
        Assert.IsTrue(westKeys.All(key => keys.TryGetEntityId(key, out _)), "The same creatures, still alive.");
    }

    [TestMethod]
    public void WindowShift_PastTheCache_UnloadsTheOldestDrop()
    {
        var session = BuildWindow();

        foreach (var cellX in new[] { 1, 2, 3, 4 })
        {
            ShiftTo(session, cellX);
            RunUntilIdle(session);
        }

        CollectionAssert.AreEqual(new[] { (0, 0), (1, 0), (2, 0) }, session.Streamer.Cache.ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, LoadedCells(session));
        Assert.IsEmpty(EntitiesIn(session, -1, 0));
        Assert.AreEqual(session.Ecs.EntityManager.LivingEntityCount, session.Resolver.Membership.Count);
    }

    /// <summary>Walking back before an eviction's unload has started cancels it: the neighborhood was never touched, so its creatures are the same ones.</summary>
    [TestMethod]
    public void WindowShift_BackBeforeAnEvictionStarts_CancelsTheUnload()
    {
        var session = BuildWindow();
        var keys = session.Ecs.EntityManager.Keys;
        var westKeys = EntitiesIn(session, -1, 0).Select(keys.GetKey).ToList();
        foreach (var cellX in new[] { 1, 2, 3 })
        {
            ShiftTo(session, cellX);
            RunUntilIdle(session);
        }

        ShiftTo(session, 4);
        foreach (var cellX in new[] { 3, 2, 1, 0 })
        {
            ShiftTo(session, cellX);
        }

        RunUntilIdle(session);

        Assert.IsTrue(session.World.Map.IsNeighborhoodLoaded(-1, 0));
        Assert.IsTrue(westKeys.All(key => keys.TryGetEntityId(key, out _)));
        Assert.IsFalse(session.World.Map.IsNeighborhoodLoaded(5, 0), "The load it had queued past the far end was cancelled too.");
    }

    /// <summary>A 3x3 creature standing across the border keeps the cells on the far side when that neighborhood is unloaded and generated again.</summary>
    [TestMethod]
    public void Regenerate_RestoresTheFootprintOfACreatureStraddlingTheBorder()
    {
        var session = Build();
        var transforms = session.Ecs.ComponentManager.GetDirectPool<TransformComponent>();
        var straddlerEntityId = -1;
        var row = 0;
        for (row = 1; row < Rows - 3 && straddlerEntityId < 0; row++)
        {
            var position = new Vector3Int(Neighborhoods.SizeTiles - 2, row, (int)MapLayer.Ground);
            var entityId = session.Resolver.CreateEntityAt(session.Ecs.EntityManager, position);
            session.Ecs.ComponentManager.Merge(entityId, new TransformComponent(TransformComponent.UnplacedOn(MapLayer.Ground), new Vector2Byte(3, 3)));
            session.World.PlaceEntityOnMap(entityId, position, ref transforms.Get(entityId));
            if (session.World.IsOnMap(transforms.GetReadonly(entityId).Position))
            {
                straddlerEntityId = entityId;
            }
            else
            {
                session.Ecs.EntityManager.DestroyEntity(entityId);
            }
        }

        var farSideCell = new Vector3Int(Neighborhoods.SizeTiles, transforms.GetReadonly(straddlerEntityId).Position.Y + 1, (int)MapLayer.Ground);
        Assert.AreEqual(straddlerEntityId, session.World.GetEntityIdAt(farSideCell), "Precondition: placed across the border.");

        session.Streamer.TryRequestRegenerate(1, 0);
        RunUntilIdle(session);

        Assert.AreEqual(straddlerEntityId, session.World.GetEntityIdAt(farSideCell));
        CollectionAssert.Contains(session.World.GetOccupantEntityIdsAt(farSideCell).ToArray(), straddlerEntityId);
    }
}
