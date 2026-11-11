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
using Game.Modules.Death.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Lootboxes;
using Game.Modules.Shops.Components;
using Game.Spawning;
using Game.Tags;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Input.DragDrop;
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

    private sealed record Harness(ItemWindowCoordinator Coordinator, MapWindow MapWindow, MapViewState MapViewState, Engine.ECS.Components.ComponentManager ComponentManager, UiLayerStack Layers, ItemCatalog ItemCatalog, ContextMenuController ContextMenuController, Game.Terrain.TerrainRegistry Terrain, PointerState PointerState, ItemHoverDetails HoverDetails);

    private static Harness Build()
    {
        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(
            PotionItemId, "Test Potion", null, "p", Color.Green, Tags: [GameTags.ItemConsumablePotion, GameTags.TargetingSelf],
            Effects: [new Effect([new DirectHeal(0.5f)])],
            Activator: new PotionActivator(new TargetingSpec(TargetShape.Circle, MaximumRange: 3, AreaSize: 1), new ActionTiming(ActionTimingCategory.Immediate, 60, null))));

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
        var cursorLabelContent = new CursorLabelContent(pointerState, fontService);
        var shellServices = new ShellServices(
            layers,
            mapViewState,
            TestMapCameras.Over(world),
            TestElementPoolServiceFactory.CreateContextMenuController(elementPool, layers),
            tooltipController,
            pointerState,
            new CursorTextContent(pointerState, fontService),
            cursorLabelContent,
            new ItemHoverDetails(cursorLabelContent));

        var eventBus = new EventBus();
        var inventoryServices = TestInventoryServices.Over(componentManager, itemCatalog, world, eventBus, shellServices.ItemHoverDetails);
        var definitions = new BlueprintRegistry();
        var entityNaming = EntityNaming.For(componentManager, definitions);
        var healthView = new HealthView(componentManager, Game.Modules.Health.EntityBodyParts.For(componentManager, definitions));
        var terrain = new Game.Terrain.TerrainRegistry();
        var mapView = new MapViewQuery(world, componentManager, terrain, definitions, new SimulationClock());
        var contextMenuController = shellServices.ContextMenuController;

        elementPool.RegisterFactory<Tooltip>(() => new Tooltip(TestElementServices.For(elementPool, fontService)));
        elementPool.RegisterFactory<GridControl>(() => new GridControl(TestElementServices.For(elementPool, fontService)));
        elementPool.RegisterFactory<Toggle>(() => new Toggle(TestElementServices.For(elementPool, fontService)));
        elementPool.RegisterFactory<TextDivider>(() => new TextDivider(TestElementServices.For(elementPool, fontService)));
        elementPool.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(TestElementServices.For(elementPool, fontService), spriteSheetService));
        elementPool.RegisterFactory<ShopItemStackCell>(() => new ShopItemStackCell(TestElementServices.For(elementPool, fontService), spriteSheetService));
        elementPool.RegisterFactory<TradeItemStackCell>(() => new TradeItemStackCell(TestElementServices.For(elementPool, fontService), spriteSheetService));
        elementPool.RegisterFactory<EmptyTradeSlotCell>(() => new EmptyTradeSlotCell(TestElementServices.For(elementPool, fontService)));
        elementPool.RegisterFactory<CurrencyElement>(() => new CurrencyElement(TestElementServices.For(elementPool, fontService), spriteSheetService));
        elementPool.RegisterFactory<ItemIconElement>(() => new ItemIconElement(TestElementServices.For(elementPool, fontService), spriteSheetService));
        elementPool.RegisterFactory<TargetShapePreviewElement>(() => new TargetShapePreviewElement(TestElementServices.For(elementPool, fontService)));
        elementPool.RegisterFactory<EntityIconElement>(() => new EntityIconElement(TestElementServices.For(elementPool, fontService), spriteSheetService, mapView));
        elementPool.RegisterFactory<ItemDetailsWindow>(() => new ItemDetailsWindow(TestElementServices.For(elementPool, fontService), new ActionCatalog(), TestGameplayTags.BuiltIn, inventoryServices.ActionStateView, world));
        elementPool.RegisterFactory<InventoryManagementWindow>(() => new InventoryManagementWindow(TestElementServices.For(elementPool, fontService), inventoryServices, world, contextMenuController, mapViewState));
        elementPool.RegisterFactory<SecondaryInventoryWindow>(() => new SecondaryInventoryWindow(TestElementServices.For(elementPool, fontService), inventoryServices, world, contextMenuController, mapViewState, entityNaming: entityNaming, healthView: healthView, actionSourceNaming: TestActionSources.Naming(terrain, entityKeys: componentManager.Keys)));
        elementPool.RegisterFactory<ShopWindow>(() => new ShopWindow(TestElementServices.For(elementPool, fontService), inventoryServices, world, contextMenuController, mapViewState, entityNaming: entityNaming));
        elementPool.RegisterFactory<TradeWindow>(() => new TradeWindow(TestElementServices.For(elementPool, fontService), inventoryServices, world, contextMenuController, mapViewState));

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
            new LootCommands(componentManager, itemCatalog, world),
            new LootboxCommands(new LootboxOpener(componentManager, lootboxCatalog, itemCatalog, seed: 1)),
            mapViewState.ReservedEntityIds);

        return new Harness(coordinator, mapHarness.MapWindow, mapViewState, componentManager, layers, itemCatalog, contextMenuController, terrain, pointerState, shellServices.ItemHoverDetails);
    }

    private static int? OpenSecondaryTarget(Harness harness) => harness.Coordinator.Inventory.GetSecondaryTargetEntityId!();

    [TestMethod]
    public void ClickingACorpse_WhileAShopIsOpen_ClosesTheShopAndOpensTheLoot()
    {
        var harness = Build();
        harness.Coordinator.OpenShop(ShopEntityId);
        Assert.AreEqual(ShopEntityId, harness.MapViewState.OpenShopEntityId, "Sanity check: the shop opened.");

        harness.Coordinator.OpenLoot(CorpseEntityId);

        Assert.IsNull(harness.MapViewState.OpenShopEntityId);
        Assert.AreEqual(CorpseEntityId, OpenSecondaryTarget(harness));
    }

    [TestMethod]
    public void ClickingAShop_WhileALootWindowIsOpen_ClosesTheLootAndOpensTheShop()
    {
        var harness = Build();
        harness.Coordinator.OpenLoot(CorpseEntityId);
        Assert.AreEqual(CorpseEntityId, OpenSecondaryTarget(harness), "Sanity check: the loot window opened.");

        harness.Coordinator.OpenShop(ShopEntityId);

        Assert.AreEqual(ShopEntityId, OpenSecondaryTarget(harness));
        Assert.AreEqual(ShopEntityId, harness.MapViewState.OpenShopEntityId);
    }

    [TestMethod]
    public void ReleaseEntity_ForTheOpenShop_ClosesIt()
    {
        var harness = Build();
        harness.Coordinator.OpenShop(ShopEntityId);

        harness.Coordinator.ReleaseEntity(ShopEntityId);

        Assert.IsNull(harness.MapViewState.OpenShopEntityId);
        Assert.IsNull(OpenSecondaryTarget(harness));
    }

    [TestMethod]
    public void ReleaseEntity_ForTheItemShownInItemDetails_ClosesItemDetails()
    {
        var harness = Build();
        harness.Coordinator.OpenLoot(CorpseEntityId);
        var stackInstanceId = InventoryActions.AddItem(harness.ComponentManager, CorpseEntityId, PotionItemId, quantity: 1);
        harness.Coordinator.ItemDetails.Open(CorpseEntityId, stackInstanceId);
        Assert.AreEqual(CorpseEntityId, harness.Coordinator.ItemDetails.CurrentEntityId, "Sanity check: Item Details opened on the corpse's potion.");

        harness.Coordinator.ReleaseEntity(CorpseEntityId);

        Assert.IsNull(OpenSecondaryTarget(harness));
        Assert.IsNull(harness.Coordinator.ItemDetails.CurrentEntityId);
    }

    [TestMethod]
    public void ClickingAnItem_WithComparisonDisarmed_SelectsItAndOpensNothing()
    {
        var harness = Build();
        var stackInstanceId = InventoryActions.AddItem(harness.ComponentManager, PlayerEntityId, PotionItemId, quantity: 1);
        harness.Coordinator.Inventory.OpenInventoryWindow();

        harness.Coordinator.Inventory.OnItemSelected!(PlayerEntityId, stackInstanceId);

        Assert.AreEqual(stackInstanceId, harness.MapViewState.SelectedItemStackInstanceId);
        Assert.IsFalse(harness.Coordinator.ItemDetails.IsOpen);
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
        harness.Coordinator.OpenLoot(CorpseEntityId);
        var stackInstanceId = InventoryActions.AddItem(harness.ComponentManager, CorpseEntityId, PotionItemId, quantity: 1);
        harness.Coordinator.ItemDetails.Open(CorpseEntityId, stackInstanceId);
        Assert.IsTrue(harness.Coordinator.ItemDetails.IsOpen, "Sanity check: Item Details opened on the corpse's potion.");
        return harness;
    }

    private static (SecondaryInventoryWindow Window, Window Grid) OpenCorpseLoot(Harness harness)
    {
        harness.Coordinator.Inventory.OpenInventoryWindow();
        harness.Coordinator.OpenLoot(CorpseEntityId);
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

    private static string SlainByLine(SecondaryInventoryWindow window) =>
        window.ChildElements.OfType<TextWindow>().Single(line => line.OriginalText.StartsWith("Slain by:")).OriginalText;

    [TestMethod]
    public void ACorpseThatKilledItself_IsSlainBySelf()
    {
        var harness = Build();
        harness.ComponentManager.Keys.Issue(CorpseEntityId);
        var killer = ActionSource.FromEntity(harness.ComponentManager, harness.ComponentManager.Keys, CorpseEntityId, new BlueprintRegistry());
        harness.ComponentManager.Merge(CorpseEntityId, new DeadComponent(killer, DiedAtFrame: 0));

        var (window, _) = OpenCorpseLoot(harness);

        Assert.AreEqual("Slain by: Self", SlainByLine(window));
    }

    [TestMethod]
    public void ACorpseKilledByTerrain_IsSlainByTheTerrainsName()
    {
        var harness = Build();
        var lavaId = harness.Terrain.Register(new Game.Terrain.TerrainDefinition("test:lava", "Lava", "", "~", default));
        harness.ComponentManager.Merge(CorpseEntityId, new DeadComponent(ActionSource.FromTerrain(lavaId), DiedAtFrame: 0));

        var (window, _) = OpenCorpseLoot(harness);

        Assert.AreEqual("Slain by: Lava", SlainByLine(window));
    }

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
        harness.Coordinator.OpenShop(ShopEntityId);
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

    private sealed class NoShopPricing : IItemShopPricingSource
    {
        public IReadOnlyList<TooltipRow>? ShopPricingRowsFor(in ItemHoverTarget target) => null;
    }

    /// <summary>Two potions in the player's inventory, the inventory open, and compare mode started on the first.</summary>
    private static (Harness Harness, uint AnchorStackId, uint OtherStackId) BuildComparingTwoPotions()
    {
        var harness = Build();
        var anchorStackId = InventoryActions.AddItem(harness.ComponentManager, PlayerEntityId, PotionItemId, quantity: 1);
        var otherStack = new InventoryItemStackComponent(PotionItemId, 1);
        harness.ComponentManager.GetMultiPool<InventoryItemStackComponent>().Add(PlayerEntityId, otherStack);
        harness.Coordinator.Inventory.OpenInventoryWindow();
        harness.Coordinator.ItemComparison.Arm(PlayerEntityId, anchorStackId);
        Assert.IsTrue(harness.Coordinator.ItemComparison.IsComparing, "Sanity check: a potion has an activator, so compare mode starts.");
        return (harness, anchorStackId, otherStack.StackInstanceId);
    }

    private static UiInputController ComparisonController(Harness harness) =>
        TestUiInputController.Create(
            harness.Layers, new Vector2(2000, 2000), harness.ComponentManager, TestPlayerQuery.NoPlayer, new EventBus(), harness.ItemCatalog,
            contextMenuController: harness.ContextMenuController, itemComparisonController: harness.Coordinator.ItemComparison,
            mapViewState: harness.MapViewState, inventoryWindowController: harness.Coordinator.Inventory);

    private static void HoverOtherPotion(Harness harness, uint otherStackId)
    {
        var definition = harness.ItemCatalog.Get(PotionItemId);
        harness.Coordinator.ItemComparison.OpenAtCursor(new ItemHoverTarget(PlayerEntityId, otherStackId, definition), new NoShopPricing());
    }

    [TestMethod]
    public void ClosingTheInventoryWindow_ClearsTheSelectedItem()
    {
        var harness = Build();
        var stackId = InventoryActions.AddItem(harness.ComponentManager, PlayerEntityId, PotionItemId, quantity: 1);
        harness.Coordinator.Inventory.OpenInventoryWindow();
        harness.Coordinator.Inventory.OnItemSelected!(PlayerEntityId, stackId);
        Assert.AreEqual(stackId, harness.MapViewState.SelectedItemStackInstanceId);

        harness.Coordinator.Inventory.ToggleInventoryWindow();

        Assert.IsNull(harness.MapViewState.SelectedItemStackInstanceId);
    }

    [TestMethod]
    public void Compare_PinsItemDetailsBesideTheInventoryWindow_WithoutOverlappingIt()
    {
        var (harness, _, _) = BuildComparingTwoPotions();

        var inventoryRectangle = harness.Coordinator.Inventory.PlayerInventoryWindow!.Rectangle;
        Assert.IsTrue(harness.Coordinator.ItemDetails.IsPinned);
        Assert.IsFalse(harness.Coordinator.ItemDetails.Rectangle.Intersects(inventoryRectangle));
    }

    [TestMethod]
    public void HoveringAnotherPotionWhileComparing_OpensItsComparisonBelowTheAnchor_AndKeepsTheAnchorWhereItIs()
    {
        var (harness, anchorStackId, otherStackId) = BuildComparingTwoPotions();
        var anchorPosition = harness.Coordinator.ItemDetails.Rectangle.Location;

        HoverOtherPotion(harness, otherStackId);

        Assert.HasCount(2, harness.Layers[UiLayer.DynamicHud].OfType<ItemDetailsWindow>().ToList());
        Assert.AreEqual(anchorStackId, harness.Coordinator.ItemDetails.CurrentStackInstanceId);
        Assert.AreEqual(anchorPosition, harness.Coordinator.ItemDetails.Rectangle.Location);
        var anchor = harness.Coordinator.ItemDetails.Rectangle;
        var comparison = harness.Layers[UiLayer.DynamicHud].OfType<ItemDetailsWindow>().Single(window => window.Rectangle != anchor).Rectangle;
        Assert.AreEqual(anchor.Left, comparison.Left);
        Assert.IsGreaterThanOrEqualTo(anchor.Bottom, comparison.Top);
    }

    [TestMethod]
    public void HoveringAnItemWithNoActivatorWhileComparing_OpensNothing_AndItsCellRefusesTheCursor()
    {
        var (harness, _, _) = BuildComparingTwoPotions();
        var trinket = new ItemDefinition(Guid.NewGuid(), "Trinket", null, "t", Color.White, Tags: [], Effects: []);
        harness.ItemCatalog.Register(trinket);
        var trinketStackId = InventoryActions.AddItem(harness.ComponentManager, PlayerEntityId, trinket.Id, quantity: 1);
        var target = new ItemHoverTarget(PlayerEntityId, trinketStackId, trinket);

        Assert.IsFalse(harness.Coordinator.ItemComparison.OpensFor(target));
        harness.Coordinator.ItemComparison.OpenAtCursor(target, new NoShopPricing());
        Assert.HasCount(1, harness.Layers[UiLayer.DynamicHud].OfType<ItemDetailsWindow>().ToList(), "Only the pinned anchor is open.");

        var cell = DescendantsOf(harness.Coordinator.Inventory.PlayerInventoryWindow!).OfType<InventoryItemStackCell>().First();
        cell.CompareState = CellCompareState.Ineligible;
        Assert.IsTrue(new global::Presentation.Input.ItemComparisonInputHandler(harness.Coordinator.ItemComparison).IsInputRefusedAt(cell, Point.Zero));
    }

    [TestMethod]
    public void EscapeWhileComparing_EndsItAndClosesBothDetailsWindows_AndNothingElse()
    {
        var (harness, _, otherStackId) = BuildComparingTwoPotions();
        HoverOtherPotion(harness, otherStackId);
        var controller = ComparisonController(harness);

        controller.Update(new KeyboardState(Keys.Escape), new MouseState());

        Assert.IsFalse(harness.Coordinator.ItemComparison.IsComparing);
        Assert.IsFalse(harness.Layers[UiLayer.DynamicHud].OfType<ItemDetailsWindow>().Any());
        Assert.IsNotNull(harness.Coordinator.Inventory.PlayerInventoryWindow);
    }

    [TestMethod]
    public void ARightPressWhileComparing_EndsItAndClosesBothDetailsWindows()
    {
        var (harness, _, otherStackId) = BuildComparingTwoPotions();
        HoverOtherPotion(harness, otherStackId);
        var controller = ComparisonController(harness);

        controller.Update(new KeyboardState(), new MouseState(1990, 1990, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
        controller.Update(new KeyboardState(), new MouseState(1990, 1990, 0, ButtonState.Released, ButtonState.Released, ButtonState.Pressed, ButtonState.Released, ButtonState.Released));

        Assert.IsFalse(harness.Coordinator.ItemComparison.IsComparing);
        Assert.IsFalse(harness.Layers[UiLayer.DynamicHud].OfType<ItemDetailsWindow>().Any());
    }

    [TestMethod]
    public void HoldingEscape_WhileComparing_ClosesEveryMenuWindowAndLeavesMenuMode()
    {
        var (harness, _, otherStackId) = BuildComparingTwoPotions();
        HoverOtherPotion(harness, otherStackId);
        var controller = ComparisonController(harness);

        var escapeHeld = new KeyboardState(Keys.Escape);
        for (var frame = 0; frame < 40; frame++)
        {
            controller.Update(escapeHeld, new MouseState());
        }

        Assert.IsNull(harness.Coordinator.Inventory.PlayerInventoryWindow);
        Assert.IsFalse(harness.Layers[UiLayer.DynamicHud].OfType<ItemDetailsWindow>().Any(), "No Item Details window may be left in the DynamicHud layer.");
        Assert.IsNull(harness.Layers.BottommostMenuWindow, "No menu window may be left in a layer to draw the dim under.");
        Assert.IsFalse(harness.Layers.IsMenuModeActive, "Menu mode, and the game's pause with it, must end once every menu window has closed.");
    }

    private static IEnumerable<Element> DescendantsOf(Element element)
    {
        foreach (var child in element.ChildElements)
        {
            yield return child;
            foreach (var descendant in DescendantsOf(child))
            {
                yield return descendant;
            }
        }
    }

    private static ItemDetailsWindow OpenItemDetailsOnAPlayerPotion(Harness harness, out uint stackInstanceId)
    {
        stackInstanceId = InventoryActions.AddItem(harness.ComponentManager, PlayerEntityId, PotionItemId, quantity: 1);
        harness.Coordinator.Inventory.OpenInventoryWindow();
        harness.Coordinator.ItemDetails.Open(PlayerEntityId, stackInstanceId);
        return harness.Layers[UiLayer.DynamicHud].OfType<ItemDetailsWindow>().Single();
    }

    [TestMethod]
    public void ItemDetails_OpensBelowAndRightOfTheCursor_WithNoTitleBar()
    {
        var harness = Build();
        harness.PointerState.CursorPosition = new Point(300, 200);

        var window = OpenItemDetailsOnAPlayerPotion(harness, out _);

        Assert.IsGreaterThan(300f, window.RelativePosition.X);
        Assert.IsGreaterThan(200f, window.RelativePosition.Y);
        Assert.IsFalse(window.ShowHeader);
    }

    [TestMethod]
    public void ItemDetails_PointerOverNeitherItsItemNorItself_StaysForAMomentThenCloses()
    {
        var harness = Build();
        var window = OpenItemDetailsOnAPlayerPotion(harness, out _);
        harness.PointerState.HoveredElement = null;

        window.Update(new GameTime());
        Assert.IsTrue(harness.Coordinator.ItemDetails.IsOpen, "The pointer may cross the gap to the popup.");

        for (var frame = 0; frame < 60 && harness.Coordinator.ItemDetails.IsOpen; frame++)
        {
            window.Update(new GameTime());
        }

        Assert.IsFalse(harness.Coordinator.ItemDetails.IsOpen);
    }

    private static IEnumerable<string> ButtonLabels(ItemDetailsWindow window) =>
        DescendantsOf(window).OfType<Button>().Select(button => button.LeftText);

    [TestMethod]
    public void ItemDetails_ForAComparableStack_HasCompareAndNoStopComparison()
    {
        var harness = Build();

        var window = OpenItemDetailsOnAPlayerPotion(harness, out _);

        CollectionAssert.AreEqual(new[] { "Compare" }, ButtonLabels(window).ToArray());
    }

    [TestMethod]
    public void ItemDetails_WhileComparing_HasOnlyStopComparison_AndItEndsTheComparisonAndCloses()
    {
        var harness = Build();
        var window = OpenItemDetailsOnAPlayerPotion(harness, out var stackInstanceId);
        harness.Coordinator.ItemComparison.Arm(PlayerEntityId, stackInstanceId);
        window.Update(new GameTime());

        var buttons = DescendantsOf(window).OfType<Button>().ToList();
        CollectionAssert.AreEqual(new[] { "Stop Comparison" }, buttons.Select(button => button.LeftText).ToArray());

        buttons[0].HandleClick(buttons[0].Rectangle.Center);

        Assert.IsFalse(harness.Coordinator.ItemComparison.IsComparing);
        Assert.IsFalse(harness.Coordinator.ItemDetails.IsOpen, "Stopping closes Item Details too.");
    }


    private static ItemDragDetailsDismissal DragDismissal(Harness harness) => new(harness.Coordinator, harness.HoverDetails);

    private static int ItemDetailsWindowsOpen(Harness harness) => harness.Layers[UiLayer.DynamicHud].OfType<ItemDetailsWindow>().Count();

    [TestMethod]
    public void StartingToDragAnItem_WhileComparing_EndsTheComparison_AndClosesBothItemDetailsWindows()
    {
        var (harness, _, otherStackId) = BuildComparingTwoPotions();
        HoverOtherPotion(harness, otherStackId);
        Assert.AreEqual(2, ItemDetailsWindowsOpen(harness), "Sanity check: the anchor and the comparison popup are open.");

        DragDismissal(harness).OnDragStarted(new DragPayload(DragPayloadOrigin.InventoryCell, ItemStackInstanceId: otherStackId, OriginEntityId: PlayerEntityId));

        Assert.IsFalse(harness.Coordinator.ItemComparison.IsComparing);
        Assert.AreEqual(0, ItemDetailsWindowsOpen(harness));
        Assert.IsNull(harness.MapViewState.CompareRequiredActivatorType);
    }

    [TestMethod]
    public void StartingToDragAnItem_ClosesItemDetails()
    {
        var harness = Build();
        OpenItemDetailsOnAPlayerPotion(harness, out var stackInstanceId);

        DragDismissal(harness).OnDragStarted(new DragPayload(DragPayloadOrigin.InventoryCell, ItemStackInstanceId: stackInstanceId, OriginEntityId: PlayerEntityId));

        Assert.IsFalse(harness.Coordinator.ItemDetails.IsOpen);
    }

    [TestMethod]
    public void StartingToDragAMergedStack_ClosesItemDetails()
    {
        var harness = Build();
        OpenItemDetailsOnAPlayerPotion(harness, out _);

        DragDismissal(harness).OnDragStarted(new DragPayload(DragPayloadOrigin.InventoryCell, MergedItemDefinitionId: PotionItemId, OriginEntityId: PlayerEntityId));

        Assert.IsFalse(harness.Coordinator.ItemDetails.IsOpen);
    }

    [TestMethod]
    public void StartingToDragCurrencyOrAnAction_LeavesItemDetailsAndTheComparisonAlone()
    {
        var (harness, _, _) = BuildComparingTwoPotions();
        var dismissal = DragDismissal(harness);

        dismissal.OnDragStarted(new DragPayload(DragPayloadOrigin.Currency, CurrencyType: Game.Modules.Currency.CurrencyType.Gold, OriginEntityId: PlayerEntityId));
        dismissal.OnDragStarted(new DragPayload(DragPayloadOrigin.HotbarSlot, ActionId: BuiltInActionIds.Heal));

        Assert.IsTrue(harness.Coordinator.ItemComparison.IsComparing);
        Assert.IsTrue(harness.Coordinator.ItemDetails.IsOpen);
        Assert.IsFalse(harness.HoverDetails.IsPopupHeldBack);
    }

    [TestMethod]
    public void WhileAnItemIsDragged_AHoverOpensNoItemDetails_UntilTheDragEnds()
    {
        var harness = Build();
        var stackInstanceId = InventoryActions.AddItem(harness.ComponentManager, PlayerEntityId, PotionItemId, quantity: 1);
        harness.Coordinator.Inventory.OpenInventoryWindow();
        var payload = new DragPayload(DragPayloadOrigin.InventoryCell, ItemStackInstanceId: stackInstanceId, OriginEntityId: PlayerEntityId);
        var target = new ItemHoverTarget(PlayerEntityId, stackInstanceId, harness.ItemCatalog.Get(PotionItemId));
        var dismissal = DragDismissal(harness);

        dismissal.OnDragStarted(payload);
        harness.HoverDetails.Hover(this, target, ItemHoverDetails.DetailsDelayFrames, new NoShopPricing());
        Assert.IsFalse(harness.Coordinator.ItemDetails.IsOpen);

        dismissal.OnDragEnded(payload);
        harness.HoverDetails.Hover(this, target, ItemHoverDetails.DetailsDelayFrames * 2, new NoShopPricing());
        Assert.IsTrue(harness.Coordinator.ItemDetails.IsOpen);
    }

    [TestMethod]
    public void ItemDetails_PointerOverItsOwnContent_StaysOpen()
    {
        var harness = Build();
        var window = OpenItemDetailsOnAPlayerPotion(harness, out _);
        harness.PointerState.HoveredElement = window.ChildElements.First();

        window.Update(new GameTime());

        Assert.IsTrue(harness.Coordinator.ItemDetails.IsOpen);
    }

    [TestMethod]
    public void ItemDetails_PointerOverItsItemsCell_StaysOpen()
    {
        var harness = Build();
        var window = OpenItemDetailsOnAPlayerPotion(harness, out var stackInstanceId);
        var cell = DescendantsOf(harness.Coordinator.Inventory.PlayerInventoryWindow!).OfType<InventoryItemStackCell>().Single(candidate => candidate.StackInstanceId == stackInstanceId);
        harness.PointerState.HoveredElement = cell;

        window.Update(new GameTime());

        Assert.IsTrue(harness.Coordinator.ItemDetails.IsOpen);
    }

    [TestMethod]
    public void ItemDetails_WhileComparing_StaysOpenWhereverThePointerIs()
    {
        var harness = Build();
        var window = OpenItemDetailsOnAPlayerPotion(harness, out var stackInstanceId);
        harness.Coordinator.ItemComparison.Arm(PlayerEntityId, stackInstanceId);
        harness.PointerState.HoveredElement = null;

        window.Update(new GameTime());

        Assert.IsTrue(harness.Coordinator.ItemDetails.IsOpen);
    }
}
