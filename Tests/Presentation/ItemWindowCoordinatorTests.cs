using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Engine.Tiles;
using Game.Blueprints;
using Game.Effects;
using Game.Effects.Entries;
using Game.Floors;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Currency.Components;
using Game.Modules.Death;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Lootboxes;
using Game.Modules.Shops.Components;
using Game.Spawning;
using Game.Tags;
using Game.Views;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.UI;
using Presentation.UI.Content;
using Presentation.UI.Inventory;
using Presentation.UI.Looting;
using Presentation.UI.Shops;
using Presentation.UI.Trade;
using Tests.UIEngine;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI;
using UIEngine.UI.Content;

namespace Tests.Presentation;

/// <summary>The rules between the item windows, driven through the entry points the rest of the UI calls: MapWindow's loot and shop clicks, a grid's item click, and an entity being destroyed.</summary>
[TestClass]
[DoNotParallelize]
public sealed class ItemWindowCoordinatorTests
{
    private const int PlayerEntityId = TestMapWindows.PlayerEntityId;
    private const int CorpseEntityId = 2;
    private const int ShopEntityId = 3;
    private const int TradeOfferPlayerEntityId = 4;
    private const int TradeOfferShopEntityId = 5;

    private static readonly Guid PotionItemId = Guid.NewGuid();

    private sealed record Harness(ItemWindowCoordinator Coordinator, MapWindow MapWindow, MapViewState MapViewState, Engine.ECS.Components.ComponentManager ComponentManager, UiLayerStack Layers, ItemCatalog ItemCatalog, ContextMenuController ContextMenuController);

    private static Harness Build()
    {
        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(
            PotionItemId, "Test Potion", null, "p", Color.Green, Tags: [GameTags.ItemConsumablePotion, GameTags.TargetingSelf],
            Effects: [new Effect([new DirectHeal(0.5f)])],
            Activator: new PotionActivator(new TargetingSpec(TargetShape.Burst, Range: 3, AreaSize: 1), new ActionTiming(ActionTimingCategory.Immediate, 60, null))));

        var mapHarness = TestMapWindows.Create(20, 20, 1, new Vector3Int(5, 5, 0), itemCatalog: itemCatalog);
        var world = mapHarness.World;
        var componentManager = mapHarness.ComponentManager;
        var mapViewState = mapHarness.MapViewState;
        mapViewState.ReservedEntityIds = new ReservedEntityIds(TradeOfferPlayerEntityId, TradeOfferShopEntityId);

