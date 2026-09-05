using Engine.ECS.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Shops.Components;

namespace Game.Modules.Shops;

/// <summary>
/// Where an item's current stock on one shop sits relative to that shop's own PreferredStockLevel
/// band for it -- see ShopStockPricing's own doc comment. Explicit underlying values, not the
/// default 0/1/2/.../ -- the sign and magnitude double as a "how far from Normal, which direction"
/// severity index (Desperate/Flooded are the two extremes, Understocked/Overstocked the two nearer
/// bands, Normal is 0) that ShopItemStackCell.PriceIsFavorable/PriceIsUnfavorable and any future
/// band-table UI can read directly via `(int)status` instead of enumerating every case.
/// </summary>
public enum StockStatus
{
    Desperate = -2,
    Understocked = -1,
    Normal = 0,
    Overstocked = 1,
    Flooded = 2,
}

/// <summary>
/// Supply/demand pricing layered on top of ShopActions.ComputeBuyPrice/ComputeSellPrice's flat
/// GoldValue * shop-multiplier price -- see PLAN-stock-based-shop-pricing.md for the full design
/// and worked examples. Everything here reads a shop's *current aggregate* stock of an item (summed
/// across every physical InventoryItemStackComponent stack of that item on the shop entity, not any
/// one stack) against that item's ShopStockPreferenceComponent par level, entirely off the shop
/// entity's own components -- so pricing is naturally per-shop with no extra isolation work.
///
/// Stock maps to one of 5 discrete bands (StockStatus), each a *flat* multiplier -- not a
/// continuously-varying curve (see PLAN-stock-based-shop-pricing.md's "Phase 5" section for why:
/// legibility for a band-table/per-trade-receipt UI, and an exact, closed-form bulk-price
/// calculation, not just a simpler one). The same band multiplier applies to both buy and sell,
/// differing only by the shop's own flat BuyMultiplier/SellMultiplier, which is what makes a
/// same-shop buy-then-sell-back round trip a guaranteed loss as long as BuyMultiplier stays above
/// SellMultiplier by any nonzero margin (see PLAN-stock-based-shop-pricing.md's "Bulk / bracket
/// pricing" section for the proof -- it never depended on the curve being continuous, only that the
/// same multiplier-per-stock-level applies to both directions).
/// </summary>
public static class ShopStockPricing
{
    /// <summary>Fallback when ItemDefinition.MaximumShopStock is null -- covers most items (see its own doc comment).</summary>
    public const int DefaultMaximumShopStock = 999;

    /// <summary>Fallback PreferredStockLevel for an item a shop has never carried before (see ShopStock.GrantRandomStock's own doc comment for when this applies).</summary>
    public const byte DefaultPreferredStockLevel = 20;

    /// <summary>Declaration order doubles as row order for the band-table UI -- Desperate (cheapest to sell into, priciest to buy from) through Flooded (the reverse).</summary>
    private static readonly StockStatus[] AllBands = [StockStatus.Desperate, StockStatus.Understocked, StockStatus.Normal, StockStatus.Overstocked, StockStatus.Flooded];

    /// <summary>All 5 bands in the same fixed display order AllBands already uses -- what the band-table UI iterates to build its rows.</summary>
    public static IReadOnlyList<StockStatus> GetAllBands() => AllBands;

    /// <summary>Sums Quantity across every physical stack of itemDefinitionId the shop currently holds -- a shop routinely holds the same item across several separate stacks (see PLAN-shops.md's live-testing section), so this is never just one stack's own Quantity.</summary>
    public static int GetTotalStock(ComponentManager componentManager, int shopEntityId, Guid itemDefinitionId)
    {
        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        var total = 0;

        for (var denseIndex = stacks.GetFirstDenseIndex(shopEntityId); denseIndex != -1; denseIndex = stacks.GetNextDenseIndex(denseIndex))
        {
            var stack = stacks.GetReadonlyByDenseIndex(denseIndex);
            if (stack.ItemDefinitionId == itemDefinitionId)
            {
                total += stack.Quantity;
            }
        }

        return total;
    }

    /// <summary>DefaultPreferredStockLevel if this shop has never been granted this item (no ShopStockPreferenceComponent recorded for it yet).</summary>
    public static byte GetPreferredStockLevel(ComponentManager componentManager, int shopEntityId, Guid itemDefinitionId)
    {
        var preferences = componentManager.GetMultiPool<ShopStockPreferenceComponent>();
        return preferences.TryGetFirst(shopEntityId, itemDefinitionId, static (ref readonly ShopStockPreferenceComponent pref, Guid id) => pref.ItemDefinitionId == id, out var match)
            ? match.PreferredStockLevel
            : DefaultPreferredStockLevel;
    }

