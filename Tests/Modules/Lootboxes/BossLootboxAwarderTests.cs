using Engine.Math;
using Game.Admin;
using Game.Blueprints;
using Game.Blueprints.Composites;
using Game.Blueprints.NPCs.Generic;
using Game.Blueprints.Parts;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Lootboxes;
using Game.Resources;
using Game.Spawning;
using Game.World;

namespace Tests.Modules.Lootboxes;

/// <summary>A slain boss's loot box, through the real game build: the player's killing blow grants it, nothing else does, and it's read from what the boss was built from.</summary>
[TestClass]
public sealed class BossLootboxAwarderTests
{
    private static readonly LootboxKind BronzeBossBox = new(LootboxTypes.Boss.Id, LootboxRarity.Bronze);

    private sealed record Setup(GameSession Result, int PlayerEntityId)
    {
        public void Kill(int entityId, int killerEntityId)
        {
            var componentManager = Result.EcsContext.ComponentManager;
            var source = ActionSource.FromEntity(componentManager, Result.EcsContext.EntityManager.Keys, killerEntityId, Result.Catalogs.Definitions);
            Result.EcsContext.EventBus.Publish(new EntityDiedEvent(entityId, source));
            Result.EcsContext.EventBus.DispatchBuffered<EntityDiedEvent>();
        }

        public int CountOfPlayerBoxes(LootboxKind kind)
        {
            var stacks = new List<InventoryItemStackComponent>();
            InventoryQueries.CopyStacksForEntity(Result.EcsContext.ComponentManager.GetMultiPool<InventoryItemStackComponent>(), PlayerEntityId, stacks);
            return stacks.Where(stack => stack.ItemDefinitionId == LootboxCatalog.ItemIdFor(kind)).Sum(stack => stack.Quantity);
        }
    }

    private static Setup Build()
    {
        var map = new Map(new Vector3Int(20, 20, 3));
        var result = GameBootstrapper.Build(ValidatedMods.None, map, new MathUtility(new Random(1)), initialEntityCapacity: 1_000, initialComponentCapacity: 100, crawlerNumbers: new UniqueNumberAllocator(1, 1, 24), runtimeSpawnSeed: 7);
        var playerEntityId = result.Internals.Factory.Spawn(TestDummyBlueprint.Id, 1, 1);
        result.World.PlayerEntityId = playerEntityId;
        return new Setup(result, playerEntityId);
    }

    [TestMethod]
    public void PlayerKillsTheForeman_GrantsTheBossTraitsBronzeBossBox()
    {
        var setup = Build();
        var foreman = setup.Result.Internals.Factory.Spawn(GoblinForeman.Id, 5, 5);

        setup.Kill(foreman, setup.PlayerEntityId);

        Assert.AreEqual(1, setup.CountOfPlayerBoxes(BronzeBossBox));
    }

    [TestMethod]
    public void AnotherEntityKillsTheForeman_GrantsNothing()
    {
        var setup = Build();
        var foreman = setup.Result.Internals.Factory.Spawn(GoblinForeman.Id, 5, 5);
        var goblin = setup.Result.Internals.Factory.Spawn(Goblin.Id, 7, 5);

        setup.Kill(foreman, goblin);

        Assert.AreEqual(0, setup.CountOfPlayerBoxes(BronzeBossBox));
    }

    [TestMethod]
    public void PlayerKillsAnOrdinaryGoblin_GrantsNothing()
    {
        var setup = Build();
        var goblin = setup.Result.Internals.Factory.Spawn(Goblin.Id, 5, 5);

        setup.Kill(goblin, setup.PlayerEntityId);

        Assert.AreEqual(0, setup.CountOfPlayerBoxes(BronzeBossBox));
    }

    [TestMethod]
    public void BossAppliedToAGoblin_MakesItPayOutTheBossBox()
    {
        var setup = Build();
        var goblin = setup.Result.Internals.Factory.Spawn(Goblin.Id, 5, 5);
        setup.Result.Internals.Factory.Apply(goblin, setup.Result.Catalogs.Definitions.GetId(Boss.Id));

        setup.Kill(goblin, setup.PlayerEntityId);

        Assert.AreEqual(1, setup.CountOfPlayerBoxes(BronzeBossBox));
    }

