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
using Game.Modules.Currency;
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
using Presentation.UI.Chrome;
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
        var cursorTextContent = new CursorTextContent(pointerState, fontService);
        var shellServices = new ShellServices(
            layers,
            mapViewState,
            TestMapCameras.Over(world),
            TestElementPoolServiceFactory.CreateContextMenuController(elementPool, layers),
            tooltipController,
            pointerState,
            cursorTextContent,
            new ItemHoverDetails(cursorTextContent),
            new ItemDetailsModeSetting());

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
        elementPool.RegisterFactory<TextDivider>(() => new TextDivider(TestElementServices.For(elementPool, fontService)));
        elementPool.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(TestElementServices.For(elementPool, fontService), spriteSheetService));
        elementPool.RegisterFactory<ShopItemStackCell>(() => new ShopItemStackCell(TestElementServices.For(elementPool, fontService), spriteSheetService));
        elementPool.RegisterFactory<TradeItemStackCell>(() => new TradeItemStackCell(TestElementServices.For(elementPool, fontService), spriteSheetService));
        elementPool.RegisterFactory<EmptyTradeSlotCell>(() => new EmptyTradeSlotCell(TestElementServices.For(elementPool, fontService)));
        elementPool.RegisterFactory<CurrencyElement>(() => new CurrencyElement(TestElementServices.For(elementPool, fontService), spriteSheetService));
        elementPool.RegisterFactory<TargetShapePreviewElement>(() => new TargetShapePreviewElement(TestElementServices.For(elementPool, fontService)));
        elementPool.RegisterFactory<EntityIconElement>(() => new EntityIconElement(TestElementServices.For(elementPool, fontService), spriteSheetService, mapView));
        elementPool.RegisterFactory<ItemDetailsWindow>(() => new ItemDetailsWindow(TestElementServices.For(elementPool, fontService), new ActionCatalog(), new EffectTextNames(TestGameplayTags.BuiltIn), inventoryServices.ActionStateView, TestActionStateViews.Previews(componentManager), world));
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

    private static Button TakeAllButton(SecondaryInventoryWindow window) =>
        window.ChildElements.OfType<Window>().Single(child => child.Content is CurrencyRowContent).ChildElements.OfType<Button>().Single();

    [TestMethod]
    public void ACorpsesFooterButton_ReadsLootAll_AndMovesEveryItemAndAllCurrencyToThePlayer()
    {
        var harness = Build();
        InventoryActions.AddItem(harness.ComponentManager, CorpseEntityId, PotionItemId, quantity: 2);
        harness.ComponentManager.Merge(CorpseEntityId, new CurrencyComponent(gold: 7, credits: 3));
        var (window, _) = OpenCorpseLoot(harness);
        var button = TakeAllButton(window);
        Assert.AreEqual("Loot All", button.Text);
        Assert.IsTrue(button.Enabled);

        button.Press();
        window.Update(new GameTime());

        Assert.AreEqual(0, harness.ComponentManager.GetMultiPool<InventoryItemStackComponent>().CountForEntity(CorpseEntityId));
        Assert.AreEqual(1, harness.ComponentManager.GetMultiPool<InventoryItemStackComponent>().CountForEntity(PlayerEntityId));
        Assert.IsTrue(harness.ComponentManager.GetPackedPool<CurrencyComponent>().TryGetReadonly(PlayerEntityId, out var playerCurrency));
        Assert.AreEqual(7, playerCurrency.Gold);
        Assert.AreEqual(3, playerCurrency.Credits);
        Assert.IsFalse(button.Enabled, "Nothing is left to take.");
    }

    [TestMethod]
    public void AnAmountPastFourDigits_PutsEachCurrencyOnItsOwnRow_WithTheButtonBesideTheTopOne()
    {
        var harness = Build();
        harness.ComponentManager.Merge(CorpseEntityId, new CurrencyComponent(gold: 10000, credits: 3));
        var (window, grid) = OpenCorpseLoot(harness);
        var footer = window.ChildElements.OfType<Window>().Single(child => child.Content is CurrencyRowContent);
        var gold = footer.ChildElements.OfType<CurrencyElement>().Single(element => element.Type == CurrencyType.Gold);
        var credits = footer.ChildElements.OfType<CurrencyElement>().Single(element => element.Type == CurrencyType.Credits);
        var button = TakeAllButton(window);

        Assert.AreEqual(CurrencyRowContent.Height * 2, footer.CurrentSize.Y);
        Assert.AreEqual(gold.Rectangle.Bottom, credits.Rectangle.Top);
        Assert.AreEqual(gold.Rectangle.Top, button.Rectangle.Top);
        Assert.IsLessThanOrEqualTo(button.Rectangle.Left, gold.Rectangle.Right);
        Assert.IsLessThanOrEqualTo(footer.Rectangle.Top, grid.Rectangle.Bottom);
        Assert.IsLessThanOrEqualTo(window.Rectangle.Bottom, footer.Rectangle.Bottom);
    }

    [TestMethod]
    public void TakingALargeAmount_ReturnsTheAmountsToOneRow_AndShrinksTheWindow()
    {
        var harness = Build();
        harness.ComponentManager.Merge(CorpseEntityId, new CurrencyComponent(gold: 123456, credits: 3));
        var (window, grid) = OpenCorpseLoot(harness);
        var footer = window.ChildElements.OfType<Window>().Single(child => child.Content is CurrencyRowContent);
        var stackedHeight = window.CurrentSize.Y;
        var gridBottom = grid.Rectangle.Bottom;

        TakeAllButton(window).Press();
        window.Update(new GameTime());

        Assert.AreEqual(CurrencyRowContent.Height, footer.CurrentSize.Y);
        Assert.AreEqual(stackedHeight - CurrencyRowContent.Height, window.CurrentSize.Y);
        Assert.AreEqual(gridBottom, grid.Rectangle.Bottom);
        var gold = footer.ChildElements.OfType<CurrencyElement>().Single(element => element.Type == CurrencyType.Gold);
        var credits = footer.ChildElements.OfType<CurrencyElement>().Single(element => element.Type == CurrencyType.Credits);
        Assert.AreEqual(gold.Rectangle.Top, credits.Rectangle.Top);
    }

    [TestMethod]
    public void ALongSummaryLine_WrapsAndPushesTheLinesAndGridBelowItDown()
    {
        var harness = Build();
        var longTerrainId = harness.Terrain.Register(new Game.Terrain.TerrainDefinition("test:long", "A very long terrain name that cannot fit on one summary line at all", "", "~", default));
        harness.ComponentManager.Merge(CorpseEntityId, new DeadComponent(ActionSource.FromTerrain(longTerrainId), DiedAtFrame: 0));

        var (window, grid) = OpenCorpseLoot(harness);

        var slainBy = window.ChildElements.OfType<TextWindow>().Single(line => line.OriginalText.StartsWith("Slain by:"));
        var diedAt = window.ChildElements.OfType<TextWindow>().Single(line => line.OriginalText.StartsWith("Died at tick:"));
        var header = window.ChildElements.OfType<TextWindow>().Single(line => line.OriginalText.Contains('/') && !line.OriginalText.StartsWith("Slain by:"));
        Assert.IsGreaterThan(1, slainBy.DisplayText.LineCount);
        Assert.IsGreaterThanOrEqualTo(slainBy.Rectangle.Bottom, diedAt.Rectangle.Top);
        Assert.IsGreaterThanOrEqualTo(diedAt.Rectangle.Bottom, header.Rectangle.Top);
        Assert.IsGreaterThanOrEqualTo(header.Rectangle.Bottom, grid.Rectangle.Top);
    }

    [TestMethod]
    public void AContainersFooterButton_ReadsTakeAll()
    {
        var harness = Build();
        harness.ComponentManager.Merge(CorpseEntityId, new Game.Modules.Containers.Components.ContainerComponent());

        var (window, _) = OpenCorpseLoot(harness);

        Assert.AreEqual("Take All", TakeAllButton(window).Text);
    }

    [TestMethod]
    public void ALootWindowHoldingOnlyAnUntradeableItem_DisablesItsFooterButton()
    {
        var harness = Build();
        var untradeableItemId = Guid.NewGuid();
        harness.ItemCatalog.Register(new ItemDefinition(untradeableItemId, "Bound", null, "b", Color.White, Tags: [], Effects: [], CanTrade: false));
        InventoryActions.AddItem(harness.ComponentManager, CorpseEntityId, untradeableItemId, quantity: 1);

        var (window, _) = OpenCorpseLoot(harness);

        Assert.IsFalse(TakeAllButton(window).Enabled);
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

    private static float CardsWide(int count) => count * InventoryGridContent.CellSize.X * InventoryGridContent.ShopCellWidthMultiplier + (count - 1) * InventoryGridContent.CellGap;

    private static IEnumerable<Element> SelfAndDescendants(Element element) => element.ChildElements.SelectMany(SelfAndDescendants).Prepend(element);

    private static Toggle InventoryCardViewSwitch(Window inventoryWindow) => SelfAndDescendants(inventoryWindow).OfType<Toggle>().ElementAt(InventoryTabContent.CardViewToggleIndex);

    private static float SquaresWide(int count) => count * InventoryGridContent.CellSize.X + (count - 1) * InventoryGridContent.CellGap;

    [TestMethod]
    public void AShopWindow_HoldsTwoCardsAndAScrollbar_CountsItsStacksOutOfInfinity_AndCantBeResized()
    {
        var harness = Build();
        AddDifferentItems(harness, 3, ShopEntityId);
        var (window, grid) = OpenShop(harness);

        Assert.AreEqual(CardsWide(2), grid.CurrentSize.X);
        Assert.AreEqual(CardsWide(2) + global::UIEngine.UI.Chrome.WindowChrome.ScrollbarThickness + window.ContentInset.X, window.CurrentSize.X);
        Assert.AreEqual(5, RowsShown(grid));
        Assert.IsTrue(window.ChildElements.OfType<TextWindow>().Any(line => line.OriginalText == "3/∞"));
        Assert.IsFalse(window.CanUserResize);
        Assert.IsFalse(window.ChildElements.OfType<Toggle>().Any(), "The shop has no view switch of its own: the player's Inventory window switches both sides.");
    }

    [TestMethod]
    public void AShopWindow_PastTwelveRowsScrolls_AndItsScrollbarFitsTheWindowItKept()
    {
        var harness = Build();
        var (window, grid) = OpenShop(harness);
        var widthBefore = window.CurrentSize.X;

        AddDifferentItems(harness, 30, ShopEntityId);
        window.Update(new GameTime());

        Assert.AreEqual(12, RowsShown(grid));
        Assert.AreEqual(CardsWide(2) + global::UIEngine.UI.Chrome.WindowChrome.ScrollbarThickness, grid.CurrentSize.X);
        Assert.AreEqual(widthBefore, window.CurrentSize.X);
    }

    [TestMethod]
    public void TheInventoryWindowsSwitch_ShowsTheShopAsSquaresAcrossTheSameWidth()
    {
        var harness = Build();
        AddDifferentItems(harness, 3, ShopEntityId);
        var (window, grid) = OpenShop(harness);
        var widthBefore = window.CurrentSize.X;
        var inventoryWindow = harness.Coordinator.Inventory.PlayerInventoryWindow!;
        inventoryWindow.Update(new GameTime());
        var inventoryCardViewSwitch = InventoryCardViewSwitch(inventoryWindow);

        inventoryCardViewSwitch.HandleClick(inventoryCardViewSwitch.Rectangle.Center);
        window.Update(new GameTime());

        Assert.AreEqual(SquaresWide(7), grid.CurrentSize.X, "As many squares as fit two cards' width.");
        Assert.AreEqual(widthBefore, window.CurrentSize.X);
        Assert.IsFalse(grid.ChildElements.OfType<ShopItemStackCell>().Any());
    }

    [TestMethod]
    public void TheInventoryWindow_HoldsFifteenSquaresOrFourCardsARow_EvenWithItsScrollbarShowing()
    {
        var harness = Build();
        AddDifferentItems(harness, 120, PlayerEntityId);
        harness.Coordinator.Inventory.OpenInventoryWindow();
        var inventoryWindow = harness.Coordinator.Inventory.PlayerInventoryWindow!;
        int CellsInFirstRow() => SelfAndDescendants(inventoryWindow).OfType<InventoryItemStackCell>().Count(cell => cell.RelativePosition.Y == 0);

        inventoryWindow.Update(new GameTime());
        Assert.AreEqual(InventoryChrome.WindowWidth, inventoryWindow.CurrentSize.X);
        Assert.AreEqual(15, CellsInFirstRow());

        var cardViewSwitch = InventoryCardViewSwitch(inventoryWindow);
        cardViewSwitch.HandleClick(cardViewSwitch.Rectangle.Center);
        inventoryWindow.Update(new GameTime());
        Assert.AreEqual(4, CellsInFirstRow());
    }

    [TestMethod]
    public void WhileShopping_TheInventoryWindowsCardViewSwitch_SwitchesBothSides()
    {
        var harness = Build();
        AddDifferentItems(harness, 3, ShopEntityId);
        var (window, grid) = OpenShop(harness);
        var inventoryWindow = harness.Coordinator.Inventory.PlayerInventoryWindow!;
        inventoryWindow.Update(new GameTime());
        var inventoryCardViewSwitch = InventoryCardViewSwitch(inventoryWindow);
        Assert.IsTrue(inventoryCardViewSwitch.IsOn, "It shows the shop's Card view, not the inventory's own Icon view.");

        inventoryCardViewSwitch.HandleClick(inventoryCardViewSwitch.Rectangle.Center);
        window.Update(new GameTime());

        Assert.IsFalse(grid.ChildElements.OfType<ShopItemStackCell>().Any(), "The shop's side follows.");
        window.Close();
        inventoryWindow.Update(new GameTime());
        Assert.IsFalse(inventoryCardViewSwitch.IsOn, "Once the shop closes, the inventory's own Icon view again.");
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

        public (int Buy, int Sell)? ReferencePricesFor(in ItemHoverTarget target) => null;
    }

    /// <summary>Two potions in the player's inventory, the inventory open, and compare mode started on the first.</summary>
    private static (Harness Harness, uint AnchorStackId, uint OtherStackId) BuildComparingTwoPotions()
    {
        var harness = Build();
        var anchorStackId = InventoryActions.AddItem(harness.ComponentManager, PlayerEntityId, PotionItemId, quantity: 1);
        var otherStack = new InventoryItemStackComponent(PotionItemId, 1);
        harness.ComponentManager.GetMultiPool<InventoryItemStackComponent>().Add(PlayerEntityId, otherStack);
        harness.Coordinator.Inventory.OpenInventoryWindow();
        harness.Coordinator.Inventory.PlayerInventoryWindow!.SetRelativePosition(new Vector2(0, 150));
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
        Assert.IsTrue(new global::Presentation.Input.ComparisonInputHandler(harness.Coordinator.ItemComparison).IsInputRefusedAt(cell, Point.Zero));
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
        DescendantsOf(window).OfType<Button>().Select(ButtonContent.TextOf);

    [TestMethod]
    public void ItemDetails_ForAComparableStack_HasCompareAndTheModeButton_AndNoStopComparison()
    {
        var harness = Build();

        var window = OpenItemDetailsOnAPlayerPotion(harness, out _);

        CollectionAssert.AreEquivalent(new[] { "Compare", "Advanced" }, ButtonLabels(window).ToArray());
    }

    [TestMethod]
    public void TheButtons_SitOnTheNameRow_RightOfTheName()
    {
        var harness = Build();
        var window = OpenItemDetailsOnAPlayerPotion(harness, out _);
        var name = harness.ItemCatalog.Get(PotionItemId).Name;

        foreach (var button in DescendantsOf(window).OfType<Button>())
        {
            var row = button.ParentElement!;
            var nameLine = row.ChildElements.OfType<TextWindow>().Single(line => line.OriginalText == name);
            Assert.IsGreaterThanOrEqualTo(nameLine.Rectangle.Right, button.Rectangle.Left, $"{ButtonContent.TextOf(button)} overlaps the name.");
        }
    }

    [TestMethod]
    public void TheComparedItem_HasNoButtons_WhileTheAnchorHasStopComparison()
    {
        var (harness, _, otherStackId) = BuildComparingTwoPotions();
        HoverOtherPotion(harness, otherStackId);

        var anchor = harness.Coordinator.ItemDetails.Window!;
        var compared = harness.Layers[UiLayer.DynamicHud].OfType<ItemDetailsWindow>().Single(window => !ReferenceEquals(window, anchor));
        anchor.Update(new GameTime());
        compared.Update(new GameTime());

        CollectionAssert.AreEqual(new[] { "Stop Comparison" }, ButtonLabels(anchor).ToArray());
        Assert.IsFalse(ButtonLabels(compared).Any(), string.Join(", ", ButtonLabels(compared)));
    }

    [TestMethod]
    public void TheModeButton_SwitchesToTheModeItNames_AndTheChoiceSurvivesReopening()
    {
        var harness = Build();
        var window = OpenItemDetailsOnAPlayerPotion(harness, out _);
        Assert.AreEqual(DetailsMode.Simple, window.ShownMode);

        var modeButton = DescendantsOf(window).OfType<Button>().Single(button => ButtonContent.TextOf(button) == "Advanced");
        modeButton.HandleClick(modeButton.Rectangle.Center);
        window.Update(new GameTime());

        Assert.AreEqual(DetailsMode.Advanced, window.ShownMode);
        CollectionAssert.AreEquivalent(new[] { "Compare", "Simple" }, ButtonLabels(window).ToArray());

        harness.Coordinator.ItemDetails.Close();
        var reopened = OpenItemDetailsOnAPlayerPotion(harness, out _);
        Assert.AreEqual(DetailsMode.Advanced, reopened.ShownMode);
    }

    [TestMethod]
    public void Comparing_ShowsSimple_AndHidesTheModeButton_ThenStoppingRestoresTheChoice()
    {
        var harness = Build();
        harness.Coordinator.ItemDetailsMode.Toggle();
        var window = OpenItemDetailsOnAPlayerPotion(harness, out var stackInstanceId);
        Assert.AreEqual(DetailsMode.Advanced, window.ShownMode);

        harness.Coordinator.ItemComparison.Arm(PlayerEntityId, stackInstanceId);
        window.Update(new GameTime());

        Assert.AreEqual(DetailsMode.Simple, window.ShownMode);
        CollectionAssert.DoesNotContain(ButtonLabels(window).ToList(), "Simple");
        Assert.AreEqual(DetailsMode.Advanced, harness.Coordinator.ItemDetailsMode.Chosen);
    }

    [TestMethod]
    public void ItemDetails_WhileComparing_HasOnlyStopComparison_AndItEndsTheComparisonAndCloses()
    {
        var harness = Build();
        var window = OpenItemDetailsOnAPlayerPotion(harness, out var stackInstanceId);
        harness.Coordinator.ItemComparison.Arm(PlayerEntityId, stackInstanceId);
        window.Update(new GameTime());

        var buttons = DescendantsOf(window).OfType<Button>().ToList();
        CollectionAssert.AreEqual(new[] { "Stop Comparison" }, buttons.Select(ButtonContent.TextOf).ToArray());

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
