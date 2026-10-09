using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints.Composites;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Modules.Core.Components;
using Game.Modules.Currency.Components;
using Game.Modules.Death;
using Game.Modules.Death.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement.Components;
using Game.Modules.NpcBehavior;
using Game.Modules.NpcBehavior.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Spawning;
using Game.World;

namespace Tests.Modules.NpcBehavior;

/// <summary>An NPC looting a corpse it stands on or next to, through the real module build -- corpses die through DeathSystem.</summary>
[TestClass]
public sealed class NpcCorpseLootingTests
{
    private static readonly Vector3Int LooterPosition = new(10, 10, (int)MapLayer.Ground);
    private static readonly Vector3Int AdjacentPosition = new(11, 10, (int)MapLayer.Ground);

    private sealed record Session(GameBuildPassResult Build)
    {
        public ComponentManager Components => Build.EcsContext.ComponentManager;

        public MultiComponentPool<InventoryItemStackComponent> Stacks => Components.GetMultiPool<InventoryItemStackComponent>();

        public NpcCorpseLooting Looting { get; } = new(Build.EcsContext.ComponentManager, Build.Context.Items, Build.World, Build.Context.EntityKeys, Build.World,
            new ProcessingTierQuery(Build.EcsContext.ComponentManager.GetDirectPool<ProcessingTierComponent>()));

        /// <summary>A goblin holding nothing at all, so a test decides everything it carries.</summary>
        public int SpawnEmptyGoblin(Vector3Int position)
        {
            var goblin = Build.Factory.Spawn(SpawnRequest.At(Build.Context.Definitions.GetId(Goblin.Id), position) with { Seed = 1 });
            Stacks.Remove(goblin);
            Components.GetPackedPool<CurrencyComponent>().Remove(goblin);
            return goblin;
        }

        /// <summary>A goblin killed at position by Admin damage (so nobody owns its loot), holding itemQuantity of itemId and gold.</summary>
        public int SpawnCorpse(Vector3Int position, Guid itemId, ushort itemQuantity, int gold = 0)
        {
            var corpse = SpawnEmptyGoblin(position);
            if (itemQuantity > 0)
            {
                InventoryActions.AddItem(Components, corpse, itemId, itemQuantity);
            }

            if (gold > 0)
            {
                Components.Merge(corpse, new CurrencyComponent(gold, credits: 0));
            }

            Build.EcsContext.EventBus.Publish(new EntityDiedEvent(corpse, ActionSource.Admin));
            Build.EcsContext.EventBus.DispatchBuffered<EntityDiedEvent>();
            return corpse;
        }

        public void ReserveLootFor(int corpse, int lootOwner) =>
            Components.GetPackedPool<DeadComponent>().TryUpdate(corpse, Build.Context.EntityKeys.GetKey(lootOwner),
                static (ref DeadComponent dead, Engine.ECS.Entities.EntityKey owner) => dead = dead with { LootOwnerEntityKey = owner });

        public bool TryLoot(int looter, long now) =>
            Looting.TryLootNearbyCorpse(looter, Components.GetDirectPool<TransformComponent>().GetReadonly(looter), now);

        public ushort QuantityOf(int entityId, Guid itemId) =>
            InventoryQueries.TryGetStack(Stacks, entityId, itemId, out var stack) ? stack.Quantity : (ushort)0;

        public bool IsLooted(int entityId) => Components.GetPackedPool<LootedComponent>().Has(entityId);
    }

