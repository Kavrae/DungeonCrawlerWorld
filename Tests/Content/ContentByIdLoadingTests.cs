using Engine.Bootstrap;
using Engine.Content;
using Engine.Math;
using Engine.Tiles;
using Game;
using Game.Blueprints;
using Game.Bootstrap;
using Game.Content;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Game.Modules.Lootboxes;
using Game.World;

namespace Tests.Content;

/// <summary>A game session loading its actions and items by id: nothing registered until asked for, one instance per Guid, whole-catalog questions answered from summaries, and every plan naming what its spawns read.</summary>
[TestClass]
public sealed class ContentByIdLoadingTests
{
    private static readonly ContentTypeRegistry Types = EcsBuilder.Declare(GameBootstrapper.BuiltInModules()).ContentTypes;

    private static GameBuildPassResult BuildById(MapBounds bounds, ContentLayers? layers = null) =>
        GameBuildPass.Run(GameBootstrapper.BuiltInModules(), [], new Map(bounds), layers ?? TestContent.Layers, sessionSeed: 1, [], initialEntityCapacity: 5000, initialComponentCapacity: 5000, new UniqueNumberAllocator(1, 1, 24), loadsContentById: true);

    private static TestMapBuilder BuilderFor(GameBuildPassResult pass)
    {
        var context = pass.Context;
        return new TestMapBuilder(pass.EcsContext.EntityManager, pass.Factory, context.Terrain, context.Auras, context.Definitions, context.Areas, context.UniqueEntities);
    }

    private static bool IsRegistered(ActionCatalog actions, Guid id) => actions.Definitions.Any(action => action.Id == id);

    [TestMethod]
    public void ASession_RegistersAnActionOnlyWhenItIsAskedFor_AsOneInstance_CountingTheLoadNoPlanNamed()
    {
        var pass = BuildById(new MapBounds(0, 0, 40, 40, 3));
        var actions = pass.Context.Actions;
        var loader = pass.Context.ContentCatalogLoader;
        var missedBefore = loader.MissedPlanLoads;

        Assert.IsFalse(IsRegistered(actions, BuiltInActionIds.Fireball));
        Assert.IsLessThan(pass.Context.Content.ActionSummaries.Count, actions.Count);

        var fireball = actions.Get(BuiltInActionIds.Fireball);

        Assert.IsTrue(IsRegistered(actions, BuiltInActionIds.Fireball));
        Assert.AreSame(fireball, actions.Get(BuiltInActionIds.Fireball));
        Assert.AreEqual(missedBefore + 1, loader.MissedPlanLoads);
    }

    [TestMethod]
    public void ARecordDecodedFromManyThreadsAtOnce_IsOneInstance()
    {
        var content = GameContent.Open(TestContent.Layers, Types);
        var loaded = new ActionDefinition[32];

        Parallel.For(0, loaded.Length, index => content.TryLoadAction(BuiltInActionIds.MagicMissile, out loaded[index]));

        Assert.IsTrue(loaded.All(action => ReferenceEquals(action, loaded[0])));
        Assert.AreEqual(1, content.LoadedCounts.Actions);
    }

    [TestMethod]
    public void TheSummaries_HoldEveryRecordLoadedOrNot_InGuidOrder_AndAgreeWithTheDefinitions()
    {
        var pass = BuildById(new MapBounds(0, 0, 40, 40, 3));
        var items = pass.Context.Items;
        var content = GameContent.Load(TestContent.Layers, Types);

        var summaries = items.Summaries;

        CollectionAssert.AreEqual(content.Items.Select(item => item.Id).Order().ToArray(), summaries.Select(summary => summary.Id).ToArray());
        foreach (var item in content.Items)
        {
            var summary = summaries.Single(each => each.Id == item.Id);
            Assert.AreEqual((item.Name, item.SpriteName, item.CanTrade, item.Contents is not null), (summary.Name, summary.SpriteName, summary.CanTrade, summary.HasContents), item.Name);
            Assert.AreEqual(item.DeclaredTags, summary.DeclaredTags, item.Name);
        }
    }

    [TestMethod]
    public void ALootBoxRoll_PicksFromTheWholeCatalog_TheSameLoadedByIdAsAllAtOnce()
    {
        var byId = BuildById(new MapBounds(0, 0, 40, 40, 3)).Context.Items;
        var eager = new ItemCatalog();
        foreach (var item in TestContent.Items)
        {
            eager.Register(item);
        }

        for (var seed = 1UL; seed <= 50; seed++)
        {
            CollectionAssert.AreEqual(
                RandomSingleStackContents.Instance.Roll(new SeededRandom(seed), eager).ToArray(),
                RandomSingleStackContents.Instance.Roll(new SeededRandom(seed), byId).ToArray(),
                $"seed {seed}");
        }
    }

