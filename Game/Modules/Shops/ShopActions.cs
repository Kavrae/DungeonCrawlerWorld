using Engine.ECS.Components;
using Game.Modules.Currency;
using Game.Modules.Currency.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Shops.Components;
using Game.World;

namespace Game.Modules.Shops;

/// <summary>
/// The buy/sell chokepoint every shop trade goes through: tag eligibility, pricing, and the
/// combined item-for-Gold swap (check every precondition first, then commit both halves --
/// never leave a trade half-applied). TryBuyFromShop/TrySellToShop each move one exact stack (the
/// same "whole stack, not a partial quantity" semantics InventoryActions.TryTransferStack already
/// has) for ShopStockPricing.ComputeBulkBuyPrice/ComputeBulkSellPrice's stock-aware total, not a
/// flat per-unit price times quantity -- see PLAN-stock-based-shop-pricing.md.
/// </summary>
public static class ShopActions
{
    /// <summary>AllowedTags null means the shop trades any item (General Shop); otherwise the item must carry at least one matching tag (Potion Shop).</summary>
    public static bool CanTrade(ShopComponent shop, ItemDefinition item) =>
        shop.AllowedTags is null || item.Tags.Any(shop.AllowedTags.Contains);

    /// <summary>The flat, stock-*un*aware base price: GoldValue marked up by the shop's BuyMultiplier, no ShopStockPricing curve applied. What a trade actually charges is ShopStockPricing.ComputeBulkBuyPrice below -- this stays as the simple "Normal band" reference value.</summary>
    public static int ComputeBuyPrice(ShopComponent shop, ItemDefinition item) => (int)MathF.Round(item.GoldValue * shop.BuyMultiplier);

    /// <summary>The flat, stock-*un*aware base price: GoldValue marked down by the shop's SellMultiplier, no ShopStockPricing curve applied. What a trade actually pays out is ShopStockPricing.ComputeBulkSellPrice below -- this stays as the simple "Normal band" reference value.</summary>
    public static int ComputeSellPrice(ShopComponent shop, ItemDefinition item) => (int)MathF.Round(item.GoldValue * shop.SellMultiplier);

    /// <summary>
    /// Player buys one exact stack out of the shop's inventory for ShopStockPricing.
    /// ComputeBulkBuyPrice's total Gold (per-unit "bracket" pricing off the shop's own current
    /// stock of the item, not a flat price times quantity -- see PLAN-stock-based-shop-pricing.md).
    /// Fails with no state changed if the shop entity has no ShopComponent, the stack isn't found
    /// on the shop, the item's tags don't match the shop's AllowedTags, the player has no room for
    /// a new stack, or the player can't afford it. The currency transfer commits before the item
    /// transfer; if the item transfer still fails afterward (defense in depth -- shouldn't happen
    /// given the capacity check above), the currency is rolled back rather than leaving Gold moved
    /// with no item to show for it.
    /// </summary>
    public static bool TryBuyFromShop(ComponentManager componentManager, ItemCatalog itemCatalog, int playerEntityId, int shopEntityId, Guid stackInstanceId, IPlayerQuery? playerQuery)
    {
        if (!componentManager.GetPackedPool<ShopComponent>().TryGetReadonly(shopEntityId, out var shop))
        {
            return false;
        }

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        if (!InventoryQueries.TryFindByStackInstanceId(stacks, shopEntityId, stackInstanceId, out var stack) ||
            !InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var item) ||
            !CanTrade(shop, item) ||
            !InventoryCapacity.HasRoomForNewStack(componentManager, playerEntityId, playerQuery))
        {
            return false;
        }

        var totalPrice = ShopStockPricing.ComputeBulkBuyPrice(componentManager, shopEntityId, shop, item, stack.Quantity);

        componentManager.GetPackedPool<CurrencyComponent>().TryGetReadonly(playerEntityId, out var playerCurrency);
        if (playerCurrency.Gold < totalPrice)
        {
            return false;
        }

        if (!CurrencyActions.TryTransfer(componentManager, playerEntityId, shopEntityId, CurrencyType.Gold, totalPrice))
        {
            return false;
        }

        if (!InventoryActions.TryTransferStack(componentManager, shopEntityId, playerEntityId, stackInstanceId, playerQuery))
        {
            CurrencyActions.TryTransfer(componentManager, shopEntityId, playerEntityId, CurrencyType.Gold, totalPrice);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Player sells one exact stack out of their own inventory to the shop for ShopStockPricing.
    /// ComputeBulkSellPrice's total Gold. Fails with no state changed if the shop entity has no
    /// ShopComponent, the stack isn't found on the player, the item's tags don't match the shop's
    /// AllowedTags, the shop is already at its ItemDefinition.MaximumShopStock cap for this item (a
    /// hard sell-cap, not just a price floor -- see PLAN-stock-based-shop-pricing.md), the shop has
    /// no room for a new stack, or the shop can't afford it. Same commit-then-verify-then-rollback-
    /// on-failure shape as TryBuyFromShop, roles reversed.
    /// </summary>
    public static bool TrySellToShop(ComponentManager componentManager, ItemCatalog itemCatalog, int playerEntityId, int shopEntityId, Guid stackInstanceId, IPlayerQuery? playerQuery)
    {
        if (!componentManager.GetPackedPool<ShopComponent>().TryGetReadonly(shopEntityId, out var shop))
        {
            return false;
        }

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        if (!InventoryQueries.TryFindByStackInstanceId(stacks, playerEntityId, stackInstanceId, out var stack) ||
            !InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var item) ||
            !CanTrade(shop, item) ||
            ShopStockPricing.GetTotalStock(componentManager, shopEntityId, item.Id) >= (item.MaximumShopStock ?? ShopStockPricing.DefaultMaximumShopStock) ||
            !InventoryCapacity.HasRoomForNewStack(componentManager, shopEntityId, playerQuery))
        {
            return false;
        }

        var totalPrice = ShopStockPricing.ComputeBulkSellPrice(componentManager, shopEntityId, shop, item, stack.Quantity);

        componentManager.GetPackedPool<CurrencyComponent>().TryGetReadonly(shopEntityId, out var shopCurrency);
        if (shopCurrency.Gold < totalPrice)
        {
            return false;
        }

        if (!CurrencyActions.TryTransfer(componentManager, shopEntityId, playerEntityId, CurrencyType.Gold, totalPrice))
        {
            return false;
        }

        if (!InventoryActions.TryTransferStack(componentManager, playerEntityId, shopEntityId, stackInstanceId, playerQuery))
        {
            CurrencyActions.TryTransfer(componentManager, playerEntityId, shopEntityId, CurrencyType.Gold, totalPrice);
            return false;
        }

        return true;
    }
}
