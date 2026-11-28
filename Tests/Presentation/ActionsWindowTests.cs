using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Blueprints;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Mana.Components;
using Game.Modules.Movement.Components;
using Game.Spawning;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.Input;
using Presentation.Input.DragDrop;
using Presentation.MapRendering;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Actions;
using Presentation.UI.Content;
using Presentation.UI.Details;
using Presentation.UI.Inventory;
using Presentation.UI.Menus;
using Tests.UIEngine;
using UIEngine.Rendering;
using UIEngine.UI;
using UIEngine.UI.Content;

namespace Tests.Presentation;

/// <summary>The Actions window's tabs -- Menu and Core, a gap, All and the categories -- what each tab's grid lists, and what a cell does: double-click, its menu, dragging to the hotbar and nowhere else, and highlighting with the hotbar -- through a real UiInputController.</summary>
[TestClass]
[DoNotParallelize]
public sealed class ActionsWindowTests
{
    private const int PlayerEntityId = 1;
    private static readonly Vector3Int PlayerPosition = new(5, 5, 0);

    private static readonly Guid TestMenuCommandId = new("77777777-8888-9999-aaaa-bbbbbbbbbbbb");

    private sealed record Harness(ActionsWindow Window, ComponentManager ComponentManager, ActionCatalog Actions, ControlTestHarness Ui, MapViewState MapViewState, ContextMenuController ContextMenus, ActionTargetingController ActionTargeting, Func<int> MenuCommandRuns, ActionDetailsCoordinator Details, ItemDetailsModeSetting DetailsMode, CursorTextContent CursorLabel)
    {
        public ActionDetailsWindowController ActionDetails => Details.ActionDetails;
    }

