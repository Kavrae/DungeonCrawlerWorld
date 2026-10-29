using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Shops.Components;

namespace Game.Blueprints.Objects;

/// <summary>The "class" half of GeneralShop's composition (see Shop's own doc comment) -- adds ShopComponent with no tag restriction at a 20% generalist modifier (a wider spread than PotionShopStock's 10%, since a specialist's focus earns the player a better deal), plus a random selection of every item in the catalog.</summary>
public static class GeneralShopStock
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000112");

    public const string Name = "General Shop Stock";

    private const float BuyMultiplier = 1.20f;
    private const float SellMultiplier = 0.80f;

    /// <summary>
    /// Every current CoreItemsModule item, named by its BuiltInItemIds Guid --
    /// a stock entry needs only the id.
    /// PreferredStockLevel per item is hand-tuned the same way ItemDefinition.GoldValue is
    /// -- lower across the board than PotionShopStock's own par levels, since a
    /// generalist spreads its Gold across every tag instead of leaning on one.
    /// </summary>
    private static readonly ShopStockEntry[] Stock =
    [
        new(BuiltInItemIds.HealthPotion, PreferredStockLevel: 40),
        new(BuiltInItemIds.ManaPotion, PreferredStockLevel: 40),
        new(BuiltInItemIds.HotkeyExpansionPotion, PreferredStockLevel: 15),
        new(BuiltInItemIds.DamagePotion, PreferredStockLevel: 20),
        new(BuiltInItemIds.ToxicPotion, PreferredStockLevel: 20),
        new(BuiltInItemIds.ToxicIdol, PreferredStockLevel: 10),
        new(BuiltInItemIds.ScrollOfHealing, PreferredStockLevel: 25),
        new(BuiltInItemIds.ScrollOfTorch, PreferredStockLevel: 25),
        new(BuiltInItemIds.WandOfFireball, PreferredStockLevel: 5),
        new(BuiltInItemIds.ImmunityTestPotion, PreferredStockLevel: 10),
        new(BuiltInItemIds.ResistanceTestPotion, PreferredStockLevel: 10),
    ];

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Build = Build
    };

    private static void Build(BlueprintContext context)
    {
        var componentManager = context.ComponentManager;
        var entityId = context.EntityId;

        componentManager.Merge(entityId, new ShopComponent(acceptedItems: null, BuyMultiplier, SellMultiplier));
        componentManager.Merge(entityId, new UnlimitedStackCountComponent());
        ShopStock.GrantRandomStock(componentManager, entityId, context.Rolls, Stock);
    }
}
