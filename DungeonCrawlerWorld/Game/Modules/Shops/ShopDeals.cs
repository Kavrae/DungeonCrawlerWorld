using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Currency;
using Game.Modules.Currency.Components;
using Game.Modules.Death.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Shops.Components;
using Game.World;

namespace Game.Modules.Shops;

/// <summary>What one side of a shop deal puts in: stacks and Gold its holder has.</summary>
/// <param name="HolderEntityId">Who holds them until the deal completes: the player or the shop itself, or a trade offer entity.</param>
public readonly record struct ShopDealSide(int HolderEntityId, IReadOnlyList<uint> StackInstanceIds, int Gold);

/// <summary>A swap between the player and a shop: the player's offer goes to the shop, the shop's offer to the player.</summary>
public readonly record struct ShopDeal(int PlayerEntityId, int ShopEntityId, ShopDealSide PlayerOffer, ShopDealSide ShopOffer);

/// <summary>Why a shop deal can't complete now.</summary>
public enum ShopDealRefusal : byte
{
    None,

    /// <summary>The shop isn't one (or is destroyed).</summary>
    NotAShop,

    /// <summary>A stack isn't where its side says it is.</summary>
    StackMissing,

    /// <summary>An item the shop doesn't trade in, or that can't change hands at all.</summary>
    NotAccepted,

    /// <summary>The shop already holds as many of an item the player offers as it will (ItemDefinition.MaximumShopStock).</summary>
    ShopStockFull,

    /// <summary>The player or the shop has no room for the stacks it would receive.</summary>
    NoRoom,

    /// <summary>A side's holder hasn't the Gold its side offers.</summary>
    GoldMissing,

    /// <summary>The player's offer is worth less than the shop's.</summary>
    OfferTooLow,
}

/// <summary>What a deal's two offers are worth at the shop's prices now, and why it can't complete, if it can't.</summary>
public readonly record struct ShopDealValuation(int PlayerOfferValue, int ShopOfferValue, ShopDealRefusal Refusal);

/// <summary>The one rule for trading with a shop: a direct buy or sale and a completed trade are all shop deals, valued, refused and completed here.</summary>
/// <remarks>
/// Each side is worth its stacks at the shop's stock-aware bulk price (ShopStockPricing -- what the shop pays for the
/// player's, what it charges for its own; same-item stacks priced together, against the shop's stock including any of its
/// own stacks staged in a trade) plus its Gold. A deal completes only when nothing refuses it, all at once. The Gold the
/// player offers beyond what the deal is worth to the shop is a gift (TryComplete's giftedGold); Gold paid as a price is not.
/// </remarks>
public static class ShopDeals
{
    /// <summary>The player buys one stack from the shop for its price.</summary>
    public static ShopDeal Purchase(ComponentManager componentManager, ItemCatalog itemCatalog, int playerEntityId, int shopEntityId, uint stackInstanceId, IPlayerQuery playerQuery)
    {
        var deal = new ShopDeal(playerEntityId, shopEntityId, new ShopDealSide(playerEntityId, [], 0), new ShopDealSide(shopEntityId, [stackInstanceId], 0));
        var price = Evaluate(componentManager, itemCatalog, deal, playerQuery).ShopOfferValue;
        return deal with { PlayerOffer = deal.PlayerOffer with { Gold = price } };
    }

    /// <summary>The player sells one stack to the shop for its price.</summary>
    public static ShopDeal Sale(ComponentManager componentManager, ItemCatalog itemCatalog, int playerEntityId, int shopEntityId, uint stackInstanceId, IPlayerQuery playerQuery)
    {
        var deal = new ShopDeal(playerEntityId, shopEntityId, new ShopDealSide(playerEntityId, [stackInstanceId], 0), new ShopDealSide(shopEntityId, [], 0));
        var price = Evaluate(componentManager, itemCatalog, deal, playerQuery).PlayerOfferValue;
        return deal with { ShopOffer = deal.ShopOffer with { Gold = price } };
    }

