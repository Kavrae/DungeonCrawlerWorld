using Engine.Math;
using Engine.Settings;
using Game.Blueprints.Objects;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Modules.Auras;
using Game.Modules.Auras.Components;
using Game.Spawning;
using Game.World;

namespace Tests.GameTests.Modules.Auras;

[TestClass]
public class AuraStressTestTests
{
    [TestMethod]
    public void WithTheBenchmarkOff_NoStressAuraIsRegistered_AndACreatureRadiatesNothing()
    {
        var result = Build(creaturePercent: null);
        var goblin = SpawnGoblin(result, 5);

        Assert.IsFalse(result.Context.Auras.TryGetId(AuraStressTest.GuidOf(0, 0), out _));
        Assert.IsFalse(result.EcsContext.ComponentManager.GetMultiPool<AuraSourceComponent>().Has(goblin));
    }

    [TestMethod]
    public void AtOneHundredPercent_EveryStressAuraIsRegistered_AndABuiltCreatureRadiatesOneOfEachSize()
    {
        var result = Build(creaturePercent: 100);
        var goblin = SpawnGoblin(result, 5);

        for (var index = 0; index < AuraStressTest.AuraCount; index++)
        {
            Assert.IsTrue(result.Context.Auras.TryGetId(AuraStressTest.GuidOf(index / 3, index % 3), out _), $"Stress aura {index} is registered.");
        }

        CollectionAssert.AreEqual(new byte[] { 3, 8, 15 }, SourceSizesOf(result, goblin));
    }

    [TestMethod]
    public void AThingWithNoRace_RadiatesNoStressAura()
    {
        var result = Build(creaturePercent: 100);
        var chest = result.Factory.Spawn(new SpawnRequest(result.Context.Definitions.GetId(TreasureChest.Id), 10, 10));
        result.Factory.Skeletons.EnsureBuilt(chest);

        Assert.IsFalse(result.EcsContext.ComponentManager.GetMultiPool<AuraSourceComponent>().Has(chest));
    }

    [TestMethod]
    public void TheSizePercent_ScalesEveryReach()
    {
        var result = Build(creaturePercent: 100, sizePercent: 200);
        var goblin = SpawnGoblin(result, 5);

        CollectionAssert.AreEqual(new byte[] { 6, 16, 30 }, SourceSizesOf(result, goblin));
    }

    [TestMethod]
    public void TheSameSpawnSeed_PicksTheSameAuras()
    {
        var first = Build(creaturePercent: 100);
        var second = Build(creaturePercent: 100);

        CollectionAssert.AreEqual(SourceGuidsOf(first, SpawnGoblin(first, 7)), SourceGuidsOf(second, SpawnGoblin(second, 7)));
    }

    private static GameBuildPassResult Build(int? creaturePercent, int? sizePercent = null)
    {
        List<string> arguments = [];
        if (creaturePercent is { } percent)
        {
            arguments.Add($"--setting=AurasModule.StressAuraCreaturePercent={percent}");
        }

        if (sizePercent is { } size)
        {
            arguments.Add($"--setting=AurasModule.StressAuraSizePercent={size}");
        }

        return GameBuildPass.Run(GameBootstrapper.BuiltInModules(), [], new Map(new Vector3Int(32, 32, 3)), TestContent.Layers, 0, [new CommandLineSettingsSource([.. arguments])], 100, 50);
    }

    private static int SpawnGoblin(GameBuildPassResult result, uint seed)
    {
        var goblin = result.Factory.Spawn(new SpawnRequest(result.Context.Definitions.GetId(Goblin.Id), 5, 5) { Seed = seed });
        result.Factory.Skeletons.EnsureBuilt(goblin);
        return goblin;
    }

    private static byte[] SourceSizesOf(GameBuildPassResult result, int entityId)
    {
        var sources = result.EcsContext.ComponentManager.GetMultiPool<AuraSourceComponent>();
        List<byte> sizes = [];
        for (var denseIndex = sources.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = sources.GetNextDenseIndex(denseIndex))
        {
            sizes.Add(sources.GetReadonlyByDenseIndex(denseIndex).Size);
        }

        return [.. sizes.Order()];
    }

    private static Guid[] SourceGuidsOf(GameBuildPassResult result, int entityId)
    {
        var sources = result.EcsContext.ComponentManager.GetMultiPool<AuraSourceComponent>();
        List<Guid> guids = [];
        for (var denseIndex = sources.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = sources.GetNextDenseIndex(denseIndex))
        {
            guids.Add(result.Context.Auras.Get(sources.GetReadonlyByDenseIndex(denseIndex).AuraId).Id);
        }

        return [.. guids.Order()];
    }
}
