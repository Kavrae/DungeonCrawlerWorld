using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints.Composites;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Auras;
using Game.Modules.Core.Components;
using Game.Modules.Currency;
using Game.Modules.Currency.Components;
using Game.Modules.Death;
using Game.Modules.Death.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.ItemPiles;
using Game.Modules.ItemPiles.Components;
using Game.Spawning;
using Game.Terrain;
using Game.World;

namespace Tests.Modules.ItemPiles;

/// <summary>Dropping items and currency into item piles, and piles ending once empty, through the real module build.</summary>
[TestClass]
public sealed class ItemDropsTests
{
    private static readonly Vector3Int PlayerPosition = new(10, 10, (int)MapLayer.Ground);
    private static readonly Vector3Int AdjacentTile = new(11, 10, (int)MapLayer.Ground);
    private static readonly Vector3Int DiagonalTile = new(9, 9, (int)MapLayer.Ground);
    private static readonly Vector3Int TwoTilesAway = new(12, 10, (int)MapLayer.Ground);

    private sealed class Harness
    {
        private long _frame;

        public required GameBuildPassResult Build { get; init; }

        public required int PlayerEntityId { get; init; }

        public required ItemDrops Drops { get; init; }

        public ComponentManager Components => Build.EcsContext.ComponentManager;

        public MultiComponentPool<InventoryItemStackComponent> Stacks => Components.GetMultiPool<InventoryItemStackComponent>();

        public PackedComponentPool<CurrencyComponent> Currencies => Components.GetPackedPool<CurrencyComponent>();

        public int PileAt(Vector3Int tile) => Drops.TryFindPileAt(tile, out var pileEntityId) ? pileEntityId : -1;

        public List<InventoryItemStackComponent> StacksOf(int entityId)
        {
            var stacks = new List<InventoryItemStackComponent>();
            InventoryQueries.CopyStacksForEntity(Stacks, entityId, stacks);
            return stacks;
        }

        public uint GiveItem(Guid itemId, ushort quantity) => InventoryActions.AddItem(Components, PlayerEntityId, itemId, quantity);

