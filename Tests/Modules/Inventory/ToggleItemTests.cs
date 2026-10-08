using Engine.ECS.Components;
using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints.Composites;
using Game.Bootstrap;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Auras;
using Game.Modules.Auras.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death;
using Game.Modules.Death.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Inventory.Definitions;
using Game.Spawning;
using Game.World;

namespace Tests.Modules.Inventory;

/// <summary>Toggle items in a real session: the flip, the holder's side of each lit unit, and how an activation is timed.</summary>
[TestClass]
public sealed class ToggleItemTests
{
    private const byte IdolStrength = 16;

    private static readonly Vector3Int PlayerPosition = new(10, 10, (int)MapLayer.Ground);
    private static readonly Vector3Int OtherPosition = new(30, 30, (int)MapLayer.Ground);

    private sealed class Harness
    {
        public required GameSession Session { get; init; }
        public required int PlayerEntityId { get; init; }

        private long _frame;

        public EcsContext Ecs => Session.EcsContext;
        public ComponentManager Components => Session.EcsContext.ComponentManager;
        public ItemCatalog Items => Session.Catalogs.ItemCatalog;
        public long CurrentFrame => _frame;

        public List<InventoryItemStackComponent> StacksOf(int entityId)
        {
            var stacks = new List<InventoryItemStackComponent>();
            InventoryQueries.CopyStacksForEntity(Components.GetMultiPool<InventoryItemStackComponent>(), entityId, stacks);
            return stacks.Where(stack => stack.ItemDefinitionId == ToxicIdol.Id).ToList();
        }

        public uint PlainIdolStackId(int entityId) => StacksOf(entityId).Single(stack => !IsLit(stack)).StackInstanceId;

        public InventoryItemStackComponent LitIdolStack(int entityId) => StacksOf(entityId).Single(IsLit);

        public static bool IsLit(InventoryItemStackComponent stack) => stack.Override?.Activator is ToggleItemActivator { IsToggledOn: true };

        public int IdolCount(int entityId) => StacksOf(entityId).Sum(stack => stack.Quantity);

        public int ActiveToggleCount(int entityId) => Components.GetMultiPool<ActiveToggleComponent>().CountForEntity(entityId);

        public int HeldSourceCount(int entityId) =>
            Components.GetMultiPool<AuraSourceComponent>().CountMatching(entityId, static (ref readonly AuraSourceComponent source) => source.HeldGrantKey != AuraSourceComponent.NoHeldGrantKey);

        public int PoisonStrengthAt(Vector3Int position)
        {
            Assert.IsTrue(Session.Catalogs.Auras.TryGetId(ToxicIdol.Aura.Id, out var auraId));
            return Session.Internals.AuraField.GetTotalPowerAt(position, auraId);
        }

        public uint Light(int entityId)
        {
            Assert.IsTrue(ToggleItemActions.TryToggle(Components, Items, entityId, PlainIdolStackId(entityId), out var litStackId));
            return litStackId;
        }

        public int CreatePlacedEntity(Vector3Int position)
        {
            var entityId = Session.Internals.ProcessingTierResolver.CreateEntityAt(Ecs.EntityManager, position);
            Components.Merge(entityId, new TransformComponent(TransformComponent.UnplacedOn(MapLayer.Ground), new Vector2Byte(1, 1)));
            Session.World.PlaceEntityOnMap(entityId, position, ref Components.GetDirectPool<TransformComponent>().Get(entityId));
            return entityId;
        }

