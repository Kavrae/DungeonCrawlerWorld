using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Mana.Components;
using Game.Modules.Movement.Components;
using Microsoft.Xna.Framework.Input;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>
/// Regression coverage for a real, confirmed bug: Dodge's directional relocation used to call
/// World.MoveEntity directly, bypassing MovementComponent.NextMapPosition entirely -- which both
/// desynced TransformComponent.Position from Map's own occupancy index (making the player's sprite
/// vanish -- see MapWindow.DrawPrimaryOccupant) and left ordinary movement's own queued destination
/// stale, causing MovementSystem to later "catch up" and silently revert the dodge. Routing through
/// the same NextMapPosition queue ordinary WASD movement uses (ActionTargetingController.
/// TryRelocateForDodge) fixes both -- these tests assert that queuing, not a real test of
/// MovementSystem itself (see MovementSystemTests for that).
/// </summary>
[TestClass]
public sealed class ActionTargetingControllerDodgeTests
{
    private const int PlayerEntityId = 1;
    private static readonly Vector3Int PlayerPosition = new(5, 5, 0);

    private static (ActionTargetingController ActionTargeting, MapViewState MapViewState, ComponentManager ComponentManager) Build()
    {
        var world = new Game.World.World(new Game.World.Map(new Vector3Int(20, 20, 1))) { PlayerEntityId = PlayerEntityId };
        var mapViewState = new MapViewState();

        var componentManager = new ComponentManager(20, 10);
        componentManager.RegisterDirectPool<TransformComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<MovementComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<ActionInstanceComponent>();
        componentManager.RegisterMultiPool<ActionHotkeyBindingComponent>();
        componentManager.RegisterMultiPool<ItemHotkeyBindingComponent>();
        componentManager.RegisterMultiPool<InventoryItemStackComponent>();
        componentManager.RegisterPackedPool<PendingActionActivationComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PendingConsumableActivationComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PendingDelayedActionComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ActionLockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ManaComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<HotkeyExpansionUnlockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<AbilityScoreComponent>();

        componentManager.Merge(PlayerEntityId, new TransformComponent(PlayerPosition, new Vector2Byte(1, 1)));
        componentManager.Merge(PlayerEntityId, new MovementComponent(MovementMode.PlayerControlled, null, null));
        componentManager.Merge(PlayerEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(DodgeAction.Id, overrideDefinition: null, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new HotkeyExpansionUnlockComponent(unlockedSlotCount: 5));
        componentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.DefaultAttack, DodgeAction.Id));

        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(DodgeAction.Build());
        var itemCatalog = new ItemCatalog();

        var camera = new MapCamera(world);
        var actionTargeting = new ActionTargetingController(
            world,
            mapViewState,
            camera,
            new UiLayerStack(),
            actionCatalog,
            itemCatalog,
            componentManager.GetDirectPool<TransformComponent>(),
            componentManager.GetMultiPool<ActionHotkeyBindingComponent>(),
            componentManager.GetMultiPool<ItemHotkeyBindingComponent>(),
            componentManager.GetMultiPool<InventoryItemStackComponent>(),
            componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>(),
            componentManager.GetPackedPool<PendingActionActivationComponent>(),
            componentManager.GetPackedPool<PendingConsumableActivationComponent>(),
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<MovementComponent>(),
            componentManager.GetPackedPool<ManaComponent>(),
            componentManager.GetMultiPool<AbilityScoreComponent>());

        return (actionTargeting, mapViewState, componentManager);
    }

    [TestMethod]
    public void TryClaimDodgeDirectionalKey_DodgeArmed_QueuesNextMapPosition_DoesNotMoveImmediately()
    {
        var (actionTargeting, mapViewState, componentManager) = Build();
        actionTargeting.HandleHotkeySlotPress(HotkeySlot.DefaultAttack);
        Assert.AreEqual(DodgeAction.Id, mapViewState.ArmedActionId, "Sanity check: Dodge must actually be armed before exercising the directional confirm.");
        var claimedKeys = new HashSet<Keys>();

        actionTargeting.TryClaimDodgeDirectionalKey(new KeyboardState(Keys.D), new KeyboardState(), claimedKeys);

        var movementPool = componentManager.GetPackedPool<MovementComponent>();
        Assert.AreEqual(new Vector3Int(6, 5, 0), movementPool.GetReadonly(PlayerEntityId).NextMapPosition,
            "The directional dodge must queue the same NextMapPosition ordinary movement uses, not move the entity directly.");
        Assert.AreEqual(PlayerPosition, componentManager.GetDirectPool<TransformComponent>().GetReadonly(PlayerEntityId).Position,
            "TransformComponent.Position must be untouched until MovementSystem actually applies the queued move -- a direct change here is exactly the bug this guards against.");
        Assert.IsTrue(claimedKeys.Contains(Keys.D), "The key must be claimed so PlayerMovementController doesn't also treat it as an ordinary move this frame.");
    }

    [TestMethod]
    public void TryClaimDodgeDirectionalKey_OverwritesAnyStalePreviouslyQueuedMove()
    {
        var (actionTargeting, _, componentManager) = Build();
        var movementPool = componentManager.GetPackedPool<MovementComponent>();

        // Simulate the player already mid-stride from ordinary WASD movement -- a stale queued
        // destination unrelated to where the dodge is about to send them.
        movementPool.TryUpdate(PlayerEntityId, static (ref MovementComponent m) => m.NextMapPosition = new Vector3Int(9, 9, 0));

        actionTargeting.HandleHotkeySlotPress(HotkeySlot.DefaultAttack);
        actionTargeting.TryClaimDodgeDirectionalKey(new KeyboardState(Keys.A), new KeyboardState(), []);

        Assert.AreEqual(new Vector3Int(4, 5, 0), movementPool.GetReadonly(PlayerEntityId).NextMapPosition,
            "The dodge's own destination must replace any stale queued move, not sit alongside it -- a leftover stale destination is exactly what caused MovementSystem to revert the dodge.");
    }

    [TestMethod]
    public void TryClaimDodgeDirectionalKey_DodgeNotArmed_DoesNothing()
    {
        var (actionTargeting, _, componentManager) = Build();
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
        var (actionTargeting, _, componentManager) = Build();
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
    /// the double-tap look like a cancel. A Tag.Self action must always auto-target the caster's own
    /// tile instead, the same rule the single-press re-confirm path already gives it.
    /// </summary>
    [TestMethod]
    public void DoubleTappingDodgeHotkey_ActivatesOnSelf_InsteadOfSilentlyDoingNothing()
    {
        var (actionTargeting, mapViewState, componentManager) = Build();

        actionTargeting.HandleHotkeySlotPress(HotkeySlot.DefaultAttack);
        actionTargeting.HandleHotkeySlotPress(HotkeySlot.DefaultAttack);

        var pending = componentManager.GetPackedPool<PendingActionActivationComponent>().GetReadonly(PlayerEntityId);
        Assert.AreEqual(DodgeAction.Id, pending.ActionId);
        CollectionAssert.AreEqual(new[] { PlayerPosition }, pending.TargetTiles,
            "A double-tapped Dodge must activate on the caster's own tile, not silently fail to queue anything at all.");
        Assert.IsNull(mapViewState.ArmedActionId,
            "The pair's first press armed the slot; now that the second press has fired it, it should disarm -- but only after actually activating, not instead of activating.");
    }
}