    [TestMethod]
    public void ANeighborhoodPlan_NamesWhatItsSpawnsRead_DecodedOnTheWorker_AndRegisteredWhenItSpawns()
    {
        var pass = BuildById(new MapBounds(0, 0, 200, 200, 3));
        var builder = BuilderFor(pass);
        var content = pass.Context.Content;

        var plan = Task.Run(() => builder.Plan(pass.World.Map, new NeighborhoodRecord(0, 0, seed: 42), populationSeed: 7)).GetAwaiter().GetResult();

        Assert.IsNotEmpty(plan.Content.ActionIds);
        Assert.IsNotEmpty(plan.Content.ItemIds);
        Assert.IsTrue(plan.Content.ActionIds.All(id => content.IsDecoded(GameContent.ActionsTable, id)));
        Assert.IsTrue(plan.Content.ItemIds.All(id => content.IsDecoded(GameContent.ItemsTable, id)));
        Assert.IsFalse(plan.Content.ActionIds.All(id => IsRegistered(pass.Context.Actions, id)), "Precondition: planning registers nothing.");

        var missedBefore = pass.Context.ContentCatalogLoader.MissedPlanLoads;
        _ = builder.Spawn(plan).First();

        Assert.IsTrue(plan.Content.ActionIds.All(id => IsRegistered(pass.Context.Actions, id)));
        Assert.IsTrue(plan.Content.ItemIds.All(id => pass.Context.Items.Definitions.Any(item => item.Id == id)));
        Assert.AreEqual(missedBefore, pass.Context.ContentCatalogLoader.MissedPlanLoads);
    }

    [TestMethod]
    public void ASaveRecordThatFailsItsChecks_IsLeftOutAndReported_WhenItIsFirstAskedFor()
    {
        var toxicAura = TestContent.Action(BuiltInActionIds.ToxicAura);
        var broken = toxicAura with
        {
            Id = Guid.NewGuid(),
            Name = "Broken",
            Activator = (Game.Modules.Actions.Activators.DirectAction)toxicAura.Activator with { Targeting = new TargetingSpec(TargetShape.SingleTarget, MaximumRange: 4) },
        };
        var save = ContentLayer.FromText("Save Test", ContentLayerKind.Save, new Dictionary<string, string>
        {
            [GameContent.ActionsFileName] = ContentTable.Header(GameContent.ActionsTable) + "\n" + ContentTable.WriteLine(broken, Types) + "\n",
        });
        var pass = BuildById(new MapBounds(0, 0, 40, 40, 3), TestContent.Layers.With(save));

        Assert.IsFalse(pass.Context.Actions.TryGet(broken.Id, out _));
        Assert.IsFalse(pass.Context.Actions.TryGet(broken.Id, out _));

        var failure = Assert.ContainsSingle(pass.Context.ContentFailures);
        Assert.AreEqual(save.Name, failure.Layer);
        StringAssert.Contains(failure.Reason, "is a toggle but is not Self-targeted");
    }

    [TestMethod]
    public void ABrokenMasterOrModRecord_FailsTheBuiltInTrial_BeforeAnyGameIsBuilt()
    {
        var mod = ContentLayer.FromText("Mod", ContentLayerKind.Mod, new Dictionary<string, string>
        {
            [GameContent.ItemsFileName] = ContentTable.Header(GameContent.ItemsTable) + "\n{ not json\n",
        });
        var modsDirectory = Directory.CreateTempSubdirectory();
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => ModValidation.Validate(modsDirectory.FullName, TestContent.Layers.With(mod), []));
        }
        finally
        {
            modsDirectory.Delete();
        }
    }

    [TestMethod]
    public void TheSummaries_AreBuiltOnce_AndAgainOnlyWhenARegistrationChangesThem()
    {
        var items = BuildById(new MapBounds(0, 0, 40, 40, 3)).Context.Items;

        var first = items.Summaries;

        Assert.AreSame(first, items.Summaries);
        items.Get(BuiltInItemIds.ManaPotion);
        var afterLoading = items.Summaries;
        CollectionAssert.AreEqual(first.ToArray(), afterLoading.ToArray());

        items.Register(TestContent.Item(BuiltInItemIds.ManaPotion) with { Id = Guid.NewGuid(), Name = "Registered beside the content" });
        Assert.Contains("Registered beside the content", items.Summaries.Select(summary => summary.Name));
    }
}
