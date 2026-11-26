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
using Presentation.UI.Menus;
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

    private sealed record Parts(HotbarController Controller, MapViewState MapViewState, ComponentManager ComponentManager, HotbarContent HotbarContent, ContextMenuController ContextMenuController, ActionTargetingController ActionTargeting);

    private static (HotbarController Controller, MapViewState MapViewState, ComponentManager ComponentManager, HotbarContent HotbarContent, ContextMenuController ContextMenuController) Build()
    {
        var parts = BuildParts(new MenuCommandCatalog());
        return (parts.Controller, parts.MapViewState, parts.ComponentManager, parts.HotbarContent, parts.ContextMenuController);
    }

    private static Parts BuildParts(MenuCommandCatalog menuCommands)
    {
        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(20, 20, 1)), playerEntityId: PlayerEntityId);
        var mapViewState = new MapViewState();

        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(20, 10));

        TestTransforms.Set(componentManager, PlayerEntityId, new TransformComponent(PlayerPosition, new Vector2Byte(1, 1)));
        componentManager.Merge(PlayerEntityId, new MovementComponent(null, null));
        componentManager.Merge(PlayerEntityId, new ActionLockComponent(currentLockTotalFrames: 0, unlockedAtFrame: 0));
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(TestActionId, overrideDefinition: null));
        componentManager.Merge(PlayerEntityId, new HotkeyExpansionUnlockComponent(unlockedSlotCount: 5));
        componentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot1, TestActionId));

        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(new ActionDefinition(
            TestActionId, "Test Self Spell", null, "*", default, [],
            Effects: [Effect.None],
            Activator: new SpellActivator(
                new TargetingSpec(TargetShape.Self, MaximumRange: 0, Anchor: TargetAnchor.Caster),
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
            simulationClock: new SimulationClock(), dropPlacement: new TestPlayerItemDrops(), menuCommands: menuCommands);

        var fontService = TestFonts.Shared;
        var hotbarContent = new HotbarContent(world, mapViewState, new HotkeyBindingView(componentManager), new InventoryView(componentManager, itemCatalog), TestActionStateViews.Over(componentManager, actionCatalog, itemCatalog), new HotkeyBindingCommands(componentManager, itemCatalog, new EventBus()), itemCatalog, fontService, new SpriteSheetService(null, "Spritesheets", TestSprites.Resolver), new Vector2(1920, 1080), simulationClock: new SimulationClock(), menuCommands: menuCommands);
        var tooltipPool = TestElementPoolServiceFactory.Create(fontService);
        tooltipPool.RegisterFactory<Tooltip>(() => new Tooltip(TestElementServices.For(tooltipPool, fontService)));
        var tooltipController = new TooltipController { ScreenBoundsOverrideForTests = new Rectangle(0, 0, 2000, 2000) };
        tooltipController.Initialize(tooltipPool, new UiLayerStack());
        var contextMenuController = TestElementPoolServiceFactory.CreateContextMenuController(tooltipPool, new UiLayerStack());
        var hotbarController = new HotbarController(mapViewState, hotbarContent, actionTargeting, tooltipController, contextMenuController);

        return new Parts(hotbarController, mapViewState, componentManager, hotbarContent, contextMenuController, actionTargeting);
    }

    private static Button MenuOption(ContextMenuController contextMenuController, string label) =>
        contextMenuController.Dropdowns.Menu.ChildElements.OfType<Button>().Single(button => MenuRows.LabelOf(button) == label);

    [TestMethod]
    public void RightClickingABoundActionSlot_OffersArmThenUnbind()
    {
        var (controller, _, _, _, contextMenuController) = Build();

        controller.OnSlotRightClicked(HotkeySlot.Slot1, new Point(100, 100));

        Assert.IsTrue(contextMenuController.Dropdowns.IsOpen);
        CollectionAssert.AreEqual(new[] { "Arm", "Unbind" }, contextMenuController.Dropdowns.Menu.ChildElements.OfType<Button>().Select(button => MenuRows.LabelOf(button)).ToArray());
        Assert.IsTrue(MenuOption(contextMenuController, "Arm").Enabled);
    }

    [TestMethod]
    public void ChoosingArm_ArmsTheSlot_AndTheArmedSpellSlotOffersCast()
    {
        var (controller, mapViewState, _, _, contextMenuController) = Build();
        controller.OnSlotRightClicked(HotkeySlot.Slot1, new Point(100, 100));

        var arm = MenuOption(contextMenuController, "Arm");
        arm.HandleClick(arm.Rectangle.Center);

        Assert.AreEqual(HotkeySlot.Slot1, mapViewState.ArmedSlot);
        controller.OnSlotRightClicked(HotkeySlot.Slot1, new Point(100, 100));
        Assert.IsTrue(MenuOption(contextMenuController, "Cast").Enabled, "A press of the armed slot casts the spell, so its option does too.");
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

    private static readonly Guid TestMenuCommandId = new("66666666-7777-8888-9999-aaaaaaaaaaaa");

    private sealed class CountingCommand
    {
        public int Runs { get; private set; }

        public bool CanRun { get; set; } = true;

        public MenuCommand Command => new(TestMenuCommandId, "Test Window", "Opens a test window.", SpriteName: null, "W", Color.White, "Open", () => Runs++, () => CanRun);
    }

    private static (Parts Parts, CountingCommand Command) BuildWithMenuCommandOn(HotkeySlot slot, Guid boundCommandId)
    {
        var command = new CountingCommand();
        var menuCommands = new MenuCommandCatalog();
        menuCommands.Register(command.Command);
        var parts = BuildParts(menuCommands);
        parts.ComponentManager.GetMultiPool<MenuCommandHotkeyBindingComponent>().Add(PlayerEntityId, new MenuCommandHotkeyBindingComponent(slot, boundCommandId));
        parts.HotbarContent.Update(new GameTime());
        return (parts, command);
    }

    [TestMethod]
    public void PressingAMenuCommandSlot_RunsTheCommandOnce_AndArmsNothing()
    {
        var (parts, command) = BuildWithMenuCommandOn(HotkeySlot.Slot2, TestMenuCommandId);

        parts.Controller.OnSlotClicked(HotkeySlot.Slot2);

        Assert.AreEqual(1, command.Runs);
        Assert.IsNull(parts.MapViewState.ArmedSlot);
        Assert.IsTrue(parts.HotbarContent.IsSlotBound(HotkeySlot.Slot2));
    }

    [TestMethod]
    public void AMenuCommandThatCantRun_GreysItsSlot_AndAPressDoesNothing()
    {
        var (parts, command) = BuildWithMenuCommandOn(HotkeySlot.Slot2, TestMenuCommandId);
        command.CanRun = false;
        parts.HotbarContent.Update(new GameTime());

        parts.Controller.OnSlotClicked(HotkeySlot.Slot2);

        Assert.IsFalse(parts.HotbarContent.IsSlotActive(HotkeySlot.Slot2));
        Assert.AreEqual(0, command.Runs);
    }

    [TestMethod]
    public void AMenuCommandNoCommandClaims_IsAnEmptySlot_AndAPressDoesNothing()
    {
        var (parts, command) = BuildWithMenuCommandOn(HotkeySlot.Slot2, Guid.NewGuid());

        parts.Controller.OnSlotClicked(HotkeySlot.Slot2);

        Assert.IsFalse(parts.HotbarContent.IsSlotBound(HotkeySlot.Slot2));
        Assert.AreEqual(0, command.Runs);
    }

    [TestMethod]
    public void RightClickingAMenuCommandSlot_OffersItsOwnLabelThenUnbind_AndUnbindClearsIt()
    {
        var (parts, command) = BuildWithMenuCommandOn(HotkeySlot.Slot2, TestMenuCommandId);

        parts.Controller.OnSlotRightClicked(HotkeySlot.Slot2, new Point(100, 100));
        CollectionAssert.AreEqual(new[] { "Open", "Unbind" }, parts.ContextMenuController.Dropdowns.Menu.ChildElements.OfType<Button>().Select(button => MenuRows.LabelOf(button)).ToArray());

        var open = MenuOption(parts.ContextMenuController, "Open");
        open.HandleClick(open.Rectangle.Center);
        Assert.AreEqual(1, command.Runs);

        parts.Controller.OnSlotRightClicked(HotkeySlot.Slot2, new Point(100, 100));
        var unbind = MenuOption(parts.ContextMenuController, "Unbind");
        unbind.HandleClick(unbind.Rectangle.Center);
        Assert.IsFalse(parts.ComponentManager.GetMultiPool<MenuCommandHotkeyBindingComponent>().Has(PlayerEntityId));
    }

    [TestMethod]
    public void DraggingAMenuCommandSlotOntoAnother_MovesTheBinding()
    {
        var (parts, _) = BuildWithMenuCommandOn(HotkeySlot.Slot2, TestMenuCommandId);
        Assert.IsTrue(parts.HotbarContent.TryBeginSlotDrag(HotkeySlot.Slot2, out var start));
        var dropHandler = new global::Presentation.Input.DragDrop.HotbarBindingDropHandler(parts.HotbarContent, new TestPlayerQuery(PlayerEntityId));

        Assert.IsTrue(dropHandler.TryDrop(start.Payload, new global::Presentation.Input.DragDrop.HotbarSlotDropTarget(parts.HotbarContent, HotkeySlot.Base1), Point.Zero));

        var bindings = new HotkeyBindingView(parts.ComponentManager);
        Assert.IsFalse(bindings.TryGetBoundMenuCommand(PlayerEntityId, HotkeySlot.Slot2, out _));
        Assert.IsTrue(bindings.TryGetBoundMenuCommand(PlayerEntityId, HotkeySlot.Base1, out var movedCommandId));
        Assert.AreEqual(TestMenuCommandId, movedCommandId);
    }

    [TestMethod]
    public void AHotbarKey_PressesItsSlotOnEitherBar_ButNotWhileTyping()
    {
        var (parts, command) = BuildWithMenuCommandOn(HotkeySlot.Menu3, TestMenuCommandId);
        var keys = new HotbarKeyInputHandler(parts.ActionTargeting);

        keys.HandleKeys(new KeyboardState(Keys.I), new KeyboardState(), isTextBoxFocused: true);
        keys.HandleKeys(new KeyboardState(Keys.D1), new KeyboardState(), isTextBoxFocused: true);
        Assert.AreEqual(0, command.Runs);
        Assert.IsNull(parts.MapViewState.ArmedSlot);

        keys.HandleKeys(new KeyboardState(Keys.I), new KeyboardState(), isTextBoxFocused: false);
        keys.HandleKeys(new KeyboardState(Keys.D1), new KeyboardState(), isTextBoxFocused: false);
        Assert.AreEqual(1, command.Runs);
        Assert.AreEqual(HotkeySlot.Slot1, parts.MapViewState.ArmedSlot);
    }

    [TestMethod]
    public void WhilePaused_ACommandRuns_AndAnActionArmsOnEitherBar_ButConfirmingItIsDenied()
    {
        var (parts, command) = BuildWithMenuCommandOn(HotkeySlot.Slot2, TestMenuCommandId);
        parts.ComponentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Menu3, TestActionId));
        var keys = new HotbarKeyInputHandler(parts.ActionTargeting);
        var denials = 0;
        parts.ActionTargeting.ActivationDeniedWhilePaused += () => denials++;
        parts.MapViewState.HoveredTile = PlayerPosition;
        parts.MapViewState.IsPausedByPlayer = true;

        keys.HandleKeys(new KeyboardState(Keys.D2), new KeyboardState(), isTextBoxFocused: false);
        keys.HandleKeys(new KeyboardState(Keys.I), new KeyboardState(), isTextBoxFocused: false);

        Assert.AreEqual(1, command.Runs);
        Assert.AreEqual(HotkeySlot.Menu3, parts.MapViewState.ArmedSlot, "Arming is allowed while paused.");
        Assert.AreEqual(0, denials);

        keys.HandleKeys(new KeyboardState(Keys.I), new KeyboardState(), isTextBoxFocused: false);

        Assert.AreEqual(1, denials);
        Assert.AreEqual(HotkeySlot.Menu3, parts.MapViewState.ArmedSlot, "A denied confirm leaves it armed.");
        Assert.IsFalse(parts.ComponentManager.GetPackedPool<PendingActionActivationComponent>().Has(PlayerEntityId));

        parts.MapViewState.IsPausedByPlayer = false;
        keys.HandleKeys(new KeyboardState(Keys.I), new KeyboardState(), isTextBoxFocused: false);

        Assert.IsTrue(parts.ComponentManager.GetPackedPool<PendingActionActivationComponent>().Has(PlayerEntityId));
        Assert.AreEqual(1, denials);
    }

    [TestMethod]
    public void WhilePaused_ChoosingArmFromASlotsMenu_ArmsIt()
    {
        var (parts, _) = BuildWithMenuCommandOn(HotkeySlot.Slot2, TestMenuCommandId);
        parts.MapViewState.IsPausedByPlayer = true;

        parts.Controller.OnSlotRightClicked(HotkeySlot.Slot1, new Point(100, 100));
        var arm = MenuOption(parts.ContextMenuController, "Arm");
        Assert.IsTrue(arm.Enabled);
        arm.HandleClick(arm.Rectangle.Center);

        Assert.AreEqual(HotkeySlot.Slot1, parts.MapViewState.ArmedSlot);
    }

    [TestMethod]
    public void AnItemDroppedOnTheSidebar_BindsThere()
    {
        var (parts, _) = BuildWithMenuCommandOn(HotkeySlot.Menu2, TestMenuCommandId);
        var dropHandler = new global::Presentation.Input.DragDrop.HotbarBindingDropHandler(parts.HotbarContent, new TestPlayerQuery(PlayerEntityId));
        var itemDrag = new global::Presentation.Input.DragDrop.DragPayload(global::Presentation.Input.DragDrop.DragPayloadOrigin.InventoryCell, ItemStackInstanceId: 42, OriginEntityId: PlayerEntityId);

        Assert.IsTrue(dropHandler.TryDrop(itemDrag, new global::Presentation.Input.DragDrop.HotbarSlotDropTarget(parts.HotbarContent, HotkeySlot.Menu2), Point.Zero));

        var bindings = new HotkeyBindingView(parts.ComponentManager);
        Assert.IsFalse(bindings.TryGetBoundMenuCommand(PlayerEntityId, HotkeySlot.Menu2, out _));
        Assert.IsTrue(bindings.TryGetBoundItem(PlayerEntityId, HotkeySlot.Menu2, out var boundStackInstanceId));
        Assert.AreEqual(42u, boundStackInstanceId);
    }

    [TestMethod]
    public void TheMenuSidebar_IsYUIOP_TopToBottom_AndNeverLocked()
    {
        var sidebar = HotkeySlotLayout.Entries.Where(entry => entry.Category == HotkeyCategory.Menu).OrderBy(entry => entry.Row).ToArray();

        CollectionAssert.AreEqual(new[] { HotkeySlot.Menu1, HotkeySlot.Menu2, HotkeySlot.Menu3, HotkeySlot.Menu4, HotkeySlot.Menu5 }, sidebar.Select(entry => entry.Slot).ToArray());
        CollectionAssert.AreEqual(new[] { Keys.Y, Keys.U, Keys.I, Keys.O, Keys.P }, sidebar.Select(entry => entry.Key).ToArray());
        Assert.IsTrue(sidebar.All(entry => !HotkeySlotLayout.IsLocked(entry.Slot, unlockedExpansionSlots: 0)));
    }
}
