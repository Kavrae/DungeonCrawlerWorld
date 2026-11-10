using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Tiles;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Currency.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Shops.Components;
using Game.Tags;
using Microsoft.Xna.Framework;
using Presentation.UI;
using Presentation.UI.Content;
using Tests.UIEngine;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Tests.Presentation;

/// <summary>While a loot or shop window is open, a double-click on a stack moves the whole stack as the "Give"/"Take" or "Sell All"/"Buy All" option does, in place of activating it.</summary>
[TestClass]
[DoNotParallelize]
public sealed class InventoryGridContentSecondaryDoubleClickTests
{
    private const int PlayerEntityId = 1;
    private const int CorpseEntityId = 2;
    private static readonly Guid PotionItemId = Guid.NewGuid();

    private sealed record Fixture(Window HostWindow, Game.Views.InventoryView InventoryView, List<uint> ActivateRequests);

    private static Fixture Build(int gridEntityId, int? secondaryTargetEntityId, bool secondaryIsShop = false, int playerGold = 0)
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 20));
        componentManager.Merge(PlayerEntityId, new ActionLockComponent(currentLockTotalFrames: 0, unlockedAtFrame: 0));

        var fontService = TestFonts.Shared;
        var windowService = TestElementPoolServiceFactory.Create(fontService);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets", TestSprites.Resolver);
        windowService.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(TestElementServices.For(windowService, fontService), spriteSheetService));
        windowService.RegisterFactory<ShopItemStackCell>(() => new ShopItemStackCell(TestElementServices.For(windowService, fontService), spriteSheetService));
        windowService.RegisterFactory<Tooltip>(() => new Tooltip(TestElementServices.For(windowService, fontService)));

        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(10, 10, 1)), playerEntityId: PlayerEntityId);
        var contextMenuController = TestElementPoolServiceFactory.CreateContextMenuController(windowService, new UiLayerStack());

        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(
            PotionItemId, "Test Potion", null, "p", Color.White, Tags: [GameTags.ItemConsumablePotion], Effects: [], GoldValue: 10,
            Activator: new PotionActivator(new TargetingSpec(TargetShape.Self, MaximumRange: 0, AreaSize: 0, Anchor: TargetAnchor.Caster), new ActionTiming(ActionTimingCategory.Immediate, 60, null))));

        var tooltipController = new TooltipController();
        tooltipController.Initialize(windowService, new UiLayerStack());

        var mapViewState = new MapViewState();
        if (secondaryIsShop && secondaryTargetEntityId is { } shopEntityId)
        {
            componentManager.Merge(shopEntityId, new ShopComponent(acceptedItems: null, buyMultiplier: 1f, sellMultiplier: 1f));
            componentManager.Merge(shopEntityId, new CurrencyComponent(gold: 1000, credits: 0));
            mapViewState.OpenShopEntityId = shopEntityId;
        }

        componentManager.Merge(PlayerEntityId, new CurrencyComponent(gold: playerGold, credits: 0));
        var activateRequests = new List<uint>();
        var inventoryServices = TestInventoryServices.Over(componentManager, itemCatalog, world);
        var grid = new InventoryGridContent(world, inventoryServices, windowService, contextMenuController, gridEntityId, filterTag: Engine.Tags.GameplayTag.None, () => secondaryTargetEntityId, mapViewState, static (_, _) => { }, (_, stackInstanceId) => activateRequests.Add(stackInstanceId), static _ => { });

        var hostWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(400, 200), DisplayMode = ElementDisplayMode.Fixed },
        });
        hostWindow.ContentPadding = Vector2.Zero;
        hostWindow.Initialize();
        grid.Initialize(hostWindow);

        InventoryActions.AddItem(componentManager, gridEntityId, PotionItemId, quantity: 3);
        grid.Update(new GameTime());

        return new Fixture(hostWindow, inventoryServices.InventoryView, activateRequests);
    }

    private static InventoryItemStackCell Cell(Fixture fixture) => fixture.HostWindow.ChildElements.OfType<InventoryItemStackCell>().Single();

    private static int UnitsHeldBy(Fixture fixture, int entityId)
    {
        var stacks = new List<InventoryItemStackComponent>();
        fixture.InventoryView.CopyStacks(entityId, stacks);
        return stacks.Sum(stack => stack.Quantity);
    }


    [TestMethod]
    public void WhileLooting_DoubleClickingThePlayersStack_GivesTheWholeStack_AndActivatesNothing()
    {
        var fixture = Build(PlayerEntityId, secondaryTargetEntityId: CorpseEntityId);
        var cell = Cell(fixture);

        Assert.IsTrue(cell.HandleDoubleClick(cell.Rectangle.Center));

        Assert.AreEqual(0, UnitsHeldBy(fixture, PlayerEntityId));
        Assert.AreEqual(3, UnitsHeldBy(fixture, CorpseEntityId));
        Assert.IsEmpty(fixture.ActivateRequests);
    }

    [TestMethod]
    public void WhileLooting_DoubleClickingTheCorpsesStack_TakesTheWholeStack()
    {
        var fixture = Build(CorpseEntityId, secondaryTargetEntityId: CorpseEntityId);
        var cell = Cell(fixture);

        Assert.IsTrue(cell.HandleDoubleClick(cell.Rectangle.Center));

        Assert.AreEqual(3, UnitsHeldBy(fixture, PlayerEntityId));
        Assert.AreEqual(0, UnitsHeldBy(fixture, CorpseEntityId));
    }

    [TestMethod]
    public void WithNoLootWindowOpen_DoubleClickingThePlayersStack_StillActivatesIt()
    {
        var fixture = Build(PlayerEntityId, secondaryTargetEntityId: null);
        var cell = Cell(fixture);

        cell.HandleDoubleClick(cell.Rectangle.Center);

        Assert.HasCount(1, fixture.ActivateRequests);
        Assert.AreEqual(3, UnitsHeldBy(fixture, PlayerEntityId));
    }

    [TestMethod]
    public void WhileShopping_DoubleClickingThePlayersStack_SellsTheWholeStack_AndActivatesNothing()
    {
        var fixture = Build(PlayerEntityId, secondaryTargetEntityId: CorpseEntityId, secondaryIsShop: true);
        var cell = Cell(fixture);

        Assert.IsTrue(cell.HandleDoubleClick(cell.Rectangle.Center));

        Assert.AreEqual(0, UnitsHeldBy(fixture, PlayerEntityId));
        Assert.AreEqual(3, UnitsHeldBy(fixture, CorpseEntityId));
        Assert.IsEmpty(fixture.ActivateRequests);
    }

    [TestMethod]
    public void WhileShopping_DoubleClickingTheShopsStack_BuysIt_OnlyWhenThePlayerCanPay()
    {
        var broke = Build(CorpseEntityId, secondaryTargetEntityId: CorpseEntityId, secondaryIsShop: true);
        var brokeCell = Cell(broke);
        brokeCell.HandleDoubleClick(brokeCell.Rectangle.Center);
        Assert.AreEqual(0, UnitsHeldBy(broke, PlayerEntityId), "Refused as Buy All is: the player hasn't the Gold.");

        var fixture = Build(CorpseEntityId, secondaryTargetEntityId: CorpseEntityId, secondaryIsShop: true, playerGold: 1000);
        var cell = Cell(fixture);
        cell.HandleDoubleClick(cell.Rectangle.Center);
        Assert.AreEqual(3, UnitsHeldBy(fixture, PlayerEntityId));
    }
}
