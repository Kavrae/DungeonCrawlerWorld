using Engine.Math;
using Game.Blueprints;
using Game.Blueprints.Composites;
using Game.Blueprints.NPCs.Generic;
using Game.Blueprints.Objects;
using Game.Blueprints.Parts;
using Game.Blueprints.Classes;
using Game.Blueprints.Races;
using Engine.ECS.Systems;
using Game.Bootstrap;
using Game.Spawning;
using Game.Modules.Class.Components;
using Game.Modules.Crawler.Components;
using Game.Modules.Core.Components;
using Game.World;
using Tests.Blueprints;

namespace Tests.Spawning;

[TestClass]
public sealed class EntityFactoryTests
{
    private static (GameSession Result, Game.World.World World) Bootstrap(bool withCrawlerNumbers = true, int crawlerNumberBits = 24)
    {
        var map = new Map(new Vector3Int(20, 20, 3));
        var mathUtility = new MathUtility(new Random(1));
        var crawlerNumbers = withCrawlerNumbers ? new UniqueNumberAllocator(1, 1, crawlerNumberBits) : null;
        var result = GameBootstrapper.Build(ValidatedMods.None, map, mathUtility, initialEntityCapacity: 1_000, initialComponentCapacity: 100, crawlerNumbers: crawlerNumbers, runtimeSpawnSeed: 7);
        return (result, result.World);
    }

    /// <summary>Every blueprint the game spawns declares a whole appearance -- one that didn't would fail at its first spawn instead.</summary>
    [TestMethod]
    public void EverySpawnedBuiltIn_IsSpawnable()
    {
        var definitions = BlueprintTestContext.Definitions;
        Guid[] spawned =
        [
            Goblin.Id, Fairy.Id, Ghost.Id, GoblinEngineer.Id, GoblinForeman.Id, StationaryFairyEngineer.Id, GoblinFairy.Id,
            GoblinEngineerTank.Id, TinyGoblin.Id, PhasingFairy.Id, LongDescriptionGoblin.Id, GeneralShop.Id, PotionShop.Id,
            TreasureChest.Id, TestDummyBlueprint.Id, Player.Id,
        ];

        foreach (var id in spawned)
        {
            var resolved = definitions.Resolve(definitions.GetId(id));
            Assert.IsTrue(resolved.IsSpawnable, $"{resolved.Definition.Name} is missing {string.Join(", ", resolved.Appearance.Missing)}.");
        }
    }

    [TestMethod]
    public void Spawn_ATraitOnItsOwn_IsRefusedBeforeAnEntityIsCreated()
    {
        var (result, _) = Bootstrap();
        var bossId = result.Catalogs.Definitions.GetId(Boss.Id);
        var entitiesBefore = result.EcsContext.EntityManager.CreateEntity();
        result.EcsContext.EntityManager.DestroyEntity(entitiesBefore);

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => result.Internals.Factory.Spawn(new SpawnRequest(bossId, 5, 5)));