    /// <summary>A crawler (so it has the Core actions) on the map, granted actionIds, with mana to spend, in an Actions window open in a real layer stack.</summary>
    private static Harness Build(params Guid[] actionIds)
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10));
        var actionCatalog = new ActionCatalog();
        foreach (var coreActionId in new[] { BuiltInActionIds.PowerAttack, BuiltInActionIds.QuickAttack, BuiltInActionIds.Dodge })
        {
            actionCatalog.Register(TestContent.Action(coreActionId));
        }

        foreach (var actionId in actionIds)
        {
            Grant(componentManager, actionCatalog, actionId);
        }

        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(20, 20, 1)), playerEntityId: PlayerEntityId);
        var playerTransform = new TransformComponent(PlayerPosition, new Vector2Byte(1, 1));
        TestTransforms.Set(componentManager, PlayerEntityId, playerTransform);
        world.PlaceEntityOnMap(PlayerEntityId, PlayerPosition, ref playerTransform);
        world.EntityKeys.Issue(PlayerEntityId);
        componentManager.Merge(PlayerEntityId, new MovementComponent(null, null));
        componentManager.Merge(PlayerEntityId, new ActionLockComponent(currentLockTotalFrames: 0, unlockedAtFrame: 0));
        componentManager.Merge(PlayerEntityId, new SpawnRecordComponent(0, SpawnFlags.Crawler, Seed: 1));
        componentManager.Merge(PlayerEntityId, new ManaComponent(100, 100));

        var menuCommandRuns = 0;
        var menuCommands = new MenuCommandCatalog();
        menuCommands.Register(new MenuCommand(TestMenuCommandId, "Test Window", "Opens a test window.", SpriteName: null, "W", Color.White, "Open", () => menuCommandRuns++, static () => true));

        var ui = new ControlTestHarness();
        var fontService = TestFonts.Shared;
        var pool = ui.Pool;
        var sprites = new SpriteSheetService(null, "Spritesheets", TestSprites.Resolver);
        var itemCatalog = new ItemCatalog();
        var actionStateView = TestActionStateViews.Over(componentManager, actionCatalog, itemCatalog);
        var mapViewState = new MapViewState();
        var playerCommands = new PlayerCommands(
            world,
            componentManager.GetDirectPool<TransformComponent>(),
            componentManager.GetPackedPool<MovementComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<PendingActionActivationComponent>(),
            componentManager.GetPackedPool<PendingItemActivationComponent>(),
            componentManager.GetPackedPool<PendingWindupComponent>(),
            componentManager.GetMultiPool<InventoryItemStackComponent>(),
            itemCatalog,
            new SimulationClock(),
            TestActionStateViews.EntityActions(componentManager, actionCatalog),
            new EventBus());
        var actionTargeting = new ActionTargetingController(
            world, mapViewState, new MapCamera(new MapViewQuery(world, componentManager, new Game.Terrain.TerrainRegistry(), creatures: new BlueprintRegistry(), new SimulationClock())), ui.Layers, itemCatalog,
            new TransformView(componentManager), new HotkeyBindingView(componentManager), new InventoryView(componentManager, itemCatalog), actionStateView,
            TestActionStateViews.Targeting(componentManager, world, actionCatalog, itemCatalog, world.EntityKeys),
            playerCommands, simulationClock: new SimulationClock(), dropPlacement: new TestPlayerItemDrops(), menuCommands: menuCommands);
        var contextMenus = new ContextMenuController(ui.Dropdowns);
        var cursorLabel = new CursorTextContent(ui.Pointer, fontService);
        var detailsMode = new ItemDetailsModeSetting();
        var actionWindows = new ElementGroup();
        ActionsWindow? actionsWindow = null;
        var actionDetails = new ActionDetailsCoordinator(pool, ControlTestHarness.ScreenSize, ui.Pointer, cursorLabel, actionWindows, detailsMode, mapViewState, actionStateView, new HotkeyBindingView(componentManager), menuCommands, world, () => actionsWindow);
        actionDetails.Initialize(ui.Layers);
        var gridView = new GridViewSetting(GridView.Icon);
        var services = new ActionsWindowServices(actionStateView, new HotkeyBindingView(componentManager), TestActionStateViews.EntityActions(componentManager, actionCatalog), menuCommands, TestGameplayTags.BuiltIn, new SimulationClock(), actionTargeting, mapViewState, contextMenus, actionDetails.HoverDetails, gridView);
        pool.RegisterFactory(() => new TextDivider(TestElementServices.For(pool, fontService)));
        pool.RegisterFactory(() => new global::Presentation.UI.Targeting.TargetSchematicElement(TestElementServices.For(pool, fontService)));
        pool.RegisterFactory(() => new ActionDetailsWindow(TestElementServices.For(pool, fontService), new EffectTextNames(TestGameplayTags.BuiltIn), user: new ActionDetailsUser(actionStateView, world)));
        pool.RegisterFactory<ActionCell>(() => new ActionCell(TestElementServices.For(pool, fontService), sprites));
        pool.RegisterFactory<GridControl>(() => new GridControl(TestElementServices.For(pool, fontService)));
        pool.RegisterFactory<Tooltip>(() => new Tooltip(TestElementServices.For(pool, fontService)));
        pool.RegisterFactory<ActionsWindow>(() => new ActionsWindow(TestElementServices.For(pool, fontService), services));

        var window = ui.AddRoot<ActionsWindow>(new Vector2(100, 100), new Vector2(600, 400), window => window.Configure(PlayerEntityId), new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserClose = true });
        actionsWindow = window;
        ui.Layers.OpenMenuWindow(window);
        actionWindows.Join(window);
        return new Harness(window, componentManager, actionCatalog, ui, mapViewState, contextMenus, actionTargeting, () => menuCommandRuns, actionDetails, detailsMode, cursorLabel);
    }

    private static void Grant(ComponentManager componentManager, ActionCatalog actionCatalog, Guid actionId)
    {
        if (!actionCatalog.TryGet(actionId, out _))
        {
            actionCatalog.Register(TestContent.Action(actionId));
        }

        componentManager.GetMultiPool<ActionInstanceComponent>().Add(PlayerEntityId, new ActionInstanceComponent(actionId, overrideDefinition: null));
    }

    private static void SetMana(ComponentManager componentManager, float current)
    {
        var mana = componentManager.GetPackedPool<ManaComponent>();
        mana.Remove(PlayerEntityId);
        mana.Add(PlayerEntityId, new ManaComponent(current, 100));
    }

    private static IEnumerable<Element> SelfAndDescendants(Element element) =>
        element.ChildElements.SelectMany(SelfAndDescendants).Prepend(element);

    private static Button TabTile(ActionsWindow window, string label) =>
        SelfAndDescendants(window).OfType<Button>().Single(button => button.Text == label);

    private static void OpenTab(ActionsWindow window, string label)
    {
        var tile = TabTile(window, label);
        tile.HandleClick(tile.Rectangle.Center);
    }

    private static string[] CellNames(ActionsWindow window) =>
        [.. SelfAndDescendants(window).OfType<ActionCell>().Select(cell => cell.Name)];

    private static ActionsTabContent ActiveTab(ActionsWindow window) =>
        (ActionsTabContent)SelfAndDescendants(window).OfType<Window>().Select(child => child.Content).OfType<ActionsTabContent>().Single();

    [TestMethod]
    public void TheTabs_AreMenuCoreAllThenTheCategories_AndTheWindowOpensOnAll()
    {
        var harness = Build(BuiltInActionIds.Heal, BuiltInActionIds.Fireball, BuiltInActionIds.MagicMissile, BuiltInActionIds.Lantern);

        CollectionAssert.AreEqual(new[] { "Menu", "Core", "All", "Spells", "Attacks" }, harness.Window.Tabs.Select(tab => tab.Label).ToArray());
        CollectionAssert.AreEquivalent(new[] { "Heal", "Fireball", "Magic Missile", "Lantern" }, CellNames(harness.Window));
    }

    [TestMethod]
    public void TheHeader_LeavesAGapBetweenCoreAndAll_AndNowhereElse()
    {
        var harness = Build(BuiltInActionIds.Fireball);

        var menu = TabTile(harness.Window, "Menu").Rectangle;
        var core = TabTile(harness.Window, "Core").Rectangle;
        var all = TabTile(harness.Window, "All").Rectangle;

        Assert.AreEqual(menu.Right, core.Left);
        Assert.AreEqual((int)TabbedContent.TabGroupGap, all.Left - core.Right);
    }

    [TestMethod]
    public void CoreHoldsTheCoreActions_AllNone_AndMenuTheCommands()
    {
        var harness = Build(BuiltInActionIds.Fireball);

        CollectionAssert.DoesNotContain(CellNames(harness.Window), "Power Attack");

        OpenTab(harness.Window, "Core");
        CollectionAssert.AreEquivalent(new[] { "Power Attack", "Quick Attack", "Dodge" }, CellNames(harness.Window));

        OpenTab(harness.Window, "Menu");
        CollectionAssert.AreEqual(new[] { "Test Window" }, CellNames(harness.Window));
    }

    [TestMethod]
    public void TheSearchAndTheSort_FilterAndOrderTheGrid()
    {
        var harness = Build(BuiltInActionIds.Heal, BuiltInActionIds.Fireball, BuiltInActionIds.FireFan, BuiltInActionIds.WallOfFire);
        var grid = ActiveTab(harness.Window).Grid;

        grid.NameFilter = "fire";
        CollectionAssert.AreEqual(new[] { "Fire Fan", "Fireball", "Wall of Fire" }, CellNames(harness.Window));

        grid.SortOrder = ActionSortOrder.NameDescending;
        CollectionAssert.AreEqual(new[] { "Wall of Fire", "Fireball", "Fire Fan" }, CellNames(harness.Window));
        Assert.AreEqual(3, grid.VisibleItemCount);
    }

    [TestMethod]
    public void AnActionThePlayerCantAfford_IsGreyed_UntilItCan()
    {
        var harness = Build(BuiltInActionIds.Fireball);
        var fireball = SelfAndDescendants(harness.Window).OfType<ActionCell>().Single();

        SetMana(harness.ComponentManager, 0);
        harness.Window.Update(new GameTime());
        Assert.IsTrue(fireball.IsBlocked);

        SetMana(harness.ComponentManager, 100);
        harness.Window.Update(new GameTime());
        Assert.IsFalse(fireball.IsBlocked);
        Assert.IsGreaterThan(0f, fireball.Costs.Mana);
    }

    [TestMethod]
    public void GainingAnActionInACategoryThatHasATab_KeepsTheTabs_AndANewCategoryAddsOne()
    {
        var harness = Build(BuiltInActionIds.Heal);
        var tabsBefore = harness.Window.Tabs;

        Grant(harness.ComponentManager, harness.Actions, BuiltInActionIds.Lantern);
        harness.Window.Update(new GameTime());
        Assert.AreSame(tabsBefore, harness.Window.Tabs, "Lantern is in no category: the tab list stays.");

        Grant(harness.ComponentManager, harness.Actions, BuiltInActionIds.Fireball);
        harness.Window.Update(new GameTime());
        CollectionAssert.Contains(harness.Window.Tabs.Select(tab => tab.Label).ToArray(), "Attacks");
        CollectionAssert.Contains(CellNames(harness.Window), "Fireball", "The open tab's grid picks up the new action itself.");
    }

    private static ActionCell CellNamed(ActionsWindow window, string name) =>
        SelfAndDescendants(window).OfType<ActionCell>().Single(cell => cell.Name == name);

    private static Button MenuOption(ContextMenuController contextMenus, string label) =>
        contextMenus.Dropdowns.Menu.ChildElements.OfType<Button>().Single(button => MenuRows.LabelOf(button) == label);

    private static void OpenMenuOn(ActionCell cell) => cell.OnRightClicked!(cell.Rectangle.Center);

    private static HotbarContent HotbarOver(Harness harness) =>
        new(TestWorlds.Create(new Game.World.Map(new Vector3Int(20, 20, 1)), playerEntityId: PlayerEntityId), harness.MapViewState,
            new HotkeyBindingView(harness.ComponentManager), new InventoryView(harness.ComponentManager, new ItemCatalog()), TestActionStateViews.Over(harness.ComponentManager, harness.Actions),
            new HotkeyBindingCommands(harness.ComponentManager, new ItemCatalog(), new EventBus()), new ItemCatalog(), TestFonts.Shared, new SpriteSheetService(null, "Spritesheets", TestSprites.Resolver),
            new Vector2(1920, 1080), simulationClock: new SimulationClock(), menuCommands: new MenuCommandCatalog());

    [TestMethod]
    public void DoubleClickingAnActionCell_ArmsItWithNoSlot_AndClosesTheWindow()
    {
        var harness = Build(BuiltInActionIds.Fireball);
        var fireball = CellNamed(harness.Window, "Fireball");
        var closed = false;
        harness.Window.Closed += _ => closed = true;

        harness.Ui.Click(fireball);
        harness.Ui.Click(fireball);

        Assert.AreEqual(BuiltInActionIds.Fireball, harness.MapViewState.ArmedActionId);
        Assert.IsNull(harness.MapViewState.ArmedSlot);
        Assert.IsTrue(closed, "Arming closes every closable window, as a hotbar press does.");
    }

    [TestMethod]
    public void TheCellsMenuOption_IsLabelledAsTheHotbarsIs_AndArmsAsADoubleClickDoes()
    {
        var harness = Build(BuiltInActionIds.Fireball);

        OpenMenuOn(CellNamed(harness.Window, "Fireball"));
        var arm = MenuOption(harness.ContextMenus, "Arm");
        Assert.IsTrue(arm.Enabled);
        arm.HandleClick(arm.Rectangle.Center);

        Assert.AreEqual(BuiltInActionIds.Fireball, harness.MapViewState.ArmedActionId);
    }

    [TestMethod]
    public void ABlockedActionsMenuOption_IsDisabled_AndItsDoubleClickDoesNothing()
    {
        var harness = Build(BuiltInActionIds.Fireball);
        SetMana(harness.ComponentManager, 0);
        harness.Window.Update(new GameTime());
        var fireball = CellNamed(harness.Window, "Fireball");

        OpenMenuOn(fireball);
        Assert.IsFalse(MenuOption(harness.ContextMenus, "Arm").Enabled);

        harness.ContextMenus.Dropdowns.Close();
        harness.Ui.Click(fireball);
        harness.Ui.Click(fireball);
        Assert.IsNull(harness.MapViewState.ArmedActionId);
    }

    [TestMethod]
    public void DoubleClickingAMenuCommandCell_RunsTheCommand()
    {
        var harness = Build();
        OpenTab(harness.Window, "Menu");
        var command = CellNamed(harness.Window, "Test Window");

        harness.Ui.Click(command);
        harness.Ui.Click(command);

        Assert.AreEqual(1, harness.MenuCommandRuns());
    }

    [TestMethod]
    public void ACellsDrag_BindsToAHotbarSlot_AndIsRefusedEverywhereElse()
    {
        var harness = Build(BuiltInActionIds.Fireball);
        var hotbar = HotbarOver(harness);
        Assert.IsTrue(CellNamed(harness.Window, "Fireball").TryBeginContentDrag(Point.Zero, out var start));
        var refusal = new ActionCellDropRefusal();
        var slot = new HotbarSlotDropTarget(hotbar, HotkeySlot.Base1);

        Assert.IsTrue(refusal.IsDropRefusedAt(start.Payload, dropTarget: null, Point.Zero), "Over nothing, the map or another window, the drop is refused.");
        Assert.IsFalse(refusal.IsDropRefusedAt(start.Payload, slot, Point.Zero));
        Assert.IsTrue(new HotbarBindingDropHandler(hotbar, new TestPlayerQuery(PlayerEntityId)).TryDrop(start.Payload, slot, Point.Zero));
        Assert.IsTrue(new HotkeyBindingView(harness.ComponentManager).TryGetBoundAction(PlayerEntityId, HotkeySlot.Base1, out var boundActionId));
        Assert.AreEqual(BuiltInActionIds.Fireball, boundActionId);
    }

    [TestMethod]
    public void ADragOffAHotbarSlot_IsNeverRefused_SoItStillUnbindsTheSlot()
    {
        var payload = new DragPayload(DragPayloadOrigin.HotbarSlot, ActionId: BuiltInActionIds.Fireball, OriginSlot: HotkeySlot.Base1);

        Assert.IsFalse(new ActionCellDropRefusal().IsDropRefusedAt(payload, dropTarget: null, Point.Zero));
    }

    [TestMethod]
    public void ACellsMenu_LightsTheHotbarSlotsBoundToItsAction_AndASlotsMenuLightsTheCell()
    {
        var harness = Build(BuiltInActionIds.Fireball);
        harness.ComponentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Base1, BuiltInActionIds.Fireball));
        var hotbar = HotbarOver(harness);
        var fireball = CellNamed(harness.Window, "Fireball");

        OpenMenuOn(fireball);
        Assert.IsTrue(hotbar.SlotShowsAction(HotkeySlot.Base1, BuiltInActionIds.Fireball));
        Assert.IsTrue(ContextMenuActions.IsAboutAction(harness.ContextMenus, BuiltInActionIds.Fireball), "What the hotbar asks of each slot bound to Fireball.");
        harness.ContextMenus.Dropdowns.Close();

        var slotElement = harness.Ui.Pool.CreateElement<HotbarSlotElement>(null, new ElementOptions { Layout = new ElementLayoutOptions { Size = HotbarContent.SlotSize, DisplayMode = ElementDisplayMode.Fixed } });
        slotElement.Configure(hotbar, HotkeySlot.Base1);
        harness.ContextMenus.Open(Vector2.Zero, [new DropdownOption("Arm", null, true, static () => { })], slotElement);
        harness.Window.Update(new GameTime());

        Assert.IsTrue(fireball.IsContextMenuTarget);
    }

    private static Func<bool> ClosedFlag(ActionsWindow window)
    {
        var closed = false;
        window.Closed += _ => closed = true;
        return () => closed;
    }

    [TestMethod]
    public void ArmingFromTheHotbar_ClosesTheActionsWindow()
    {
        var harness = Build(BuiltInActionIds.Fireball);
        harness.ComponentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Base1, BuiltInActionIds.Fireball));
        var closed = ClosedFlag(harness.Window);

        harness.ActionTargeting.HandleHotkeySlotPress(HotkeySlot.Base1);

        Assert.AreEqual(BuiltInActionIds.Fireball, harness.MapViewState.ArmedActionId);
        Assert.IsTrue(closed());
    }

    [TestMethod]
    public void TurningAToggleOnFromTheHotbar_ClosesTheActionsWindow()
    {
        var harness = Build(BuiltInActionIds.Lantern);
        harness.MapViewState.TargetingMode = TargetingMode.Target;
        harness.ComponentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Base1, BuiltInActionIds.Lantern));
        var closed = ClosedFlag(harness.Window);

        harness.ActionTargeting.HandleHotkeySlotPress(HotkeySlot.Base1);

        Assert.IsTrue(closed(), "Turning the Lantern on commits to play, as arming does.");
    }

    private static string[] DetailsLines(Harness harness) =>
        [.. SelfAndDescendants(harness.ActionDetails.Window!).OfType<TextWindow>().Select(line => line.OriginalText)];

    private static void Frames(Harness harness, int count)
    {
        for (var frame = 0; frame < count; frame++)
        {
            harness.Ui.Frame();
        }
    }

    [TestMethod]
    public void HoveringACell_NamesItAtOnce_ThenOpensActionDetailsAfterTheDelay_WithoutSelecting()
    {
        var harness = Build(BuiltInActionIds.Fireball);
        var fireball = CellNamed(harness.Window, "Fireball");

        harness.Ui.MoveTo(fireball.Rectangle.Center);
        Assert.AreEqual("Fireball", harness.CursorLabel.ShownLabelText);
        Assert.IsNull(harness.ActionDetails.Window);

        Frames(harness, ItemHoverDetails.DetailsDelayFrames);

        Assert.AreEqual("Fireball", harness.ActionDetails.Window?.Definition?.Name);
        Assert.AreEqual(string.Empty, harness.CursorLabel.ShownLabelText);
        Assert.IsNull(harness.MapViewState.SelectedActionId, "A hover shows; only a click selects.");
        Assert.IsFalse(fireball.IsSelected);
    }

    private static void HoverUntilDetailsOpen(Harness harness, ActionCell cell)
    {
        harness.Ui.MoveTo(cell.Rectangle.Center);
        Frames(harness, ItemHoverDetails.DetailsDelayFrames);
    }

    [TestMethod]
    public void ClickingACell_SelectsIt_AsAnInventoryClickDoes_AndOpensNothing()
    {
        var harness = Build(BuiltInActionIds.Fireball, BuiltInActionIds.Heal);
        var fireball = CellNamed(harness.Window, "Fireball");

        harness.Ui.Click(fireball);
        harness.Window.Update(new GameTime());

        Assert.IsNull(harness.ActionDetails.Window, "Hovering opens Action Details; a click only selects.");
        Assert.AreEqual(BuiltInActionIds.Fireball, harness.MapViewState.SelectedActionId);
        Assert.IsTrue(fireball.IsSelected);

        harness.Ui.Click(CellNamed(harness.Window, "Heal"));
        harness.Window.Update(new GameTime());
        Assert.AreEqual(BuiltInActionIds.Heal, harness.MapViewState.SelectedActionId);
        Assert.IsFalse(fireball.IsSelected);
    }

    [TestMethod]
    public void APressOutsideEveryActionsWindow_ClosesActionDetails_AndOneInsideTheActionsWindowDoesNot()
    {
        var harness = Build(BuiltInActionIds.Fireball, BuiltInActionIds.Heal);
        HoverUntilDetailsOpen(harness, CellNamed(harness.Window, "Fireball"));

        harness.Ui.Click(CellNamed(harness.Window, "Heal"));
        Assert.IsNotNull(harness.ActionDetails.Window, "A press inside the Actions window leaves it open.");

        HoverUntilDetailsOpen(harness, CellNamed(harness.Window, "Heal"));
        Assert.AreEqual("Heal", harness.ActionDetails.Window?.Definition?.Name, "Another cell shows in the same popup.");

        harness.Ui.Click(new Point(1800, 1000));
        Assert.IsNull(harness.ActionDetails.Window);
    }

    [TestMethod]
    public void ActionDetails_ClosesOnceThePointerHasLeftTheCellAndThePopupForTheGrace()
    {
        var harness = Build(BuiltInActionIds.Fireball);
        HoverUntilDetailsOpen(harness, CellNamed(harness.Window, "Fireball"));
        Assert.IsNotNull(harness.ActionDetails.Window);

        harness.Ui.MoveTo(new Point(1800, 1000));
        Frames(harness, ItemDetailsWindowController.PointerAwayGraceFrames - 1);
        Assert.IsNotNull(harness.ActionDetails.Window, "Still within the grace.");

        Frames(harness, 2);
        Assert.IsNull(harness.ActionDetails.Window);
    }

    [TestMethod]
    public void ActionDetails_ShowsThePlayersBlockerAndCooldown_AndFollowsTheSharedMode()
    {
        var harness = Build(BuiltInActionIds.Fireball);
        SetMana(harness.ComponentManager, 0);
        harness.Window.Update(new GameTime());
        HoverUntilDetailsOpen(harness, CellNamed(harness.Window, "Fireball"));
        var details = harness.ActionDetails.Window!;

        CollectionAssert.Contains(DetailsLines(harness), ActivationBlockerText.Describe(ActivationBlocker.NotEnoughMana));
        Assert.AreEqual(DetailsMode.Simple, details.ShownMode);

        harness.DetailsMode.Toggle();
        Assert.AreEqual(DetailsMode.Advanced, details.ShownMode, "Item Details' mode is Action Details' too.");

        SetMana(harness.ComponentManager, 100);
        details.Update(new GameTime());
        CollectionAssert.DoesNotContain(DetailsLines(harness), ActivationBlockerText.Describe(ActivationBlocker.NotEnoughMana), "It rebuilds when the blocker goes.");
    }

    [TestMethod]
    public void AMenuCommandsDetails_ShowItsDescriptionAndTheKeysOfTheSlotsHoldingIt()
    {
        var harness = Build();
        harness.ComponentManager.GetMultiPool<MenuCommandHotkeyBindingComponent>().Add(PlayerEntityId, new MenuCommandHotkeyBindingComponent(HotkeySlot.Menu1, TestMenuCommandId));
        harness.ComponentManager.GetMultiPool<MenuCommandHotkeyBindingComponent>().Add(PlayerEntityId, new MenuCommandHotkeyBindingComponent(HotkeySlot.Menu5, TestMenuCommandId));
        OpenTab(harness.Window, "Menu");
        var command = CellNamed(harness.Window, "Test Window");

        HoverUntilDetailsOpen(harness, command);

        Assert.AreEqual(TestMenuCommandId, harness.ActionDetails.Window?.Command?.Id);
        CollectionAssert.Contains(DetailsLines(harness), "Bound to Y, P");

        harness.Ui.Click(command);
        harness.Window.Update(new GameTime());
        Assert.AreEqual(TestMenuCommandId, harness.MapViewState.SelectedMenuCommandId);
        Assert.IsTrue(command.IsSelected);
    }

    [TestMethod]
    public void DraggingAnAction_ClosesActionDetails_AndHoldsItBackUntilTheDragEnds()
    {
        var harness = Build(BuiltInActionIds.Fireball);
        var fireball = CellNamed(harness.Window, "Fireball");
        HoverUntilDetailsOpen(harness, fireball);
        Assert.IsTrue(fireball.TryBeginContentDrag(Point.Zero, out var start));
        var dismissal = new ActionDragDetailsDismissal(harness.Details);

        dismissal.OnDragStarted(start.Payload);
        Assert.IsNull(harness.ActionDetails.Window);

        harness.Ui.MoveTo(new Point(1800, 1000));
        HoverUntilDetailsOpen(harness, fireball);
        Assert.IsNull(harness.ActionDetails.Window, "A held drag never opens it.");

        dismissal.OnDragEnded(start.Payload);
        harness.Ui.MoveTo(new Point(1800, 1000));
        HoverUntilDetailsOpen(harness, fireball);
        Assert.IsNotNull(harness.ActionDetails.Window);
    }

    private static void CompareFromMenu(Harness harness, string actionName)
    {
        OpenMenuOn(CellNamed(harness.Window, actionName));
        var compare = MenuOption(harness.ContextMenus, "Compare");
        compare.HandleClick(compare.Rectangle.Center);
        harness.Window.Update(new GameTime());
    }

    [TestMethod]
    public void CompareOnAnAction_PinsItsDetailsBesideTheWindow_AndGreysEveryActionOfAnotherKind()
    {
        var harness = Build(BuiltInActionIds.Fireball, BuiltInActionIds.MagicMissile, BuiltInActionIds.Lantern);

        CompareFromMenu(harness, "Fireball");

        Assert.IsTrue(harness.Details.Comparison.IsComparing);
        Assert.AreEqual("Fireball", harness.ActionDetails.Window?.Definition?.Name);
        Assert.IsTrue(harness.ActionDetails.IsPinned);
        Assert.AreEqual(CellCompareState.Eligible, CellNamed(harness.Window, "Magic Missile").CompareState, "Spells compare with spells.");
        Assert.AreEqual(CellCompareState.Ineligible, CellNamed(harness.Window, "Lantern").CompareState);
        Assert.IsNull(harness.MapViewState.CompareRequiredActivatorType, "An action comparison never greys inventory items.");
    }

    [TestMethod]
    public void WhileComparing_HoveringALikeAction_OpensItsComparisonBelowTheAnchor_AndAnUnlikeOneIsRefused()
    {
        var harness = Build(BuiltInActionIds.Fireball, BuiltInActionIds.MagicMissile, BuiltInActionIds.Lantern);
        CompareFromMenu(harness, "Fireball");
        var anchor = harness.ActionDetails.Window!;
        var input = new ComparisonInputHandler(harness.Details.Comparison);

        HoverUntilDetailsOpen(harness, CellNamed(harness.Window, "Magic Missile"));

        var comparison = harness.Ui.Layers[UiLayer.DynamicHud].OfType<ActionDetailsWindow>().Single(window => !ReferenceEquals(window, anchor));
        Assert.AreEqual("Magic Missile", comparison.Definition?.Name);
        Assert.AreEqual(DetailsMode.Simple, comparison.ShownMode, "A comparison is always simple.");
        Assert.AreSame(anchor, harness.ActionDetails.Window, "The anchor stays pinned.");

        Assert.IsTrue(input.IsInputRefusedAt(CellNamed(harness.Window, "Lantern"), Point.Zero));
        Assert.IsFalse(input.IsInputRefusedAt(CellNamed(harness.Window, "Magic Missile"), Point.Zero));
    }

    [TestMethod]
    public void EscapeEndsActionCompareMode_ClosingBothPopups_AndStartingItTellsTheOtherCompareModeToEnd()
    {
        var harness = Build(BuiltInActionIds.Fireball, BuiltInActionIds.MagicMissile);
        var otherCompareModeEnded = false;
        harness.Details.Comparison.OnComparisonStarting = () => otherCompareModeEnded = true;
        CompareFromMenu(harness, "Fireball");
        HoverUntilDetailsOpen(harness, CellNamed(harness.Window, "Magic Missile"));
        Assert.IsTrue(otherCompareModeEnded);

        Assert.IsTrue(new ComparisonInputHandler(harness.Details.Comparison).TryConsumeEscape());

        Assert.IsFalse(harness.Details.Comparison.IsComparing);
        Assert.IsEmpty(harness.Ui.Layers[UiLayer.DynamicHud].OfType<ActionDetailsWindow>());
        Assert.IsNull(harness.MapViewState.CompareRequiredActionActivatorType);
        harness.Window.Update(new GameTime());
        Assert.AreEqual(CellCompareState.None, CellNamed(harness.Window, "Magic Missile").CompareState);
    }

    [TestMethod]
    public void ActionDetails_OffersCompare_AndTheAnchorOffersStopComparison()
    {
        var harness = Build(BuiltInActionIds.Fireball);
        HoverUntilDetailsOpen(harness, CellNamed(harness.Window, "Fireball"));
        var details = harness.ActionDetails.Window!;
        Button ButtonLabelled(string label) => SelfAndDescendants(details).OfType<Button>().Single(button => button.Text == label);

        var compare = ButtonLabelled("Compare");
        compare.HandleClick(compare.Rectangle.Center);
        Assert.IsTrue(harness.Details.Comparison.IsComparing);

        details.Update(new GameTime());
        var stop = ButtonLabelled("Stop Comparison");
        stop.HandleClick(stop.Rectangle.Center);
        Assert.IsFalse(harness.Details.Comparison.IsComparing);
        Assert.IsNull(harness.ActionDetails.Window);
    }

    [TestMethod]
    public void ActionAndMenuCommandCells_ShowTheKeyOfTheSlotHoldingThem()
    {
        var harness = Build(BuiltInActionIds.Fireball, BuiltInActionIds.Heal);
        harness.ComponentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Base1, BuiltInActionIds.Fireball));
        harness.ComponentManager.GetMultiPool<MenuCommandHotkeyBindingComponent>().Add(PlayerEntityId, new MenuCommandHotkeyBindingComponent(HotkeySlot.Menu2, TestMenuCommandId));
        harness.Window.Update(new GameTime());

        Assert.AreEqual("Q", CellNamed(harness.Window, "Fireball").KeyLabel);
        Assert.IsNull(CellNamed(harness.Window, "Heal").KeyLabel);

        OpenTab(harness.Window, "Menu");
        harness.Window.Update(new GameTime());
        Assert.AreEqual("U", CellNamed(harness.Window, "Test Window").KeyLabel);
    }

    [TestMethod]
    public void TheCardViewSwitch_ShowsEveryTabAsCards_KeepingTheSearch_AndCellsStillArmOnADoubleClick()
    {
        var harness = Build(BuiltInActionIds.Fireball, BuiltInActionIds.Heal);
        ActiveTab(harness.Window).Grid.NameFilter = "fire";
        var cardViewSwitch = SelfAndDescendants(harness.Window).OfType<Toggle>().Single();

        harness.Ui.Click(cardViewSwitch);
        harness.Ui.Frame();

        var fireball = CellNamed(harness.Window, "Fireball");
        Assert.IsTrue(fireball.IsCard);
        Assert.IsGreaterThan(fireball.CurrentSize.Y, fireball.CurrentSize.X);
        CollectionAssert.AreEqual(new[] { "Fireball" }, CellNames(harness.Window), "The search still applies.");

        OpenTab(harness.Window, "Core");
        harness.Ui.Frame();
        Assert.IsTrue(SelfAndDescendants(harness.Window).OfType<ActionCell>().All(cell => cell.IsCard), "Another tab opens as cards too.");

        OpenTab(harness.Window, "All");
        harness.Ui.Frame();
        harness.Ui.Click(CellNamed(harness.Window, "Fireball"));
        harness.Ui.Click(CellNamed(harness.Window, "Fireball"));
        Assert.AreEqual(BuiltInActionIds.Fireball, harness.MapViewState.ArmedActionId);
    }
}
