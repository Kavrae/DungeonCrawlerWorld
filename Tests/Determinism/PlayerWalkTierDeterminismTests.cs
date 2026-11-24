using Engine.ECS.Systems;
using Engine.Math;
using Engine.Tiles;
using Game;
using Game.Bootstrap;
using Game.Floors;
using Game.Modules.Core.Components;
using Game.Modules.Crawler.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.World;

namespace Tests.Determinism;

/// <summary>The player walks a script of single steps, diagonal steps, layer changes and a neighborhood crossing that moves the window, with every system and the streamer running.</summary>
[TestClass]
[TestCategory("Determinism")]
public sealed class PlayerWalkTierDeterminismTests
{
    private const int Rows = 24;

    private sealed record Session(GameSession Result, NeighborhoodStreamer Streamer, int PlayerEntityId)
    {
        public Game.World.World World => Result.World;

        public ProcessingTierResolver Resolver => Result.Internals.ProcessingTierResolver;
    }

    [TestMethod]
    public void EveryStep_LeavesEveryTierWhereTheReferenceAndWindowPutIt()
    {
        var session = BuildSession();

        Walk(session, afterEachStep: AssertEveryTierSettled);
    }

    [TestMethod]
    public void TheSameWalk_GivesTheSameTiersPositionsAndCrawlerNumbers()
    {
        var first = new List<string>();
        var second = new List<string>();

        var firstSession = BuildSession();
        Walk(firstSession, afterEachStep: session => first.AddRange(Snapshot(session)));
        var secondSession = BuildSession();
        Walk(secondSession, afterEachStep: session => second.AddRange(Snapshot(session)));

        Assert.IsNotEmpty(first);
        for (var index = 0; index < System.Math.Min(first.Count, second.Count); index++)
        {
            Assert.AreEqual(first[index], second[index], $"The walks first differ at entry {index}.");
        }

        Assert.HasCount(first.Count, second);
    }

    /// <summary>A strip seven neighborhoods wide (-3 to 3), the window around neighborhood 0 loaded and populated, with the player in it.</summary>
    private static Session BuildSession()
    {
        var map = new Map(new MapBounds(-3 * Neighborhoods.SizeTiles, 0, 4 * Neighborhoods.SizeTiles, Rows, 3));
        foreach (var cellX in new[] { -3, -2, 2, 3 })
        {
            map.UnloadNeighborhood(cellX, 0);
        }

        var result = GameBootstrapper.Build(ValidatedMods.None, map, TestContent.Layers, sessionSeed: 1, initialEntityCapacity: 1_000, initialComponentCapacity: 100, crawlerNumbers: new UniqueNumberAllocator(1, 1, 24));
        var world = result.World;
        var ecs = result.EcsContext;
        var resolver = result.Internals.ProcessingTierResolver;
        var spawn = FloorBuilder.PlayerSpawnOrigin(result.Catalogs.Areas);
        resolver.SetReferencePosition(spawn);
        resolver.SetWindowCenter(0, 0);

        var records = new NeighborhoodRecords(worldSeed: 1, floorNumber: FloorBuilder.FirstFloorNumber);
        var factory = result.Internals.Factory;
        var playerEntityId = FloorBuilder.ReservePlayerEntity(ecs);
        FloorBuilder.PopulateFloor(world, ecs, records, factory, result.Catalogs.Terrain, result.Catalogs.Auras, result.Catalogs.Definitions, result.Catalogs.Areas, result.Internals.UniqueEntities);
        FloorBuilder.CreatePlayer(world, ecs, sessionSeed: 1, factory, result.Catalogs.Definitions, playerEntityId, spawn, resolver);
        world.PlayerEntityId = playerEntityId;

        var builder = new TestMapBuilder(ecs.EntityManager, factory, result.Catalogs.Terrain, result.Catalogs.Auras, result.Catalogs.Definitions, result.Catalogs.Areas, result.Internals.UniqueEntities);
        var streamer = new NeighborhoodStreamer(world, ecs.EntityManager, ecs.ComponentManager.GetDirectPool<TransformComponent>(), ecs.EventBus, resolver, records, builder, result.Internals.Skeletons);
        var session = new Session(result, streamer, playerEntityId);
        RunUntilSettled(session);
        return session;
    }