        public void Frame()
        {
            _frame++;
            Ecs.Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, IsRunningSlowly: false, FrameCount: _frame));
        }

        public void QueueActivation(uint stackInstanceId) =>
            Components.Merge(PlayerEntityId, new PendingItemActivationComponent(stackInstanceId, TestSelections.At(PlayerPosition)));

        public void LockPlayerUntil(uint frame) =>
            Components.GetPackedPool<ActionLockComponent>().TryUpdate(PlayerEntityId, frame, static (ref ActionLockComponent actionLock, uint unlockedAt) => actionLock.UnlockedAtFrame = unlockedAt);

        public bool IsPlayerLocked => ActionLockGate.IsBlocked(Components.GetPackedPool<ActionLockComponent>(), PlayerEntityId, _frame);
    }

    private static Harness Build()
    {
        var map = new Map(new Vector3Int(40, 40, 3));
        var session = GameBootstrapper.Build(ValidatedMods.None, map, new MathUtility(new Random(1)), initialEntityCapacity: 100, initialComponentCapacity: 50);
        session.Internals.ProcessingTierResolver.SetReferencePosition(PlayerPosition);

        var playerEntityId = session.EcsContext.EntityManager.CreateEntity();
        session.Internals.ProcessingTierResolver.PinLocalAndNotify(playerEntityId);
        session.Internals.Factory.Spawn(SpawnRequest.At(session.Catalogs.Definitions.GetId(Player.Id), PlayerPosition) with { Seed = 1, ReservedEntityId = playerEntityId });
        session.World.PlayerEntityId = playerEntityId;

        return new Harness { Session = session, PlayerEntityId = playerEntityId };
    }

    private static Harness BuildWithIdols(ushort idolCount)
    {
        var harness = Build();
        InventoryActions.AddItem(harness.Components, harness.PlayerEntityId, ToxicIdol.Id, (ushort)(idolCount - 1));
        return harness;
    }

    [TestMethod]
    public void TryToggle_PeelsOneUnitIntoALitStack_AndMergesItBackWhenPutOut()
    {
        var harness = BuildWithIdols(3);
        var player = harness.PlayerEntityId;

        var litStackId = harness.Light(player);

        Assert.AreEqual(1, harness.LitIdolStack(player).Quantity);
        Assert.AreEqual(2, harness.StacksOf(player).Single(stack => !Harness.IsLit(stack)).Quantity);

        Assert.IsTrue(ToggleItemActions.TryToggle(harness.Components, harness.Items, player, litStackId, out var plainStackId));

        var stacks = harness.StacksOf(player);
        Assert.HasCount(1, stacks);
        Assert.AreEqual(3, stacks[0].Quantity);
        Assert.IsNull(stacks[0].Override);
        Assert.AreEqual(stacks[0].StackInstanceId, plainStackId);
    }

    [TestMethod]
    public void TryToggle_TwoLitUnits_ShareOneStack_AndPuttingOneOutLeavesOneLit()
    {
        var harness = BuildWithIdols(3);
        var player = harness.PlayerEntityId;

        var firstLitStackId = harness.Light(player);
        var secondLitStackId = harness.Light(player);

        Assert.AreEqual(firstLitStackId, secondLitStackId);
        Assert.AreEqual(2, harness.LitIdolStack(player).Quantity);

        Assert.IsTrue(ToggleItemActions.TryToggle(harness.Components, harness.Items, player, firstLitStackId, out _));

        Assert.AreEqual(1, harness.LitIdolStack(player).Quantity);
        Assert.AreEqual(3, harness.IdolCount(player));
    }

    [TestMethod]
    public void TryToggle_TheHotkeyBindingFollowsTheToggledUnitBothWays()
    {
        var harness = BuildWithIdols(3);
        var player = harness.PlayerEntityId;
        var bindings = harness.Components.GetMultiPool<ItemHotkeyBindingComponent>();

        Assert.IsTrue(ToggleItemActions.TryToggle(harness.Components, harness.Items, player, harness.PlainIdolStackId(player), out var litStackId, HotkeySlot.Slot6));

        Assert.IsTrue(ItemHotkeyBindingQueries.TryGet(bindings, player, HotkeySlot.Slot6, out var boundAfterLighting));
        Assert.AreEqual(litStackId, boundAfterLighting);

        Assert.IsTrue(ToggleItemActions.TryToggle(harness.Components, harness.Items, player, litStackId, out var plainStackId, HotkeySlot.Slot6));

        Assert.IsTrue(ItemHotkeyBindingQueries.TryGet(bindings, player, HotkeySlot.Slot6, out var boundAfterPuttingOut));
        Assert.AreEqual(plainStackId, boundAfterPuttingOut);
    }

    /// <summary>Two slots bound to one stack of two idols: each slot switches one idol on and off, whichever order they are pressed in.</summary>
    [TestMethod]
    public void TwoSlotsBoundToOneStack_EachSlotTogglesItsOwnUnit()
    {
        var harness = BuildWithIdols(2);
        var player = harness.PlayerEntityId;
        var bindings = harness.Components.GetMultiPool<ItemHotkeyBindingComponent>();
        harness.Components.Merge(player, new ItemHotkeyBindingComponent(HotkeySlot.Slot7, harness.PlainIdolStackId(player)));

        void Press(HotkeySlot slot)
        {
            Assert.IsTrue(ItemHotkeyBindingQueries.TryGet(bindings, player, slot, out var boundStackId));
            Assert.IsTrue(ToggleItemActions.TryToggle(harness.Components, harness.Items, player, boundStackId, out _, slot));
        }

        int LitCount() => harness.StacksOf(player).Where(Harness.IsLit).Sum(stack => stack.Quantity);

        Press(HotkeySlot.Slot6);
        Assert.AreEqual(1, LitCount());

        Press(HotkeySlot.Slot6);
        Assert.AreEqual(0, LitCount(), "The same slot puts out the unit it lit rather than lighting the other.");

        Press(HotkeySlot.Slot7);
        Press(HotkeySlot.Slot6);
        Assert.AreEqual(2, LitCount());

        Press(HotkeySlot.Slot7);
        Assert.AreEqual(1, LitCount());

        Press(HotkeySlot.Slot7);
        Assert.AreEqual(2, LitCount());

        Press(HotkeySlot.Slot6);
        Press(HotkeySlot.Slot7);
        Assert.AreEqual(0, LitCount());
    }

    /// <summary>A unit toggled with no slot named moves no binding while its old stack remains, and every binding once that stack is gone.</summary>
    [TestMethod]
    public void TryToggle_WithNoSlot_LeavesBindingsOnAStackThatRemains_AndMovesThemOffOneThatIsGone()
    {
        var harness = BuildWithIdols(2);
        var player = harness.PlayerEntityId;
        var bindings = harness.Components.GetMultiPool<ItemHotkeyBindingComponent>();
        var plainStackId = harness.PlainIdolStackId(player);

        harness.Light(player);

        Assert.IsTrue(ItemHotkeyBindingQueries.TryGet(bindings, player, HotkeySlot.Slot6, out var boundWhileStackRemains));
        Assert.AreEqual(plainStackId, boundWhileStackRemains);

        var litStackId = harness.Light(player);

        Assert.IsTrue(ItemHotkeyBindingQueries.TryGet(bindings, player, HotkeySlot.Slot6, out var boundOnceStackIsGone));
        Assert.AreEqual(litStackId, boundOnceStackIsGone);
    }

    [TestMethod]
    public void TryToggle_NonToggleItemOrMissingStack_IsRefusedAndChangesNothing()
    {
        var harness = Build();
        var player = harness.PlayerEntityId;
        var stacks = harness.Components.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsTrue(InventoryQueries.TryGetStack(stacks, player, HealthPotion.Id, out var potionStack));
        var versionBefore = stacks.GetEntityVersion(player);

        Assert.IsFalse(ToggleItemActions.TryToggle(harness.Components, harness.Items, player, potionStack.StackInstanceId, out _));
        Assert.IsFalse(ToggleItemActions.TryToggle(harness.Components, harness.Items, player, stackInstanceId: uint.MaxValue, out _));

        Assert.AreEqual(versionBefore, stacks.GetEntityVersion(player));
    }

    [TestMethod]
    public void LitUnit_GivesItsHolderOneActiveToggleAndOneSource_AndPuttingItOutTakesBothBack()
    {
        var harness = Build();
        var player = harness.PlayerEntityId;

        var litStackId = harness.Light(player);

        Assert.AreEqual(1, harness.ActiveToggleCount(player));
        Assert.AreEqual(1, harness.HeldSourceCount(player));
        Assert.AreEqual(IdolStrength, harness.PoisonStrengthAt(PlayerPosition));

        Assert.IsTrue(ToggleItemActions.TryToggle(harness.Components, harness.Items, player, litStackId, out _));

        Assert.AreEqual(0, harness.ActiveToggleCount(player));
        Assert.AreEqual(0, harness.HeldSourceCount(player));
        Assert.AreEqual(0, harness.PoisonStrengthAt(PlayerPosition));
    }

    [TestMethod]
    public void TwoLitUnitsInOneStack_AreTwoTogglesWhoseSourcesAdd_AndEachEndsOnlyItsOwn()
    {
        var harness = BuildWithIdols(2);
        var player = harness.PlayerEntityId;

        harness.Light(player);
        var litStackId = harness.Light(player);

        Assert.AreEqual(2, harness.ActiveToggleCount(player));
        Assert.AreEqual(2, harness.HeldSourceCount(player));
        Assert.AreEqual(IdolStrength * 2, harness.PoisonStrengthAt(PlayerPosition));

        Assert.IsTrue(ToggleItemActions.TryToggle(harness.Components, harness.Items, player, litStackId, out _));

        Assert.AreEqual(1, harness.ActiveToggleCount(player));
        Assert.AreEqual(1, harness.HeldSourceCount(player));
        Assert.AreEqual(IdolStrength, harness.PoisonStrengthAt(PlayerPosition));
    }

    [TestMethod]
    public void TransferringALitStack_MovesItsToggleAndSourceToTheReceiver_AndItStaysLit()
    {
        var harness = Build();
        var player = harness.PlayerEntityId;
        var receiver = harness.CreatePlacedEntity(OtherPosition);
        var litStackId = harness.Light(player);

        Assert.IsTrue(harness.Session.Commands.InventoryCommands.TryTransferStack(player, receiver, litStackId));

        Assert.AreEqual(0, harness.ActiveToggleCount(player));
        Assert.AreEqual(0, harness.PoisonStrengthAt(PlayerPosition));
        Assert.AreEqual(1, harness.ActiveToggleCount(receiver));
        Assert.AreEqual(1, harness.HeldSourceCount(receiver));
        Assert.AreEqual(IdolStrength, harness.PoisonStrengthAt(OtherPosition));
        Assert.AreEqual(1, harness.LitIdolStack(receiver).Quantity);

        Assert.IsTrue(harness.Session.Commands.InventoryCommands.TryTransferStack(receiver, player, litStackId));

        Assert.AreEqual(0, harness.ActiveToggleCount(receiver));
        Assert.AreEqual(0, harness.PoisonStrengthAt(OtherPosition));
        Assert.AreEqual(IdolStrength, harness.PoisonStrengthAt(PlayerPosition));
        Assert.AreEqual(1, harness.LitIdolStack(player).Quantity);
    }

    [TestMethod]
    public void MergingTwoLitStacks_KeepsOneToggleAndOneSourcePerLitUnit()
    {
        var harness = Build();
        var player = harness.PlayerEntityId;
        var giver = harness.CreatePlacedEntity(OtherPosition);
        InventoryActions.AddItem(harness.Components, giver, ToxicIdol.Id, quantity: 1);
        var givenLitStackId = harness.Light(giver);
        harness.Light(player);
        Assert.IsTrue(harness.Session.Commands.InventoryCommands.TryTransferStack(giver, player, givenLitStackId));
        Assert.AreEqual(2, harness.ActiveToggleCount(player));

        harness.Session.Commands.InventoryCommands.MergeIntoEquivalentStack(player, givenLitStackId);

        Assert.AreEqual(2, harness.LitIdolStack(player).Quantity);
        Assert.AreEqual(2, harness.ActiveToggleCount(player));
        Assert.AreEqual(2, harness.HeldSourceCount(player));
        Assert.AreEqual(IdolStrength * 2, harness.PoisonStrengthAt(PlayerPosition));
    }

    [TestMethod]
    public void LitStackGivenToADeadHolder_RadiatesFromTheCorpse()
    {
        var harness = Build();
        var player = harness.PlayerEntityId;
        var corpse = harness.CreatePlacedEntity(OtherPosition);
        harness.Components.Merge(corpse, new DeadComponent(TestSources.Entity(player), DiedAtFrame: 0));
        var litStackId = harness.Light(player);

        Assert.IsTrue(harness.Session.Commands.InventoryCommands.TryTransferStack(player, corpse, litStackId));

        Assert.AreEqual(1, harness.ActiveToggleCount(corpse));
        Assert.AreEqual(IdolStrength, harness.PoisonStrengthAt(OtherPosition));
    }

    [TestMethod]
    public void LitStackHeldOffTheMap_KeepsItsToggleAndReachesNothing_AndRadiatesAgainOnceBackOnAHolderOnTheMap()
    {
        var harness = Build();
        var player = harness.PlayerEntityId;
        var offMapHolder = harness.Ecs.EntityManager.CreateEntity();
        var litStackId = harness.Light(player);

        Assert.IsTrue(harness.Session.Commands.InventoryCommands.TryTransferStack(player, offMapHolder, litStackId));

        Assert.AreEqual(1, harness.ActiveToggleCount(offMapHolder));
        Assert.AreEqual(0, harness.PoisonStrengthAt(PlayerPosition));
        Assert.AreEqual(1, harness.LitIdolStack(offMapHolder).Quantity);

        Assert.IsTrue(harness.Session.Commands.InventoryCommands.TryTransferStack(offMapHolder, player, litStackId));

        Assert.AreEqual(0, harness.ActiveToggleCount(offMapHolder));
        Assert.AreEqual(IdolStrength, harness.PoisonStrengthAt(PlayerPosition));
    }

    [TestMethod]
    public void HolderDies_ALitUnitWithNoPeriodicEffects_KeepsItsToggleAndItsSource()
    {
        var harness = Build();
        var holder = harness.CreatePlacedEntity(OtherPosition);
        InventoryActions.AddItem(harness.Components, holder, ToxicIdol.Id, quantity: 1);
        harness.Light(holder);
        harness.Components.GetMultiPool<AuraSourceComponent>().Add(holder, new AuraSourceComponent(harness.Session.Catalogs.Auras.Register(TestAuras.Light), power: 4, size: 2));

        harness.Ecs.EventBus.Publish(new EntityDiedEvent(holder, TestSources.Entity(harness.PlayerEntityId)));
        harness.Frame();

        Assert.IsTrue(harness.Components.GetPackedPool<DeadComponent>().Has(holder));
        Assert.AreEqual(1, harness.ActiveToggleCount(holder));
        Assert.AreEqual(1, harness.Components.GetMultiPool<AuraSourceComponent>().CountForEntity(holder), "The unkeyed source ends with its entity; the lit idol's stays.");
        Assert.AreEqual(1, harness.HeldSourceCount(holder));
        Assert.AreEqual(IdolStrength, harness.PoisonStrengthAt(OtherPosition));
    }

    [TestMethod]
    public void DestroyingAHolderWithLitStacks_AddsNoSourceWhileDestroying_AndLeavesNothingOnTheId()
    {
        var harness = Build();
        var holder = harness.CreatePlacedEntity(OtherPosition);
        InventoryActions.AddItem(harness.Components, holder, ToxicIdol.Id, quantity: 2);
        harness.Light(holder);
        harness.Light(holder);
        var sources = harness.Components.GetMultiPool<AuraSourceComponent>();
        var sourcesAddedWhileDestroying = 0;
        sources.ComponentChanged += (entityId, _) => sourcesAddedWhileDestroying += entityId == holder ? 1 : 0;

        harness.Ecs.EntityManager.DestroyEntity(holder);

        Assert.AreEqual(0, sourcesAddedWhileDestroying);
        Assert.IsFalse(sources.Has(holder));
        Assert.AreEqual(0, harness.ActiveToggleCount(holder));
        Assert.AreEqual(0, harness.PoisonStrengthAt(OtherPosition));
    }

    [TestMethod]
    public void FreeCastToggleItem_FlipsWhileActionLocked_SetsNoLock_AndConsumesNothing()
    {
        var harness = Build();
        var player = harness.PlayerEntityId;
        var freeCastStackId = AddFreeCastIdol(harness, player);
        harness.LockPlayerUntil(1000);

        harness.QueueActivation(freeCastStackId);
        harness.Frame();

        Assert.AreEqual(1, harness.LitIdolStack(player).Quantity);
        Assert.AreEqual(2, harness.IdolCount(player));

        harness.LockPlayerUntil(0);
        harness.QueueActivation(harness.LitIdolStack(player).StackInstanceId);
        harness.Frame();

        Assert.IsFalse(harness.StacksOf(player).Any(Harness.IsLit));
        Assert.AreEqual(2, harness.IdolCount(player));
        Assert.IsFalse(harness.IsPlayerLocked);
    }

    /// <summary>A unit of a Toxic Idol that is switched FreeCast rather than Delayed, added unlit.</summary>
    private static uint AddFreeCastIdol(Harness harness, int entityId) =>
        InventoryActions.AddDivergentItem(harness.Components, entityId, ToxicIdol.Build() with { Activator = new ToggleItemActivator(new ActionTiming(ActionTimingCategory.FreeCast)) });

    [TestMethod]
    public void ImmediateToggleItem_IsRefusedWhileActionLocked_AndSetsTheLockWhenItFlips()
    {
        var harness = Build();
        var player = harness.PlayerEntityId;
        var immediateIdol = ToxicIdol.Build() with { Activator = new ToggleItemActivator(new ActionTiming(ActionTimingCategory.Immediate)) };
        var immediateStackId = InventoryActions.AddDivergentItem(harness.Components, player, immediateIdol);
        harness.LockPlayerUntil(1000);

        harness.QueueActivation(immediateStackId);
        harness.Frame();

        Assert.IsFalse(harness.StacksOf(player).Any(Harness.IsLit));

        harness.LockPlayerUntil(0);
        harness.QueueActivation(immediateStackId);
        harness.Frame();

        Assert.AreEqual(1, harness.LitIdolStack(player).Quantity);
        Assert.AreEqual(2, harness.IdolCount(player));
        Assert.IsTrue(harness.IsPlayerLocked);
    }

    [TestMethod]
    public void CanQueueItemActivation_AFreeCastItemIsNotHeldByTheActionLock_AndAPotionIs()
    {
        var harness = Build();
        var player = harness.PlayerEntityId;
        harness.LockPlayerUntil(1000);
        var stacks = harness.Components.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsTrue(InventoryQueries.TryGetStack(stacks, player, HealthPotion.Id, out var potionStack));
        var playerCommands = harness.Session.Commands.PlayerCommands;
        var plainIdolStackId = harness.PlainIdolStackId(player);
        var freeCastStackId = AddFreeCastIdol(harness, player);
        Assert.IsFalse(playerCommands.CanQueueItemActivation(plainIdolStackId), "The Toxic Idol itself is Delayed, so it waits for the lock.");

        Assert.IsTrue(playerCommands.CanQueueItemActivation(freeCastStackId));
        Assert.IsFalse(playerCommands.CanQueueItemActivation(potionStack.StackInstanceId));

        Assert.IsTrue(playerCommands.QueueItemActivation(freeCastStackId, TestSelections.At(PlayerPosition)));
        Assert.IsTrue(harness.Components.GetPackedPool<PendingItemActivationComponent>().Has(player), "A FreeCast item is written at once, lock or no lock.");
    }
}