    private static Session BuildSession()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(40, 40, 3)), new MathUtility(new Random(1)), initialEntityCapacity: 200, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(LooterPosition);
        return new Session(build);
    }

    [TestMethod]
    public void AnAdjacentCorpse_IsLootedOfEveryStackAndAllItsGold_AndMarkedLooted()
    {
        var session = BuildSession();
        var itemId = Guid.NewGuid();
        var corpse = session.SpawnCorpse(AdjacentPosition, itemId, itemQuantity: 3, gold: 7);
        var looter = session.SpawnEmptyGoblin(LooterPosition);

        Assert.IsTrue(session.TryLoot(looter, now: 0));

        Assert.AreEqual(3, session.QuantityOf(looter, itemId));
        Assert.AreEqual(0, session.Stacks.CountForEntity(corpse));
        Assert.AreEqual(7, session.Components.GetPackedPool<CurrencyComponent>().GetReadonly(looter).Gold);
        Assert.IsTrue(session.IsLooted(corpse));
    }

    [TestMethod]
    public void ACorpseUnderfoot_IsLooted()
    {
        var session = BuildSession();
        var itemId = Guid.NewGuid();
        session.SpawnCorpse(LooterPosition, itemId, itemQuantity: 2);
        var looter = session.SpawnEmptyGoblin(LooterPosition);

        Assert.IsTrue(session.TryLoot(looter, now: 0));
        Assert.AreEqual(2, session.QuantityOf(looter, itemId));
    }

    [TestMethod]
    public void LootedStacks_MergeIntoWhatTheLooterCarries()
    {
        var session = BuildSession();
        var itemId = Guid.NewGuid();
        session.SpawnCorpse(AdjacentPosition, itemId, itemQuantity: 3);
        var looter = session.SpawnEmptyGoblin(LooterPosition);
        InventoryActions.AddItem(session.Components, looter, itemId, quantity: 2);

        session.TryLoot(looter, now: 0);

        Assert.AreEqual(1, session.Stacks.CountForEntity(looter));
        Assert.AreEqual(5, session.QuantityOf(looter, itemId));
    }

    [TestMethod]
    public void AnAlreadyLootedCorpse_IsLeftAlone()
    {
        var session = BuildSession();
        var itemId = Guid.NewGuid();
        var corpse = session.SpawnCorpse(AdjacentPosition, itemId, itemQuantity: 3);
        session.Components.Merge(corpse, new LootedComponent());
        var looter = session.SpawnEmptyGoblin(LooterPosition);

        Assert.IsFalse(session.TryLoot(looter, now: 0));
        Assert.AreEqual(3, session.QuantityOf(corpse, itemId));
    }

    [TestMethod]
    public void AnEmptyCorpse_IsNeitherLootedNorMarked()
    {
        var session = BuildSession();
        var corpse = session.SpawnCorpse(AdjacentPosition, Guid.NewGuid(), itemQuantity: 0);
        var looter = session.SpawnEmptyGoblin(LooterPosition);

        Assert.IsFalse(session.TryLoot(looter, now: 0));
        Assert.IsFalse(session.IsLooted(corpse));
    }

    [TestMethod]
    public void AGoldOnlyCorpse_IsLooted()
    {
        var session = BuildSession();
        session.SpawnCorpse(AdjacentPosition, Guid.NewGuid(), itemQuantity: 0, gold: 4);
        var looter = session.SpawnEmptyGoblin(LooterPosition);

        Assert.IsTrue(session.TryLoot(looter, now: 0));
        Assert.AreEqual(4, session.Components.GetPackedPool<CurrencyComponent>().GetReadonly(looter).Gold);
    }

    [TestMethod]
    public void AFrozenCorpse_IsLeftAlone()
    {
        var session = BuildSession();
        var itemId = Guid.NewGuid();
        var corpse = session.SpawnCorpse(AdjacentPosition, itemId, itemQuantity: 3);
        session.Components.GetDirectPool<ProcessingTierComponent>().TryUpdate(corpse, static (ref ProcessingTierComponent tier) => tier = new ProcessingTierComponent(ProcessingTierLevel.Borough));
        var looter = session.SpawnEmptyGoblin(LooterPosition);

        Assert.IsFalse(session.TryLoot(looter, now: 0));
        Assert.AreEqual(3, session.QuantityOf(corpse, itemId));
    }

    [TestMethod]
    public void ACorpseReservedForSomeoneElse_IsRefused_AndEveryCorpseWaitsThirtySeconds()
    {
        var session = BuildSession();
        var itemId = Guid.NewGuid();
        var reservedCorpse = session.SpawnCorpse(AdjacentPosition, itemId, itemQuantity: 3);
        var looter = session.SpawnEmptyGoblin(LooterPosition);
        var lootOwner = session.SpawnEmptyGoblin(new Vector3Int(20, 20, (int)MapLayer.Ground));
        session.ReserveLootFor(reservedCorpse, lootOwner);

        Assert.IsFalse(session.TryLoot(looter, now: 0));
        Assert.IsFalse(session.IsLooted(reservedCorpse));
        Assert.AreEqual((uint)NpcCorpseLooting.RetryFrames, session.Components.GetPackedPool<CorpseLootRetryComponent>().GetReadonly(looter).RetryAfterFrame);

        var openCorpse = session.SpawnCorpse(new Vector3Int(9, 10, (int)MapLayer.Ground), itemId, itemQuantity: 1);
        Assert.IsFalse(session.TryLoot(looter, now: NpcCorpseLooting.RetryFrames - 1), "A refusal holds the looter off every corpse, not only the one that refused it.");
        Assert.IsFalse(session.IsLooted(openCorpse));

        Assert.IsTrue(session.TryLoot(looter, now: NpcCorpseLooting.RetryFrames));
    }

    [TestMethod]
    public void ACorpseTheLooterOwns_IsLootedDuringTheExclusiveWindow()
    {
        var session = BuildSession();
        var itemId = Guid.NewGuid();
        var corpse = session.SpawnCorpse(AdjacentPosition, itemId, itemQuantity: 3);
        var looter = session.SpawnEmptyGoblin(LooterPosition);
        session.ReserveLootFor(corpse, looter);

        Assert.IsTrue(session.TryLoot(looter, now: LootRights.ExclusiveLootFrames - 1));
    }

    [TestMethod]
    public void ALooterWithNoRoom_StillOpensTheCorpse_AndLeavesWhatDoesNotFit()
    {
        var session = BuildSession();
        var itemId = Guid.NewGuid();
        var corpse = session.SpawnCorpse(AdjacentPosition, itemId, itemQuantity: 3);
        var looter = session.SpawnEmptyGoblin(LooterPosition);
        for (var i = 0; i < InventoryCapacity.MaxNonPlayerStackCount; i++)
        {
            InventoryActions.AddItem(session.Components, looter, Guid.NewGuid(), quantity: 1);
        }

        Assert.IsTrue(session.TryLoot(looter, now: 0));
        Assert.IsTrue(session.IsLooted(corpse));
        Assert.AreEqual(3, session.QuantityOf(corpse, itemId));
    }

    [TestMethod]
    public void TheBehaviorSystem_LootsOnceNothingIsLeftToFight()
    {
        var session = BuildSession();
        var itemId = Guid.NewGuid();
        var corpse = session.SpawnCorpse(AdjacentPosition, itemId, itemQuantity: 3);
        var looter = session.SpawnEmptyGoblin(LooterPosition);
        session.Components.GetPackedPool<MovementComponent>().TryUpdate(looter, static (ref MovementComponent movement) => movement.MovementMode = MovementMode.Random);

        for (var frame = 1L; frame <= 120 && !session.IsLooted(corpse); frame++)
        {
            session.Build.EcsContext.SystemManager.Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, false, frame));
        }

        Assert.IsTrue(session.IsLooted(corpse));
        Assert.AreEqual(3, session.QuantityOf(looter, itemId));
    }

    [TestMethod]
    public void TheBehaviorSystem_NeverReadsAnUnbuiltSkeletonBesideTheLooter()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(2048, 1024, 3)), new MathUtility(new Random(1)), initialEntityCapacity: 200, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(new Vector3Int(0, 0, (int)MapLayer.Ground));
        var session = new Session(build);
        var looter = session.SpawnEmptyGoblin(new Vector3Int(1023, 10, (int)MapLayer.Ground));
        session.Components.GetPackedPool<MovementComponent>().TryUpdate(looter, static (ref MovementComponent movement) => movement.MovementMode = MovementMode.Random);
        var skeleton = build.Factory.Spawn(SpawnRequest.At(build.Context.Definitions.GetId(Goblin.Id), new Vector3Int(1024, 10, (int)MapLayer.Ground)) with { Seed = 3 });
        Assert.IsTrue(build.Factory.Skeletons.IsSkeleton(skeleton), "Sanity check: born across the boundary, into the frozen Borough tier.");

        for (var frame = 1L; frame <= 600; frame++)
        {
            build.EcsContext.SystemManager.Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, false, frame));
        }

        Assert.IsTrue(build.Factory.Skeletons.IsSkeleton(skeleton));
    }

    [TestMethod]
    public void TheBehaviorSystem_FightsAnAdjacentEnemyBeforeLooting()
    {
        var session = BuildSession();
        var itemId = Guid.NewGuid();
        var corpse = session.SpawnCorpse(AdjacentPosition, itemId, itemQuantity: 3);
        var looter = session.SpawnEmptyGoblin(LooterPosition);
        session.Components.GetPackedPool<MovementComponent>().TryUpdate(looter, static (ref MovementComponent movement) => movement.MovementMode = MovementMode.Random);
        var player = session.Build.Factory.Spawn(SpawnRequest.At(session.Build.Context.Definitions.GetId(Player.Id), new Vector3Int(9, 10, (int)MapLayer.Ground)) with { Seed = 1 });
        session.Components.GetPackedPool<MovementComponent>().Remove(player);
        session.Build.World.PlayerEntityId = player;
        var looterAttacks = 0;
        session.Build.EcsContext.EventBus.Subscribe<ActionActivatedEvent>(activated => looterAttacks += activated.EntityId == looter ? 1 : 0);

        for (var frame = 1L; frame <= 120; frame++)
        {
            session.Build.EcsContext.SystemManager.Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, false, frame));
        }

        Assert.IsTrue(looterAttacks > 0, "Sanity check: the goblin was deciding, and chose to fight.");
        Assert.IsFalse(session.IsLooted(corpse));
    }
}
