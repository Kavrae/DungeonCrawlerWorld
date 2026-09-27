using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Spawning;
using Game.Floors;
using Game.Modules.Core.Components;
using Game.Views;
using Game.World;

namespace Tests.Spawning;

[TestClass]
public sealed class CreatureSkeletonsTests
{
    private const int Rows = 24;

    private static readonly DirectoryInfo EmptyModsDirectory = Directory.CreateTempSubdirectory();

    [ClassCleanup]
    public static void DeleteEmptyModsDirectory() => EmptyModsDirectory.Delete(recursive: true);

    private sealed record Session(Game.World.World World, EcsContext Ecs, GameBootstrapResult Result, int PlayerEntityId)
    {
        public CreatureSkeletons Skeletons => Result.Skeletons;
    }

    /// <summary>Three neighborhoods side by side (-1, 0, 1), the window centred on 0 with the player in it: neighborhood 0 is simulated, -1 and 1 are Borough.</summary>
    private static Session BuildSession(UniqueNumberAllocator? crawlerNumbers = null)
    {
        var world = new Game.World.World(new Map(new MapBounds(-Neighborhoods.SizeTiles, 0, 2 * Neighborhoods.SizeTiles, Rows, 3)));
        var mathUtility = new MathUtility(new Random(1));
        var result = GameBootstrapper.Build(world, mathUtility, EmptyModsDirectory.FullName, initialEntityCapacity: 1_000, initialComponentCapacity: 100, crawlerNumbers: crawlerNumbers ?? new UniqueNumberAllocator(1, 1, 24));
        var ecs = result.EcsContext;
        var resolver = result.ProcessingTierResolver;
        resolver.SetReferencePosition(FloorBuilder.PlayerSpawnOrigin());
        resolver.SetWindowCenter(0, 0);

        var playerEntityId = FloorBuilder.ReservePlayerEntity(ecs);
        FloorBuilder.PopulateFloor(world, ecs, new NeighborhoodRecords(mathUtility), result.Factory, result.Terrain, result.Definitions);
        FloorBuilder.CreatePlayer(world, ecs, mathUtility, result.Factory, result.Definitions, playerEntityId, resolver);
        world.PlayerEntityId = playerEntityId;
        result.MovedEntities.ClearFrame();

        return new Session(world, ecs, result, playerEntityId);
    }

    /// <summary>Every creature in neighborhood cellX -- an entity whose blueprint includes a race, which is what makes it a creature rather than one of the shops or fixtures that now carry a spawn record too.</summary>
    private static List<int> CreaturesIn(Session session, int cellX)
    {
        var spawnRecords = session.Ecs.ComponentManager.GetDirectPool<SpawnRecordComponent>();
        var transforms = session.Ecs.ComponentManager.GetDirectPool<TransformComponent>();
        var creatures = new List<int>();
        for (var entityId = 0; entityId < spawnRecords.Capacity; entityId++)
        {
            if (spawnRecords.Has(entityId)
                && session.Result.Definitions.Resolve(spawnRecords.GetReadonly(entityId).BlueprintId).Races.Count > 0
                && Neighborhoods.CellOf(transforms.GetReadonly(entityId).Position.X) == cellX)
            {
                creatures.Add(entityId);
            }
        }

        return creatures;
    }

    private static List<Type> ComponentTypesOf(Session session, int entityId)
    {
        var entries = new List<InspectedComponentEntry>();
        new ComponentInspector(session.Ecs.ComponentManager).CopyInspectionDataForEntity(entityId, entries);
        return entries.Select(static entry => entry.ComponentType).Distinct().ToList();
    }

    private static int FirstSkeletonOf(Session session, Guid raceId)
    {
        var raceSlot = session.Result.Definitions.Races.GetId(raceId);
        var spawnRecords = session.Ecs.ComponentManager.GetDirectPool<SpawnRecordComponent>();
        return CreaturesIn(session, 1).First(entityId => session.Skeletons.IsSkeleton(entityId) && spawnRecords.GetReadonly(entityId).BlueprintId == raceSlot);
    }

    private static void MovePlayerTo(Session session, Vector3Int position) =>
        Assert.IsTrue(session.Result.Teleporter.TryTeleport(session.PlayerEntityId, FloorBuilder.FindFreeGroundCellNear(session.World, position)));

