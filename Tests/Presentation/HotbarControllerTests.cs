using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Engine.Tiles;
using Game.Effects;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement.Components;
using Game.Views;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Input;
using Presentation.UI;
using Presentation.UI.Content;
using Tests.UIEngine;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Tests.Presentation;

/// <summary>
/// Covers the actual new behavior: clicking a hotbar slot (HotbarController.OnSlotClicked) must
/// behave exactly like pressing that slot's key (ActionTargetingController.HandleHotkeySlotPress)
/// -- arm an unarmed bound slot, confirm/fire an already-armed one, rather than the old
/// click-to-preview/click-to-cancel behavior. Uses a real ActionTargetingController (not a fake)
/// so this actually proves the forwarding wiring works end-to-end, not just that OnSlotClicked
/// calls some method.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class HotbarControllerTests
{
    private const int PlayerEntityId = 1;
    private static readonly Vector3Int PlayerPosition = new(5, 5, 0);
    private static readonly Guid TestActionId = new("55555555-6666-7777-8888-999999999999");

    private static (HotbarController Controller, MapViewState MapViewState, ComponentManager ComponentManager, HotbarContent HotbarContent, ContextMenuController ContextMenuController) Build()
    {
        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(20, 20, 1)), playerEntityId: PlayerEntityId);
        var mapViewState = new MapViewState();

        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(20, 10));

        TestTransforms.Set(componentManager, PlayerEntityId, new TransformComponent(PlayerPosition, new Vector2Byte(1, 1)));
        componentManager.Merge(PlayerEntityId, new MovementComponent(MovementMode.PlayerControlled, null, null));
        componentManager.Merge(PlayerEntityId, new ActionLockComponent(currentLockTotalFrames: 0, unlockedAtFrame: 0));
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(TestActionId, overrideDefinition: null));
        componentManager.Merge(PlayerEntityId, new HotkeyExpansionUnlockComponent(unlockedSlotCount: 5));
        componentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot1, TestActionId));

        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(new ActionDefinition(
            TestActionId, "Test Self Spell", null, "*", default, [],
            Effects: [Effect.None],
            Activator: new SpellActivator(
                new TargetingSpec(TargetShape.Self, Range: 0),
                new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null))));
        var itemCatalog = new ItemCatalog();

        var camera = TestMapCameras.Over(world);
        var actionTargeting = new ActionTargetingController(
            world,
            mapViewState,
            camera,
            new UiLayerStack(),
            itemCatalog,
            new TransformView(componentManager),
            new HotkeyBindingView(componentManager),
            new InventoryView(componentManager, itemCatalog),
            TestActionStateViews.Over(componentManager, actionCatalog, itemCatalog),
            TestActionStateViews.Targeting(componentManager, world, actionCatalog, itemCatalog),
            new PlayerCommands(
                world,
                componentManager.GetDirectPool<TransformComponent>(),
                componentManager.GetPackedPool<MovementComponent>(),
                componentManager.GetPackedPool<ActionLockComponent>(),
                componentManager.GetPackedPool<PendingActionActivationComponent>(),
                componentManager.GetPackedPool<PendingItemActivationComponent>(),
                componentManager.GetPackedPool<PendingWindupComponent>(),
                componentManager.GetMultiPool<InventoryItemStackComponent>(),
                itemCatalog,
                new Engine.ECS.Systems.SimulationClock(),
                TestActionStateViews.EntityActions(componentManager, actionCatalog),
                new EventBus()),
            simulationClock: new SimulationClock());

        var fontService = TestFonts.Shared;
        var hotbarContent = new HotbarContent(world, mapViewState, new HotkeyBindingView(componentManager), new InventoryView(componentManager, itemCatalog), TestActionStateViews.Over(componentManager, actionCatalog, itemCatalog), new HotkeyBindingCommands(componentManager, itemCatalog, new EventBus()), itemCatalog, fontService, new SpriteSheetService(null, "Spritesheets", TestSprites.Resolver), new Vector2(1920, 1080), simulationClock: new SimulationClock());
        var tooltipPool = TestElementPoolServiceFactory.Create(fontService);
        tooltipPool.RegisterFactory<Tooltip>(() => new Tooltip(fontService, tooltipPool, new UiRenderService()));
        var tooltipController = new TooltipController { ScreenBoundsOverrideForTests = new Rectangle(0, 0, 2000, 2000) };
        tooltipController.Initialize(tooltipPool, new UiLayerStack());
        var contextMenuController = TestElementPoolServiceFactory.CreateContextMenuController(tooltipPool, new UiLayerStack());
        var hotbarController = new HotbarController(mapViewState, hotbarContent, actionTargeting, tooltipController, contextMenuController);

        return (hotbarController, mapViewState, componentManager, hotbarContent, contextMenuController);
    }

    private static Button MenuOption(ContextMenuController contextMenuController, string label) =>
        contextMenuController.Dropdowns.Menu.ChildElements.OfType<Button>().Single(button => button.LeftText == label);

    [TestMethod]
    public void RightClickingABoundActionSlot_OffersArmThenUnbind()
    {
        var (controller, _, _, _, contextMenuController) = Build();

        controller.OnSlotRightClicked(HotkeySlot.Slot1, new Point(100, 100));

        Assert.IsTrue(contextMenuController.Dropdowns.IsOpen);
        CollectionAssert.AreEqual(new[] { "Arm", "Unbind" }, contextMenuController.Dropdowns.Menu.ChildElements.OfType<Button>().Select(button => button.LeftText).ToArray());
        Assert.IsTrue(MenuOption(contextMenuController, "Arm").Enabled);
    }

    [TestMethod]
    public void ChoosingArm_ArmsTheSlot_AndItsArmIsDisabledOnceArmed()
    {
        var (controller, mapViewState, _, _, contextMenuController) = Build();
        controller.OnSlotRightClicked(HotkeySlot.Slot1, new Point(100, 100));

        var arm = MenuOption(contextMenuController, "Arm");
        arm.HandleClick(arm.Rectangle.Center);

        Assert.AreEqual(HotkeySlot.Slot1, mapViewState.ArmedSlot);
        controller.OnSlotRightClicked(HotkeySlot.Slot1, new Point(100, 100));
        Assert.IsFalse(MenuOption(contextMenuController, "Arm").Enabled);
    }

    [TestMethod]
    public void ChoosingUnbind_ClearsTheSlot_AndTheArmedSlot()
    {
        var (controller, mapViewState, componentManager, _, contextMenuController) = Build();
        controller.OnSlotClicked(HotkeySlot.Slot1);
        controller.OnSlotRightClicked(HotkeySlot.Slot1, new Point(100, 100));

        var unbind = MenuOption(contextMenuController, "Unbind");
        unbind.HandleClick(unbind.Rectangle.Center);

        Assert.IsFalse(ActionHotkeyBindingQueries.TryGet(componentManager.GetMultiPool<ActionHotkeyBindingComponent>(), PlayerEntityId, HotkeySlot.Slot1, out _));
        Assert.IsNull(mapViewState.ArmedSlot);
    }

    [TestMethod]
    public void OnSlotClicked_UnarmedBoundSlot_ArmsItExactlyLikeAKeyPressWould()
    {
        var (controller, mapViewState, _, _, _) = Build();

        controller.OnSlotClicked(HotkeySlot.Slot1);

        Assert.AreEqual(TestActionId, mapViewState.ArmedActionId);
        Assert.AreEqual(HotkeySlot.Slot1, mapViewState.ArmedSlot);
    }

    /// <summary>Regression coverage for the actual behavior change: clicking the already-armed slot used to cancel it (click-to-preview era); it must now confirm/fire instead, the same as re-pressing the key does.</summary>
    [TestMethod]
    public void OnSlotClicked_AlreadyArmedSlot_ConfirmsAgainstHoveredTileInsteadOfCancelling()
    {
        var (controller, mapViewState, componentManager, _, _) = Build();
        controller.OnSlotClicked(HotkeySlot.Slot1);
        Assert.IsNotNull(mapViewState.ArmedActionId, "Sanity check: the first tap must have armed it.");

        mapViewState.HoveredTile = PlayerPosition; // Self-targeted -- always resolves to the caster's own tile regardless of where the cursor actually is.

        controller.OnSlotClicked(HotkeySlot.Slot1);

        Assert.IsNull(mapViewState.ArmedActionId, "A confirmed activation disarms.");
        Assert.IsTrue(componentManager.GetPackedPool<PendingActionActivationComponent>().Has(PlayerEntityId), "The second tap must have queued a real activation, not just cancelled the arm.");
    }

    [TestMethod]
    public void OnSlotClicked_UnboundSlot_DoesNothing()
    {
        var (controller, mapViewState, _, _, _) = Build();

        controller.OnSlotClicked(HotkeySlot.Slot2);

        Assert.IsNull(mapViewState.ArmedActionId);
        Assert.IsNull(mapViewState.ArmedItemStackInstanceId);
    }

    /// <summary>
    /// The reported bug: a binding placed on a not-yet-unlocked Expansion slot (e.g. by a
    /// blueprint grant, which -- unlike HotbarContent.BindItem's own drag-drop path -- writes
    /// the ItemHotkeyBindingComponent/ActionHotkeyBindingComponent directly and never checks
    /// the lock) must still refuse to activate, not just render dim.
    /// </summary>
    [TestMethod]
    public void OnSlotClicked_BoundButLockedSlot_DoesNothing()
    {
        var (controller, mapViewState, componentManager, _, _) = Build();
        // Slot6 is the 6th Expansion slot, beyond Build()'s 5 unlocked -- bound directly the same
        // way a blueprint grant would, bypassing HotbarContent.BindItem's own lock refusal.
        componentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot6, TestActionId));

        controller.OnSlotClicked(HotkeySlot.Slot6);

        Assert.IsNull(mapViewState.ArmedActionId, "A locked slot must not arm even though something is bound to it.");
    }

    private static (Window HotbarWindow, Point SlotCenter) HostHotbar(HotbarContent hotbarContent)
    {
        var windowService = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var hotbarWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(100, 100), Size = hotbarContent.Size, DisplayMode = ElementDisplayMode.Fixed },
        });
        hotbarWindow.SetContent(hotbarContent);
        hotbarWindow.Initialize();

        return (hotbarWindow, hotbarContent.GetSlotBounds(HotkeySlot.Slot1).Center);
    }

    private static MouseState MouseAt(Point position, ButtonState leftButton) =>
        new(position.X, position.Y, 0, leftButton, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

    private static UiInputController InputOver(Window hotbarWindow, HotbarController controller, ComponentManager componentManager, MapViewState mapViewState)
    {
        var layers = new UiLayerStack();
        layers.Add(UiLayer.StaticHud, hotbarWindow);
        return TestUiInputController.Create(layers, new Vector2(2000, 2000), componentManager, TestPlayerQuery.NoPlayer, new EventBus(), new ItemCatalog(), hotbarController: controller, mapViewState: mapViewState);
    }

    [TestMethod]
    public void ClickOnSlot_WithJitterUnderTheTapThreshold_ArmsIt()
    {
        var (controller, mapViewState, componentManager, hotbarContent, _) = Build();
        var (hotbarWindow, slotCenter) = HostHotbar(hotbarContent);
        var input = InputOver(hotbarWindow, controller, componentManager, mapViewState);

        input.Update(new KeyboardState(), MouseAt(slotCenter, ButtonState.Released));
        input.Update(new KeyboardState(), MouseAt(slotCenter, ButtonState.Pressed));
        input.Update(new KeyboardState(), MouseAt(slotCenter + new Point(2, 0), ButtonState.Pressed));
        input.Update(new KeyboardState(), MouseAt(slotCenter + new Point(2, 0), ButtonState.Released));

        Assert.AreEqual(TestActionId, mapViewState.ArmedActionId);
        Assert.AreEqual(HotkeySlot.Slot1, mapViewState.ArmedSlot);
    }

    [TestMethod]
    public void PressOnOneSlot_ReleaseOnAnother_ArmsNeither()
    {
        var (controller, mapViewState, componentManager, hotbarContent, _) = Build();
        var (hotbarWindow, _) = HostHotbar(hotbarContent);
        var input = InputOver(hotbarWindow, controller, componentManager, mapViewState);
        var unboundSlotCenter = hotbarContent.GetSlotBounds(HotkeySlot.Slot2).Center;
        var boundSlotCenter = hotbarContent.GetSlotBounds(HotkeySlot.Slot1).Center;

        input.Update(new KeyboardState(), MouseAt(unboundSlotCenter, ButtonState.Released));
        input.Update(new KeyboardState(), MouseAt(unboundSlotCenter, ButtonState.Pressed));
        input.Update(new KeyboardState(), MouseAt(boundSlotCenter, ButtonState.Released));

        Assert.IsNull(mapViewState.ArmedActionId);
    }

    [TestMethod]
    public void DragOffSlotAndBack_DoesNotArmIt()
    {
        var (controller, mapViewState, componentManager, hotbarContent, _) = Build();
        var (hotbarWindow, slotCenter) = HostHotbar(hotbarContent);
        var input = InputOver(hotbarWindow, controller, componentManager, mapViewState);

        input.Update(new KeyboardState(), MouseAt(slotCenter, ButtonState.Released));
        input.Update(new KeyboardState(), MouseAt(slotCenter, ButtonState.Pressed));
        input.Update(new KeyboardState(), MouseAt(slotCenter + new Point(30, 0), ButtonState.Pressed));
        input.Update(new KeyboardState(), MouseAt(slotCenter + new Point(1, 0), ButtonState.Pressed));
        input.Update(new KeyboardState(), MouseAt(slotCenter + new Point(1, 0), ButtonState.Released));

        Assert.IsNull(mapViewState.ArmedActionId);
        Assert.IsTrue(componentManager.GetMultiPool<ActionHotkeyBindingComponent>().Has(PlayerEntityId), "Dropped back on its own slot, the binding stays.");
    }
}
