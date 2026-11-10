using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Currency;
using Game.Modules.Inventory;
using Game.World;

namespace Game.Modules.Shops;

/// <summary>A session's shop transactions, for code outside a system: ShopActions with the session's pools, catalog, bus and player bound.</summary>
public sealed class ShopCommands(ComponentManager componentManager, ItemCatalog itemCatalog, EventBus eventBus, IPlayerQuery playerQuery)
{
    /// <inheritdoc cref="ShopActions.TryBuyFromShop"/>
    public bool TryBuyFromShop(int playerEntityId, int shopEntityId, uint stackInstanceId) =>
        ShopActions.TryBuyFromShop(componentManager, itemCatalog, playerEntityId, shopEntityId, stackInstanceId, playerQuery);

    /// <inheritdoc cref="ShopActions.TrySellToShop"/>
    public bool TrySellToShop(int playerEntityId, int shopEntityId, uint stackInstanceId) =>
        ShopActions.TrySellToShop(componentManager, itemCatalog, playerEntityId, shopEntityId, stackInstanceId, playerQuery);

    /// <summary>Why the player can't buy stackInstanceId from the shop for its price now (ShopDeals.Purchase), or None.</summary>
    public ShopDealRefusal GetPurchaseRefusal(int playerEntityId, int shopEntityId, uint stackInstanceId) =>
        EvaluateDeal(ShopDeals.Purchase(componentManager, itemCatalog, playerEntityId, shopEntityId, stackInstanceId, playerQuery)).Refusal;

    /// <summary>Why the player can't sell stackInstanceId to the shop for its price now (ShopDeals.Sale), or None.</summary>
    public ShopDealRefusal GetSaleRefusal(int playerEntityId, int shopEntityId, uint stackInstanceId) =>
        EvaluateDeal(ShopDeals.Sale(componentManager, itemCatalog, playerEntityId, shopEntityId, stackInstanceId, playerQuery)).Refusal;

    /// <inheritdoc cref="ShopDeals.Evaluate"/>
    public ShopDealValuation EvaluateDeal(in ShopDeal deal) => ShopDeals.Evaluate(componentManager, itemCatalog, in deal, playerQuery);

    /// <summary>Completes deal (ShopDeals.TryComplete), announcing any Gold the player gave beyond its worth (GoldGivenToShopEvent).</summary>
    /// <returns>None when it completed; otherwise why not, with nothing changed.</returns>
    public ShopDealRefusal TryCompleteDeal(in ShopDeal deal)
    {
        var refusal = ShopDeals.TryComplete(componentManager, itemCatalog, in deal, playerQuery, out var giftedGold);
        if (giftedGold > 0)
        {
            eventBus.Publish(new GoldGivenToShopEvent(deal.PlayerEntityId, deal.ShopEntityId, giftedGold));
        }

        return refusal;
    }

    /// <inheritdoc cref="ShopActions.TryGiveCurrencyToShop"/>
    public bool TryGiveCurrencyToShop(int sourceEntityId, int destinationEntityId, CurrencyType currencyType, int? eventPlayerEntityId = null) =>
        ShopActions.TryGiveCurrencyToShop(componentManager, eventBus, sourceEntityId, destinationEntityId, currencyType, eventPlayerEntityId);
}
