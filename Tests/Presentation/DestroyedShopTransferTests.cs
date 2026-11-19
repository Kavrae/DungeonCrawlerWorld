using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Game.Modules.Containers.Components;
using Game.Modules.Currency;
using Game.Modules.Currency.Components;
using Game.Modules.Death.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Shops;
using Game.Modules.Shops.Components;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.Input.DragDrop;

namespace Tests.Presentation;

/// <summary>A destroyed shop's items and currency move both ways, as any container's do.</summary>
[TestClass]
[DoNotParallelize]
public sealed class DestroyedShopTransferTests
{
    private const int PlayerEntityId = TestMapWindows.PlayerEntityId;
    private const int ShopEntityId = 20;
    private static readonly Guid PotionItemId = Guid.NewGuid();

    private sealed record Harness(ComponentManager ComponentManager, ShopView ShopView, InventoryTransferDropHandler Handler);

    private static Harness Build(bool destroyed)
    {
        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(PotionItemId, "Test Potion", null, "p", Color.White, Tags: [Game.Tags.GameTags.ItemConsumablePotion], Effects: [], GoldValue: 10));
        var map = TestMapWindows.Create(300, 300, 1, new Vector3Int(100, 100, 0), itemCatalog: itemCatalog);
        var componentManager = map.ComponentManager;
        var shopView = new ShopView(componentManager);
        var handler = new InventoryTransferDropHandler(map.MapViewState, shopView, new InventoryView(componentManager, itemCatalog), new InventoryCommands(componentManager, itemCatalog, map.World), new ShopCommands(componentManager, itemCatalog, new EventBus(), map.World), new CurrencyCommands(componentManager), map.World);

        componentManager.Merge(ShopEntityId, new ContainerComponent());
        componentManager.Merge(ShopEntityId, new ShopComponent(acceptedItems: null, buyMultiplier: 1.2f, sellMultiplier: 0.8f));
        if (destroyed)
        {
            componentManager.Merge(ShopEntityId, new DeadComponent(KilledBy: Game.World.ActionSource.Admin, DiedAtFrame: 0));
        }

        componentManager.Merge(PlayerEntityId, new CurrencyComponent(gold: 50, credits: 5));
        componentManager.Merge(ShopEntityId, new CurrencyComponent(gold: 30, credits: 3));
        return new Harness(componentManager, shopView, handler);
    }

    private static bool Holds(Harness harness, int entityId, uint stackInstanceId) =>
        InventoryQueries.TryFindByStackInstanceId(harness.ComponentManager.GetMultiPool<InventoryItemStackComponent>(), entityId, stackInstanceId, out _);

    private static CurrencyComponent CurrencyOf(Harness harness, int entityId)
    {
        harness.ComponentManager.GetPackedPool<CurrencyComponent>().TryGetReadonly(entityId, out var currency);
        return currency;
    }

    private static void Drag(Harness harness, DragPayload payload, int destinationEntityId)
    {
        var target = new InventoryDropTarget(destinationEntityId);
        Assert.IsTrue(harness.Handler.AllowsDragStart(payload));
        Assert.IsFalse(harness.Handler.IsDropRefusedAt(payload, target, Point.Zero));
        Assert.IsTrue(harness.Handler.TryDrop(payload, target, Point.Zero));
    }

    [TestMethod]
    public void ADestroyedShop_TradesNoMore()
    {
        Assert.IsTrue(Build(destroyed: false).ShopView.IsTradingShop(ShopEntityId));
        Assert.IsFalse(Build(destroyed: true).ShopView.IsTradingShop(ShopEntityId));
    }

    [TestMethod]
    public void AnItem_IsTakenFromADestroyedShop()
    {
        var harness = Build(destroyed: true);
        var stackInstanceId = InventoryActions.AddItem(harness.ComponentManager, ShopEntityId, PotionItemId, quantity: 1);

        Drag(harness, new DragPayload(DragPayloadOrigin.InventoryCell, ItemStackInstanceId: stackInstanceId, OriginEntityId: ShopEntityId), PlayerEntityId);

        Assert.IsTrue(Holds(harness, PlayerEntityId, stackInstanceId));
        Assert.AreEqual(50, CurrencyOf(harness, PlayerEntityId).Gold, "Nothing was paid for it.");
    }

    [TestMethod]
    public void AnItem_IsPutIntoADestroyedShop()
    {
        var harness = Build(destroyed: true);
        var stackInstanceId = InventoryActions.AddItem(harness.ComponentManager, PlayerEntityId, PotionItemId, quantity: 1);

        Drag(harness, new DragPayload(DragPayloadOrigin.InventoryCell, ItemStackInstanceId: stackInstanceId, OriginEntityId: PlayerEntityId), ShopEntityId);

        Assert.IsTrue(Holds(harness, ShopEntityId, stackInstanceId));
        Assert.AreEqual(30, CurrencyOf(harness, ShopEntityId).Gold, "Nothing was paid for it.");
    }

    [TestMethod]
    public void Currency_IsTakenFromADestroyedShop()
    {
        var harness = Build(destroyed: true);

        Drag(harness, new DragPayload(DragPayloadOrigin.Currency, CurrencyType: CurrencyType.Gold, OriginEntityId: ShopEntityId), PlayerEntityId);

        Assert.AreEqual(80, CurrencyOf(harness, PlayerEntityId).Gold);
        Assert.AreEqual(0, CurrencyOf(harness, ShopEntityId).Gold);
    }

    [TestMethod]
    public void Currency_IsGivenToADestroyedShop()
    {
        var harness = Build(destroyed: true);

        Drag(harness, new DragPayload(DragPayloadOrigin.Currency, CurrencyType: CurrencyType.Credits, OriginEntityId: PlayerEntityId), ShopEntityId);

        Assert.AreEqual(0, CurrencyOf(harness, PlayerEntityId).Credits);
        Assert.AreEqual(8, CurrencyOf(harness, ShopEntityId).Credits);
    }

    [TestMethod]
    public void AStandingShopsCurrency_StillCantBeDraggedOutWhileItIsClosed()
    {
        var harness = Build(destroyed: false);

        Assert.IsFalse(harness.Handler.AllowsDragStart(new DragPayload(DragPayloadOrigin.Currency, CurrencyType: CurrencyType.Gold, OriginEntityId: ShopEntityId)));
    }
}
