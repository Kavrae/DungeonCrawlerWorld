using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game;
using Game.Blueprints.Objects;
using Game.Bootstrap;
using Game.Floors;
using Game.Modules.Auras;
using Game.Modules.Auras.Components;
using Game.Modules.Core.Components;
using Game.Spawning;
using Game.World;

namespace Tests.Floors;

/// <summary>The player teleports east a neighborhood at a time, with every system and the streamer running, until the neighborhood behind is evicted with the aura sources in it.</summary>
[TestClass]
public sealed class AuraEvictionWalkTests
{
    private const int Rows = 24;
    private const int EvictedCellX = -1;
    private const int BorderStripTiles = 16;

    private sealed record Session(GameSession Result, NeighborhoodStreamer Streamer, int PlayerEntityId)
    {
        public Game.World.World World => Result.World;
    }

    /// <summary>A strip eleven neighborhoods wide (-5 to 5), the window around neighborhood 0 loaded and populated, with the player in it.</summary>
    private static Session BuildSession()
    {
        var map = new Map(new MapBounds(-5 * Neighborhoods.SizeTiles, 0, 6 * Neighborhoods.SizeTiles, Rows, 3));
        foreach (var cellX in new[] { -5, -4, -3, -2, 2, 3, 4, 5 })
        {
            map.UnloadNeighborhood(cellX, 0);
        }

        var mathUtility = new MathUtility(new Random(1));
        var result = GameBootstrapper.Build(ValidatedMods.None, map, mathUtility, initialEntityCapacity: 1_000, initialComponentCapacity: 100, crawlerNumbers: new UniqueNumberAllocator(1, 1, 24));
        var world = result.World;
        var ecs = result.EcsContext;
        var resolver = result.Internals.ProcessingTierResolver;
        resolver.SetReferencePosition(FloorBuilder.PlayerSpawnOrigin());
        resolver.SetWindowCenter(0, 0);

        var records = new NeighborhoodRecords(mathUtility);
        var factory = result.Internals.Factory;
        var playerEntityId = FloorBuilder.ReservePlayerEntity(ecs);
        FloorBuilder.PopulateFloor(world, ecs, records, factory, result.Catalogs.Terrain, result.Catalogs.Auras, result.Catalogs.Definitions);
        FloorBuilder.CreatePlayer(world, ecs, mathUtility, factory, result.Catalogs.Definitions, playerEntityId, resolver);
        world.PlayerEntityId = playerEntityId;

        var builder = new TestMapBuilder(ecs.EntityManager, factory, result.Catalogs.Terrain, result.Catalogs.Auras, result.Catalogs.Definitions);
        var streamer = new NeighborhoodStreamer(world, ecs.EntityManager, ecs.ComponentManager.GetDirectPool<TransformComponent>(), ecs.EventBus, resolver, records, builder, result.Internals.Skeletons);
        return new Session(result, streamer, playerEntityId);
    }

