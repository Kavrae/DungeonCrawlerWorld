using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Currency;
using Game.Modules.Inventory;
using Game.World;

namespace Game.Modules.Shops;

/// <summary>A session's shop transactions, for code outside a system: ShopActions with the session's pools, catalog, bus and player bound.</summary>
public sealed class ShopCommands(ComponentManager componentManager, ItemCatalog itemCatalog, EventBus eventBus, IPlayerQuery playerQuery)
{
    /// <summary>The player buys one stack from the shop for its price: a one-stack deal (ShopDeals.Purchase) completed through TryCompleteDeal.</summary>
    public bool TryBuyFromShop(int playerEntityId, int shopEntityId, uint stackInstanceId) =>
        TryCompleteDeal(ShopDeals.Purchase(componentManager, itemCatalog, playerEntityId, shopEntityId, stackInstanceId, playerQuery)) == ShopDealRefusal.None;

    /// <summary>The player sells one stack to the shop for its price: a one-stack deal (ShopDeals.Sale) completed through TryCompleteDeal.</summary>
    public bool TrySellToShop(int playerEntityId, int shopEntityId, uint stackInstanceId) =>
        TryCompleteDeal(ShopDeals.Sale(componentManager, itemCatalog, playerEntityId, shopEntityId, stackInstanceId, playerQuery)) == ShopDealRefusal.None;

    /// <summary>Why the player can't buy stackInstanceId from the shop for its price now (ShopDeals.Purchase), or None.</summary>
    public ShopDealRefusal GetPurchaseRefusal(int playerEntityId, int shopEntityId, uint stackInstanceId) =>
        EvaluateDeal(ShopDeals.Purchase(componentManager, itemCatalog, playerEntityId, shopEntityId, stackInstanceId, playerQuery)).Refusal;

    /// <summary>Why the player can't sell stackInstanceId to the shop for its price now (ShopDeals.Sale), or None.</summary>
    public ShopDealRefusal GetSaleRefusal(int playerEntityId, int shopEntityId, uint stackInstanceId) =>
        EvaluateDeal(ShopDeals.Sale(componentManager, itemCatalog, playerEntityId, shopEntityId, stackInstanceId, playerQuery)).Refusal;

    /// <inheritdoc cref="ShopDeals.Evaluate"/>
    public ShopDealValuation EvaluateDeal(in ShopDeal deal) => ShopDeals.Evaluate(componentManager, itemCatalog, in deal, playerQuery);

    /// <summary>Completes deal (ShopDeals.TryComplete), announcing what each side was worth (ShopDealCompletedEvent).</summary>
    /// <returns>None when it completed; otherwise why not, with nothing changed.</returns>
    public ShopDealRefusal TryCompleteDeal(in ShopDeal deal)
    {
        var valuation = ShopDeals.TryComplete(componentManager, itemCatalog, in deal, playerQuery);
        if (valuation.Refusal != ShopDealRefusal.None)
        {
            return valuation.Refusal;
        }

        var shopOfferIsEmpty = deal.ShopOffer.StackInstanceIds.Count == 0 && deal.ShopOffer.Gold == 0;
        eventBus.Publish(new ShopDealCompletedEvent(deal.PlayerEntityId, deal.ShopEntityId, valuation.PlayerOfferValue, valuation.ShopOfferValue, deal.PlayerOffer.Gold, shopOfferIsEmpty));
        return ShopDealRefusal.None;
    }

    /// <inheritdoc cref="ShopActions.TryGiveCurrencyToShop"/>
    public bool TryGiveCurrencyToShop(int sourceEntityId, int destinationEntityId, CurrencyType currencyType) =>
        ShopActions.TryGiveCurrencyToShop(componentManager, eventBus, sourceEntityId, destinationEntityId, currencyType);
}
