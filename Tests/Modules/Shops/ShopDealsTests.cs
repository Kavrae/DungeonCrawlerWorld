using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Currency.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Shops;
using Game.Modules.Shops.Components;
using Game.Tags;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Shops;

/// <summary>A trade and a direct buy or sale are the same shop deal: valued, refused and completed by one rule.</summary>
[TestClass]
public sealed class ShopDealsTests
{
    private const int PlayerEntityId = 0;
    private const int ShopEntityId = 1;
    private const int PlayerOfferEntityId = 2;
    private const int ShopOfferEntityId = 3;

    private static readonly Guid PotionItemId = Guid.NewGuid();
    private static readonly Guid CappedItemId = Guid.NewGuid();

    private static readonly ShopComponent GeneralShop = new(acceptedItems: null, buyMultiplier: 1f, sellMultiplier: 1f);

    private sealed record Fixture(ComponentManager Manager, ShopCommands Commands, List<ShopDealCompletedEvent> Completed);

    private static Fixture Build()
    {
        var manager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8));
        var catalog = new ItemCatalog();
        catalog.Register(new ItemDefinition(PotionItemId, "Test Potion", null, "p", Color.White, Tags: [GameTags.ItemConsumablePotion], Effects: [], GoldValue: 10));
        catalog.Register(new ItemDefinition(CappedItemId, "Test Capped Potion", null, "c", Color.White, Tags: [GameTags.ItemConsumablePotion], Effects: [], GoldValue: 10, MaximumShopStock: 5));
        manager.Merge(ShopEntityId, GeneralShop);
        manager.Merge(ShopEntityId, new CurrencyComponent(gold: 1000, credits: 0));

        var eventBus = new EventBus();
        var completed = new List<ShopDealCompletedEvent>();
        eventBus.Subscribe<ShopDealCompletedEvent>(completed.Add);
        return new Fixture(manager, new ShopCommands(manager, catalog, eventBus, TestPlayerQuery.NoPlayer), completed);
    }

    private static int GoldOf(ComponentManager manager, int entityId) =>
        manager.GetPackedPool<CurrencyComponent>().TryGetReadonly(entityId, out var currency) ? currency.Gold : 0;

    private static ShopDeal TradeOf(Fixture fixture, uint[] playerStacks, uint[] shopStacks) =>
        new(PlayerEntityId, ShopEntityId,
            new ShopDealSide(PlayerOfferEntityId, playerStacks, GoldOf(fixture.Manager, PlayerOfferEntityId)),
            new ShopDealSide(ShopOfferEntityId, shopStacks, GoldOf(fixture.Manager, ShopOfferEntityId)));

    [TestMethod]
    public void ACompletedTrade_IsAnnouncedOnceWithBothSidesValues()
    {
        var fixture = Build();
        var bought = InventoryActions.AddItem(fixture.Manager, ShopOfferEntityId, PotionItemId, quantity: 1);
        fixture.Manager.Merge(PlayerOfferEntityId, new CurrencyComponent(gold: 100, credits: 0));
        var deal = TradeOf(fixture, [], [bought]);
        var price = fixture.Commands.EvaluateDeal(deal).ShopOfferValue;

        Assert.AreEqual(ShopDealRefusal.None, fixture.Commands.TryCompleteDeal(deal));

        CollectionAssert.AreEqual(new[] { new ShopDealCompletedEvent(PlayerEntityId, ShopEntityId, PlayerOfferValue: 100, ShopOfferValue: price, PlayerOfferGold: 100, ShopOfferIsEmpty: false) }, fixture.Completed);
        Assert.AreEqual(1000 + 100, GoldOf(fixture.Manager, ShopEntityId));
    }

    [TestMethod]
    public void ATradeGivingGoldForNothing_IsAnnouncedWithAnEmptyShopSide()
    {
        var fixture = Build();
        fixture.Manager.Merge(PlayerOfferEntityId, new CurrencyComponent(gold: 40, credits: 0));

        Assert.AreEqual(ShopDealRefusal.None, fixture.Commands.TryCompleteDeal(TradeOf(fixture, [], [])));

        CollectionAssert.AreEqual(new[] { new ShopDealCompletedEvent(PlayerEntityId, ShopEntityId, PlayerOfferValue: 40, ShopOfferValue: 0, PlayerOfferGold: 40, ShopOfferIsEmpty: true) }, fixture.Completed);
    }

    [TestMethod]
    public void ARefusedTrade_IsNotAnnounced()
    {
        var fixture = Build();
        var bought = InventoryActions.AddItem(fixture.Manager, ShopOfferEntityId, PotionItemId, quantity: 1);

        Assert.AreEqual(ShopDealRefusal.OfferTooLow, fixture.Commands.TryCompleteDeal(TradeOf(fixture, [], [bought])));

        Assert.IsEmpty(fixture.Completed);
    }

    [TestMethod]
    public void ADirectPurchase_PaysThePrice_AndIsAnnouncedAsAnEvenDeal()
    {
        var fixture = Build();
        var bought = InventoryActions.AddItem(fixture.Manager, ShopEntityId, PotionItemId, quantity: 1);
        fixture.Manager.Merge(PlayerEntityId, new CurrencyComponent(gold: 100, credits: 0));

        Assert.IsTrue(fixture.Commands.TryBuyFromShop(PlayerEntityId, ShopEntityId, bought));

        Assert.HasCount(1, fixture.Completed);
        Assert.AreEqual(fixture.Completed[0].PlayerOfferValue, fixture.Completed[0].ShopOfferValue);
        Assert.IsFalse(fixture.Completed[0].ShopOfferIsEmpty);
        Assert.IsLessThan(100, GoldOf(fixture.Manager, PlayerEntityId));
    }

    [TestMethod]
    public void ATradeSellingIntoAFullShop_IsRefusedAsADirectSaleIs_AndNothingMoves()
    {
        var fixture = Build();
        InventoryActions.AddItem(fixture.Manager, ShopEntityId, CappedItemId, quantity: 5);
        var offered = InventoryActions.AddItem(fixture.Manager, PlayerOfferEntityId, CappedItemId, quantity: 1);
        var sold = InventoryActions.AddItem(fixture.Manager, PlayerEntityId, CappedItemId, quantity: 1);

        Assert.AreEqual(ShopDealRefusal.ShopStockFull, fixture.Commands.TryCompleteDeal(TradeOf(fixture, [offered], [])));
        Assert.AreEqual(ShopDealRefusal.ShopStockFull, fixture.Commands.GetSaleRefusal(PlayerEntityId, ShopEntityId, sold));

        Assert.AreEqual(1, fixture.Manager.GetMultiPool<InventoryItemStackComponent>().CountForEntity(PlayerOfferEntityId));
        Assert.AreEqual(1000, GoldOf(fixture.Manager, ShopEntityId));
    }

    [TestMethod]
    public void ARefusedTrade_StillValuesBothOffers()
    {
        var fixture = Build();
        var bought = InventoryActions.AddItem(fixture.Manager, ShopOfferEntityId, PotionItemId, quantity: 2);

        var valuation = fixture.Commands.EvaluateDeal(TradeOf(fixture, [], [bought]));

        Assert.AreEqual(ShopDealRefusal.OfferTooLow, valuation.Refusal);
        Assert.IsGreaterThan(0, valuation.ShopOfferValue);
    }
}
