using Engine.ECS.Components;
using Game.Modules.Inventory;
using Microsoft.Xna.Framework;
using Presentation.UI;
using Presentation.UI.Content;
using Presentation.UI.Inventory;
using Tests.UIEngine;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Tests.Presentation;

/// <summary>An inventory grid draws Icon or Card cells by its window kind's view, rebuilding when it changes, and while a shop is open follows the shop's view, with prices only then.</summary>
[TestClass]
[DoNotParallelize]
public sealed class InventoryGridViewTests
{
    private const int PlayerEntityId = 1;
    private const int ShopEntityId = 2;
    private static readonly Guid AppleId = Guid.NewGuid();

    private sealed record Fixture(InventoryGridContent Grid, Window HostWindow, MapViewState MapViewState, GridViewSettings Views);

    private static Fixture Build()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 20));
        var fontService = TestFonts.Shared;
        var pool = TestElementPoolServiceFactory.Create(fontService);
        var sprites = new SpriteSheetService(null, "Spritesheets", TestSprites.Resolver);
        pool.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(TestElementServices.For(pool, fontService), sprites));
        pool.RegisterFactory<ShopItemStackCell>(() => new ShopItemStackCell(TestElementServices.For(pool, fontService), sprites));
        pool.RegisterFactory<Tooltip>(() => new Tooltip(TestElementServices.For(pool, fontService)));

        var world = TestWorlds.Create(new Game.World.Map(new Engine.Math.Vector3Int(10, 10, 1)), playerEntityId: PlayerEntityId);
        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(AppleId, "Apple", null, "a", Color.White, Tags: [], Effects: []));
        var inventoryServices = TestInventoryServices.Over(componentManager, itemCatalog, world);
        var mapViewState = new MapViewState();

        var grid = new InventoryGridContent(world, inventoryServices, pool, TestElementPoolServiceFactory.CreateContextMenuController(pool, new UiLayerStack()), PlayerEntityId, Engine.Tags.GameplayTag.None, static () => null, mapViewState, static (_, _) => { }, static (_, _) => { }, static _ => { }, viewSetting: inventoryServices.GridViews.Inventory);
        var hostWindow = pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(400, 200), DisplayMode = ElementDisplayMode.Fixed },
        });
        hostWindow.ContentPadding = Vector2.Zero;
        hostWindow.Initialize();
        grid.Initialize(hostWindow);
        InventoryActions.AddItem(componentManager, PlayerEntityId, AppleId, quantity: 3);
        grid.Update(new GameTime());
        return new Fixture(grid, hostWindow, mapViewState, inventoryServices.GridViews);
    }

    private static InventoryItemStackCell AppleCell(Fixture fixture) => fixture.HostWindow.ChildElements.OfType<InventoryItemStackCell>().Single();

    [TestMethod]
    public void TheInventoryView_IsIconUntilCardIsChosen_ThenACardWithNoPrice_AndBackAgain()
    {
        var fixture = Build();
        Assert.IsNotInstanceOfType<ShopItemStackCell>(AppleCell(fixture));
        Assert.AreEqual(InventoryGridContent.CellSize, AppleCell(fixture).CurrentSize);

        fixture.Views.Inventory.Choose(GridView.Card);
        fixture.Grid.Update(new GameTime());

        var card = (ShopItemStackCell)AppleCell(fixture);
        Assert.IsFalse(card.ShowsPrice, "Outside a shop a card shows the name and quantity, never a price.");
        Assert.IsGreaterThan(card.CurrentSize.Y, card.CurrentSize.X);

        fixture.Views.Inventory.Choose(GridView.Icon);
        fixture.Grid.Update(new GameTime());
        Assert.IsNotInstanceOfType<ShopItemStackCell>(AppleCell(fixture));
    }

    [TestMethod]
    public void WhileAShopIsOpen_TheGridFollowsTheShopsView_WithPrices_ThenGoesBackToItsOwn()
    {
        var fixture = Build();
        fixture.Views.Inventory.Choose(GridView.Card);

        fixture.MapViewState.OpenShopEntityId = ShopEntityId;
        fixture.Views.Shop.Choose(GridView.Icon);
        fixture.Grid.Update(new GameTime());
        Assert.IsNotInstanceOfType<ShopItemStackCell>(AppleCell(fixture), "The shop's Icon view, not the inventory's Card.");

        fixture.Views.Shop.Choose(GridView.Card);
        fixture.Grid.Update(new GameTime());
        Assert.IsTrue(((ShopItemStackCell)AppleCell(fixture)).ShowsPrice);

        fixture.Views.Shop.Choose(GridView.Icon);
        fixture.MapViewState.OpenShopEntityId = null;
        fixture.Grid.Update(new GameTime());
        Assert.IsFalse(((ShopItemStackCell)AppleCell(fixture)).ShowsPrice, "Its own Card view again, without prices.");
    }
}