    /// <summary>What each offer is worth now, and why the deal can't complete, or None.</summary>
    public static ShopDealValuation Evaluate(ComponentManager componentManager, ItemCatalog itemCatalog, in ShopDeal deal, IPlayerQuery playerQuery)
    {
        var shops = componentManager.GetPackedPool<ShopComponent>();
        if (!shops.TryGetReadonly(deal.ShopEntityId, out var shop) || componentManager.GetPackedPool<DeadComponent>().Has(deal.ShopEntityId))
        {
            return new ShopDealValuation(0, 0, ShopDealRefusal.NotAShop);
        }

        shop = ShopMarginPricing.ResolveEffectiveShop(componentManager, shop, deal.PlayerEntityId);

        var playerItems = ValueSide(componentManager, itemCatalog, in deal, shop, deal.PlayerOffer, isShopSide: false, out var refusal);
        var shopItems = ValueSide(componentManager, itemCatalog, in deal, shop, deal.ShopOffer, isShopSide: true, out var shopSideRefusal);
        var valuation = new ShopDealValuation(playerItems + deal.PlayerOffer.Gold, shopItems + deal.ShopOffer.Gold, ShopDealRefusal.None);

        if (refusal == ShopDealRefusal.None)
        {
            refusal = shopSideRefusal;
        }

        if (refusal == ShopDealRefusal.None &&
            (!InventoryCapacity.HasRoomForNewStacks(componentManager, deal.ShopEntityId, playerQuery, deal.PlayerOffer.StackInstanceIds.Count) ||
             !InventoryCapacity.HasRoomForNewStacks(componentManager, deal.PlayerEntityId, playerQuery, deal.ShopOffer.StackInstanceIds.Count)))
        {
            refusal = ShopDealRefusal.NoRoom;
        }

        if (refusal == ShopDealRefusal.None && (GoldOf(componentManager, deal.PlayerOffer.HolderEntityId) < deal.PlayerOffer.Gold || GoldOf(componentManager, deal.ShopOffer.HolderEntityId) < deal.ShopOffer.Gold))
        {
            refusal = ShopDealRefusal.GoldMissing;
        }

        if (refusal == ShopDealRefusal.None && valuation.PlayerOfferValue < valuation.ShopOfferValue)
        {
            refusal = ShopDealRefusal.OfferTooLow;
        }

        return valuation with { Refusal = refusal };
    }

    /// <summary>Completes deal: every stack and the Gold of each side moves to the other, or, when anything refuses it, nothing does.</summary>
    /// <param name="giftedGold">The Gold the player gave beyond what the deal was worth to the shop, which ShopCommands announces (GoldGivenToShopEvent); 0 when refused.</param>
    /// <returns>None when it completed; otherwise why not.</returns>
    public static ShopDealRefusal TryComplete(ComponentManager componentManager, ItemCatalog itemCatalog, in ShopDeal deal, IPlayerQuery playerQuery, out int giftedGold)
    {
        giftedGold = 0;
        var valuation = Evaluate(componentManager, itemCatalog, in deal, playerQuery);
        if (valuation.Refusal != ShopDealRefusal.None)
        {
            return valuation.Refusal;
        }

        foreach (var stackInstanceId in deal.PlayerOffer.StackInstanceIds)
        {
            InventoryActions.TryTransferStack(componentManager, itemCatalog, deal.PlayerOffer.HolderEntityId, deal.ShopEntityId, stackInstanceId, playerQuery);
        }

        foreach (var stackInstanceId in deal.ShopOffer.StackInstanceIds)
        {
            if (InventoryActions.TryTransferStack(componentManager, itemCatalog, deal.ShopOffer.HolderEntityId, deal.PlayerEntityId, stackInstanceId, playerQuery))
            {
                InventoryActions.MergeIntoEquivalentStack(componentManager, deal.PlayerEntityId, stackInstanceId);
            }
        }

        if (deal.PlayerOffer.Gold > 0)
        {
            CurrencyActions.TryTransfer(componentManager, deal.PlayerOffer.HolderEntityId, deal.ShopEntityId, CurrencyType.Gold, deal.PlayerOffer.Gold);
        }

        if (deal.ShopOffer.Gold > 0)
        {
            CurrencyActions.TryTransfer(componentManager, deal.ShopOffer.HolderEntityId, deal.PlayerEntityId, CurrencyType.Gold, deal.ShopOffer.Gold);
        }

        giftedGold = Math.Max(0, Math.Min(deal.PlayerOffer.Gold, valuation.PlayerOfferValue - valuation.ShopOfferValue));
        return ShopDealRefusal.None;
    }