    /// <summary>
    /// The 4 edges splitting stock into 5 bands, each edge still a percentage of preferredStockLevel
    /// the same way the old 2-edge dead-zone was (E2/E3 here are exactly that old Understock/
    /// OverstockThreshold pair). E3/E4 clamp to maximumShopStock so a preferred level already close
    /// to the cap doesn't imply an edge past it. All 4 collapse toward 0 when preferredStockLevel is
    /// 0 -- there's no meaningful "below zero demand" state, so an item a shop doesn't really want
    /// to carry can never read as Desperate/Understocked, only Normal (at exactly 0 on hand) or
    /// Flooded (the moment it has any at all) -- same "no special-case code needed" edge-case
    /// handling the old 2-edge version had.
    /// </summary>
    public static (int E1, int E2, int E3, int E4) GetBandEdges(byte preferredStockLevel, int maximumShopStock)
    {
        var stockBandEdge1 = (int)MathF.Floor(preferredStockLevel * 0.50f);
        var stockBandEdge2 = (int)MathF.Floor(preferredStockLevel * 0.75f);
        var stockBandEdge3 = System.Math.Min(maximumShopStock, (int)MathF.Ceiling(preferredStockLevel * 1.25f));
        var stockBandEdge4 = System.Math.Min(maximumShopStock, (int)MathF.Ceiling(preferredStockLevel * 1.50f));
        return (stockBandEdge1, stockBandEdge2, stockBandEdge3, stockBandEdge4);
    }

    public static StockStatus GetStockStatus(int stock, int e1, int e2, int e3, int e4)
    {
        if (stock < e1)
        {
            return StockStatus.Desperate;
        }

        if (stock < e2)
        {
            return StockStatus.Understocked;
        }

        if (stock <= e3)
        {
            return StockStatus.Normal;
        }

        return stock <= e4 ? StockStatus.Overstocked : StockStatus.Flooded;
    }

    /// <summary>Same 5-edge lookup as the (stock, stockBandEdge1, stockBandEdge2, stockBandEdge3, stockBandEdge4) overload, but from a preferredStockLevel/maximumShopStock pair rather than already-computed edges -- what InventoryGridContent.ComputeShopStockStatus calls with its own effective (trade-column-aware) stock level, mirroring the explicit-stock overloads ComputeBulkBuyPrice/ComputeBulkSellPrice already have for the identical reason.</summary>
    public static StockStatus GetStockStatus(int currentStock, byte preferredStockLevel, int maximumShopStock)
    {
        var (stockBandEdge1, stockBandEdge2, stockBandEdge3, stockBandEdge4) = GetBandEdges(preferredStockLevel, maximumShopStock);
        return GetStockStatus(currentStock, stockBandEdge1, stockBandEdge2, stockBandEdge3, stockBandEdge4);
    }

