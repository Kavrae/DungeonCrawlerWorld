using Engine.ECS.Components;
using Game.Modules;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Shops;
using Game.Modules.Shops.Components;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Shops;

[TestClass]
public sealed class ShopStockPricingTests
{
    private const int ShopEntityId = 1;

    private static readonly Guid ItemId = Guid.NewGuid();

    private static ComponentManager BuildManager()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8);
        manager.RegisterMultiPool<InventoryItemStackComponent>();
        manager.RegisterMultiPool<ShopStockPreferenceComponent>();
        return manager;
    }

    private static ItemDefinition CreateItem(int goldValue = 10, int? maximumShopStock = null) =>
        new(ItemId, "Test Potion", null, "p", Color.White, Tags: [Tag.Potion], Effects: [], GoldValue: goldValue, MaximumShopStock: maximumShopStock);

    [TestMethod]
    public void GetTotalStock_SumsAcrossSeveralPhysicalStacksOfTheSameItem()
    {
        var manager = BuildManager();
        var stacks = manager.GetMultiPool<InventoryItemStackComponent>();
        stacks.Add(ShopEntityId, new InventoryItemStackComponent(ItemId, quantity: 3));
        stacks.Add(ShopEntityId, new InventoryItemStackComponent(ItemId, quantity: 4));
        stacks.Add(ShopEntityId, new InventoryItemStackComponent(Guid.NewGuid(), quantity: 100)); // different item, must not count

        Assert.AreEqual(7, ShopStockPricing.GetTotalStock(manager, ShopEntityId, ItemId));
    }

    [TestMethod]
    public void GetPreferredStockLevel_NeverGranted_FallsBackToDefault()
    {
        var manager = BuildManager();

        Assert.AreEqual(ShopStockPricing.DefaultPreferredStockLevel, ShopStockPricing.GetPreferredStockLevel(manager, ShopEntityId, ItemId));
    }

    [TestMethod]
    public void GetPreferredStockLevel_Recorded_ReturnsRecordedValue()
    {
        var manager = BuildManager();
        manager.GetMultiPool<ShopStockPreferenceComponent>().Add(ShopEntityId, new ShopStockPreferenceComponent(ItemId, preferredStockLevel: 50));

        Assert.AreEqual((byte)50, ShopStockPricing.GetPreferredStockLevel(manager, ShopEntityId, ItemId));
    }

    [TestMethod]
    public void GetBandEdges_MatchesThePlanExample_Preferred50()
    {
        var (e1, e2, e3, e4) = ShopStockPricing.GetBandEdges(preferredStockLevel: 50, maximumShopStock: 999);

        Assert.AreEqual(25, e1);
        Assert.AreEqual(37, e2);
        Assert.AreEqual(63, e3);
        Assert.AreEqual(75, e4);
    }

    [TestMethod]
    public void GetBandEdges_OuterEdges_ClampedToMaximumShopStock()
    {
        var (_, _, e3, e4) = ShopStockPricing.GetBandEdges(preferredStockLevel: 250, maximumShopStock: 300);

        Assert.AreEqual(300, e3);
        Assert.AreEqual(300, e4);
    }

    [TestMethod]
    public void GetBandEdges_PreferredZero_AllInnerEdgesAreZero()
    {
        var (e1, e2, e3, e4) = ShopStockPricing.GetBandEdges(preferredStockLevel: 0, maximumShopStock: 999);

        Assert.AreEqual(0, e1);
        Assert.AreEqual(0, e2);
        Assert.AreEqual(0, e3);
        Assert.AreEqual(0, e4);
    }

    [TestMethod]
    [DataRow(24, StockStatus.Desperate)]
    [DataRow(25, StockStatus.Understocked)]
    [DataRow(36, StockStatus.Understocked)]
    [DataRow(37, StockStatus.Normal)]
    [DataRow(50, StockStatus.Normal)]
    [DataRow(63, StockStatus.Normal)]
    [DataRow(64, StockStatus.Overstocked)]
    [DataRow(75, StockStatus.Overstocked)]
    [DataRow(76, StockStatus.Flooded)]
    public void GetStockStatus_ClassifiesAgainstBandEdges(int stock, StockStatus expected)
    {
        Assert.AreEqual(expected, ShopStockPricing.GetStockStatus(stock, e1: 25, e2: 37, e3: 63, e4: 75));
    }

    [TestMethod]
    [DataRow(StockStatus.Desperate, 1.5f)]
    [DataRow(StockStatus.Understocked, 1.25f)]
    [DataRow(StockStatus.Normal, 1.0f)]
    [DataRow(StockStatus.Overstocked, 0.75f)]
    [DataRow(StockStatus.Flooded, 0.5f)]
    public void GetBandMultiplier_ReturnsTheFlatMultiplierForEachBand(StockStatus status, float expected)
    {
        Assert.AreEqual(expected, ShopStockPricing.GetBandMultiplier(status), delta: 0.001f);
    }

    [TestMethod]
    public void ComputeBulkBuyPrice_SingleUnitInsideNormalBand_MatchesFlatShopPrice()
    {
        var manager = BuildManager();
        manager.GetMultiPool<ShopStockPreferenceComponent>().Add(ShopEntityId, new ShopStockPreferenceComponent(ItemId, preferredStockLevel: 50));
        manager.GetMultiPool<InventoryItemStackComponent>().Add(ShopEntityId, new InventoryItemStackComponent(ItemId, quantity: 50));

        var shop = new ShopComponent(allowedTags: null, buyMultiplier: 1.10f, sellMultiplier: 0.90f);
        var item = CreateItem(goldValue: 10);

        Assert.AreEqual(11, ShopStockPricing.ComputeBulkBuyPrice(manager, ShopEntityId, shop, item, quantity: 1));
        Assert.AreEqual(9, ShopStockPricing.ComputeBulkSellPrice(manager, ShopEntityId, shop, item, quantity: 1));
    }

    [TestMethod]
    public void ComputeBulkBuyPrice_QuantityCrossingABandBoundary_SplitsAcrossBothBands()
    {
        var manager = BuildManager();
        manager.GetMultiPool<ShopStockPreferenceComponent>().Add(ShopEntityId, new ShopStockPreferenceComponent(ItemId, preferredStockLevel: 50));
        // Edges for preferred 50: (25, 37, 63, 75). Stock at 65 sits in Overstocked [64,75]; buying 3
        // walks stock 65, 64 (both still Overstocked, 8G each) then 63 (crosses into Normal, 11G).
        manager.GetMultiPool<InventoryItemStackComponent>().Add(ShopEntityId, new InventoryItemStackComponent(ItemId, quantity: 65));

        var shop = new ShopComponent(allowedTags: null, buyMultiplier: 1.10f, sellMultiplier: 0.90f);
        var item = CreateItem(goldValue: 10);

        Assert.AreEqual(2 * 8 + 1 * 11, ShopStockPricing.ComputeBulkBuyPrice(manager, ShopEntityId, shop, item, quantity: 3));
    }

    [TestMethod]
    public void GetAllBands_ReturnsAllFiveInDeclarationOrder()
    {
        CollectionAssert.AreEqual(
            new[] { StockStatus.Desperate, StockStatus.Understocked, StockStatus.Normal, StockStatus.Overstocked, StockStatus.Flooded },
            (System.Collections.ICollection)ShopStockPricing.GetAllBands());
    }

    [TestMethod]
    [DataRow(StockStatus.Desperate, 16)]
    [DataRow(StockStatus.Understocked, 14)]
    [DataRow(StockStatus.Normal, 11)]
    [DataRow(StockStatus.Overstocked, 8)]
    [DataRow(StockStatus.Flooded, 6)]
    public void GetBandPricePerUnit_MatchesThePlanExample_Preferred50BuySide(StockStatus band, int expectedPrice)
    {
        var item = CreateItem(goldValue: 10);

        Assert.AreEqual(expectedPrice, ShopStockPricing.GetBandPricePerUnit(item, shopMultiplier: 1.10f, band));
    }

    [TestMethod]
    public void ComputeBulkBuyBreakdown_QuantityCrossingABandBoundary_ReturnsEntriesInWalkedOrder()
    {
        var manager = BuildManager();
        manager.GetMultiPool<ShopStockPreferenceComponent>().Add(ShopEntityId, new ShopStockPreferenceComponent(ItemId, preferredStockLevel: 50));
        // Same scenario as ComputeBulkBuyPrice_QuantityCrossingABandBoundary_SplitsAcrossBothBands --
        // buying walks stock high-to-low, so Overstocked (65, 64) is crossed before Normal (63).
        manager.GetMultiPool<InventoryItemStackComponent>().Add(ShopEntityId, new InventoryItemStackComponent(ItemId, quantity: 65));

        var shop = new ShopComponent(allowedTags: null, buyMultiplier: 1.10f, sellMultiplier: 0.90f);
        var item = CreateItem(goldValue: 10);

        var breakdown = ShopStockPricing.ComputeBulkBuyBreakdown(manager, ShopEntityId, shop, item, quantity: 3);

        Assert.AreEqual(2, breakdown.Count);
        Assert.AreEqual(new ShopStockPricing.BulkPriceBand(StockStatus.Overstocked, 2, 8, 16), breakdown[0]);
        Assert.AreEqual(new ShopStockPricing.BulkPriceBand(StockStatus.Normal, 1, 11, 11), breakdown[1]);
    }

    [TestMethod]
    public void ComputeBulkSellBreakdown_QuantityCrossingABandBoundary_ReturnsEntriesInWalkedOrder()
    {
        var manager = BuildManager();
        manager.GetMultiPool<ShopStockPreferenceComponent>().Add(ShopEntityId, new ShopStockPreferenceComponent(ItemId, preferredStockLevel: 50));
        // Stock at 61 sits in Normal [37,63]; selling 5 walks stock 61, 62, 63 (Normal, 9G each) then
        // 64, 65 (crosses into Overstocked, 7G each) -- selling walks stock low-to-high.
        manager.GetMultiPool<InventoryItemStackComponent>().Add(ShopEntityId, new InventoryItemStackComponent(ItemId, quantity: 61));

        var shop = new ShopComponent(allowedTags: null, buyMultiplier: 1.10f, sellMultiplier: 0.90f);
        var item = CreateItem(goldValue: 10);

        var breakdown = ShopStockPricing.ComputeBulkSellBreakdown(manager, ShopEntityId, shop, item, quantity: 5);

        Assert.AreEqual(2, breakdown.Count);
        Assert.AreEqual(new ShopStockPricing.BulkPriceBand(StockStatus.Normal, 3, 9, 27), breakdown[0]);
        Assert.AreEqual(new ShopStockPricing.BulkPriceBand(StockStatus.Overstocked, 2, 7, 14), breakdown[1]);
    }

    [TestMethod]
    public void ComputeBulkBuyBreakdown_SumOfSubtotals_MatchesComputeBulkBuyPrice()
    {
        var manager = BuildManager();
        manager.GetMultiPool<ShopStockPreferenceComponent>().Add(ShopEntityId, new ShopStockPreferenceComponent(ItemId, preferredStockLevel: 50));
        manager.GetMultiPool<InventoryItemStackComponent>().Add(ShopEntityId, new InventoryItemStackComponent(ItemId, quantity: 999));

        var shop = new ShopComponent(allowedTags: null, buyMultiplier: 1.10f, sellMultiplier: 0.90f);
        var item = CreateItem(goldValue: 10);

        var breakdown = ShopStockPricing.ComputeBulkBuyBreakdown(manager, ShopEntityId, shop, item, quantity: 999);
        var total = ShopStockPricing.ComputeBulkBuyPrice(manager, ShopEntityId, shop, item, quantity: 999);

        Assert.AreEqual(5, breakdown.Count); // spans all 5 bands starting fully Flooded down to Desperate
        var summedSubtotals = 0;
        foreach (var band in breakdown)
        {
            summedSubtotals += band.Subtotal;
        }

        Assert.AreEqual(total, summedSubtotals);
    }

    [TestMethod]
    public void ComputeBulkBuyPrice_QuantityZero_ReturnsZero()
    {
        var manager = BuildManager();
        manager.GetMultiPool<InventoryItemStackComponent>().Add(ShopEntityId, new InventoryItemStackComponent(ItemId, quantity: 50));
        var shop = new ShopComponent(allowedTags: null, buyMultiplier: 1.10f, sellMultiplier: 0.90f);
        var item = CreateItem(goldValue: 10);

        Assert.AreEqual(0, ShopStockPricing.ComputeBulkBuyPrice(manager, ShopEntityId, shop, item, quantity: 0));
    }

    /// <summary>
    /// ComputeBulkBuyPrice/ComputeBulkSellPrice are pure price queries -- they never mutate a
    /// shop's actual stacks (that's ShopActions' job). Simulating a real round trip therefore means
    /// manually replacing the shop's stock between the buy and sell calls, exactly as an actual
    /// buy-then-sell-back trade through ShopActions would leave it.
    /// </summary>
    private static void SetStock(ComponentManager manager, int shopEntityId, Guid itemDefinitionId, int quantity)
    {
        var stacks = manager.GetMultiPool<InventoryItemStackComponent>();
        for (var denseIndex = stacks.GetFirstDenseIndex(shopEntityId); denseIndex != -1; denseIndex = stacks.GetNextDenseIndex(denseIndex))
        {
            if (stacks.GetReadonlyByDenseIndex(denseIndex).ItemDefinitionId == itemDefinitionId)
            {
                stacks.RemoveByDenseIndex(denseIndex);
                break;
            }
        }

        if (quantity > 0)
        {
            stacks.Add(shopEntityId, new InventoryItemStackComponent(itemDefinitionId, (ushort)quantity));
        }
    }

    [TestMethod]
    public void SameShopRoundTrip_BuyingOutFullOverstockThenSellingItAllBack_IsAlwaysALoss()
    {
        var manager = BuildManager();
        manager.GetMultiPool<ShopStockPreferenceComponent>().Add(ShopEntityId, new ShopStockPreferenceComponent(ItemId, preferredStockLevel: 50));
        SetStock(manager, ShopEntityId, ItemId, 999);

        // Even a razor-thin spread (0.505 vs 0.495 -- the kind of near-parity a maxed-out future
        // Charisma/skill discount might approach) must never flip this into a profit -- the proof
        // (PLAN-stock-based-shop-pricing.md's "Bulk / bracket pricing" section) never depended on
        // the curve being continuous, and banding doesn't change that.
        var shop = new ShopComponent(allowedTags: null, buyMultiplier: 0.505f, sellMultiplier: 0.495f);
        var item = CreateItem(goldValue: 10);

        var buyCost = ShopStockPricing.ComputeBulkBuyPrice(manager, ShopEntityId, shop, item, quantity: 999);
        SetStock(manager, ShopEntityId, ItemId, 0); // the buyout actually happened -- shop now has none left
        var sellRevenue = ShopStockPricing.ComputeBulkSellPrice(manager, ShopEntityId, shop, item, quantity: 999);

        Assert.IsLessThan(buyCost, sellRevenue, $"Round trip should always lose money: bought for {buyCost}, sold back for {sellRevenue}.");
    }

    [TestMethod]
    public void SameShopRoundTrip_WithTodaysActualPotionShopMargins_LosesRoughlyThePlanExample()
    {
        var manager = BuildManager();
        manager.GetMultiPool<ShopStockPreferenceComponent>().Add(ShopEntityId, new ShopStockPreferenceComponent(ItemId, preferredStockLevel: 50));
        SetStock(manager, ShopEntityId, ItemId, 999);

        var shop = new ShopComponent(allowedTags: null, buyMultiplier: 1.10f, sellMultiplier: 0.90f);
        var item = CreateItem(goldValue: 10);

        var buyCost = ShopStockPricing.ComputeBulkBuyPrice(manager, ShopEntityId, shop, item, quantity: 999);
        SetStock(manager, ShopEntityId, ItemId, 0); // the buyout actually happened -- shop now has none left
        var sellRevenue = ShopStockPricing.ComputeBulkSellPrice(manager, ShopEntityId, shop, item, quantity: 999);
        var loss = buyCost - sellRevenue;

        // ~1988G under the 5-band model (Buy 6489, Sell 4501) -- higher than the old continuous
        // curve's ~1550G for the same scenario, since Flooded's flat 0.5x now covers most of the
        // 76-999 range instead of only approaching 0.5x right at the very top (see
        // PLAN-stock-based-shop-pricing.md's Phase 5 section).
        Assert.IsTrue(loss > 1800 && loss < 2200, $"Expected a loss around ~1988G (see PLAN-stock-based-shop-pricing.md's Phase 5 worked example), got {loss}.");
    }
}