    private static void RunFrames(Session session, int count)
    {
        var frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);
        var start = session.Ecs.SystemManager.Clock.CurrentFrame + 1;
        for (var frame = start; frame < start + count; frame++)
        {
            session.Ecs.SystemManager.Update(new EngineTime(frameDuration * frame, frameDuration, false, frame));
        }
    }

    [TestMethod]
    public void Population_BuildsTheSimulatedNeighborhood_AndLeavesBoroughCreaturesAsSkeletons()
    {
        var session = BuildSession();

        var centre = CreaturesIn(session, 0);
        var borough = CreaturesIn(session, 1);
        var actionLocks = session.Ecs.ComponentManager.GetPackedPool<ActionLockComponent>();

        Assert.IsNotEmpty(centre);
        Assert.IsNotEmpty(borough);
        Assert.IsTrue(centre.All(entityId => !session.Skeletons.IsSkeleton(entityId) && actionLocks.Has(entityId)));
        Assert.IsTrue(borough.All(session.Skeletons.IsSkeleton));
        Assert.AreEqual(borough.Count + CreaturesIn(session, -1).Count(session.Skeletons.IsSkeleton), session.Skeletons.Count);
    }

    /// <summary>Local reaches 80 tiles past the spawn into neighborhood -1: a creature born there is simulated from the start, so it is built even though its neighborhood is Borough.</summary>
    [TestMethod]
    public void Population_BuildsCreaturesBornLocal_EvenInABoroughNeighborhood()
    {
        var session = BuildSession();
        var tiers = session.Ecs.ComponentManager.GetDirectPool<Game.Modules.ProcessingTier.Components.ProcessingTierComponent>();

        var bornLocal = CreaturesIn(session, -1).Where(entityId => tiers.GetReadonly(entityId).Tier == Game.Modules.ProcessingTier.Components.ProcessingTierLevel.Local).ToList();

        Assert.IsNotEmpty(bornLocal);
        Assert.IsTrue(bornLocal.All(entityId => !session.Skeletons.IsSkeleton(entityId)));
    }

    [TestMethod]
    public void Skeleton_HoldsOnlyWhatItNeedsToExist()
    {
        var session = BuildSession();

        foreach (var entityId in CreaturesIn(session, 1))
        {
            var extras = ComponentTypesOf(session, entityId).Where(static type => !EntityFactory.SkeletonComponentTypes.Contains(type)).ToList();
            Assert.IsEmpty(extras, $"Skeleton {entityId} holds {string.Join(", ", extras.Select(static type => type.Name))}.");
        }
    }

    [TestMethod]
    public void GhostSkeleton_NeverBlocksItsCell()
    {
        var session = BuildSession();
        var ghostId = FirstSkeletonOf(session, Ghost.Id);

        Assert.IsFalse(session.World.IsBlocking(ghostId));
    }

    [TestMethod]
    public void EnsureBuilt_BuildsTheBody_KeepsPlacement_AndRecordsTheSpawnMove()
    {
        var session = BuildSession();
        var goblinId = FirstSkeletonOf(session, Goblin.Id);
        var transforms = session.Ecs.ComponentManager.GetDirectPool<TransformComponent>();
        var before = transforms.GetReadonly(goblinId);
        _ = session.Result.MovedEntities.Items; // This frame's readers have run, as they have by the tier transition drain.

        Assert.IsTrue(session.Skeletons.EnsureBuilt(goblinId));

        Assert.IsFalse(session.Skeletons.IsSkeleton(goblinId));
        Assert.IsTrue(session.Ecs.ComponentManager.GetPackedPool<ActionLockComponent>().Has(goblinId));
        Assert.IsTrue(session.Ecs.ComponentManager.GetPackedPool<ActionLockComponent>().Has(goblinId));
        Assert.AreEqual(before, transforms.GetReadonly(goblinId));
        Assert.IsFalse(session.Skeletons.EnsureBuilt(goblinId), "Building is one-way and happens once.");

        Assert.IsFalse(session.Result.MovedEntities.Items.ToArray().Any(moved => moved.EntityId == goblinId), "Too late for this frame's readers.");

        session.Result.MovedEntities.ClearFrame();
        session.Result.Factory.SpawnMoves!.Update(default, 0);
        Assert.IsTrue(session.Result.MovedEntities.Items.ToArray().Any(moved => moved.EntityId == goblinId && moved.NewPosition == before.Position));
    }

    /// <summary>Applying a blueprint is a gameplay write, so an unbuilt creature is built first -- the class lands on a whole creature, not on a skeleton the build would later overwrite.</summary>
    [TestMethod]
    public void Apply_ToASkeleton_BuildsItFirst()
    {
        var session = BuildSession();
        var goblinId = FirstSkeletonOf(session, Goblin.Id);
        var engineerId = session.Result.Definitions.GetId(Game.Blueprints.Classes.Engineer.Id);

        session.Result.Factory.Apply(goblinId, engineerId);

        Assert.IsFalse(session.Skeletons.IsSkeleton(goblinId));
        Assert.IsTrue(session.Ecs.ComponentManager.GetPackedPool<Game.Modules.Class.Components.ClassSlotsComponent>().GetReadonly(goblinId).Has(engineerId));
        Assert.IsTrue(session.Ecs.ComponentManager.GetPackedPool<ActionLockComponent>().Has(goblinId));
    }

    [TestMethod]
    public void SpawnRecord_StaysEightBytes() =>
        Assert.AreEqual(8, System.Runtime.CompilerServices.Unsafe.SizeOf<SpawnRecordComponent>());

    private static IEnumerable<int> UnbuiltCrawlers(Session session)
    {
        var spawnRecords = session.Ecs.ComponentManager.GetDirectPool<SpawnRecordComponent>();
        return CreaturesIn(session, -1).Concat(CreaturesIn(session, 1))
            .Where(entityId => session.Skeletons.IsSkeleton(entityId) && spawnRecords.GetReadonly(entityId).Flags.HasFlag(SpawnFlags.Crawler));
    }

    /// <summary>An unbuilt crawler is flagged but holds no number; building it gives it one, and nothing that builds or applies to it again gives it another.</summary>
    [TestMethod]
    public void Crawler_GetsItsNumberWhenFirstBuilt_AndOnlyOnce()
    {
        var session = BuildSession();
        var crawlers = session.Ecs.ComponentManager.GetPackedPool<Game.Modules.Crawler.Components.CrawlerComponent>();
        var crawlerId = UnbuiltCrawlers(session).First();
        Assert.IsFalse(crawlers.Has(crawlerId));

        session.Skeletons.EnsureBuilt(crawlerId);
        var number = crawlers.GetReadonly(crawlerId).CrawlerNumber;
        session.Result.Factory.Apply(crawlerId, session.Result.Definitions.GetId(Game.Blueprints.Classes.Engineer.Id));

        Assert.AreEqual(number, crawlers.GetReadonly(crawlerId).CrawlerNumber);
    }

    [TestMethod]
    public void Crawler_BuiltAfterTheCrawlerNumbersRunOut_BecomesAPlainNpc_AndNumberedCrawlersKeepTheirs()
    {
        var crawlerNumbers = new UniqueNumberAllocator(1, 1, 24);
        var session = BuildSession(crawlerNumbers);
        var crawlers = session.Ecs.ComponentManager.GetPackedPool<Game.Modules.Crawler.Components.CrawlerComponent>();
        var spawnRecords = session.Ecs.ComponentManager.GetDirectPool<SpawnRecordComponent>();
        var skeletonId = UnbuiltCrawlers(session).First();
        var numberedId = CreaturesIn(session, 0).First(crawlers.Has);
        var number = crawlers.GetReadonly(numberedId).CrawlerNumber;

        while (crawlerNumbers.TryAllocate(out _))
        {
        }

        session.Skeletons.EnsureBuilt(skeletonId);

        Assert.IsFalse(session.Skeletons.IsSkeleton(skeletonId));
        Assert.IsFalse(crawlers.Has(skeletonId));
        Assert.IsFalse(spawnRecords.GetReadonly(skeletonId).Flags.HasFlag(SpawnFlags.Crawler));
        Assert.AreEqual(number, crawlers.GetReadonly(numberedId).CrawlerNumber);
    }

    /// <summary>Admin inspection rebuilds an unbuilt crawler in the staging world, which has no crawler numbers to give.</summary>
    [TestMethod]
    public void Crawler_RebuiltForInspection_HasNoNumber()
    {
        var session = BuildSession();
        var record = session.Ecs.ComponentManager.GetDirectPool<SpawnRecordComponent>().GetReadonly(UnbuiltCrawlers(session).First());
        var hasNumber = true;

        session.Result.SpawnRecordRebuilder.Rebuild(record, (componentManager, entityId) =>
            hasNumber = componentManager.GetPackedPool<Game.Modules.Crawler.Components.CrawlerComponent>().Has(entityId));

        Assert.IsFalse(hasNumber);
    }

    [TestMethod]
    public void Population_StillRollsAboutTwoPercentCrawlers()
    {
        var session = BuildSession();
        var spawnRecords = session.Ecs.ComponentManager.GetDirectPool<SpawnRecordComponent>();
        var creatures = CreaturesIn(session, -1).Concat(CreaturesIn(session, 1)).ToList();

        var share = creatures.Count(entityId => spawnRecords.GetReadonly(entityId).Flags.HasFlag(SpawnFlags.Crawler)) / (double)creatures.Count;

        Assert.IsTrue(share is > 0.01 and < 0.03, $"Crawler share was {share:P2} of {creatures.Count} creatures.");
    }

    [TestMethod]
    public void DestroyingACreatureBuiltThisFrame_DropsItsSpawnMove()
    {
        var session = BuildSession();
        var goblinId = FirstSkeletonOf(session, Goblin.Id);
        _ = session.Result.MovedEntities.Items;
        session.Skeletons.EnsureBuilt(goblinId);

        session.Ecs.EntityManager.DestroyEntity(goblinId);
        session.Result.MovedEntities.ClearFrame();
        session.Result.Factory.SpawnMoves!.Update(default, 0);

        Assert.IsFalse(session.Result.MovedEntities.Items.ToArray().Any(moved => moved.EntityId == goblinId));
    }

    [TestMethod]
    public void Skeleton_DrawsAndIsNamedExactlyAsItWillBeOnceBuilt()
    {
        var session = BuildSession();
        var mapView = new MapViewQuery(session.World, session.Ecs.ComponentManager, session.Result.ActionCatalog, session.Result.Terrain, session.Result.Definitions);

        foreach (var raceId in new[] { Goblin.Id, Fairy.Id, Ghost.Id })
        {
            var entityId = FirstSkeletonOf(session, raceId);
            Assert.IsTrue(mapView.TryGetVisual(entityId, out var skeletonVisual));
            var skeletonName = mapView.GetInteraction(entityId).Name;

            session.Skeletons.EnsureBuilt(entityId);

            Assert.IsTrue(mapView.TryGetVisual(entityId, out var builtVisual));
            Assert.AreEqual(builtVisual, skeletonVisual);
            Assert.AreEqual(mapView.GetInteraction(entityId).Name, skeletonName);
        }
    }

    [TestMethod]
    public void DestroyingASkeleton_ForgetsIt()
    {
        var session = BuildSession();
        var goblinId = FirstSkeletonOf(session, Goblin.Id);
        var count = session.Skeletons.Count;

        session.Ecs.EntityManager.DestroyEntity(goblinId);

        Assert.IsFalse(session.Skeletons.IsSkeleton(goblinId));
        Assert.AreEqual(count - 1, session.Skeletons.Count);
    }

    /// <summary>The whole promotion path: the player crosses into Borough, the window shifts, and ProcessingTierSystem's transition drain builds each creature as it lands in a simulated tier -- while every system runs over them.</summary>
    [TestMethod]
    public void WindowShift_BuildsEverySkeletonPromotedIntoASimulatedTier()
    {
        var session = BuildSession();
        var promoted = CreaturesIn(session, 1);
        var transforms = session.Ecs.ComponentManager.GetDirectPool<TransformComponent>();
        var placements = promoted.ToDictionary(static entityId => entityId, entityId => transforms.GetReadonly(entityId));
        var frozenSkeletons = CreaturesIn(session, -1).Where(session.Skeletons.IsSkeleton).ToList();

        MovePlayerTo(session, new Vector3Int(Neighborhoods.OriginOf(1) + 200, 5, (int)MapLayer.Ground));
        RunFrames(session, 60);

        var actionLocks = session.Ecs.ComponentManager.GetPackedPool<ActionLockComponent>();
        var survivors = promoted.Where(session.Ecs.EntityManager.EntityExists).ToList();
        Assert.IsNotEmpty(survivors);
        Assert.IsTrue(survivors.All(entityId => !session.Skeletons.IsSkeleton(entityId) && actionLocks.Has(entityId)));
        Assert.IsNotEmpty(frozenSkeletons);
        Assert.IsTrue(frozenSkeletons.All(session.Skeletons.IsSkeleton), "Neighborhood -1 fell to Beyond, not simulated -- its skeletons stay unbuilt.");
        Assert.IsTrue(survivors.All(entityId => transforms.GetReadonly(entityId).Size == placements[entityId].Size));
    }

