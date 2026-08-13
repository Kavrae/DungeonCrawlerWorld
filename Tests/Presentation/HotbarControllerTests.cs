using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Mana.Components;
using Game.Modules.Movement.Components;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Content;

namespace Tests.Presentation;

/// <summary>
/// Covers the actual new behavior: clicking a hotbar slot (HotbarController.OnSlotTapped) must
/// behave exactly like pressing that slot's key (ActionTargetingController.HandleHotkeySlotPress)
/// -- arm an unarmed bound slot, confirm/fire an already-armed one, rather than the old
/// click-to-preview/click-to-cancel behavior. Uses a real ActionTargetingController (not a fake)
/// so this actually proves the forwarding wiring works end-to-end, not just that OnSlotTapped
/// calls some method.
/// </summary>
[TestClass]
public sealed class HotbarControllerTests
{
    private const int PlayerEntityId = 1;
    private static readonly Vector3Int PlayerPosition = new(5, 5, 0);
    private static readonly Guid TestActionId = new("55555555-6666-7777-8888-999999999999");

    private static (HotbarController Controller, MapViewState MapViewState, ComponentManager ComponentManager) Build()
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
        componentManager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PendingActionActivationComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PendingConsumableActivationComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PendingDelayedActionComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ActionLockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ManaComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PotionCooldownComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<HotkeyExpansionUnlockComponent>(static (ref existing, incoming) => existing = incoming);

        componentManager.Merge(PlayerEntityId, new TransformComponent(PlayerPosition, new Vector2Byte(1, 1)));
        componentManager.Merge(PlayerEntityId, new MovementComponent(MovementMode.PlayerControlled, 0, null, null));
        componentManager.Merge(PlayerEntityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(TestActionId, damageAmount: 0, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new HotkeyExpansionUnlockComponent(unlockedSlotCount: 5));
        componentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot1, TestActionId));

        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(new ActionDefinition(
            TestActionId, "Test Self Spell", null, "*", default, [],
            Effects: [ActionEffect.None],
            Activator: new SpellActivator(
                new TargetingSpec(TargetShape.Self, Range: 0),
                new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null))));
        var itemCatalog = new ItemCatalog();

        var camera = new MapCamera(world);
        var actionTargeting = new ActionTargetingController(
            world,
            mapViewState,
            camera,
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
            componentManager.GetPackedPool<ManaComponent>());

        var fontService = new FontService("Fonts");
        var hotbarContent = new HotbarContent(world, mapViewState, componentManager, new EventBus(), actionCatalog, itemCatalog, fontService, new SpriteSheetService(null, "Spritesheets"), new SpriteRenderer(), new Vector2(1920, 1080));
        var hotbarController = new HotbarController(mapViewState, hotbarContent, actionTargeting);

        return (hotbarController, mapViewState, componentManager);
    }

    [TestMethod]
    public void OnSlotTapped_UnarmedBoundSlot_ArmsItExactlyLikeAKeyPressWould()
    {
        var (controller, mapViewState, _) = Build();

        controller.OnSlotPressed(HotkeySlot.Slot1);
        controller.OnSlotTapped(HotkeySlot.Slot1);

        Assert.AreEqual(TestActionId, mapViewState.ArmedActionId);
        Assert.AreEqual(HotkeySlot.Slot1, mapViewState.ArmedSlot);
    }

    /// <summary>Regression coverage for the actual behavior change: clicking the already-armed slot used to cancel it (click-to-preview era); it must now confirm/fire instead, the same as re-pressing the key does.</summary>
    [TestMethod]
    public void OnSlotTapped_AlreadyArmedSlot_ConfirmsAgainstHoveredTileInsteadOfCancelling()
    {
        var (controller, mapViewState, componentManager) = Build();
        controller.OnSlotPressed(HotkeySlot.Slot1);
        controller.OnSlotTapped(HotkeySlot.Slot1);
        Assert.IsNotNull(mapViewState.ArmedActionId, "Sanity check: the first tap must have armed it.");

        mapViewState.HoveredTile = PlayerPosition; // Self-targeted -- always resolves to the caster's own tile regardless of where the cursor actually is.

        controller.OnSlotPressed(HotkeySlot.Slot1);
        controller.OnSlotTapped(HotkeySlot.Slot1);

        Assert.IsNull(mapViewState.ArmedActionId, "A confirmed activation disarms.");
        Assert.IsTrue(componentManager.GetPackedPool<PendingActionActivationComponent>().Has(PlayerEntityId), "The second tap must have queued a real activation, not just cancelled the arm.");
    }

    [TestMethod]
    public void OnSlotTapped_ReleaseSlotDoesNotMatchPressedSlot_DoesNothing()
    {
        var (controller, mapViewState, _) = Build();

        controller.OnSlotPressed(HotkeySlot.Slot2); // Pressed a different, unbound slot...
        controller.OnSlotTapped(HotkeySlot.Slot1); // ...but the release is reported against this one.

        Assert.IsNull(mapViewState.ArmedActionId);
    }

    [TestMethod]
    public void OnSlotTapped_UnboundSlot_DoesNothing()
    {
        var (controller, mapViewState, _) = Build();

        controller.OnSlotPressed(HotkeySlot.Slot2);
        controller.OnSlotTapped(HotkeySlot.Slot2);

        Assert.IsNull(mapViewState.ArmedActionId);
        Assert.IsNull(mapViewState.ArmedItemDefinitionId);
    }

    /// <summary>
    /// The reported bug: a binding placed on a not-yet-unlocked Expansion slot (e.g. by a
    /// blueprint grant, which -- unlike HotbarContent.BindItem's own drag-drop path -- writes
    /// the ItemHotkeyBindingComponent/ActionHotkeyBindingComponent directly and never checks
    /// the lock) must still refuse to activate, not just render dim.
    /// </summary>
    [TestMethod]
    public void OnSlotTapped_BoundButLockedSlot_DoesNothing()
    {
        var (controller, mapViewState, componentManager) = Build();
        // Slot6 is the 6th Expansion slot, beyond Build()'s 5 unlocked -- bound directly the same
        // way a blueprint grant would, bypassing HotbarContent.BindItem's own lock refusal.
        componentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot6, TestActionId));

        controller.OnSlotPressed(HotkeySlot.Slot6);
        controller.OnSlotTapped(HotkeySlot.Slot6);

        Assert.IsNull(mapViewState.ArmedActionId, "A locked slot must not arm even though something is bound to it.");
    }
}