    /// <summary>The flat multiplier for one band -- shared by buy and sell, see this class's own doc comment for why.</summary>
    public static float GetBandMultiplier(StockStatus status) => status switch
    {
        StockStatus.Desperate => 1.5f,
        StockStatus.Understocked => 1.25f,
        StockStatus.Normal => 1.0f,
        StockStatus.Overstocked => 0.75f,
        StockStatus.Flooded => 0.5f,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    /// <summary>The inclusive stock range for one band, given the 4 edges -- Flooded's own upper bound is unbounded (int.MaxValue) rather than maximumShopStock, since a shop's actual stock can still exceed that cap by design (it's a hard *sell* cap, see ShopActions.TrySellToShop, not a ceiling stock can never cross by other means). Public -- the band-table UI reads this directly to show each row's own range alongside its price.</summary>
    public static (int Low, int High) GetBandRange(StockStatus status, int e1, int e2, int e3, int e4) => status switch
    {
        StockStatus.Desperate => (0, e1 - 1),
        StockStatus.Understocked => (e1, e2 - 1),
        StockStatus.Normal => (e2, e3),
        StockStatus.Overstocked => (e3 + 1, e4),
        StockStatus.Flooded => (e4 + 1, int.MaxValue),
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    /// <summary>One unit's price in one band, at the shop's own flat buy/sell multiplier -- what the band-table UI shows per row, and the same per-unit formula ComputeBulkPrice's own loop below uses for each band it crosses.</summary>
    public static int GetBandPricePerUnit(ItemDefinition item, float shopMultiplier, StockStatus band) =>
        (int)MathF.Round(item.GoldValue * shopMultiplier * GetBandMultiplier(band));

    /// <summary>Total Gold for buying quantity units from the shop -- bracket pricing, each unit priced at the band the stock level it actually leaves at falls into, as the shop's stock depletes. See this class's own doc comment.</summary>
    public static int ComputeBulkBuyPrice(ComponentManager componentManager, int shopEntityId, ShopComponent shop, ItemDefinition item, ushort quantity) =>
        ComputeBulkBuyPrice(GetTotalStock(componentManager, shopEntityId, item.Id), GetPreferredStockLevel(componentManager, shopEntityId, item.Id), shop, item, quantity);

    /// <summary>
    /// Same as the ComponentManager/shopEntityId overload, but for a caller that already knows the
    /// *effective* current stock level it wants banding computed against, rather than "however much
    /// is literally sitting on shopEntityId's own component pool right now" -- the trade window's
    /// own trade-shop column needs this: a stack staged there has already physically moved off the
    /// real shop entity (a plain InventoryActions.TryTransferStack, see UiInputController.
    /// ResolveTradeAwareItemDrag's "Shop grid &lt;-&gt; Trade: shop column" branch), but hasn't
    /// actually left the shop's *true* ownership yet -- no Gold has changed hands, the trade could
    /// still be cancelled. Deriving currentStock from shopEntityId alone would undercount it (down
    /// to 0 once the whole stack has moved), clamping the bulk-price walk in ComputeBulkPrice below
    /// to a single stock level and pricing only 1 unit regardless of the stack's real Quantity --
    /// confirmed live as the trade window's own "buying 5 scrolls back out of the trade column only
    /// charges for 1" bug.
    /// </summary>
    public static int ComputeBulkBuyPrice(int currentStock, byte preferredStockLevel, ShopComponent shop, ItemDefinition item, ushort quantity) =>
        ComputeBulkPrice(currentStock, preferredStockLevel, shop.BuyMultiplier, item, quantity, ascending: false);

    /// <summary>Total Gold for selling quantity units to the shop -- bracket pricing, each unit priced at the band the stock level it actually arrives at falls into, as the shop's stock builds. See this class's own doc comment.</summary>
    public static int ComputeBulkSellPrice(ComponentManager componentManager, int shopEntityId, ShopComponent shop, ItemDefinition item, ushort quantity) =>
        ComputeBulkSellPrice(GetTotalStock(componentManager, shopEntityId, item.Id), GetPreferredStockLevel(componentManager, shopEntityId, item.Id), shop, item, quantity);

    /// <summary>See the explicit-stock ComputeBulkBuyPrice overload's own doc comment -- identical reasoning, sell direction.</summary>
    public static int ComputeBulkSellPrice(int currentStock, byte preferredStockLevel, ShopComponent shop, ItemDefinition item, ushort quantity) =>
        ComputeBulkPrice(currentStock, preferredStockLevel, shop.SellMultiplier, item, quantity, ascending: true);

    /// <summary>
    /// Closed form, not a per-unit loop: every unit within one band shares the same flat
    /// multiplier, so a band's contribution is exactly unitsInBand * round(one unit's price) --
    /// mathematically identical to rounding each of those units individually and summing them (they
    /// were already all the same value), not an approximation. At most 5 bands are ever touched
    /// regardless of quantity (up to a few hundred units in practice), where the old continuous
    /// curve needed one loop iteration per unit.
    /// </summary>
    private static int ComputeBulkPrice(int currentStock, byte preferredStockLevel, float shopMultiplier, ItemDefinition item, ushort quantity, bool ascending)
    {
        if (quantity == 0)
        {
            return 0;
        }

        var maximumShopStock = item.MaximumShopStock ?? DefaultMaximumShopStock;
        var (e1, e2, e3, e4) = GetBandEdges(preferredStockLevel, maximumShopStock);

        // The set of stock levels this trade actually walks -- a contiguous range regardless of
        // direction (buying walks it high-to-low, selling low-to-high, but the *set* of levels
        // touched, and so which bands they fall into, is the same either way).
        var low = ascending ? currentStock : System.Math.Max(0, currentStock - quantity + 1);
        var high = ascending ? currentStock + quantity - 1 : currentStock;

        var total = 0;
        foreach (var status in AllBands)
        {
            var (bandLow, bandHigh) = GetBandRange(status, e1, e2, e3, e4);
            var overlapLow = System.Math.Max(low, bandLow);
            var overlapHigh = System.Math.Min(high, bandHigh);
            var unitsInBand = overlapHigh - overlapLow + 1;
            if (unitsInBand <= 0)
            {
                continue;
            }

            total += unitsInBand * GetBandPricePerUnit(item, shopMultiplier, status);
        }

        return total;
    }

    /// <summary>One band's contribution to a bulk trade -- Units at PerUnitPrice each, summing to Subtotal. What the per-trade bracket receipt UI shows one row per entry of.</summary>
    public readonly record struct BulkPriceBand(StockStatus Status, int Units, int PerUnitPrice, int Subtotal);

    /// <summary>Same total ComputeBulkBuyPrice returns, broken out one entry per band actually crossed, in the order the trade actually walks them (highest stock first, since buying depletes it) -- what the per-trade bracket receipt UI reads for a specific hovered stack's own Quantity.</summary>
    public static IReadOnlyList<BulkPriceBand> ComputeBulkBuyBreakdown(ComponentManager componentManager, int shopEntityId, ShopComponent shop, ItemDefinition item, ushort quantity) =>
        ComputeBulkBuyBreakdown(GetTotalStock(componentManager, shopEntityId, item.Id), GetPreferredStockLevel(componentManager, shopEntityId, item.Id), shop, item, quantity);

    /// <summary>See ComputeBulkBuyPrice's own explicit-stock overload doc comment -- identical reasoning, breakdown form.</summary>
    public static IReadOnlyList<BulkPriceBand> ComputeBulkBuyBreakdown(int currentStock, byte preferredStockLevel, ShopComponent shop, ItemDefinition item, ushort quantity) =>
        ComputeBulkBreakdown(currentStock, preferredStockLevel, shop.BuyMultiplier, item, quantity, ascending: false);

    /// <summary>Same total ComputeBulkSellPrice returns, broken out one entry per band actually crossed, in the order the trade actually walks them (lowest stock first, since selling builds it) -- what the per-trade bracket receipt UI reads for a specific hovered stack's own Quantity.</summary>
    public static IReadOnlyList<BulkPriceBand> ComputeBulkSellBreakdown(ComponentManager componentManager, int shopEntityId, ShopComponent shop, ItemDefinition item, ushort quantity) =>
        ComputeBulkSellBreakdown(GetTotalStock(componentManager, shopEntityId, item.Id), GetPreferredStockLevel(componentManager, shopEntityId, item.Id), shop, item, quantity);

    /// <summary>See ComputeBulkBuyPrice's own explicit-stock overload doc comment -- identical reasoning, breakdown form.</summary>
    public static IReadOnlyList<BulkPriceBand> ComputeBulkSellBreakdown(int currentStock, byte preferredStockLevel, ShopComponent shop, ItemDefinition item, ushort quantity) =>
        ComputeBulkBreakdown(currentStock, preferredStockLevel, shop.SellMultiplier, item, quantity, ascending: true);

    /// <summary>
    /// Same band-overlap logic ComputeBulkPrice's own loop uses, kept as a separate method (rather
    /// than having ComputeBulkPrice call this and sum the result) so the hot trade-execution path
    /// (ShopActions.TryBuyFromShop/TrySellToShop, via ComputeBulkBuyPrice/ComputeBulkSellPrice)
    /// never allocates a list just to immediately sum it away -- this one's for the UI, called once
    /// per hovered item per frame, not once per trade.
    /// </summary>
    private static IReadOnlyList<BulkPriceBand> ComputeBulkBreakdown(int currentStock, byte preferredStockLevel, float shopMultiplier, ItemDefinition item, ushort quantity, bool ascending)
    {
        if (quantity == 0)
        {
            return [];
        }

        var maximumShopStock = item.MaximumShopStock ?? DefaultMaximumShopStock;
        var (e1, e2, e3, e4) = GetBandEdges(preferredStockLevel, maximumShopStock);

        var low = ascending ? currentStock : System.Math.Max(0, currentStock - quantity + 1);
        var high = ascending ? currentStock + quantity - 1 : currentStock;

        var results = new List<BulkPriceBand>(AllBands.Length);
        for (var i = 0; i < AllBands.Length; i++)
        {
            // Selling walks stock upward (Desperate -> Flooded, AllBands' own declared order);
            // buying walks it downward (Flooded -> Desperate) as the shop's stock depletes -- the
            // receipt should read in the order the trade actually happens, first unit moved first.
            var status = ascending ? AllBands[i] : AllBands[AllBands.Length - 1 - i];
            var (bandLow, bandHigh) = GetBandRange(status, e1, e2, e3, e4);
            var overlapLow = System.Math.Max(low, bandLow);
            var overlapHigh = System.Math.Min(high, bandHigh);
            var unitsInBand = overlapHigh - overlapLow + 1;
            if (unitsInBand <= 0)
            {
                continue;
            }

            var perUnitPrice = GetBandPricePerUnit(item, shopMultiplier, status);
            results.Add(new BulkPriceBand(status, unitsInBand, perUnitPrice, unitsInBand * perUnitPrice));
        }

        return results;
    }
}