        componentManager.Merge(ShopEntityId, new ShopComponent(acceptedItems: Engine.Tags.GameplayTagQuery.Any([GameTags.ItemConsumablePotion]), buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        foreach (var entityId in new[] { PlayerEntityId, ShopEntityId, TradeOfferPlayerEntityId, TradeOfferShopEntityId })
        {
            componentManager.Merge(entityId, new CurrencyComponent(gold: 0, credits: 0));
        }

        var fontService = TestFonts.Shared;
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets", TestSprites.Resolver);
        var elementPool = TestElementPoolServiceFactory.Create(fontService);
        var layers = new UiLayerStack();
        var pointerState = new PointerState();
        var tooltipController = new TooltipController();
        var shellServices = new ShellServices(
            layers,
            mapViewState,
            TestMapCameras.Over(world),
            TestElementPoolServiceFactory.CreateContextMenuController(elementPool, layers),
            tooltipController,
            pointerState,
            new CursorTextContent(pointerState, fontService));

        var eventBus = new EventBus();
        var inventoryServices = TestInventoryServices.Over(componentManager, itemCatalog, world, eventBus);
        var definitions = new BlueprintRegistry();
        var entityNaming = EntityNaming.For(componentManager, definitions);
        var healthView = new HealthView(componentManager, Game.Modules.Health.EntityBodyParts.For(componentManager, definitions));
        var mapView = new MapViewQuery(world, componentManager, new ActionCatalog(), new Game.Terrain.TerrainRegistry(), definitions, new SimulationClock());
        var contextMenuController = shellServices.ContextMenuController;

        elementPool.RegisterFactory<Tooltip>(() => new Tooltip(fontService, elementPool, new UiRenderService()));
        elementPool.RegisterFactory<GridControl>(() => new GridControl(fontService, elementPool, new UiRenderService()));
        elementPool.RegisterFactory<Toggle>(() => new Toggle(fontService, elementPool, new UiRenderService()));
        elementPool.RegisterFactory<TextDivider>(() => new TextDivider(fontService, elementPool, new UiRenderService()));
        elementPool.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(fontService, elementPool, new UiRenderService(), spriteSheetService));
        elementPool.RegisterFactory<ShopItemStackCell>(() => new ShopItemStackCell(fontService, elementPool, new UiRenderService(), spriteSheetService));
        elementPool.RegisterFactory<TradeItemStackCell>(() => new TradeItemStackCell(fontService, elementPool, new UiRenderService(), spriteSheetService));
        elementPool.RegisterFactory<EmptyTradeSlotCell>(() => new EmptyTradeSlotCell(fontService, elementPool, new UiRenderService()));
        elementPool.RegisterFactory<CurrencyElement>(() => new CurrencyElement(fontService, elementPool, new UiRenderService(), spriteSheetService));
        elementPool.RegisterFactory<ItemIconElement>(() => new ItemIconElement(fontService, elementPool, new UiRenderService(), spriteSheetService));
        elementPool.RegisterFactory<TargetShapePreviewElement>(() => new TargetShapePreviewElement(fontService, elementPool, new UiRenderService()));
        elementPool.RegisterFactory<EntityIconElement>(() => new EntityIconElement(fontService, elementPool, new UiRenderService(), spriteSheetService, mapView));
        elementPool.RegisterFactory<ItemDetailsWindow>(() => new ItemDetailsWindow(fontService, elementPool, new UiRenderService(), new ActionCatalog(), TestGameplayTags.BuiltIn, inventoryServices.ActionStateView, world));
        elementPool.RegisterFactory<InventoryManagementWindow>(() => new InventoryManagementWindow(fontService, elementPool, new UiRenderService(), inventoryServices, world, contextMenuController, mapViewState, simulationClock: new SimulationClock()));
        elementPool.RegisterFactory<SecondaryInventoryWindow>(() => new SecondaryInventoryWindow(fontService, elementPool, new UiRenderService(), inventoryServices, world, contextMenuController, mapViewState, simulationClock: new SimulationClock(), entityNaming: entityNaming, healthView: healthView));
        elementPool.RegisterFactory<ShopWindow>(() => new ShopWindow(fontService, elementPool, new UiRenderService(), inventoryServices, world, contextMenuController, mapViewState, simulationClock: new SimulationClock(), entityNaming: entityNaming));
        elementPool.RegisterFactory<TradeWindow>(() => new TradeWindow(fontService, elementPool, new UiRenderService(), inventoryServices, world, contextMenuController, mapViewState, simulationClock: new SimulationClock()));

        tooltipController.Initialize(elementPool, layers);

        var lootboxCatalog = new LootboxCatalog(itemCatalog);
        var coordinator = new ItemWindowCoordinator(
            elementPool,
            shellServices,
            world,
            mapHarness.MapWindow,
            mapHarness.ActionTargetingController,
            inventoryServices.InventoryView,
            itemCatalog,
            lootboxCatalog,
            new LootCommands(componentManager),
            new LootboxCommands(new LootboxOpener(componentManager, lootboxCatalog, itemCatalog, seed: 1)),
            mapViewState.ReservedEntityIds);

        return new Harness(coordinator, mapHarness.MapWindow, mapViewState, componentManager, layers, itemCatalog, contextMenuController);
    }

    private static int? OpenSecondaryTarget(Harness harness) => harness.Coordinator.Inventory.GetSecondaryTargetEntityId!();