#if DEBUG
    private sealed class ProbeSystem(Action probe) : ISystem
    {
        public byte StripeCount => 1;

        public void Update(EngineTime time, byte stripeIndex) => probe();
    }

    [TestMethod]
    public void Guard_TheSimulationTouchingAnUnbuiltSkeleton_Throws()
    {
        var session = BuildSession();
        var goblinId = FirstSkeletonOf(session, Goblin.Id);
        var actionLocks = session.Ecs.ComponentManager.GetPackedPool<ActionLockComponent>();
        session.Ecs.SystemManager.Register(new ProbeSystem(() => actionLocks.Has(goblinId)));

        Assert.ThrowsExactly<InvalidOperationException>(() => RunFrames(session, 1));
    }

    [TestMethod]
    public void Guard_ReadsBetweenFramesAndOfWhatASkeletonHolds_AreAllowed()
    {
        var session = BuildSession();
        var goblinId = FirstSkeletonOf(session, Goblin.Id);
        var transforms = session.Ecs.ComponentManager.GetDirectPool<TransformComponent>();
        session.Ecs.SystemManager.Register(new ProbeSystem(() => transforms.GetReadonly(goblinId)));

        RunFrames(session, 1);

        Assert.IsFalse(session.Ecs.ComponentManager.GetPackedPool<ActionLockComponent>().Has(goblinId));
    }
#endif
}
