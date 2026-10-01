using Engine.ECS.Systems;
using Game.Effects;
using Game.Effects.Entries;
using Engine.Events;
using Engine.Math;
using Game.Blueprints;
using Game.Floors;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Currency.Components;
using Game.Modules.Death;
using Game.Modules.Inventory;
using Game.Modules.Lootboxes;
using Game.Modules.Shops.Components;
using Game.Spawning;
using Game.Tags;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.Input;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Content;
using Presentation.UI.Inventory;
using Presentation.UI.Looting;
using Presentation.UI.Shops;
using Presentation.UI.Trade;

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

    private sealed record Harness(ItemWindowCoordinator Coordinator, MapWindow MapWindow, MapViewState MapViewState, Engine.ECS.Components.ComponentManager ComponentManager);

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
        var labelRenderer = new LabelRenderer();
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets");
        var spriteRenderer = new SpriteRenderer();
        var elementPool = TestElementPoolServiceFactory.Create(fontService, labelRenderer);
        var layers = new UiLayerStack();
        var pointerState = new PointerState();
        var tooltipController = new TooltipController();
        var shellServices = new ShellServices(
            layers,
            mapViewState,
            new MapCamera(world),
            TestElementPoolServiceFactory.CreateContextMenuController(elementPool, layers),
            tooltipController,
            pointerState,
            new CursorTextContent(pointerState, fontService, labelRenderer));

        var eventBus = new EventBus();
        var inventoryServices = TestInventoryServices.Over(componentManager, itemCatalog, world, eventBus);
        var definitions = new BlueprintRegistry();
        var entityNaming = EntityNaming.For(componentManager, definitions);
        var healthView = new HealthView(componentManager, Game.Modules.Health.EntityBodyParts.For(componentManager, definitions));
        var mapView = new MapViewQuery(world, componentManager, new ActionCatalog(), new Game.Terrain.TerrainRegistry(), definitions, new SimulationClock());
        var contextMenuController = shellServices.ContextMenuController;

        elementPool.RegisterFactory<Tooltip>(() => new Tooltip(fontService, elementPool, labelRenderer));
        elementPool.RegisterFactory<GridControl>(() => new GridControl(fontService, elementPool, labelRenderer));
        elementPool.RegisterFactory<Toggle>(() => new Toggle(fontService, elementPool, labelRenderer));
        elementPool.RegisterFactory<TextDivider>(() => new TextDivider(fontService, elementPool, labelRenderer));
        elementPool.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(fontService, elementPool, labelRenderer, spriteSheetService, spriteRenderer));
        elementPool.RegisterFactory<ShopItemStackCell>(() => new ShopItemStackCell(fontService, elementPool, labelRenderer, spriteSheetService, spriteRenderer));
        elementPool.RegisterFactory<TradeItemStackCell>(() => new TradeItemStackCell(fontService, elementPool, labelRenderer, spriteSheetService, spriteRenderer));
        elementPool.RegisterFactory<EmptyTradeSlotCell>(() => new EmptyTradeSlotCell(fontService, elementPool, labelRenderer));
        elementPool.RegisterFactory<CurrencyElement>(() => new CurrencyElement(fontService, elementPool, labelRenderer, spriteSheetService, spriteRenderer));
        elementPool.RegisterFactory<ItemIconElement>(() => new ItemIconElement(fontService, elementPool, labelRenderer, spriteSheetService, spriteRenderer));
        elementPool.RegisterFactory<TargetShapePreviewElement>(() => new TargetShapePreviewElement(fontService, elementPool, labelRenderer));
        elementPool.RegisterFactory<EntityIconElement>(() => new EntityIconElement(fontService, elementPool, labelRenderer, spriteSheetService, spriteRenderer, mapView));
        elementPool.RegisterFactory<ItemDetailsWindow>(() => new ItemDetailsWindow(fontService, elementPool, labelRenderer, new ActionCatalog(), TestGameplayTags.BuiltIn, inventoryServices.ActionStateView, world));
        elementPool.RegisterFactory<InventoryManagementWindow>(() => new InventoryManagementWindow(fontService, elementPool, labelRenderer, inventoryServices, world, contextMenuController, mapViewState, simulationClock: new SimulationClock()));
        elementPool.RegisterFactory<SecondaryInventoryWindow>(() => new SecondaryInventoryWindow(fontService, elementPool, labelRenderer, inventoryServices, world, contextMenuController, mapViewState, simulationClock: new SimulationClock(), entityNaming: entityNaming, healthView: healthView));
        elementPool.RegisterFactory<ShopWindow>(() => new ShopWindow(fontService, elementPool, labelRenderer, inventoryServices, world, contextMenuController, mapViewState, simulationClock: new SimulationClock(), entityNaming: entityNaming));
        elementPool.RegisterFactory<TradeWindow>(() => new TradeWindow(fontService, elementPool, labelRenderer, inventoryServices, world, contextMenuController, mapViewState, simulationClock: new SimulationClock()));

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

        return new Harness(coordinator, mapHarness.MapWindow, mapViewState, componentManager);
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
        Assert.HasCount(1, harness.Coordinator.ItemComparison.ColumnRectangles);
    }
}