    [TestMethod]
    public void BossAppliedThroughAdminToACrawlerGoblin_KilledByRealDamage_PaysOutTheBossBox()
    {
        var setup = Build();
        var result = setup.Result;
        var componentManager = result.EcsContext.ComponentManager;
        var goblin = result.Internals.Factory.Spawn(new SpawnRequest(result.Catalogs.Definitions.GetId(Goblin.Id), 5, 5) { Crawler = true });
        new BlueprintAdminCommands(result.Internals.Factory, result.Catalogs.Definitions).Apply(goblin, result.Catalogs.Definitions.GetId(Boss.Id));
        var source = ActionSource.FromEntity(componentManager, result.EcsContext.EntityManager.Keys, setup.PlayerEntityId, result.Catalogs.Definitions);
        var deadEntities = componentManager.GetPackedPool<Game.Modules.Death.Components.DeadComponent>();

        for (var hit = 0; hit < 50 && !deadEntities.Has(goblin); hit++)
        {
            Game.Modules.Health.HealthDamage.Apply(
                componentManager.GetPackedPool<Game.Modules.Health.Components.SimpleHealthComponent>(), result.EcsContext.EventBus, goblin, 1000, source, result.World, "Test", now: 0,
                componentManager.GetMultiPool<Game.Modules.StatModifiers.Components.StatModifierComponent>(), Game.Modules.Health.EntityBodyParts.For(componentManager, result.Catalogs.Definitions),
                new MathUtility(new Random(1)), deadEntities, new Game.World.FloatingTextFeed(result.EcsContext.EventBus, componentManager.GetDirectPool<Game.Modules.ProcessingTier.Components.ProcessingTierComponent>(), componentManager.GetDirectPool<Game.Modules.Core.Components.TransformComponent>()),
                ResourceLossCategory.Direct, targetMode: Game.Modules.Health.BodyPartTargetMode.All);
            result.EcsContext.EventBus.DispatchBuffered<EntityDiedEvent>();
        }

        Assert.IsTrue(deadEntities.Has(goblin), "Sanity check: the goblin died.");
        Assert.AreEqual(1, setup.CountOfPlayerBoxes(BronzeBossBox));
    }

    [TestMethod]
    public void TheMostRecentlyAppliedPartsBox_WinsOverTheSpawnBlueprints()
    {
        var setup = Build();
        var questBox = new LootboxReward(LootboxTypes.Quest.Id, LootboxRarity.Gold);
        var questBossPart = setup.Result.Catalogs.Definitions.Register(new BlueprintDefinition(Guid.NewGuid(), "Quest Boss") { Build = static _ => { }, Lootbox = questBox });
        var foreman = setup.Result.Internals.Factory.Spawn(GoblinForeman.Id, 5, 5);
        setup.Result.Internals.Factory.Apply(foreman, questBossPart);

        setup.Kill(foreman, setup.PlayerEntityId);

        Assert.AreEqual(1, setup.CountOfPlayerBoxes(questBox.Kind));
        Assert.AreEqual(0, setup.CountOfPlayerBoxes(BronzeBossBox));
    }

    [TestMethod]
    public void ABossThatWasNeverBuilt_StillPaysOut()
    {
        var setup = Build();
        var componentManager = setup.Result.EcsContext.ComponentManager;
        var skeleton = setup.Result.EcsContext.EntityManager.CreateEntity();
        setup.Result.Internals.Factory.BuildSkeleton(componentManager, skeleton, setup.Result.Catalogs.Definitions.GetId(GoblinForeman.Id), seed: 3);

        setup.Kill(skeleton, setup.PlayerEntityId);

        Assert.AreEqual(1, setup.CountOfPlayerBoxes(BronzeBossBox));
        Assert.IsFalse(componentManager.GetMultiPool<AppliedBlueprintComponent>().Has(skeleton));
    }

    [TestMethod]
    public void TheForemansResolvedBlueprint_DeclaresTheBossBox_ThroughItsBossInclude()
    {
        var definitions = Tests.Blueprints.BlueprintTestContext.Definitions;

        Assert.AreEqual(new LootboxReward(LootboxTypes.Boss.Id, LootboxRarity.Bronze), definitions.Resolve(definitions.GetId(GoblinForeman.Id)).Lootbox);
        Assert.IsNull(definitions.Resolve(definitions.GetId(Goblin.Id)).Lootbox);
    }
}
