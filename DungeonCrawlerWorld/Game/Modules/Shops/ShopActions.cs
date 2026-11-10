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
        return ShopDeals.TryComplete(componentManager, itemCatalog, in deal, playerQuery, out _) == ShopDealRefusal.None;
    }

    /// <summary>The player sells one stack to the shop for its price: a one-stack shop deal (ShopDeals.Sale), refused as any deal is -- the shop's sell cap among them.</summary>
    public static bool TrySellToShop(ComponentManager componentManager, ItemCatalog itemCatalog, int playerEntityId, int shopEntityId, uint stackInstanceId, IPlayerQuery playerQuery)
    {
        var deal = ShopDeals.Sale(componentManager, itemCatalog, playerEntityId, shopEntityId, stackInstanceId, playerQuery);
        return ShopDeals.TryComplete(componentManager, itemCatalog, in deal, playerQuery, out _) == ShopDealRefusal.None;
    }

    /// <summary>
    /// Moves sourceEntityId's whole balance of currencyType to destinationEntityId (the same
    /// whole-balance CurrencyActions.TryTransfer overload every "give" gesture already uses),
    /// publishing GoldGivenToShopEvent afterward if Gold actually moved, there was some to give,
    /// and destinationEntityId genuinely carries a ShopComponent. This is the one chokepoint every
    /// currency-to-a-shop gesture -- context-menu Give/Give All, a direct currency drag dropped on
    /// the shop, and Complete swapping a trade's player-side Gold to the real shop -- should route
    /// through, so the "Angel Investor" achievement's own trigger never depends on which specific
    /// UI gesture the player used to give it (confirmed live gap: only the context menu published
    /// this before).
    ///
    /// eventPlayerEntityId defaults to sourceEntityId -- true for the context-menu and direct-drag
    /// callers, where the real player's own Gold is what's moving. TradeWindow.CompleteTrade is the
    /// one caller that must override it: its own sourceEntityId is the trade-offer player column
    /// (a reserved placeholder entity, never the real player -- see TradeWindow's own doc comment on
    /// _playerSideEntityId), so GoldGivenToShopEvent.PlayerEntityId would otherwise report that
    /// placeholder instead of the real player.
    /// </summary>
    public static bool TryGiveCurrencyToShop(ComponentManager componentManager, EventBus eventBus, int sourceEntityId, int destinationEntityId, CurrencyType currencyType, int? eventPlayerEntityId = null)
    {
        componentManager.GetPackedPool<CurrencyComponent>().TryGetReadonly(sourceEntityId, out var sourceCurrencyBefore);
        var amountBefore = currencyType == CurrencyType.Gold ? sourceCurrencyBefore.Gold : sourceCurrencyBefore.Credits;

        if (!CurrencyActions.TryTransfer(componentManager, sourceEntityId, destinationEntityId, currencyType))
        {
            return false;
        }

        if (currencyType == CurrencyType.Gold && amountBefore > 0 && componentManager.GetPackedPool<ShopComponent>().Has(destinationEntityId))
        {
            eventBus.Publish(new GoldGivenToShopEvent(eventPlayerEntityId ?? sourceEntityId, destinationEntityId, amountBefore));
        }

        return true;
    }
}
