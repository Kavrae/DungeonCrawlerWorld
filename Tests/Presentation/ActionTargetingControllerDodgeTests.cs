using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement.Components;
using Game.Tags;
using Game.Views;
using Microsoft.Xna.Framework.Input;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>
/// A directional Dodge steps through MovementComponent.NextMapPosition, the path ordinary movement uses, and never
/// moves the entity directly. PlayerCommands writes the step only once the Dodge's ActionActivatedEvent arrives,
/// which ActivateDodge stands in for here -- these tests cover the Presentation side, not MovementSystem itself (see
/// MovementSystemTests for that).
/// </summary>
[TestClass]
public sealed class ActionTargetingControllerDodgeTests
{
    private const int PlayerEntityId = 1;
    private static readonly Vector3Int PlayerPosition = new(5, 5, 0);

    private static (ActionTargetingController ActionTargeting, MapViewState MapViewState, ComponentManager ComponentManager, PlayerMovementController PlayerMovement, SimulationClock Clock, EventBus EventBus) Build()
    {
        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(20, 20, 1)), playerEntityId: PlayerEntityId);
        var mapViewState = new MapViewState();

        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(20, 10));

        TestTransforms.Set(componentManager, PlayerEntityId, new TransformComponent(PlayerPosition, new Vector2Byte(1, 1)));
        componentManager.Merge(PlayerEntityId, new MovementComponent(MovementMode.PlayerControlled, null, null));
        componentManager.Merge(PlayerEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, unlockedAtFrame: 0));
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(DodgeAction.Id, overrideDefinition: null));
        componentManager.Merge(PlayerEntityId, new HotkeyExpansionUnlockComponent(unlockedSlotCount: 5));
        componentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.DefaultAttack, DodgeAction.Id));

        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(DodgeAction.Build());
        var itemCatalog = new ItemCatalog();

        var camera = new MapCamera(world);
        var clock = new SimulationClock();
        var eventBus = new EventBus();
        var playerCommands = new PlayerCommands(
            world,
            componentManager.GetDirectPool<TransformComponent>(),
            componentManager.GetPackedPool<MovementComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<PendingActionActivationComponent>(),
            componentManager.GetPackedPool<PendingConsumableActivationComponent>(),
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            clock,
            eventBus);
        var actionTargeting = new ActionTargetingController(
            world,
            mapViewState,
            camera,
            new UiLayerStack(),
            actionCatalog,
            itemCatalog,
            new TransformView(componentManager),
            new HotkeyBindingView(componentManager),
            new InventoryView(componentManager, itemCatalog),
            new ActionStateView(componentManager, localTierRoster: null),
            new AbilityScoreView(componentManager),
            playerCommands, simulationClock: new SimulationClock());

        return (actionTargeting, mapViewState, componentManager, new PlayerMovementController(playerCommands), clock, eventBus);
    }

    /// <summary>What ActionActivationSystem does with a Dodge request that succeeds: consumes it and publishes the activation.</summary>
    private static void ActivateDodge(ComponentManager componentManager, EventBus eventBus)
    {
        componentManager.GetPackedPool<PendingActionActivationComponent>().Remove(PlayerEntityId);
        eventBus.Publish(new Game.World.ActionActivatedEvent(PlayerEntityId, DodgeAction.Id));
    }

    [TestMethod]
    public void TryClaimDodgeDirectionalKey_DodgeArmed_StepsOnlyOnceTheDodgeActivates()
    {
        var (actionTargeting, mapViewState, componentManager, _, _, eventBus) = Build();
        actionTargeting.HandleHotkeySlotPress(HotkeySlot.DefaultAttack);
        Assert.AreEqual(DodgeAction.Id, mapViewState.ArmedActionId, "Sanity check: Dodge must actually be armed before exercising the directional confirm.");
        var claimedKeys = new HashSet<Keys>();

        actionTargeting.TryClaimDodgeDirectionalKey(new KeyboardState(Keys.D), new KeyboardState(), claimedKeys);

        var movementPool = componentManager.GetPackedPool<MovementComponent>();
        Assert.IsNull(movementPool.GetReadonly(PlayerEntityId).NextMapPosition, "No step until the Dodge has actually activated.");
        Assert.Contains(Keys.D, claimedKeys, "The key must be claimed so PlayerMovementController doesn't also treat it as an ordinary move this frame.");

        ActivateDodge(componentManager, eventBus);

        Assert.AreEqual(new Vector3Int(6, 5, 0), movementPool.GetReadonly(PlayerEntityId).NextMapPosition);
        Assert.AreEqual(PlayerPosition, componentManager.GetDirectPool<TransformComponent>().GetReadonly(PlayerEntityId).Position,
            "TransformComponent.Position must be untouched until MovementSystem actually takes the step.");
    }

    [TestMethod]
    public void TryClaimDodgeDirectionalKey_WithdrawsAnyStalePreviouslyQueuedMove()
    {
        var (actionTargeting, _, componentManager, _, _, eventBus) = Build();
        var movementPool = componentManager.GetPackedPool<MovementComponent>();
        movementPool.TryUpdate(PlayerEntityId, static (ref MovementComponent m) => m.NextMapPosition = new Vector3Int(9, 9, 0));

        actionTargeting.HandleHotkeySlotPress(HotkeySlot.DefaultAttack);
        actionTargeting.TryClaimDodgeDirectionalKey(new KeyboardState(Keys.A), new KeyboardState(), []);
        Assert.IsNull(movementPool.GetReadonly(PlayerEntityId).NextMapPosition, "The stale step must be withdrawn the moment the Dodge is confirmed.");

        ActivateDodge(componentManager, eventBus);

        Assert.AreEqual(new Vector3Int(4, 5, 0), movementPool.GetReadonly(PlayerEntityId).NextMapPosition);
    }

    [TestMethod]
    public void DodgeConsumedWithoutActivating_NeverSteps()
    {
        var (actionTargeting, _, componentManager, playerMovement, clock, eventBus) = Build();

        actionTargeting.HandleHotkeySlotPress(HotkeySlot.DefaultAttack);
        actionTargeting.TryClaimDodgeDirectionalKey(new KeyboardState(Keys.D), new KeyboardState(), []);
        componentManager.GetPackedPool<PendingActionActivationComponent>().Remove(PlayerEntityId);
        clock.Advance(1);
        playerMovement.HandleInput(new KeyboardState(), new KeyboardState(), new HashSet<Keys>());
        eventBus.Publish(new Game.World.ActionActivatedEvent(PlayerEntityId, DodgeAction.Id));

        Assert.IsNull(componentManager.GetPackedPool<MovementComponent>().GetReadonly(PlayerEntityId).NextMapPosition);
    }

    [TestMethod]
    public void TryClaimDodgeDirectionalKey_DodgeNotArmed_DoesNothing()
    {
        var (actionTargeting, _, componentManager, _, _, _) = Build();
        var claimedKeys = new HashSet<Keys>();

        actionTargeting.TryClaimDodgeDirectionalKey(new KeyboardState(Keys.D), new KeyboardState(), claimedKeys);

        Assert.IsNull(componentManager.GetPackedPool<MovementComponent>().GetReadonly(PlayerEntityId).NextMapPosition);
        Assert.IsEmpty(claimedKeys);
    }

    /// <summary>
    /// Regression coverage for a second, real bug found once the above fix landed: with the move
    /// only queued (not yet applied), the *destination* tile is empty at the moment
    /// ActionActivationSystem processes the activation -- if PendingActionActivationComponent.
    /// TargetTiles named that empty destination, ActionEffectResolver.Apply's own occupant lookup
    /// would find nobody there and DodgeActivation would never run at all (confirmed live: no
    /// DodgingComponent ever granted for a directional dodge). The stored TargetTiles must instead
    /// name the caster's own *current* tile, which is guaranteed occupied by the caster right now.
    /// </summary>
    [TestMethod]
    public void TryClaimDodgeDirectionalKey_QueuesEffectAgainstCastersCurrentTile_NotTheDestination()
    {
        var (actionTargeting, _, componentManager, _, _, _) = Build();
        actionTargeting.HandleHotkeySlotPress(HotkeySlot.DefaultAttack);

        actionTargeting.TryClaimDodgeDirectionalKey(new KeyboardState(Keys.D), new KeyboardState(), []);

        var pending = componentManager.GetPackedPool<PendingActionActivationComponent>().GetReadonly(PlayerEntityId);
        Assert.AreEqual(DodgeAction.Id, pending.ActionId);
        CollectionAssert.AreEqual(new[] { PlayerPosition }, pending.TargetTiles,
            "Must name the caster's own current tile (still occupied by the caster, since the move is only queued) -- not (6,5,0), the destination MovementSystem hasn't placed anyone at yet.");
    }

    /// <summary>
    /// Regression coverage for a real, confirmed bug: double-tapping Dodge's hotkey read as
    /// silently cancelling instead of activating. TryActivateWithAutoTarget's double-tap path
    /// filters the reachable set down to *occupied* tiles for ClosestPointSelector to pick from --
    /// exactly what QuickAttack/PowerAttack/ToxicStrike/MagicMissile want (auto-attack the nearest
    /// enemy), but Dodge's own reachable 3x3 block is normally all-empty, so that filter found no
    /// candidate, did nothing, and the caller's own "now that it fired, disarm" cleanup then made
    /// the double-tap look like a cancel. A GameTags.TargetingSelf action must always auto-target the caster's own
    /// tile instead, the same rule the single-press re-confirm path already gives it.
    /// </summary>
    [TestMethod]
    public void DoubleTappingDodgeHotkey_ActivatesOnSelf_InsteadOfSilentlyDoingNothing()
    {
        var (actionTargeting, mapViewState, componentManager, _, _, _) = Build();

        actionTargeting.HandleHotkeySlotPress(HotkeySlot.DefaultAttack);
        actionTargeting.HandleHotkeySlotPress(HotkeySlot.DefaultAttack);

        var pending = componentManager.GetPackedPool<PendingActionActivationComponent>().GetReadonly(PlayerEntityId);
        Assert.AreEqual(DodgeAction.Id, pending.ActionId);
        CollectionAssert.AreEqual(new[] { PlayerPosition }, pending.TargetTiles,
            "A double-tapped Dodge must activate on the caster's own tile, not silently fail to queue anything at all.");
        Assert.IsNull(mapViewState.ArmedActionId,
            "The pair's first press armed the slot; now that the second press has fired it, it should disarm -- but only after actually activating, not instead of activating.");
    }

    [TestMethod]
    public void DirectionalDodge_ReplacesMoveBufferedDuringTheLock()
    {
        var (actionTargeting, _, componentManager, playerMovement, clock, eventBus) = Build();
        var actionLocks = componentManager.GetPackedPool<ActionLockComponent>();
        ActionLockGate.Lock(actionLocks, PlayerEntityId, now: 0, framesToWait: 60);

        clock.Advance(50);
        playerMovement.HandleInput(new KeyboardState(Keys.W), new KeyboardState(), new HashSet<Keys>());
        actionTargeting.HandleHotkeySlotPress(HotkeySlot.DefaultAttack);
        actionTargeting.TryClaimDodgeDirectionalKey(new KeyboardState(Keys.D), new KeyboardState(), []);

        clock.Advance(51);
        ActivateDodge(componentManager, eventBus);
        ActionLockGate.Release(actionLocks, PlayerEntityId, now: 51);
        playerMovement.HandleInput(new KeyboardState(), new KeyboardState(), new HashSet<Keys>());
        Assert.AreEqual(new Vector3Int(6, 5, 0), componentManager.GetPackedPool<MovementComponent>().GetReadonly(PlayerEntityId).NextMapPosition);

        clock.Advance(60);
        playerMovement.HandleInput(new KeyboardState(), new KeyboardState(), new HashSet<Keys>());

        Assert.AreEqual(new Vector3Int(6, 5, 0), componentManager.GetPackedPool<MovementComponent>().GetReadonly(PlayerEntityId).NextMapPosition);
    }
}