        public void RunFrame()
        {
            _frame++;
            Build.EcsContext.SystemManager.Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, false, _frame));
        }
    }

    private static Harness CreateHarness()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(40, 40, 3)), sessionSeed: 1, initialEntityCapacity: 200, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(PlayerPosition);
        var player = build.Factory.Spawn(SpawnRequest.At(build.Context.Definitions.GetId(Player.Id), PlayerPosition) with { Seed = 1 });
        build.EcsContext.ComponentManager.GetMultiPool<InventoryItemStackComponent>().Remove(player);
        build.EcsContext.ComponentManager.Merge(player, new CurrencyComponent(gold: 0, credits: 0));
        build.World.PlayerEntityId = player;
        return new Harness { Build = build, PlayerEntityId = player, Drops = ItemPilesModule.ItemDropsFor(build.Context) };
    }

    [TestMethod]
    public void DroppingOneUnit_OntoAnAdjacentTile_StartsAPileHoldingIt()
    {
        var harness = CreateHarness();
        var itemId = Guid.NewGuid();
        var stackId = harness.GiveItem(itemId, quantity: 3);

        Assert.AreEqual(ItemDropRefusal.None, harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.OneUnitOf(stackId), AdjacentTile));

        var pile = harness.PileAt(AdjacentTile);
        Assert.AreNotEqual(-1, pile);
        Assert.AreEqual(1, harness.StacksOf(pile).Single().Quantity);
        Assert.AreEqual(2, harness.StacksOf(harness.PlayerEntityId).Single().Quantity);
        Assert.IsTrue(harness.Components.GetPackedPool<Game.Modules.Containers.Components.ContainerComponent>().Has(pile));
        Assert.IsFalse(harness.Build.World.IsBlocking(pile));
    }

    [TestMethod]
    public void TheDroppersOwnTileAndDiagonals_AreInReach_AndTwoTilesAwayIsNot()
    {
        var harness = CreateHarness();
        var stackId = harness.GiveItem(Guid.NewGuid(), quantity: 5);

        Assert.AreEqual(ItemDropRefusal.None, harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.OneUnitOf(stackId), PlayerPosition));
        Assert.AreEqual(ItemDropRefusal.None, harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.OneUnitOf(stackId), DiagonalTile));
        Assert.AreEqual(ItemDropRefusal.OutOfReach, harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.OneUnitOf(stackId), TwoTilesAway));

        Assert.AreEqual(-1, harness.PileAt(TwoTilesAway));
        Assert.AreEqual(3, harness.StacksOf(harness.PlayerEntityId).Single().Quantity);
    }

    [TestMethod]
    public void AWallTile_IsOutOfReach()
    {
        var harness = CreateHarness();
        var stackId = harness.GiveItem(Guid.NewGuid(), quantity: 1);
        harness.Build.World.SetStructure(AdjacentTile, new TerrainCell(harness.Build.World.Terrain.GetId(BuiltInTerrain.StoneWallKey)));

        Assert.AreEqual(ItemDropRefusal.OutOfReach, harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.OneUnitOf(stackId), AdjacentTile));
        Assert.AreEqual(-1, harness.PileAt(AdjacentTile));
    }

    [TestMethod]
    public void ASecondDrop_JoinsThePileAlreadyThere_MergingInterchangeableUnits()
    {
        var harness = CreateHarness();
        var stackId = harness.GiveItem(Guid.NewGuid(), quantity: 3);

        harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.OneUnitOf(stackId), AdjacentTile);
        var pile = harness.PileAt(AdjacentTile);
        harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.WholeStackOf(stackId), AdjacentTile);

        Assert.AreEqual(pile, harness.PileAt(AdjacentTile));
        Assert.AreEqual(3, harness.StacksOf(pile).Single().Quantity);
        Assert.AreEqual(0, harness.Stacks.CountForEntity(harness.PlayerEntityId));
    }

    [TestMethod]
    public void AWholeStack_KeepsItsIdentityAndOverride()
    {
        var harness = CreateHarness();
        var potion = TestContent.Item(BuiltInItemIds.HealthPotion);
        var stackId = InventoryActions.AddDivergentItem(harness.Components, harness.PlayerEntityId, potion with { Name = "Odd Potion" });

        harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.WholeStackOf(stackId), AdjacentTile);

        var dropped = harness.StacksOf(harness.PileAt(AdjacentTile)).Single();
        Assert.AreEqual(stackId, dropped.StackInstanceId);
        Assert.AreEqual("Odd Potion", dropped.Override?.Name);
    }

    [TestMethod]
    public void OneUnitOfADivergentStack_KeepsItsOverride()
    {
        var harness = CreateHarness();
        var potion = TestContent.Item(BuiltInItemIds.HealthPotion) with { Name = "Odd Potion" };
        var stackId = InventoryActions.AddItemWithOverride(harness.Components, harness.PlayerEntityId, potion, quantity: 2);

        harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.OneUnitOf(stackId), AdjacentTile);

        Assert.AreEqual("Odd Potion", harness.StacksOf(harness.PileAt(AdjacentTile)).Single().Override?.Name);
    }

    [TestMethod]
    public void AnItemThatCanNeverBeTraded_IsRefused()
    {
        var harness = CreateHarness();
        var bound = TestContent.Item(BuiltInItemIds.HealthPotion) with { CanTrade = false };
        var stackId = InventoryActions.AddDivergentItem(harness.Components, harness.PlayerEntityId, bound);

        Assert.AreEqual(ItemDropRefusal.CannotTrade, harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.WholeStackOf(stackId), AdjacentTile));
        Assert.AreEqual(-1, harness.PileAt(AdjacentTile));
    }

    [TestMethod]
    public void AStackWindingUp_IsRefused()
    {
        var harness = CreateHarness();
        var stackId = harness.GiveItem(Guid.NewGuid(), quantity: 1);
        harness.Components.Merge(harness.PlayerEntityId, new PendingWindupComponent(ActivatableReference.ItemStack(stackId), default, readyAtFrame: 100));

        Assert.AreEqual(ItemDropRefusal.StackInUse, harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.WholeStackOf(stackId), AdjacentTile));
    }

    [TestMethod]
    public void TheDead_CannotDrop()
    {
        var harness = CreateHarness();
        var stackId = harness.GiveItem(Guid.NewGuid(), quantity: 1);
        harness.Components.Merge(harness.PlayerEntityId, new DeadComponent(ActionSource.Admin, DiedAtFrame: 0));

        Assert.AreEqual(ItemDropRefusal.DropperDead, harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.WholeStackOf(stackId), AdjacentTile));
    }

    [TestMethod]
    public void Currency_DropsOneTypeOrAll_AndNoneHeldIsRefused()
    {
        var harness = CreateHarness();
        harness.Components.Merge(harness.PlayerEntityId, new CurrencyComponent(gold: 25, credits: 3));

        Assert.AreEqual(ItemDropRefusal.None, harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.AllOf(CurrencyType.Gold), AdjacentTile));
        var pile = harness.PileAt(AdjacentTile);
        Assert.AreEqual(25, harness.Currencies.GetReadonly(pile).Gold);
        Assert.AreEqual(ItemDropRefusal.NothingToDrop, harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.AllOf(CurrencyType.Gold), AdjacentTile));

        Assert.AreEqual(ItemDropRefusal.None, harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.AllCurrency, AdjacentTile));
        Assert.AreEqual(3, harness.Currencies.GetReadonly(pile).Credits);
        Assert.AreEqual(0, harness.Currencies.GetReadonly(harness.PlayerEntityId).Credits);
    }

    [TestMethod]
    public void ALitUnit_StaysLitInThePile_AndItsToggleMovesWithIt()
    {
        var harness = CreateHarness();
        var idolStackId = harness.GiveItem(BuiltInItemIds.ToxicIdol, quantity: 1);
        Assert.IsTrue(ToggleItemActions.TryToggle(harness.Components, harness.Build.Context.Items, harness.PlayerEntityId, idolStackId, out var litStackId));
        var activeToggles = harness.Components.GetMultiPool<ActiveToggleComponent>();
        Assert.AreEqual(1, activeToggles.CountForEntity(harness.PlayerEntityId), "Sanity check: the idol is lit.");

        harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.WholeStackOf(litStackId), AdjacentTile);

        var pile = harness.PileAt(AdjacentTile);
        Assert.IsTrue(harness.StacksOf(pile).Single().Override?.Activator is ToggleItemActivator { IsToggledOn: true });
        Assert.AreEqual(0, activeToggles.CountForEntity(harness.PlayerEntityId));
        Assert.AreEqual(1, activeToggles.CountForEntity(pile));
    }

    [TestMethod]
    public void ALitUnitTakenBackFromAPile_TakesItsAuraWithIt()
    {
        var harness = CreateHarness();
        var idolStackId = harness.GiveItem(BuiltInItemIds.ToxicIdol, quantity: 1);
        Assert.IsTrue(ToggleItemActions.TryToggle(harness.Components, harness.Build.Context.Items, harness.PlayerEntityId, idolStackId, out var litStackId));
        harness.RunFrame();
        Assert.IsTrue(harness.Build.Context.Auras.TryGetId(BuiltInAuraIds.ToxicIdolPoison, out var auraId));
        var whileHeld = PowerAround(harness, auraId);
        Assert.IsTrue(whileHeld.Values.Sum() > 0, "Sanity check: the lit idol radiates from the player.");

        harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.WholeStackOf(litStackId), DiagonalTile);
        harness.RunFrame();
        var pile = harness.PileAt(DiagonalTile);

        InventoryActions.LootEveryStack(harness.Components, harness.Build.Context.Items, pile, harness.PlayerEntityId, harness.Build.World);
        harness.RunFrame();
        harness.RunFrame();

        Assert.IsFalse(harness.Build.EcsContext.EntityManager.EntityExists(pile));
        var afterTakingItBack = PowerAround(harness, auraId);
        foreach (var (tile, power) in whileHeld)
        {
            Assert.AreEqual(power, afterTakingItBack[tile], $"Aura power at {tile}.");
        }
    }

    [TestMethod]
    public void ALitUnitTakenBackAfterWalkingAway_RadiatesFromWhereThePlayerNowStands()
    {
        var harness = CreateHarness();
        var idolStackId = harness.GiveItem(BuiltInItemIds.ToxicIdol, quantity: 1);
        Assert.IsTrue(ToggleItemActions.TryToggle(harness.Components, harness.Build.Context.Items, harness.PlayerEntityId, idolStackId, out var litStackId));
        harness.RunFrame();
        Assert.IsTrue(harness.Build.Context.Auras.TryGetId(BuiltInAuraIds.ToxicIdolPoison, out var auraId));
        var powerAtHolder = harness.Build.Context.AuraField.GetTotalPowerAt(PlayerPosition, auraId);

        harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.WholeStackOf(litStackId), DiagonalTile);
        harness.RunFrame();
        var pile = harness.PileAt(DiagonalTile);

        var steppedTo = new Vector3Int(PlayerPosition.X + 1, PlayerPosition.Y, PlayerPosition.Z);
        WalkPlayerTo(harness, steppedTo);
        WalkPlayerTo(harness, PlayerPosition);
        WalkPlayerTo(harness, steppedTo);

        InventoryActions.LootEveryStack(harness.Components, harness.Build.Context.Items, pile, harness.PlayerEntityId, harness.Build.World);
        harness.RunFrame();
        harness.RunFrame();

        var field = harness.Build.Context.AuraField;
        Assert.AreEqual(powerAtHolder, field.GetTotalPowerAt(steppedTo, auraId), "The aura is centred on the player.");
        Assert.AreEqual(0, field.GetTotalPowerAt(new Vector3Int(steppedTo.X - 5, steppedTo.Y, steppedTo.Z), auraId), "Nothing is left behind past the player's reach.");
        Assert.AreEqual(0, field.GetTotalPowerAt(new Vector3Int(DiagonalTile.X - 4, DiagonalTile.Y, DiagonalTile.Z), auraId), "Nothing radiates from where the pile was.");
    }

    private static void WalkPlayerTo(Harness harness, Vector3Int destination)
    {
        var movements = harness.Components.GetPackedPool<Game.Modules.Movement.Components.MovementComponent>();
        var transforms = harness.Components.GetDirectPool<TransformComponent>();
        for (var frame = 0; frame < 120 && transforms.GetReadonly(harness.PlayerEntityId).Position != destination; frame++)
        {
            using (var movement = movements.Write(harness.PlayerEntityId))
            {
                movement.Value.NextMapPosition = destination;
            }

            harness.RunFrame();
        }

        Assert.AreEqual(destination, transforms.GetReadonly(harness.PlayerEntityId).Position, "Sanity check: the player walked.");
    }

    private static Dictionary<Vector3Int, int> PowerAround(Harness harness, byte auraId)
    {
        var powers = new Dictionary<Vector3Int, int>();
        for (var y = PlayerPosition.Y - 8; y <= PlayerPosition.Y + 8; y++)
        {
            for (var x = PlayerPosition.X - 8; x <= PlayerPosition.X + 8; x++)
            {
                var tile = new Vector3Int(x, y, PlayerPosition.Z);
                powers[tile] = harness.Build.Context.AuraField.GetTotalPowerAt(tile, auraId);
            }
        }

        return powers;
    }

    [TestMethod]
    public void AnEmptiedPile_IsDestroyedTheNextFrame()
    {
        var harness = CreateHarness();
        var stackId = harness.GiveItem(Guid.NewGuid(), quantity: 2);
        harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.WholeStackOf(stackId), AdjacentTile);
        var pile = harness.PileAt(AdjacentTile);

        InventoryActions.LootEveryStack(harness.Components, harness.Build.Context.Items, pile, harness.PlayerEntityId, harness.Build.World);
        Assert.IsTrue(harness.Build.EcsContext.EntityManager.EntityExists(pile), "Never destroyed inside the removal.");

        harness.RunFrame();

        Assert.IsFalse(harness.Build.EcsContext.EntityManager.EntityExists(pile));
        Assert.AreEqual(-1, harness.PileAt(AdjacentTile));
    }

    [TestMethod]
    public void APileStillHoldingCurrency_IsKept()
    {
        var harness = CreateHarness();
        var stackId = harness.GiveItem(Guid.NewGuid(), quantity: 1);
        harness.Components.Merge(harness.PlayerEntityId, new CurrencyComponent(gold: 5, credits: 0));
        harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.WholeStackOf(stackId), AdjacentTile);
        harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.AllCurrency, AdjacentTile);
        var pile = harness.PileAt(AdjacentTile);

        InventoryActions.LootEveryStack(harness.Components, harness.Build.Context.Items, pile, harness.PlayerEntityId, harness.Build.World);
        harness.RunFrame();
        Assert.IsTrue(harness.Build.EcsContext.EntityManager.EntityExists(pile));

        CurrencyActions.TryTransferAll(harness.Components, pile, harness.PlayerEntityId);
        harness.RunFrame();
        Assert.IsFalse(harness.Build.EcsContext.EntityManager.EntityExists(pile));
    }

    [TestMethod]
    public void ADropCommand_LandsAtOnce_WhateverTheActionLock()
    {
        var harness = CreateHarness();
        var stackId = harness.GiveItem(Guid.NewGuid(), quantity: 2);
        var locks = harness.Components.GetPackedPool<ActionLockComponent>();
        locks.TrySet(harness.PlayerEntityId, new ActionLockComponent(currentLockTotalFrames: 600, unlockedAtFrame: 600));
        var commands = new ItemDropCommands(harness.Drops, harness.Build.World);

        Assert.AreEqual(ItemDropRefusal.None, commands.TryDrop(ItemDrop.OneUnitOf(stackId), AdjacentTile));

        Assert.AreEqual(1, harness.StacksOf(harness.PileAt(AdjacentTile)).Single().Quantity);
        Assert.AreEqual(600u, locks.GetReadonly(harness.PlayerEntityId).UnlockedAtFrame, "A drop neither waits for the lock nor sets it.");
    }

    [TestMethod]
    public void EveryFootprintTileOfALargeCorpse_TakesTheDropIntoIt()
    {
        var foremanTile = new Vector3Int(11, 11, (int)MapLayer.Ground);
        var footprint = new[] { foremanTile, foremanTile + new Vector3Int(1, 0, 0), foremanTile + new Vector3Int(0, 1, 0), foremanTile + new Vector3Int(1, 1, 0) };
        var harness = CreateHarness();
        var corpse = SpawnCorpse(harness, GoblinForeman.Id, foremanTile);
        harness.Stacks.Remove(corpse);
        var stackId = harness.GiveItem(Guid.NewGuid(), quantity: 4);

        foreach (var tile in footprint)
        {
            Assert.AreEqual(ItemDropRefusal.None, harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.OneUnitOf(stackId), tile), $"Refused at {tile}.");
            Assert.AreEqual(-1, harness.PileAt(tile), "Into the corpse, never a pile beside it.");
        }

        Assert.AreEqual(4, harness.StacksOf(corpse).Single().Quantity);
    }

    [TestMethod]
    public void ACorpseReservedForSomeoneElse_RefusesTheDrop()
    {
        var harness = CreateHarness();
        var corpse = SpawnCorpse(harness, Goblin.Id, AdjacentTile);
        var dead = harness.Components.GetPackedPool<DeadComponent>();
        dead.TrySet(corpse, dead.GetReadonly(corpse) with { LootOwnerEntityKey = harness.Build.Context.EntityKeys.GetKey(harness.Build.Factory.Spawn(SpawnRequest.At(harness.Build.Context.Definitions.GetId(Goblin.Id), new Vector3Int(30, 30, (int)MapLayer.Ground)) with { Seed = 2 })) });
        var stackId = harness.GiveItem(Guid.NewGuid(), quantity: 1);

        Assert.AreEqual(ItemDropRefusal.LootReserved, harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.WholeStackOf(stackId), AdjacentTile));
    }

    [TestMethod]
    public void AStandingShop_RefusesTheDrop_AndNoPileAppearsUnderIt()
    {
        var harness = CreateHarness();
        var chest = harness.Build.Factory.Spawn(SpawnRequest.At(harness.Build.Context.Definitions.GetId(Game.Blueprints.Objects.TreasureChest.Id), AdjacentTile) with { Seed = 1 });
        harness.Components.Merge(chest, new Game.Modules.Shops.Components.ShopComponent(acceptedItems: null, buyMultiplier: 1.2f, sellMultiplier: 0.8f));
        var stackId = harness.GiveItem(Guid.NewGuid(), quantity: 1);

        Assert.AreEqual(ItemDropRefusal.IntoShop, harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.WholeStackOf(stackId), AdjacentTile));
        Assert.AreEqual(-1, harness.PileAt(AdjacentTile));
    }

    [TestMethod]
    public void AChest_TakesTheDrop_AndAFullOneRefusesIt()
    {
        var harness = CreateHarness();
        var chest = harness.Build.Factory.Spawn(SpawnRequest.At(harness.Build.Context.Definitions.GetId(Game.Blueprints.Objects.TreasureChest.Id), AdjacentTile) with { Seed = 1 });
        harness.Stacks.Remove(chest);
        var stackId = harness.GiveItem(Guid.NewGuid(), quantity: 1);

        Assert.AreEqual(ItemDropRefusal.None, harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.WholeStackOf(stackId), AdjacentTile));
        Assert.AreEqual(stackId, harness.StacksOf(chest).Single().StackInstanceId);

        for (var i = 1; i < InventoryCapacity.MaxNonPlayerStackCount; i++)
        {
            InventoryActions.AddItem(harness.Components, chest, Guid.NewGuid(), quantity: 1);
        }

        var anotherStackId = harness.GiveItem(Guid.NewGuid(), quantity: 1);
        Assert.AreEqual(ItemDropRefusal.NoRoom, harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.WholeStackOf(anotherStackId), AdjacentTile));
        Assert.AreEqual(1, harness.StacksOf(harness.PlayerEntityId).Count);
    }

    [TestMethod]
    public void AContainerOutOfReach_RefusesTheDrop()
    {
        var harness = CreateHarness();
        SpawnCorpse(harness, Goblin.Id, TwoTilesAway);
        var stackId = harness.GiveItem(Guid.NewGuid(), quantity: 1);

        Assert.AreEqual(ItemDropRefusal.OutOfReach, harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.WholeStackOf(stackId), TwoTilesAway));
    }

    private static int SpawnCorpse(Harness harness, Guid blueprintId, Vector3Int position)
    {
        var entity = harness.Build.Factory.Spawn(SpawnRequest.At(harness.Build.Context.Definitions.GetId(blueprintId), position) with { Seed = 1 });
        harness.Build.EcsContext.EventBus.Publish(new EntityDiedEvent(entity, ActionSource.Admin));
        harness.Build.EcsContext.EventBus.DispatchBuffered<EntityDiedEvent>();
        return entity;
    }

    [TestMethod]
    public void OpeningAPile_NeverMarksItLooted()
    {
        var harness = CreateHarness();
        var stackId = harness.GiveItem(Guid.NewGuid(), quantity: 1);
        harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.WholeStackOf(stackId), AdjacentTile);
        var pile = harness.PileAt(AdjacentTile);

        new LootCommands(harness.Components, harness.Build.Context.Items, harness.Build.World).MarkLooted(pile);

        Assert.IsFalse(harness.Components.GetPackedPool<LootedComponent>().Has(pile));
    }

    [TestMethod]
    public void TakeAll_MovesAPilesItemsAndGoldToThePlayer()
    {
        var harness = CreateHarness();
        var stackId = harness.GiveItem(Guid.NewGuid(), quantity: 2);
        harness.Drops.TryDrop(harness.PlayerEntityId, ItemDrop.WholeStackOf(stackId), AdjacentTile);
        var pile = harness.PileAt(AdjacentTile);
        harness.Components.Merge(pile, new Game.Modules.Currency.Components.CurrencyComponent(gold: 7, credits: 0));
        harness.Components.GetPackedPool<Game.Modules.Currency.Components.CurrencyComponent>().TryGetReadonly(harness.PlayerEntityId, out var before);

        new LootCommands(harness.Components, harness.Build.Context.Items, harness.Build.World).TakeAll(pile, harness.PlayerEntityId);

        Assert.AreEqual(0, harness.Components.GetMultiPool<Game.Modules.Inventory.Components.InventoryItemStackComponent>().CountForEntity(pile));
        Assert.AreEqual(before.Gold + 7, harness.Components.GetPackedPool<Game.Modules.Currency.Components.CurrencyComponent>().GetReadonly(harness.PlayerEntityId).Gold);
    }

    [TestMethod]
    public void AGoblinsCorpse_IsStillMarkedLooted()
    {
        var harness = CreateHarness();
        var goblin = harness.Build.Factory.Spawn(SpawnRequest.At(harness.Build.Context.Definitions.GetId(Goblin.Id), TwoTilesAway) with { Seed = 1 });

        new LootCommands(harness.Components, harness.Build.Context.Items, harness.Build.World).MarkLooted(goblin);

        Assert.IsTrue(harness.Components.GetPackedPool<LootedComponent>().Has(goblin));
    }
}
