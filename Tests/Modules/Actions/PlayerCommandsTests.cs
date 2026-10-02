using Engine.ECS.Components;
using Game.Effects;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement.Components;
using Game.Tags;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.UI;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class PlayerCommandsTests
{
    private const int PlayerEntityId = 1;
    private const int LockEndsAtFrame = 60;
    private static readonly Vector3Int PlayerPosition = new(5, 5, 0);
    private static readonly Vector3Int North = new(5, 4, 0);
    private static readonly Guid SelfActionId = Guid.Parse("7d1b6b8e-3f0a-4a57-9a55-1c2f3e4d5a61");
    private const uint StackInstanceId = 1;

    private sealed class Harness
    {
        public required ActionTargetingController ActionTargeting { get; init; }
        public required PlayerMovementController Movement { get; init; }
        public required PlayerCommands Buffer { get; init; }
        public required SimulationClock Clock { get; init; }
        public required ComponentManager ComponentManager { get; init; }
        public required EventBus EventBus { get; init; }
        public required EntityActions Actions { get; init; }

        private KeyboardState _previous;

        public Vector3Int? NextMapPosition => ComponentManager.GetPackedPool<MovementComponent>().GetReadonly(PlayerEntityId).NextMapPosition;
        public bool HasPendingAction => ComponentManager.GetPackedPool<PendingActionActivationComponent>().Has(PlayerEntityId);
        public bool HasPendingConsumable => ComponentManager.GetPackedPool<PendingConsumableActivationComponent>().Has(PlayerEntityId);
        public Guid PendingActionId => ComponentManager.GetPackedPool<PendingActionActivationComponent>().GetReadonly(PlayerEntityId).ActionId;

        public void Frame(long frame, params Keys[] held)
        {
            Clock.Advance(frame);
            var current = new KeyboardState(held);
            Movement.HandleInput(current, _previous, new HashSet<Keys>());
            _previous = current;
        }

        public void ConfirmSelfAction(long frame)
        {
            Clock.Advance(frame);
            ActionTargeting.HandleHotkeySlotPress(HotkeySlot.Slot1);
            ActionTargeting.HandleHotkeySlotPress(HotkeySlot.Slot1);
        }

        public void ConfirmDodgeInPlace(long frame)
        {
            Clock.Advance(frame);
            ActionTargeting.HandleHotkeySlotPress(HotkeySlot.DefaultAttack);
            ActionTargeting.HandleHotkeySlotPress(HotkeySlot.DefaultAttack);
        }
    }

    private static Harness Build(bool locked = true)
    {
        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(20, 20, 1)));
        world.PlayerEntityId = PlayerEntityId;
        var mapViewState = new MapViewState();

        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(20, 10));

        TestTransforms.Set(componentManager, PlayerEntityId, new TransformComponent(PlayerPosition, new Vector2Byte(1, 1)));
        componentManager.Merge(PlayerEntityId, new MovementComponent(MovementMode.PlayerControlled, null, null));
        componentManager.Merge(PlayerEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, unlockedAtFrame: locked ? LockEndsAtFrame : 0u));
        componentManager.Merge(PlayerEntityId, new HotkeyExpansionUnlockComponent(unlockedSlotCount: 5));
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(SelfActionId, overrideDefinition: null));
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(DodgeAction.Id, overrideDefinition: null));
        componentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot1, SelfActionId));
        componentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.DefaultAttack, DodgeAction.Id));

        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(new ActionDefinition(
            SelfActionId, "Test Self Action", null, "*", Color.White, [GameTags.TargetingSelf],
            Effects: [Effect.None],
            Activator: new SpellActivator(
                new TargetingSpec(TargetShape.Self, Range: 0),
                new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null))));
        actionCatalog.Register(DodgeAction.Build());

        var clock = new SimulationClock();
        var eventBus = new EventBus();
        var entityActions = TestActionStateViews.EntityActions(componentManager, actionCatalog);
        var playerCommands = new PlayerCommands(
            world,
            componentManager.GetDirectPool<TransformComponent>(),
            componentManager.GetPackedPool<MovementComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<PendingActionActivationComponent>(),
            componentManager.GetPackedPool<PendingConsumableActivationComponent>(),
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            clock,
            entityActions,
            eventBus);

        var actionTargeting = new ActionTargetingController(
            world,
            mapViewState,
            new MapCamera(world),
            new UiLayerStack(),
            actionCatalog,
            new ItemCatalog(),
            new TransformView(componentManager),
            new HotkeyBindingView(componentManager),
            new InventoryView(componentManager, new ItemCatalog()),
            TestActionStateViews.Over(componentManager, actionCatalog),
            new AbilityScoreView(componentManager),
            playerCommands,
            simulationClock: clock);

        return new Harness
        {
            ActionTargeting = actionTargeting,
            Movement = new PlayerMovementController(playerCommands),
            Buffer = playerCommands,
            Clock = clock,
            ComponentManager = componentManager,
            EventBus = eventBus,
            Actions = entityActions,
        };
    }

    [TestMethod]
    public void ActionConfirmedDuringTheLock_WaitsAndIsWrittenWhenTheLockClears()
    {
        var harness = Build();

        harness.ConfirmSelfAction(50);
        harness.Frame(59);
        Assert.IsFalse(harness.HasPendingAction);

        harness.Frame(LockEndsAtFrame);
        Assert.AreEqual(SelfActionId, harness.PendingActionId);
    }

    [TestMethod]
    public void ActionConfirmedWhileFree_IsWrittenTheSameFrame()
    {
        var harness = Build(locked: false);

        harness.ConfirmSelfAction(10);

        Assert.AreEqual(SelfActionId, harness.PendingActionId);
    }

    [TestMethod]
    public void ActionOlderThanTheBufferWindow_IsDropped()
    {
        var harness = Build();

        harness.ConfirmSelfAction(LockEndsAtFrame - PlayerCommands.ExpiryFrames);
        harness.Frame(LockEndsAtFrame);

        Assert.IsFalse(harness.HasPendingAction);
    }

    [TestMethod]
    public void ActionQueuedJustBeforeItsCooldownEnds_IsWrittenTheFrameItEnds()
    {
        var harness = Build(locked: false);
        harness.Clock.Advance(10);
        harness.Actions.SetCooldown(PlayerEntityId, SelfActionId, 2, now: 10);

        Assert.IsTrue(harness.Buffer.QueueAction(SelfActionId, [PlayerPosition]));
        Assert.IsFalse(harness.HasPendingAction);

        harness.Frame(11);
        Assert.IsFalse(harness.HasPendingAction);

        harness.Frame(12);
        Assert.AreEqual(SelfActionId, harness.PendingActionId);
    }

    [TestMethod]
    public void ActionWhoseCooldownOutlastsTheBuffer_IsRefused_AndLeavesTheBufferedCommand()
    {
        var harness = Build();
        harness.Clock.Advance(50);
        Assert.IsTrue(harness.Buffer.QueueConsumable(StackInstanceId, [PlayerPosition]));
        harness.Actions.SetCooldown(PlayerEntityId, SelfActionId, (ushort)(PlayerCommands.ExpiryFrames + 5), now: 50);

        Assert.IsFalse(harness.Buffer.CanQueueAction(SelfActionId));
        Assert.IsFalse(harness.Buffer.QueueAction(SelfActionId, [PlayerPosition]));

        harness.Frame(LockEndsAtFrame);
        Assert.IsTrue(harness.HasPendingConsumable);
        Assert.IsFalse(harness.HasPendingAction);
    }

    [TestMethod]
    public void ActionOrConsumableConfirmedTooEarlyInALongLock_IsRefused()
    {
        var harness = Build();
        harness.Clock.Advance(LockEndsAtFrame - PlayerCommands.ExpiryFrames);

        Assert.IsFalse(harness.Buffer.QueueAction(SelfActionId, [PlayerPosition]));
        Assert.IsFalse(harness.Buffer.QueueConsumable(StackInstanceId, [PlayerPosition]));

        harness.Clock.Advance(LockEndsAtFrame - PlayerCommands.ExpiryFrames + 1);

        Assert.IsTrue(harness.Buffer.CanQueueAction(SelfActionId));
        Assert.IsTrue(harness.Buffer.CanQueueConsumable());
    }

    [TestMethod]
    public void ActionThePlayerDoesNotHave_IsRefused()
    {
        var harness = Build(locked: false);

        Assert.IsFalse(harness.Buffer.QueueAction(Guid.NewGuid(), [PlayerPosition]));
        Assert.IsFalse(harness.HasPendingAction);
    }

    [TestMethod]
    public void ConfirmedAction_ReplacesAMoveBufferedBeforeIt()
    {
        var harness = Build();

        harness.Frame(50, Keys.W);
        harness.Frame(51);
        harness.ConfirmSelfAction(55);
        harness.Frame(LockEndsAtFrame);

        Assert.AreEqual(SelfActionId, harness.PendingActionId);
        Assert.IsNull(harness.NextMapPosition);
    }

    [TestMethod]
    public void ConfirmedAction_WithdrawsAStepMovementSystemHasNotTakenYet()
    {
        var harness = Build(locked: false);

        harness.Frame(10, Keys.W);
        Assert.AreEqual(North, harness.NextMapPosition);

        harness.ConfirmSelfAction(11);

        Assert.AreEqual(SelfActionId, harness.PendingActionId);
        Assert.IsNull(harness.NextMapPosition);
    }

    [TestMethod]
    public void MoveTappedAfterABufferedAction_ReplacesIt()
    {
        var harness = Build();

        harness.ConfirmSelfAction(50);
        harness.Frame(55, Keys.W);
        harness.Frame(56);
        harness.Frame(LockEndsAtFrame);

        Assert.IsFalse(harness.HasPendingAction);
        Assert.AreEqual(North, harness.NextMapPosition);
    }

    [TestMethod]
    public void HeldKey_DoesNotReplaceAnActionPressedAfterIt()
    {
        var harness = Build();

        harness.Frame(50, Keys.W);
        harness.ConfirmSelfAction(55);
        harness.Frame(56, Keys.W);
        harness.Frame(LockEndsAtFrame, Keys.W);
        harness.Frame(LockEndsAtFrame + 1, Keys.W);

        Assert.AreEqual(SelfActionId, harness.PendingActionId);
        Assert.IsNull(harness.NextMapPosition);
    }

    [TestMethod]
    public void Consumable_FollowsTheSameRules_AndReplacesABufferedAction()
    {
        var harness = Build();

        harness.ConfirmSelfAction(50);
        harness.Clock.Advance(55);
        harness.Buffer.QueueConsumable(StackInstanceId, [PlayerPosition]);
        harness.Frame(59);
        Assert.IsFalse(harness.HasPendingConsumable);

        harness.Frame(LockEndsAtFrame);

        Assert.IsTrue(harness.HasPendingConsumable);
        Assert.IsFalse(harness.HasPendingAction);
    }

    [TestMethod]
    public void FreeCastAction_IsWrittenAtOnceDuringTheLock_AndDropsTheBufferedMove()
    {
        var harness = Build();

        harness.Frame(50, Keys.W);
        harness.Frame(51);
        harness.ConfirmDodgeInPlace(52);
        Assert.AreEqual(DodgeAction.Id, harness.PendingActionId);

        harness.Frame(LockEndsAtFrame);

        Assert.IsNull(harness.NextMapPosition);
    }

    [TestMethod]
    public void Cancel_DropsTheBufferedCommand_AndReportsNothingLeftOnceItIsGone()
    {
        var harness = Build();

        harness.ConfirmSelfAction(50);

        Assert.IsTrue(harness.ActionTargeting.CancelArmedOrPendingAction());
        Assert.IsFalse(harness.ActionTargeting.CancelArmedOrPendingAction());

        harness.Frame(LockEndsAtFrame);
        Assert.IsFalse(harness.HasPendingAction);
    }

    [TestMethod]
    public void Stagger_DropsTheBufferedAction()
    {
        var harness = Build();

        harness.ConfirmSelfAction(50);
        harness.EventBus.Publish(new EntityStaggeredEvent(PlayerEntityId, ActionSource.AI));
        harness.Frame(LockEndsAtFrame);

        Assert.IsFalse(harness.HasPendingAction);
    }

    [TestMethod]
    public void Stagger_DropsTheBufferedMove()
    {
        var harness = Build();

        harness.Frame(50, Keys.W);
        harness.Frame(51);
        harness.EventBus.Publish(new EntityStaggeredEvent(PlayerEntityId, ActionSource.AI));
        harness.Frame(LockEndsAtFrame);

        Assert.IsNull(harness.NextMapPosition);
    }

    [TestMethod]
    public void Stagger_DoesNotCancelHeldMovement()
    {
        var harness = Build();

        harness.Frame(50, Keys.W);
        harness.EventBus.Publish(new EntityStaggeredEvent(PlayerEntityId, ActionSource.AI));
        harness.Frame(55, Keys.W);
        harness.Frame(LockEndsAtFrame, Keys.W);

        Assert.AreEqual(North, harness.NextMapPosition);
    }

    [TestMethod]
    public void StaggerOfAnotherEntity_LeavesThePlayersBufferAlone()
    {
        var harness = Build();

        harness.ConfirmSelfAction(50);
        harness.EventBus.Publish(new EntityStaggeredEvent(PlayerEntityId + 1, ActionSource.AI));
        harness.Frame(LockEndsAtFrame);

        Assert.AreEqual(SelfActionId, harness.PendingActionId);
    }

    [TestMethod]
    public void Stagger_KeepsAStepWaitingOnADodgeAlreadyConfirmed()
    {
        var harness = Build();
        harness.Clock.Advance(50);
        harness.ActionTargeting.HandleHotkeySlotPress(HotkeySlot.DefaultAttack);
        harness.ActionTargeting.TryClaimDodgeDirectionalKey(new KeyboardState(Keys.D), new KeyboardState(), []);

        harness.EventBus.Publish(new EntityStaggeredEvent(PlayerEntityId, ActionSource.AI));
        harness.ComponentManager.GetPackedPool<PendingActionActivationComponent>().Remove(PlayerEntityId);
        harness.EventBus.Publish(new ActionActivatedEvent(PlayerEntityId, DodgeAction.Id));

        Assert.AreEqual(new Vector3Int(6, 5, 0), harness.NextMapPosition);
    }
}