    /// <summary>What side's stacks are worth at the shop's price -- what it charges for its own (isShopSide), else what it pays -- and the first thing about them that refuses the deal.</summary>
    /// <remarks>
    /// Every stack that is there is priced, refused or not, so what an offer is worth shows even while the deal can't go
    /// ahead. Same-item stacks are priced together, once, at the first of them: bulk prices cross bands, so pricing each
    /// alone would price every one against the same starting stock.
    /// </remarks>
    private static int ValueSide(ComponentManager componentManager, ItemCatalog itemCatalog, in ShopDeal deal, ShopComponent shop, ShopDealSide side, bool isShopSide, out ShopDealRefusal refusal)
    {
        refusal = ShopDealRefusal.None;
        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        var total = 0;
        for (var index = 0; index < side.StackInstanceIds.Count; index++)
        {
            if (!TryResolve(stacks, itemCatalog, side.HolderEntityId, side.StackInstanceIds[index], out _, out var item))
            {
                RefuseFirst(ref refusal, ShopDealRefusal.StackMissing);
                continue;
            }

            if (!ShopActions.CanTrade(shop, item))
            {
                RefuseFirst(ref refusal, ShopDealRefusal.NotAccepted);
            }

            if (IsPricedAtAnEarlierStack(stacks, itemCatalog, side, index, item.Id))
            {
                continue;
            }

            var quantity = QuantityOf(stacks, itemCatalog, side, index, item.Id);
            var stock = ShopStockOf(componentManager, in deal, item.Id);
            if (!isShopSide && stock >= (item.MaximumShopStock ?? ShopStockPricing.DefaultMaximumShopStock))
            {
                RefuseFirst(ref refusal, ShopDealRefusal.ShopStockFull);
            }

            var preferredStockLevel = ShopStockPricing.GetPreferredStockLevel(componentManager, deal.ShopEntityId, item.Id);
            total += isShopSide
                ? ShopStockPricing.ComputeBulkBuyPrice(stock, preferredStockLevel, shop, item, (ushort)Math.Min(quantity, ushort.MaxValue))
                : ShopStockPricing.ComputeBulkSellPrice(stock, preferredStockLevel, shop, item, (ushort)Math.Min(quantity, ushort.MaxValue));
        }

        return total;
    }

    private static void RefuseFirst(ref ShopDealRefusal refusal, ShopDealRefusal reason)
    {
        if (refusal == ShopDealRefusal.None)
        {
            refusal = reason;
        }
    }

    /// <summary>How many of itemDefinitionId the shop has: its own stacks, and its own staged in a trade offer.</summary>
    private static int ShopStockOf(ComponentManager componentManager, in ShopDeal deal, Guid itemDefinitionId)
    {
        var stock = ShopStockPricing.GetTotalStock(componentManager, deal.ShopEntityId, itemDefinitionId);
        if (deal.ShopOffer.HolderEntityId != deal.ShopEntityId)
        {
            stock += ShopStockPricing.GetTotalStock(componentManager, deal.ShopOffer.HolderEntityId, itemDefinitionId);
        }

        return stock;
    }

    private static bool IsPricedAtAnEarlierStack(MultiComponentPool<InventoryItemStackComponent> stacks, ItemCatalog itemCatalog, ShopDealSide side, int index, Guid itemDefinitionId)
    {
        for (var earlier = 0; earlier < index; earlier++)
        {
            if (TryResolve(stacks, itemCatalog, side.HolderEntityId, side.StackInstanceIds[earlier], out _, out var earlierItem) && earlierItem.Id == itemDefinitionId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The units of itemDefinitionId in side's stacks from index on.</summary>
    private static int QuantityOf(MultiComponentPool<InventoryItemStackComponent> stacks, ItemCatalog itemCatalog, ShopDealSide side, int index, Guid itemDefinitionId)
    {
        var quantity = 0;
        for (var later = index; later < side.StackInstanceIds.Count; later++)
        {
            if (TryResolve(stacks, itemCatalog, side.HolderEntityId, side.StackInstanceIds[later], out var stack, out var item) && item.Id == itemDefinitionId)
            {
                quantity += stack.Quantity;
            }
        }

        return quantity;
    }

    private static bool TryResolve(MultiComponentPool<InventoryItemStackComponent> stacks, ItemCatalog itemCatalog, int holderEntityId, uint stackInstanceId, out InventoryItemStackComponent stack, out ItemDefinition item)
    {
        item = null!;
        return InventoryQueries.TryFindByStackInstanceId(stacks, holderEntityId, stackInstanceId, out stack) &&
               InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out item);
    }

    private static int GoldOf(ComponentManager componentManager, int entityId) =>
        componentManager.GetPackedPool<CurrencyComponent>().TryGetReadonly(entityId, out var currency) ? currency.Gold : 0;
}
