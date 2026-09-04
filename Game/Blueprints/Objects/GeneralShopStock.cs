using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Definitions;
using Game.Modules.Shops.Components;

namespace Game.Blueprints.Objects;

/// <summary>The "class" half of GeneralShop's composition (see Shop's own doc comment) -- adds ShopComponent with no tag restriction at a 20% generalist modifier (a wider spread than PotionShopStock's 10%, since a specialist's focus earns the player a better deal), plus a random selection of every item in the catalog.</summary>
public sealed class GeneralShopStock(MathUtility mathUtility) : IBlueprint
{
    private const float BuyMultiplier = 1.20f;
    private const float SellMultiplier = 0.80f;

    /// <summary>
    /// Every current CoreItemsModule item, built once via each item's own pure Build() factory --
    /// mirrors TreasureChest.LootTable's own "no ItemCatalog injection needed" shape.
    /// PreferredStockLevel per item is hand-tuned the same way ItemDefinition.GoldValue is
    /// (PLAN-shops.md) -- lower across the board than PotionShopStock's own par levels, since a
    /// generalist spreads its Gold across every tag instead of leaning on one.
    /// </summary>
    private static readonly ShopStockEntry[] Stock =
    [
        new(HealthPotion.Build(), PreferredStockLevel: 40),
        new(ManaPotion.Build(), PreferredStockLevel: 40),
        new(HotkeyExpansionPotion.Build(), PreferredStockLevel: 15),
        new(DamagePotion.Build(), PreferredStockLevel: 20),
        new(ToxicPotion.Build(), PreferredStockLevel: 20),
        new(ToxicIdol.Build(), PreferredStockLevel: 10),
        new(ScrollOfHealing.Build(), PreferredStockLevel: 25),
        new(ScrollOfTorch.Build(), PreferredStockLevel: 25),
        new(WandOfFireball.Build(), PreferredStockLevel: 5),
        new(ImmunityTestPotion.Build(), PreferredStockLevel: 10),
        new(ResistanceTestPotion.Build(), PreferredStockLevel: 10),
    ];

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new ShopComponent(allowedTags: null, BuyMultiplier, SellMultiplier));
        ShopStock.GrantRandomStock(componentManager, entityId, mathUtility, Stock);
    }
}