    private static void RunFrame(Session session)
    {
        var systems = session.Result.EcsContext.SystemManager;
        var frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);
        var frame = systems.Clock.CurrentFrame + 1;
        session.Streamer.Update(default, 0);
        systems.Update(new EngineTime(frameDuration * frame, frameDuration, false, frame));
    }

    /// <summary>Runs frames until the streamer and the tier transitions have nothing left to do, then a second more for queued aura resyncs.</summary>
    private static void RunUntilSettled(Session session)
    {
        var transitions = session.Result.Internals.ProcessingTierResolver.Transitions;
        RunFrame(session);
        for (var frame = 0; session.Streamer.IsBusy || transitions.HasPending; frame++)
        {
            Assert.IsLessThan(20_000, frame, "The window shift never settled.");
            RunFrame(session);
        }

        for (var frame = 0; frame < 60; frame++)
        {
            RunFrame(session);
        }
    }

    private static List<int> AuraSourceEntitiesIn(Session session, int cellX)
    {
        var sources = session.Result.EcsContext.ComponentManager.GetMultiPool<AuraSourceComponent>();
        var entityIds = new List<int>();
        for (var z = 0; z < 3; z++)
        {
            session.Result.Internals.ProcessingTierResolver.Membership.CopyCell(cellX, 0, z, entityIds);
        }

        return entityIds.Where(sources.Has).ToList();
    }

    /// <summary>What the field would hold if it were built now from nothing: the loaded terrain and every source in the pool where its entity stands.</summary>
    private static AuraField BuildFieldFromScratch(Session session)
    {
        var components = session.Result.EcsContext.ComponentManager;
        var field = new AuraField(session.World, session.Result.Catalogs.Terrain, session.Result.Catalogs.Auras, new EventBus());
        field.EnsureBuilt();

        var sources = components.GetMultiPool<AuraSourceComponent>();
        var transforms = components.GetDirectPool<TransformComponent>();
        var sourceEntityIds = sources.EntityIds;
        var sourceComponents = sources.Components;
        for (var index = 0; index < sourceEntityIds.Length; index++)
        {
            var position = transforms.GetReadonly(sourceEntityIds[index]).Position;
            if (session.World.IsOnMap(position))
            {
                field.AddSource(position, sourceComponents[index]);
            }
        }

        return field;
    }

    [TestMethod]
    public void WalkingPastTheCache_EvictsTheNeighborhoodsAuraSources_AndLeavesNothingInTheField()
    {
        var session = BuildSession();
        var ecs = session.Result.EcsContext;
        var components = ecs.ComponentManager;
        var keys = ecs.EntityManager.Keys;
        var transforms = components.GetDirectPool<TransformComponent>();
        var liveField = session.Result.Internals.AuraField;

        // A shrine just inside the neighborhood that will be evicted, close enough to the border to reach into the one that stays.
        var borderShrineCell = FloorBuilder.FindFreeGroundCellNear(session.World, new Vector3Int(-2, 5, (int)MapLayer.Ground));
        Assert.IsTrue(borderShrineCell.X is >= -4 and < 0, $"Precondition: the border shrine's cell {borderShrineCell} is in the evicted neighborhood, within reach of the border.");
        session.Result.Internals.Factory.Spawn(HealingShrine.Id, borderShrineCell.X, borderShrineCell.Y);
        RunUntilSettled(session);

        var healingAuraId = session.Result.Catalogs.Auras.GetId(HealingShrine.Aura.Id);
        var probeAcrossTheBorder = new Vector3Int(0, borderShrineCell.Y, borderShrineCell.Z);
        var powerAtProbeBefore = liveField.GetTotalPowerAt(probeAcrossTheBorder, healingAuraId);
        var borderShrinePowerAtProbe = AuraFalloff.Linear.ValueAt(power: 16, size: 4, -borderShrineCell.X);
        Assert.IsGreaterThanOrEqualTo(borderShrinePowerAtProbe, powerAtProbeBefore, "Precondition: the border shrine reaches across the border.");

        var evictedSourceEntityIds = AuraSourceEntitiesIn(session, EvictedCellX);
        Assert.IsNotEmpty(evictedSourceEntityIds, "Precondition: the neighborhood that will be evicted holds aura sources.");
        var evictedSourceKeys = evictedSourceEntityIds.Select(keys.GetKey).ToList();
        var reachedAcrossTheBorder = evictedSourceEntityIds.Count(entityId => transforms.GetReadonly(entityId).Position.X >= -8);

        foreach (var cellX in new[] { 1, 2, 3, 4 })
        {
            var destination = FloorBuilder.FindFreeGroundCellNear(session.World, new Vector3Int(Neighborhoods.OriginOf(cellX) + 200, 5, (int)MapLayer.Ground));
            Assert.IsTrue(session.Result.Internals.Teleporter.TryTeleport(session.PlayerEntityId, destination), $"Teleport into neighborhood {cellX}.");
            RunUntilSettled(session);
        }

        Assert.IsFalse(session.World.Map.IsNeighborhoodLoaded(EvictedCellX, 0), "The walk went far enough to evict the neighborhood.");
        Assert.IsTrue(session.World.Map.IsNeighborhoodLoaded(0, 0), "Its neighbor is still loaded, in the cache.");
        Assert.IsFalse(evictedSourceKeys.Any(key => keys.TryGetEntityId(key, out _)), "Every source entity of the evicted neighborhood is gone.");

        var sources = components.GetMultiPool<AuraSourceComponent>();
        var exposures = components.GetMultiPool<AuraExposureComponent>();
        Assert.IsTrue(sources.EntityIds.ToArray().All(ecs.EntityManager.EntityExists), "No source is left on a destroyed entity.");
        Assert.IsTrue(exposures.EntityIds.ToArray().All(ecs.EntityManager.EntityExists), "No exposure is left on a destroyed entity.");
        Assert.AreEqual(ecs.EntityManager.LivingEntityCount, session.Result.Internals.ProcessingTierResolver.Membership.Count, "Every living entity is indexed exactly once.");

        Assert.AreEqual(powerAtProbeBefore - borderShrinePowerAtProbe, liveField.GetTotalPowerAt(probeAcrossTheBorder, healingAuraId), "The border shrine's reach into the loaded neighbor went with it.");

        var expectedField = BuildFieldFromScratch(session);
        var auraCount = session.Result.Catalogs.Auras.Count;
        var mismatches = new List<string>();
        var coveredCells = 0;
        for (var z = 0; z < 3; z++)
        {
            for (var y = 0; y < Rows; y++)
            {
                for (var x = -BorderStripTiles; x < BorderStripTiles; x++)
                {
                    var position = new Vector3Int(x, y, z);
                    coveredCells += expectedField.AnyAuraReaches(position) ? 1 : 0;
                    for (var auraId = 0; auraId < auraCount; auraId++)
                    {
                        var live = liveField.GetTotalPowerAt(position, (byte)auraId);
                        var expected = expectedField.GetTotalPowerAt(position, (byte)auraId);
                        if (live != expected)
                        {
                            mismatches.Add($"{position} aura {auraId}: field {live}, expected {expected}");
                        }
                    }

                    if (liveField.AnyAuraReaches(position) != expectedField.AnyAuraReaches(position))
                    {
                        mismatches.Add($"{position}: covered {liveField.AnyAuraReaches(position)}, expected {expectedField.AnyAuraReaches(position)}");
                    }
                }
            }
        }

        Console.WriteLine($"Evicted {evictedSourceKeys.Count} aura source entities ({reachedAcrossTheBorder} within 8 tiles of the border); {coveredCells} cells of the border strip are still reached by a remaining aura.");
        Assert.IsEmpty(mismatches, string.Join("; ", mismatches.Take(10)));
        Assert.AreEqual(expectedField.TotalsChunkCount, liveField.TotalsChunkCount, "The live field holds chunks a field built from what is loaded now would not: an evicted neighborhood's storage was kept.");
    }
}