    private static void Walk(Session session, Action<Session> afterEachStep)
    {
        void StepBy(int deltaX, int deltaY)
        {
            var position = PlayerPosition(session);
            TeleportNear(session, position with { X = position.X + deltaX, Y = position.Y + deltaY });
            RunUntilSettled(session);
            afterEachStep(session);
        }

        for (var step = 0; step < 30; step++)
        {
            StepBy(1, 0);
        }

        for (var step = 0; step < 20; step++)
        {
            StepBy(1, step % 2 == 0 ? 1 : -1);
        }

        var groundPosition = PlayerPosition(session);
        var changedLayer = false;
        foreach (var layer in new[] { MapLayer.UnderGround, MapLayer.Flying })
        {
            if (TryTeleportNear(session, groundPosition with { Z = (int)layer }))
            {
                RunUntilSettled(session);
                afterEachStep(session);
                Assert.IsTrue(TryTeleportNear(session, groundPosition), "The player gets back to the ground.");
                RunUntilSettled(session);
                afterEachStep(session);
                changedLayer = true;
                break;
            }
        }

        Assert.IsTrue(changedLayer, "The walk changed MapLayer.");

        Assert.AreEqual(((int CellX, int CellY)?)(0, 0), session.Resolver.WindowCenter, "Precondition: nothing so far moves the window.");
        while (PlayerPosition(session).X < Neighborhoods.SizeTiles + ProcessingTierResolver.WindowShiftGraceTiles + 8)
        {
            StepBy(25, 0);
        }

        Assert.AreEqual(((int CellX, int CellY)?)(1, 0), session.Resolver.WindowCenter, "The walk east moved the window.");
        for (var step = 0; step < 10; step++)
        {
            StepBy(-1, 0);
        }
    }

    private static Vector3Int PlayerPosition(Session session) =>
        session.Result.EcsContext.ComponentManager.GetDirectPool<TransformComponent>().GetReadonly(session.PlayerEntityId).Position;

    private static void TeleportNear(Session session, Vector3Int target) =>
        Assert.IsTrue(TryTeleportNear(session, target), $"Nowhere free near {target}.");

    private static bool TryTeleportNear(Session session, Vector3Int target)
    {
        for (var radius = 0; radius <= 8; radius++)
        {
            for (var deltaY = -radius; deltaY <= radius; deltaY++)
            {
                for (var deltaX = -radius; deltaX <= radius; deltaX++)
                {
                    if (System.Math.Max(System.Math.Abs(deltaX), System.Math.Abs(deltaY)) == radius &&
                        session.Result.Internals.Teleporter.TryTeleport(session.PlayerEntityId, target with { X = target.X + deltaX, Y = target.Y + deltaY }))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static void RunFrame(Session session)
    {
        var systems = session.Result.EcsContext.SystemManager;
        var frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);
        var frame = systems.Clock.CurrentFrame + 1;
        session.Streamer.Update(default, 0);
        systems.Update(new EngineTime(frameDuration * frame, frameDuration, false, frame));
    }

    private static void RunUntilSettled(Session session)
    {
        RunFrame(session);
        for (var frame = 0; session.Streamer.IsBusy || session.Resolver.Transitions.HasPending; frame++)
        {
            Assert.IsLessThan(20_000, frame, "The walk never settled.");
            RunFrame(session);
        }
    }

    private static void AssertEveryTierSettled(Session session)
    {
        var components = session.Result.EcsContext.ComponentManager;
        var transforms = components.GetDirectPool<TransformComponent>();
        var tiers = components.GetDirectPool<ProcessingTierComponent>();
        var reference = session.Resolver.ReferencePosition!.Value;
        var center = session.Resolver.WindowCenter!.Value;
        for (var entityId = 0; entityId < transforms.Capacity; entityId++)
        {
            if (!transforms.TryGetReadonly(entityId, out var transform) || !tiers.TryGetReadonly(entityId, out var tier) ||
                session.Resolver.IsPinned(entityId) || !session.World.IsOnMap(transform.Position))
            {
                continue;
            }

            var expected = ProcessingTierResolver.ComputeTier(transform.Position, reference, center.CellX, center.CellY, tier.Tier);
            Assert.AreEqual(expected, tier.Tier, $"Entity {entityId} at {transform.Position} with the player at {reference} and the window on {center}.");
        }
    }

    /// <summary>Every entity on the map, by its key: its tier, where it stands, and its crawler number.</summary>
    private static List<string> Snapshot(Session session)
    {
        var components = session.Result.EcsContext.ComponentManager;
        var transforms = components.GetDirectPool<TransformComponent>();
        var tiers = components.GetDirectPool<ProcessingTierComponent>();
        var crawlers = components.GetPackedPool<CrawlerComponent>();
        var keys = session.Result.EcsContext.EntityManager.Keys;
        var lines = new List<string>();
        for (var entityId = 0; entityId < transforms.Capacity; entityId++)
        {
            if (transforms.TryGetReadonly(entityId, out var transform) && tiers.TryGetReadonly(entityId, out var tier) && session.World.IsOnMap(transform.Position))
            {
                var crawlerNumber = crawlers.TryGetReadonly(entityId, out var crawler) ? crawler.CrawlerNumber : 0;
                lines.Add($"{keys.GetKey(entityId).Value}: {tier.Tier} {transform.Position} {crawlerNumber}");
            }
        }

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }
}