    [TestMethod]
    public void ClickingACorpse_WhileAShopIsOpen_ClosesTheShopAndOpensTheLoot()
    {
        var harness = Build();
        harness.MapWindow.OnShopClicked!(ShopEntityId);
        Assert.AreEqual(ShopEntityId, harness.MapViewState.OpenShopEntityId, "Sanity check: the shop opened.");

        harness.MapWindow.OnCorpseClicked!(CorpseEntityId);

        Assert.IsNull(harness.MapViewState.OpenShopEntityId);
        Assert.AreEqual(CorpseEntityId, OpenSecondaryTarget(harness));
    }

    [TestMethod]
    public void ClickingAShop_WhileALootWindowIsOpen_ClosesTheLootAndOpensTheShop()
    {
        var harness = Build();
        harness.MapWindow.OnCorpseClicked!(CorpseEntityId);
        Assert.AreEqual(CorpseEntityId, OpenSecondaryTarget(harness), "Sanity check: the loot window opened.");

        harness.MapWindow.OnShopClicked!(ShopEntityId);

        Assert.AreEqual(ShopEntityId, OpenSecondaryTarget(harness));
        Assert.AreEqual(ShopEntityId, harness.MapViewState.OpenShopEntityId);
    }

    [TestMethod]
    public void ReleaseEntity_ForTheOpenShop_ClosesIt()
    {
        var harness = Build();
        harness.MapWindow.OnShopClicked!(ShopEntityId);

        harness.Coordinator.ReleaseEntity(ShopEntityId);

        Assert.IsNull(harness.MapViewState.OpenShopEntityId);
        Assert.IsNull(OpenSecondaryTarget(harness));
    }

    [TestMethod]
    public void ReleaseEntity_ForTheItemShownInItemDetails_ClosesItemDetails()
    {
        var harness = Build();
        harness.MapWindow.OnCorpseClicked!(CorpseEntityId);
        var stackInstanceId = InventoryActions.AddItem(harness.ComponentManager, CorpseEntityId, PotionItemId, quantity: 1);
        harness.Coordinator.Inventory.OnItemSelected!(CorpseEntityId, stackInstanceId);
        Assert.AreEqual(CorpseEntityId, harness.Coordinator.ItemDetails.CurrentEntityId, "Sanity check: Item Details opened on the corpse's potion.");

        harness.Coordinator.ReleaseEntity(CorpseEntityId);

        Assert.IsNull(OpenSecondaryTarget(harness));
        Assert.IsNull(harness.Coordinator.ItemDetails.CurrentEntityId);
    }

    [TestMethod]
    public void ClickingAnItem_WithComparisonDisarmed_OpensItInItemDetails()
    {
        var harness = Build();
        var stackInstanceId = InventoryActions.AddItem(harness.ComponentManager, PlayerEntityId, PotionItemId, quantity: 1);
        harness.Coordinator.Inventory.OpenInventoryWindow();

        harness.Coordinator.Inventory.OnItemSelected!(PlayerEntityId, stackInstanceId);

        Assert.AreEqual(PlayerEntityId, harness.Coordinator.ItemDetails.CurrentEntityId);
        Assert.AreEqual(stackInstanceId, harness.Coordinator.ItemDetails.CurrentStackInstanceId);
    }

