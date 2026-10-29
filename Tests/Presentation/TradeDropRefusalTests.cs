using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Game.Floors;
using Game.Modules.Currency;
using Game.Modules.Inventory;
using Game.Modules.Shops;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.Input.DragDrop;
using Presentation.UI;

namespace Tests.Presentation;

[TestClass]
public sealed class TradeDropRefusalTests
{
    private const int PlayerEntityId = 1;
    private const int ShopEntityId = 2;
    private const int TradePlayerEntityId = 3;
    private const int TradeShopEntityId = 4;
    private const int OtherEntityId = 5;

    private static InventoryTransferDropHandler BuildHandler()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 20));
        componentManager.Merge(ShopEntityId, new Game.Modules.Shops.Components.ShopComponent(acceptedItems: Engine.Tags.GameplayTagQuery.Any([Game.Tags.GameTags.ItemConsumablePotion]), buyMultiplier: 1.10f, sellMultiplier: 0.90f));

        var itemCatalog = new ItemCatalog();
        var playerQuery = TestWorlds.Create(new Game.World.Map(new Vector3Int(10, 10, 1)), playerEntityId: PlayerEntityId);
        var mapViewState = new MapViewState { OpenShopEntityId = ShopEntityId, ReservedEntityIds = new ReservedEntityIds(TradePlayerEntityId, TradeShopEntityId) };

        return new InventoryTransferDropHandler(mapViewState, new ShopView(componentManager), new InventoryView(componentManager, itemCatalog), new InventoryCommands(componentManager, itemCatalog, playerQuery), new ShopCommands(componentManager, itemCatalog, new EventBus(), playerQuery), new CurrencyCommands(componentManager), playerQuery);
    }

    private static bool IsGoldFromRefusedOn(int originEntityId, int destinationEntityId) =>
        BuildHandler().IsDropRefusedAt(new DragPayload(DragPayloadOrigin.Currency, CurrencyType: CurrencyType.Gold, OriginEntityId: originEntityId), new InventoryDropTarget(destinationEntityId), Point.Zero);

    [TestMethod]
    [DataRow(PlayerEntityId)]
    [DataRow(TradePlayerEntityId)]
    [DataRow(OtherEntityId)]
    public void TheShopsCurrency_IsRefusedAnywhereButItsTradeColumn(int destinationEntityId) =>
        Assert.IsTrue(IsGoldFromRefusedOn(ShopEntityId, destinationEntityId));

    [TestMethod]
    public void TheShopsCurrency_OnItsTradeColumn_IsNotRefused() =>
        Assert.IsFalse(IsGoldFromRefusedOn(ShopEntityId, TradeShopEntityId));

    [TestMethod]
    public void TheShopsCurrency_BackOnTheShop_IsNotRefused() =>
        Assert.IsFalse(IsGoldFromRefusedOn(ShopEntityId, ShopEntityId));

    [TestMethod]
    public void ThePlayersCurrency_OnTheShopsTradeColumn_IsRefused() =>
        Assert.IsTrue(IsGoldFromRefusedOn(PlayerEntityId, TradeShopEntityId));

    [TestMethod]
    public void ThePlayersCurrency_OnTheirTradeColumn_IsNotRefused() =>
        Assert.IsFalse(IsGoldFromRefusedOn(PlayerEntityId, TradePlayerEntityId));
}