        StringAssert.Contains(error.Message, "Boss");
        Assert.AreEqual(entitiesBefore, result.EcsContext.EntityManager.CreateEntity(), "No entity id was taken for the refused spawn.");
    }

    [TestMethod]
    public void Spawn_TheDummy_IsBornWithNoDisplayComponentsOfItsOwn()
    {
        var (result, _) = Bootstrap();
        var components = result.EcsContext.ComponentManager;

        var entityId = result.Internals.Factory.Spawn(TestDummyBlueprint.Id, 5, 5);

        Assert.IsFalse(components.GetPackedPool<DisplayTextComponent>().Has(entityId));
        Assert.IsFalse(components.GetPackedPool<GlyphComponent>().Has(entityId));
        Assert.IsFalse(components.GetPackedPool<SpriteComponent>().Has(entityId));
        Assert.AreEqual(TestDummyBlueprint.Name, Game.Spawning.EntityNaming.For(components, result.Catalogs.Definitions).NameOf(entityId));
    }

    private static TransformComponent TransformOf(GameSession result, int entityId) =>
        result.EcsContext.ComponentManager.GetDirectPool<TransformComponent>().GetReadonly(entityId);

    [TestMethod]
    public void Spawn_WithNoLayerNamed_LandsOnTheBlueprintsOwnLayer()
    {
        var (result, _) = Bootstrap();

        var fairy = result.Internals.Factory.Spawn(Fairy.Id, 5, 5);
        var goblinFairy = result.Internals.Factory.Spawn(GoblinFairy.Id, 6, 6);

        Assert.AreEqual((int)MapLayer.Flying, TransformOf(result, fairy).Position.Z);
        Assert.AreEqual((int)MapLayer.Ground, TransformOf(result, goblinFairy).Position.Z, "A hybrid spawns where its first race does.");
    }

    [TestMethod]
    public void Spawn_ANamedLayer_WinsOverTheBlueprints()
    {
        var (result, _) = Bootstrap();

        var entityId = result.Internals.Factory.Spawn(new SpawnRequest(result.Catalogs.Definitions.GetId(Fairy.Id), 5, 5) { Layer = MapLayer.Ground });

        Assert.AreEqual((int)MapLayer.Ground, TransformOf(result, entityId).Position.Z);
    }

    [TestMethod]
    public void Spawn_WithNoSizeNamed_TakesTheBlueprintsFootprint_AndANamedSizeWins()
    {
        var (result, _) = Bootstrap();
        var foremanId = result.Catalogs.Definitions.GetId(GoblinForeman.Id);

        var byDefault = result.Internals.Factory.Spawn(new SpawnRequest(foremanId, 2, 2));
        var resized = result.Internals.Factory.Spawn(new SpawnRequest(foremanId, 8, 8) { Size = new Vector2Byte(3, 3) });

        Assert.AreEqual(new Vector2Byte(2, 2), TransformOf(result, byDefault).Size);
        Assert.AreEqual(new Vector2Byte(3, 3), TransformOf(result, resized).Size);
    }

    [TestMethod]
    public void Spawn_AsACrawler_IsGivenACrawlerNumber()
    {
        var (result, _) = Bootstrap();
        var crawlers = result.EcsContext.ComponentManager.GetPackedPool<CrawlerComponent>();

        var crawler = result.Internals.Factory.Spawn(new SpawnRequest(result.Catalogs.Definitions.GetId(Goblin.Id), 5, 5) { Crawler = true });
        var ordinary = result.Internals.Factory.Spawn(Goblin.Id, 7, 7);

        Assert.IsTrue(crawlers.Has(crawler));
        Assert.IsFalse(crawlers.Has(ordinary));
    }

    [TestMethod]
    public void Spawn_AsACrawler_OnceTheCrawlerNumbersRunOut_IsAPlainNpc()
    {
        var (result, _) = Bootstrap(crawlerNumberBits: 2);
        var crawlers = result.EcsContext.ComponentManager.GetPackedPool<CrawlerComponent>();
        var spawnRecords = result.EcsContext.ComponentManager.GetDirectPool<SpawnRecordComponent>();
        var goblinId = result.Catalogs.Definitions.GetId(Goblin.Id);

        var numbered = Enumerable.Range(0, 4).Select(index => result.Internals.Factory.Spawn(new SpawnRequest(goblinId, 2 + (3 * index), 2) { Crawler = true })).ToList();
        var late = result.Internals.Factory.Spawn(new SpawnRequest(goblinId, 2, 8) { Crawler = true });

        CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4 }, numbered.Select(entityId => crawlers.GetReadonly(entityId).CrawlerNumber).ToList());
        Assert.IsFalse(crawlers.Has(late));
        Assert.IsFalse(spawnRecords.GetReadonly(late).Flags.HasFlag(SpawnFlags.Crawler));
    }

    [TestMethod]
    public void Spawn_AsACrawler_WithoutTheSessionsCrawlerNumbers_IsRefused()
    {
        var (result, _) = Bootstrap(withCrawlerNumbers: false);

        Assert.ThrowsExactly<InvalidOperationException>(() => result.Internals.Factory.Spawn(new SpawnRequest(result.Catalogs.Definitions.GetId(Goblin.Id), 5, 5) { Crawler = true }));
    }

    /// <summary>A spawn that names no seed still gets its own, from a sequence that is the same every session run with the same runtime seed.</summary>
    [TestMethod]
    public void Spawn_WithNoSeedNamed_DrawsDistinctSeeds_Reproducibly()
    {
        uint[] SeedsOf((GameSession Result, Game.World.World World) session)
        {
            var records = session.Result.EcsContext.ComponentManager.GetDirectPool<SpawnRecordComponent>();
            return [.. Enumerable.Range(0, 3).Select(index => records.GetReadonly(session.Result.Internals.Factory.Spawn(Goblin.Id, 2 + (3 * index), 2)).Seed)];
        }

        var first = SeedsOf(Bootstrap());
        var second = SeedsOf(Bootstrap());

        Assert.HasCount(3, first.Distinct());
        CollectionAssert.AreEqual(first, second);
    }

    [TestMethod]
    public void Compose_TheSameIncludes_IsTheSameBlueprint_InEveryRegistry()
    {
        var (result, _) = Bootstrap();
        var other = new BlueprintRegistry();
        foreach (var definition in new[] { Goblin.Definition, Boss.Definition })
        {
            other.Register(definition);
        }

        var composed = result.Catalogs.Definitions.Compose(Goblin.Id, Boss.Id);
        var again = result.Catalogs.Definitions.Compose(Goblin.Id, Boss.Id);
        var reversed = result.Catalogs.Definitions.Compose(Boss.Id, Goblin.Id);

        Assert.AreEqual(composed, again);
        Assert.AreNotEqual(composed, reversed, "Build order is part of what a blueprint is.");
        Assert.AreEqual(result.Catalogs.Definitions.Get(composed).Id, other.Get(other.Compose(Goblin.Id, Boss.Id)).Id);
        Assert.AreEqual("Goblin + Boss", result.Catalogs.Definitions.Get(composed).Name);
    }

    /// <summary>Reads this frame's moves, the way every reader of them does.</summary>
    private sealed class MoveReader(FrameEventBuffer<EntityMovedEvent> moves) : ISystem
    {
        public List<EntityMovedEvent> Seen { get; } = [];

        public byte StripeCount => 1;

        public void Update(EngineTime time, byte stripeIndex) => Seen.AddRange(moves.Items);
    }

    /// <summary>Spawns once, on its first update -- a summon, from a system that runs after the moves were read.</summary>
    private sealed class Summoner(EntityFactory factory, ushort blueprintId) : ISystem
    {
        public int EntityId { get; private set; } = EntityFactory.NoEntity;

        public byte StripeCount => 1;

        public void Update(EngineTime time, byte stripeIndex)
        {
            if (EntityId == EntityFactory.NoEntity)
            {
                EntityId = factory.Spawn(new SpawnRequest(blueprintId, 5, 5));
            }
        }
    }

    [TestMethod]
    public void Spawn_FromASystemAfterTheMovesWereRead_ReachesTheirReadersNextFrame()
    {
        var (result, _) = Bootstrap();
        var systems = result.EcsContext.SystemManager;
        var reader = new MoveReader(result.Internals.MovedEntities);
        var summoner = new Summoner(result.Internals.Factory, result.Catalogs.Definitions.GetId(Goblin.Id));
        systems.Register(reader);
        systems.Register(summoner);
        var frameDuration = TimeSpan.FromSeconds(1.0 / 60);

        systems.Update(new EngineTime(frameDuration, frameDuration, false, 1));
        Assert.AreNotEqual(EntityFactory.NoEntity, summoner.EntityId);
        Assert.IsFalse(reader.Seen.Any(moved => moved.EntityId == summoner.EntityId), "Spawned after this frame's reader ran.");

        systems.Update(new EngineTime(frameDuration * 2, frameDuration, false, 2));
        Assert.IsTrue(reader.Seen.Any(moved => moved.EntityId == summoner.EntityId && moved.OldPosition == moved.NewPosition));
    }

    private static int SpawnGoblin(GameSession result, int x = 5, uint seed = 1) =>
        result.Internals.Factory.Spawn(new SpawnRequest(result.Catalogs.Definitions.GetId(Goblin.Id), x, 5) { Seed = seed });

    [TestMethod]
    public void Apply_AClass_GrantsIt_NamesIt_AndApplyingItAgainChangesNothing()
    {
        var (result, _) = Bootstrap();
        var components = result.EcsContext.ComponentManager;
        var goblin = SpawnGoblin(result);
        var engineerId = result.Catalogs.Definitions.GetId(Engineer.Id);
        var lockFrames = components.GetPackedPool<ActionLockComponent>();
        var before = lockFrames.GetReadonly(goblin).StandardLockFrames;

        result.Internals.Factory.Apply(goblin, engineerId);
        var afterOnce = lockFrames.GetReadonly(goblin).StandardLockFrames;
        result.Internals.Factory.Apply(goblin, engineerId);

        Assert.IsTrue(components.GetPackedPool<ClassSlotsComponent>().GetReadonly(goblin).Has(engineerId));
        Assert.IsLessThan(before, afterOnce, "Engineer shortens the lock it finds.");
        Assert.AreEqual(afterOnce, lockFrames.GetReadonly(goblin).StandardLockFrames, "A class the entity already holds is skipped.");
        StringAssert.EndsWith(Game.Spawning.EntityNaming.For(components, result.Catalogs.Definitions).NameOf(goblin), $" Goblin {Engineer.Name}");
    }

    [TestMethod]
    public void Apply_AClassToTheSpawnedEngineer_ChangesNothing()
    {
        var (result, _) = Bootstrap();
        var lockFrames = result.EcsContext.ComponentManager.GetPackedPool<ActionLockComponent>();
        var engineer = result.Internals.Factory.Spawn(new SpawnRequest(result.Catalogs.Definitions.GetId(GoblinEngineer.Id), 5, 5));
        var before = lockFrames.GetReadonly(engineer).StandardLockFrames;

        result.Internals.Factory.Apply(engineer, result.Catalogs.Definitions.GetId(Engineer.Id));

        Assert.AreEqual(before, lockFrames.GetReadonly(engineer).StandardLockFrames);
    }

    /// <summary>A part already applied is never built again, so a merge policy never merges its writes into themselves -- a trait included.</summary>
    [TestMethod]
    public void Apply_BossTwice_DoublesHealthOnce_AndNamesItBoss()
    {
        var (result, _) = Bootstrap();
        var components = result.EcsContext.ComponentManager;
        var bodyParts = Game.Modules.Health.EntityBodyParts.For(components, result.Catalogs.Definitions);
        var goblin = SpawnGoblin(result);
        var bossId = result.Catalogs.Definitions.GetId(Boss.Id);
        bodyParts.TryGetTotals(goblin, out var before, out _);

        result.Internals.Factory.Apply(goblin, bossId);
        result.Internals.Factory.Apply(goblin, bossId);

        bodyParts.TryGetTotals(goblin, out var after, out _);
        Assert.AreEqual(before * 2, after, 0.01f);
        StringAssert.EndsWith(Game.Spawning.EntityNaming.For(components, result.Catalogs.Definitions).NameOf(goblin), $" Goblin {Boss.Name}");
    }

    [TestMethod]
    public void Apply_RecordsThePartsItBuilt_InOrder_SkippingWhatTheEntitySpawnedWith()
    {
        var (result, _) = Bootstrap();
        var goblin = SpawnGoblin(result);

        result.Internals.Factory.Apply(goblin, result.Catalogs.Definitions.GetId(GoblinEngineer.Id));

        var applied = new List<AppliedBlueprintComponent>();
        result.EcsContext.ComponentManager.GetMultiPool<AppliedBlueprintComponent>().CopyAll(goblin, applied);
        CollectionAssert.AreEqual(
            new[] { (result.Catalogs.Definitions.GetId(Engineer.Id), (ushort)0), (result.Catalogs.Definitions.GetId(GoblinEngineer.Id), (ushort)1) },
            applied.OrderBy(static part => part.Order).Select(static part => (part.BlueprintId, part.Order)).ToArray(),
            "Goblin came with the entity; Engineer and GoblinEngineer's own step are what was built.");
    }

    [TestMethod]
    public void Apply_ExplicitlyNamedEntity_KeepsItsNameWhenItGainsAClass()
    {
        var (result, _) = Bootstrap();
        var dummy = result.Internals.Factory.Spawn(TestDummyBlueprint.Id, 5, 5);

        result.Internals.Factory.Apply(dummy, result.Catalogs.Definitions.GetId(Tank.Id));

        Assert.AreEqual(TestDummyBlueprint.Name, Game.Spawning.EntityNaming.For(result.EcsContext.ComponentManager, result.Catalogs.Definitions).NameOf(dummy));
    }

    [TestMethod]
    public void Apply_ATraitsActions_AreGrantedToThatEntityAlone()
    {
        var (result, _) = Bootstrap();
        var trait = new BlueprintDefinition(Guid.NewGuid(), "Toxic Trait") { Actions = [new Game.Modules.Actions.ActionGrant(Game.Modules.Actions.Definitions.Spells.ToxicStrikeAction.Id)] };
        var traitId = result.Catalogs.Definitions.Register(trait);
        var goblin = SpawnGoblin(result);
        var other = SpawnGoblin(result, x: 8);
        var actions = Game.Modules.Actions.EntityActions.For(result.EcsContext.ComponentManager, result.Catalogs.ActionCatalog, result.Catalogs.Definitions);

        result.Internals.Factory.Apply(goblin, traitId);

        Assert.IsTrue(actions.Has(goblin, Game.Modules.Actions.Definitions.Spells.ToxicStrikeAction.Id));
        Assert.IsFalse(actions.Has(other, Game.Modules.Actions.Definitions.Spells.ToxicStrikeAction.Id));
    }

    [TestMethod]
    public void Apply_Tiny_MakesTheEntityShareItsCell()
    {
        var (result, world) = Bootstrap();
        var goblin = SpawnGoblin(result);

        result.Internals.Factory.Apply(goblin, result.Catalogs.Definitions.GetId(Tiny.Id));

        Assert.IsFalse(world.IsBlocking(goblin));
    }

    /// <summary>The same blueprint applied to two entities spawned alike rolls alike -- here, a shop's randomly chosen stock.</summary>
    [TestMethod]
    public void Apply_RollsFromTheEntitysSeed_SoItIsReproducible()
    {
        var (result, _) = Bootstrap();
        var stockId = result.Catalogs.Definitions.GetId(GeneralShopStock.Id);
        var first = SpawnGoblin(result, x: 3, seed: 42);
        var second = SpawnGoblin(result, x: 9, seed: 42);
        var stacks = result.EcsContext.ComponentManager.GetMultiPool<Game.Modules.Inventory.Components.InventoryItemStackComponent>();

        result.Internals.Factory.Apply(first, stockId);
        result.Internals.Factory.Apply(second, stockId);

        static string[] Stock(Engine.ECS.Components.Stores.MultiComponentPool<Game.Modules.Inventory.Components.InventoryItemStackComponent> pool, int entityId)
        {
            var copied = new List<Game.Modules.Inventory.Components.InventoryItemStackComponent>();
            pool.CopyAll(entityId, copied);
            return [.. copied.Select(static stack => $"{stack.ItemDefinitionId} x{stack.Quantity}").Order(StringComparer.Ordinal)];
        }

        CollectionAssert.AreEqual(Stock(stacks, first), Stock(stacks, second));
    }

    [TestMethod]
    public void Compose_ThenSpawn_BuildsEveryIncludedPart()
    {
        var (result, _) = Bootstrap();
        var composed = result.Catalogs.Definitions.Compose(Goblin.Id, Engineer.Id, Boss.Id);

        var entityId = result.Internals.Factory.Spawn(new SpawnRequest(composed, 5, 5));

        StringAssert.EndsWith(Game.Spawning.EntityNaming.For(result.EcsContext.ComponentManager, result.Catalogs.Definitions).NameOf(entityId), " Goblin Engineer Boss");
    }
}