    private static void Press(UiInputController controller, Point position)
    {
        controller.Update(new KeyboardState(), new MouseState(position.X, position.Y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
        controller.Update(new KeyboardState(), new MouseState(position.X, position.Y, 0, ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
    }

    private static UiInputController Controller(Harness harness) =>
        TestUiInputController.Create(harness.Layers, new Vector2(2000, 2000), harness.ComponentManager, TestPlayerQuery.NoPlayer, new EventBus(), harness.ItemCatalog, contextMenuController: harness.ContextMenuController, mapViewState: harness.MapViewState);

    private static Harness BuildWithItemDetailsOpenOverACorpse()
    {
        var harness = Build();
        harness.Coordinator.Inventory.OpenInventoryWindow();
        harness.MapWindow.OnCorpseClicked!(CorpseEntityId);
        var stackInstanceId = InventoryActions.AddItem(harness.ComponentManager, CorpseEntityId, PotionItemId, quantity: 1);
        harness.Coordinator.Inventory.OnItemSelected!(CorpseEntityId, stackInstanceId);
        Assert.IsTrue(harness.Coordinator.ItemDetails.IsOpen, "Sanity check: Item Details opened on the corpse's potion.");
        return harness;
    }

    private static (SecondaryInventoryWindow Window, Window Grid) OpenCorpseLoot(Harness harness)
    {
        harness.Coordinator.Inventory.OpenInventoryWindow();
        harness.MapWindow.OnCorpseClicked!(CorpseEntityId);
        var window = harness.Layers[UiLayer.DynamicHud].OfType<SecondaryInventoryWindow>().Single();
        var grid = window.ChildElements.OfType<Window>().Single(child => child.Content is InventoryGridContent);
        return (window, grid);
    }

    /// <summary>Gives the corpse count new items, each its own definition, so each is a cell of its own.</summary>
    private static List<uint> AddDifferentItems(Harness harness, int count, int entityId = CorpseEntityId)
    {
        var stackInstanceIds = new List<uint>();
        for (var index = 0; index < count; index++)
        {
            var itemId = Guid.NewGuid();
            harness.ItemCatalog.Register(new ItemDefinition(itemId, $"Trinket {itemId}", null, "t", Color.White, Tags: [], Effects: []));
            stackInstanceIds.Add(InventoryActions.AddItem(harness.ComponentManager, entityId, itemId, quantity: 1));
        }

        return stackInstanceIds;
    }

    private static void TakeFromCorpse(Harness harness, uint stackInstanceId) =>
        InventoryActions.TryTransferStack(harness.ComponentManager, harness.ItemCatalog, CorpseEntityId, PlayerEntityId, stackInstanceId, TestPlayerQuery.NoPlayer);

    private static string HeaderText(SecondaryInventoryWindow window) =>
        window.ChildElements.OfType<TextWindow>().Single(line => line.OriginalText.Contains('/')).OriginalText;

    private static int RowsShown(Window grid) =>
        (int)System.Math.Round(grid.CurrentSize.Y / (InventoryGridContent.CellSize.Y + InventoryGridContent.CellGap));

    [TestMethod]
    public void ALootWindow_IsAsWideAsFiveColumns_AndCantBeResized()
    {
        var harness = Build();
        var (window, grid) = OpenCorpseLoot(harness);

        var fiveColumns = 5 * InventoryGridContent.CellSize.X + 4 * InventoryGridContent.CellGap;
        Assert.AreEqual(fiveColumns, grid.CurrentSize.X);
        Assert.AreEqual(fiveColumns, window.ContentSize.X);
        Assert.IsFalse(window.CanUserResize);
    }

    [TestMethod]
    public void ALootWindow_GainsARowWhenItsLastRowFills_AndLosesItOnceARowAndOneMoreEmpty()
    {
        var harness = Build();
        var (window, grid) = OpenCorpseLoot(harness);
        Assert.AreEqual(2, RowsShown(grid), "Sanity check: an empty corpse shows the two-row minimum.");

        var stackInstanceIds = AddDifferentItems(harness, 9);
        window.Update(new GameTime());
        Assert.AreEqual(2, RowsShown(grid), "Nine cells leave the second row a slot to drop into.");

        stackInstanceIds.AddRange(AddDifferentItems(harness, 1));
        window.Update(new GameTime());
        Assert.AreEqual(3, RowsShown(grid), "The tenth fills both rows, so a third opens.");

        TakeFromCorpse(harness, stackInstanceIds[^1]);
        window.Update(new GameTime());
        Assert.AreEqual(2, RowsShown(grid), "Back to nine, the third row and one more cell are empty.");
    }

    [TestMethod]
    public void ALootWindow_StopsAtFourRows_AndCountsItsStacksOutOfTwenty()
    {
        var harness = Build();
        var (window, grid) = OpenCorpseLoot(harness);

        AddDifferentItems(harness, 20);
        window.Update(new GameTime());

        Assert.AreEqual(4, RowsShown(grid));
        Assert.AreEqual("20/20", HeaderText(window));
    }

    private static (ShopWindow Window, Window Grid) OpenShop(Harness harness)
    {
        harness.Coordinator.Inventory.OpenInventoryWindow();
        harness.MapWindow.OnShopClicked!(ShopEntityId);
        var window = harness.Layers[UiLayer.DynamicHud].OfType<ShopWindow>().Single();
        var grid = window.ChildElements.OfType<Window>().Single(child => child.Content is InventoryGridContent);
        return (window, grid);
    }

    [TestMethod]
    public void AShopWindow_IsAsWideAsTwoShopCells_CountsItsStacksOutOfInfinity_AndCantBeResized()
    {
        var harness = Build();
        AddDifferentItems(harness, 3, ShopEntityId);
        var (window, grid) = OpenShop(harness);

        var twoShopCells = 2 * InventoryGridContent.CellSize.X * InventoryGridContent.ShopCellWidthMultiplier + InventoryGridContent.CellGap;
        Assert.AreEqual(twoShopCells, grid.CurrentSize.X);
        Assert.AreEqual(5, RowsShown(grid));
        Assert.IsTrue(window.ChildElements.OfType<TextWindow>().Any(line => line.OriginalText == "3/∞"));
        Assert.IsFalse(window.CanUserResize);
    }

    [TestMethod]
    public void AShopWindow_PastTwelveRowsScrolls_AndWidensForTheScrollbar()
    {
        var harness = Build();
        var (window, grid) = OpenShop(harness);

        AddDifferentItems(harness, 30, ShopEntityId);
        window.Update(new GameTime());

        Assert.AreEqual(12, RowsShown(grid));
        var twoShopCells = 2 * InventoryGridContent.CellSize.X * InventoryGridContent.ShopCellWidthMultiplier + InventoryGridContent.CellGap;
        Assert.AreEqual(twoShopCells + global::UIEngine.UI.Chrome.WindowChrome.ScrollbarThickness, grid.CurrentSize.X);
    }

    [TestMethod]
    public void APressOnTheInventoryOrTheCorpseWindow_LeavesItemDetailsOpen()
    {
        var harness = BuildWithItemDetailsOpenOverACorpse();
        var controller = Controller(harness);
        var corpseWindow = harness.Layers[UiLayer.DynamicHud].OfType<SecondaryInventoryWindow>().Single();

        Press(controller, harness.Coordinator.Inventory.PlayerInventoryWindow!.Rectangle.Location + new Point(2, 2));
        Press(controller, corpseWindow.Rectangle.Location + new Point(2, 2));

        Assert.IsTrue(harness.Coordinator.ItemDetails.IsOpen);
    }

    [TestMethod]
    public void APressAwayFromEveryTiedWindow_ClosesItemDetails_AndOnlyIt()
    {
        var harness = BuildWithItemDetailsOpenOverACorpse();

        Press(Controller(harness), new Point(1990, 1990));

        Assert.IsFalse(harness.Coordinator.ItemDetails.IsOpen);
        Assert.IsNotNull(harness.Coordinator.Inventory.PlayerInventoryWindow);
        Assert.AreEqual(CorpseEntityId, OpenSecondaryTarget(harness));
    }

    [TestMethod]
    public void ClickingAnItem_WithComparisonArmed_AddsItAsAColumnAndKeepsTheAnchor()
    {
        var harness = Build();
        var anchorStackId = InventoryActions.AddItem(harness.ComponentManager, PlayerEntityId, PotionItemId, quantity: 1);
        var comparedStack = new Game.Modules.Inventory.Components.InventoryItemStackComponent(PotionItemId, 1);
        harness.ComponentManager.GetMultiPool<Game.Modules.Inventory.Components.InventoryItemStackComponent>().Add(PlayerEntityId, comparedStack);
        harness.Coordinator.Inventory.OpenInventoryWindow();
        harness.Coordinator.ItemComparison.Arm(PlayerEntityId, anchorStackId);
        Assert.IsTrue(harness.Coordinator.ItemComparison.IsArmed, "Sanity check: a potion has an activator, so comparison arms.");

        harness.Coordinator.Inventory.OnItemSelected!(PlayerEntityId, comparedStack.StackInstanceId);

        Assert.AreEqual(anchorStackId, harness.Coordinator.ItemDetails.CurrentStackInstanceId);
        Assert.HasCount(1, harness.Coordinator.ItemComparison.Columns);
    }

    [TestMethod]
    public void HoldingEscape_OverInventoryAndSeveralComparisonColumns_ClosesEveryMenuWindowAndLeavesMenuMode()
    {
        var harness = Build();
        var anchorStackId = InventoryActions.AddItem(harness.ComponentManager, PlayerEntityId, PotionItemId, quantity: 1);
        var stackPool = harness.ComponentManager.GetMultiPool<InventoryItemStackComponent>();
        var firstComparedStack = new InventoryItemStackComponent(PotionItemId, 1);
        var secondComparedStack = new InventoryItemStackComponent(PotionItemId, 1);
        var thirdComparedStack = new InventoryItemStackComponent(PotionItemId, 1);
        stackPool.Add(PlayerEntityId, firstComparedStack);
        stackPool.Add(PlayerEntityId, secondComparedStack);
        stackPool.Add(PlayerEntityId, thirdComparedStack);
        harness.Coordinator.Inventory.OpenInventoryWindow();
        harness.Coordinator.ItemComparison.Arm(PlayerEntityId, anchorStackId);
        harness.Coordinator.Inventory.OnItemSelected!(PlayerEntityId, firstComparedStack.StackInstanceId);
        harness.Coordinator.Inventory.OnItemSelected!(PlayerEntityId, secondComparedStack.StackInstanceId);
        harness.Coordinator.Inventory.OnItemSelected!(PlayerEntityId, thirdComparedStack.StackInstanceId);
        Assert.HasCount(3, harness.Coordinator.ItemComparison.Columns, "Sanity check: three comparison columns beside the anchor.");

        var controller = TestUiInputController.Create(
            harness.Layers,
            new Vector2(2000, 2000),
            harness.ComponentManager,
            TestPlayerQuery.NoPlayer,
            new EventBus(),
            harness.ItemCatalog,
            contextMenuController: harness.ContextMenuController,
            itemComparisonController: harness.Coordinator.ItemComparison,
            mapViewState: harness.MapViewState,
            inventoryWindowController: harness.Coordinator.Inventory);

        var escapeHeld = new KeyboardState(Keys.Escape);
        var mouseIdle = new MouseState();
        for (var frame = 0; frame < 40; frame++)
        {
            controller.Update(escapeHeld, mouseIdle);
        }

        Assert.IsNull(harness.Coordinator.Inventory.PlayerInventoryWindow);
        Assert.IsFalse(harness.Coordinator.ItemDetails.IsOpen);
        Assert.IsEmpty(harness.Coordinator.ItemComparison.Columns);
        Assert.IsFalse(harness.Layers[UiLayer.DynamicHud].OfType<ItemDetailsWindow>().Any(), "No Item Details window may be left in the DynamicHud layer.");
        Assert.IsNull(harness.Layers.BottommostMenuWindow, "No menu window may be left in a layer to draw the dim under.");
        Assert.IsFalse(harness.Layers.IsMenuModeActive, "Menu mode, and the game's pause with it, must end once every menu window has closed.");
    }
}
