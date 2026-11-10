using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Currency;
using Game.Modules.Currency.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Shops.Components;
using Game.World;

namespace Game.Modules.Shops;

/// <summary>Shop trading for systems and tests: what a shop accepts, its reference prices, a one-stack buy or sale (a shop deal, ShopDeals), and giving currency to a shop.</summary>
public static class ShopActions
{
    /// <summary>AcceptedItems null means the shop trades any item (General Shop); otherwise the item's tags must match it (Potion Shop). An item that can't be traded at all (ItemDefinition.CanTrade) is never tradeable with any shop.</summary>
    public static bool CanTrade(ShopComponent shop, ItemDefinition item) =>
        item.CanTrade && (shop.AcceptedItems?.Matches(item.Tags) ?? true);

    /// <summary>The flat, stock-*un*aware base price: GoldValue marked up by the shop's BuyMultiplier, no ShopStockPricing curve applied. What a trade actually charges is ShopStockPricing.ComputeBulkBuyPrice below -- this stays as the simple "Normal band" reference value.</summary>
    public static int ComputeBuyPrice(ShopComponent shop, ItemDefinition item) => (int)MathF.Round(item.GoldValue * shop.BuyMultiplier);

    /// <summary>The flat, stock-*un*aware base price: GoldValue marked down by the shop's SellMultiplier, no ShopStockPricing curve applied. What a trade actually pays out is ShopStockPricing.ComputeBulkSellPrice below -- this stays as the simple "Normal band" reference value.</summary>
    public static int ComputeSellPrice(ShopComponent shop, ItemDefinition item) => (int)MathF.Round(item.GoldValue * shop.SellMultiplier);

    /// <summary>The player buys one stack from the shop for its price: a one-stack shop deal (ShopDeals.Purchase), refused as any deal is.</summary>
    /// <remarks>A bought stack merges into an equivalent stack the player already holds, so buying more of something tops up that stack.</remarks>
    public static bool TryBuyFromShop(ComponentManager componentManager, ItemCatalog itemCatalog, int playerEntityId, int shopEntityId, uint stackInstanceId, IPlayerQuery playerQuery)
    {
        var deal = ShopDeals.Purchase(componentManager, itemCatalog, playerEntityId, shopEntityId, stackInstanceId, playerQuery);
        return ShopDeals.TryComplete(componentManager, itemCatalog, in deal, playerQuery).Refusal == ShopDealRefusal.None;
    }

    /// <summary>The player sells one stack to the shop for its price: a one-stack shop deal (ShopDeals.Sale), refused as any deal is -- the shop's sell cap among them.</summary>
    public static bool TrySellToShop(ComponentManager componentManager, ItemCatalog itemCatalog, int playerEntityId, int shopEntityId, uint stackInstanceId, IPlayerQuery playerQuery)
    {
        var deal = ShopDeals.Sale(componentManager, itemCatalog, playerEntityId, shopEntityId, stackInstanceId, playerQuery);
        return ShopDeals.TryComplete(componentManager, itemCatalog, in deal, playerQuery).Refusal == ShopDealRefusal.None;
    }

    /// <summary>Moves sourceEntityId's whole balance of currencyType to destinationEntityId, announcing Gold that reached a shop as a completed trade in which the giver got nothing back (ShopDealCompletedEvent).</summary>
    /// <remarks>Every gesture that gives currency outright -- the currency row's Give and Give All, a currency drag -- goes through here, so each is announced the same way; to anything but a shop it is a plain transfer.</remarks>
    public static bool TryGiveCurrencyToShop(ComponentManager componentManager, EventBus eventBus, int sourceEntityId, int destinationEntityId, CurrencyType currencyType)
    {
        componentManager.GetPackedPool<CurrencyComponent>().TryGetReadonly(sourceEntityId, out var sourceCurrencyBefore);
        var amountBefore = currencyType == CurrencyType.Gold ? sourceCurrencyBefore.Gold : sourceCurrencyBefore.Credits;

        if (!CurrencyActions.TryTransfer(componentManager, sourceEntityId, destinationEntityId, currencyType))
        {
            return false;
        }

        if (currencyType == CurrencyType.Gold && amountBefore > 0 && componentManager.GetPackedPool<ShopComponent>().Has(destinationEntityId))
        {
            eventBus.Publish(new ShopDealCompletedEvent(sourceEntityId, destinationEntityId, PlayerOfferValue: amountBefore, ShopOfferValue: 0, PlayerOfferGold: amountBefore, ShopOfferIsEmpty: true));
        }

        return true;
    }
}
