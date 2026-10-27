using Engine.Math;
using Engine.Tiles;
using Game;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Effects;
using Game.Floors;
using Game.Modules.Burning;
using Game.Modules.Core.Components;
using Game.Modules.NpcBehavior;
using Game.Modules.Poison;
using Game.Spawning;
using Game.Terrain;
using Game.World;

namespace Tests.Floors;

/// <summary>A seeded session generates the same neighborhoods whatever is played before them and whatever order they are first reached in.</summary>
[TestClass]
public sealed class WorldGenerationIndependenceTests
{
    private const int SessionSeed = 9;

    private static readonly MapBounds ThreeNeighborhoodStrip = new(-1024, 0, 2048, 24, 3);

    private static readonly (int CellX, int CellY)[] StripCells = [(-1, 0), (0, 0), (1, 0)];

    private static readonly RandomStreamKey[] GameplayStreams =
        [EffectServices.RandomStream, BurningModule.TickRandomStream, PoisonModule.TickRandomStream, NpcBehaviorModule.BehaviorRandomStream];

    private sealed record PlanFingerprint(
        int LayoutSeed,
        List<(TerrainCell Ground, TerrainCell UnderGround)> Terrain,
        List<SpawnRequest> Spawns,
        List<(Guid Area, ulong Seed, int OriginX, int OriginY)> Areas);

    private static GameBuildPassResult BuildStrip() =>
        BuiltInTestModules.Build(new Map(ThreeNeighborhoodStrip), SessionSeed, initialEntityCapacity: 5000, initialComponentCapacity: 5000);

    private static void PlayUnrelatedGameplay(GameBuildPassResult pass)
    {
        foreach (var key in GameplayStreams)
        {
            var stream = pass.Context.RandomStreams.Get(key);
            for (var roll = 0; roll < 5_000; roll++)
            {
                stream.Next(0, 1_000);
            }
        }

        var goblin = pass.Context.Definitions.GetId(Goblin.Id);
        for (var index = 0; index < 50; index++)
        {
            var entityId = pass.Factory.Spawn(new SpawnRequest(goblin, index * 3, 5) { Layer = MapLayer.Ground });
            if (entityId != EntityFactory.NoEntity)
            {
                pass.EcsContext.EntityManager.DestroyEntity(entityId);
            }
        }
    }

    private static Dictionary<(int CellX, int CellY), PlanFingerprint> PlanInOrder(GameBuildPassResult pass, NeighborhoodRecords records, IEnumerable<(int CellX, int CellY)> cells)
    {
        var context = pass.Context;
        var builder = new TestMapBuilder(pass.EcsContext.EntityManager, pass.Factory, context.Terrain, context.Auras, context.Definitions, context.Areas, context.UniqueEntities);
        var plans = new Dictionary<(int CellX, int CellY), PlanFingerprint>();
        foreach (var (cellX, cellY) in cells)
        {
            var record = records.GetOrCreate(cellX, cellY);
            plans.Add((cellX, cellY), Fingerprint(builder.Plan(pass.World.Map, record, record.NextPopulationSeed())));
        }

        return plans;
    }

    private static PlanFingerprint Fingerprint(NeighborhoodPlan plan)
    {
        var layout = plan.Layout;
        var terrain = new List<(TerrainCell, TerrainCell)>();
        for (var y = layout.MinY; y < layout.MaxY; y++)
        {
            for (var x = layout.MinX; x < layout.MaxX; x++)
            {
                terrain.Add((layout.GetTerrain(x, y, TerrainLayer.Ground), layout.GetTerrain(x, y, TerrainLayer.UnderGround)));
            }
        }

        var areas = plan.Areas.Instances.Select(static instance => (instance.Definition.Id, instance.Seed, instance.OriginX, instance.OriginY)).ToList();
        return new PlanFingerprint(layout.LayoutSeed, terrain, [.. plan.Spawns], areas);
    }

    private static void AssertSamePlan(PlanFingerprint expected, PlanFingerprint actual, (int CellX, int CellY) cell)
    {
        Assert.AreEqual(expected.LayoutSeed, actual.LayoutSeed, $"Layout seed of {cell}.");
        CollectionAssert.AreEqual(expected.Terrain, actual.Terrain, $"Terrain of {cell}.");
        CollectionAssert.AreEqual(expected.Spawns, actual.Spawns, $"Spawns of {cell}.");
        CollectionAssert.AreEqual(expected.Areas, actual.Areas, $"Areas of {cell}.");
    }

    [TestMethod]
    public void NeighborhoodPlans_AreTheSame_AfterUnrelatedGameplay_AndReachedInAnotherOrder()
    {
        var quiet = BuildStrip();
        var played = BuildStrip();
        PlayUnrelatedGameplay(played);

        var quietPlans = PlanInOrder(quiet, new NeighborhoodRecords(SessionSeed, FloorBuilder.FirstFloorNumber), StripCells);
        var playedPlans = PlanInOrder(played, new NeighborhoodRecords(SessionSeed, FloorBuilder.FirstFloorNumber), StripCells.Reverse());

        foreach (var cell in StripCells)
        {
            AssertSamePlan(quietPlans[cell], playedPlans[cell], cell);
        }

        Assert.IsNotEmpty(quietPlans[(0, 0)].Spawns, "Precondition: the plans spawn something to compare.");
    }

    [TestMethod]
    public void TheSameCoordinate_OnAnotherFloor_IsAnotherNeighborhood()
    {
        var pass = BuildStrip();

        var firstFloor = PlanInOrder(pass, new NeighborhoodRecords(SessionSeed, FloorBuilder.FirstFloorNumber), [(1, 0)])[(1, 0)];
        var secondFloor = PlanInOrder(pass, new NeighborhoodRecords(SessionSeed, FloorBuilder.FirstFloorNumber + 1), [(1, 0)])[(1, 0)];

        Assert.AreNotEqual(firstFloor.LayoutSeed, secondFloor.LayoutSeed);
        CollectionAssert.AreNotEqual(firstFloor.Terrain, secondFloor.Terrain);
    }
}
