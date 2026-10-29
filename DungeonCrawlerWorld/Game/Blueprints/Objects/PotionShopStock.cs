using Engine.Tags;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Shops.Components;
using Game.Tags;

namespace Game.Blueprints.Objects;

/// <summary>The "class" half of PotionShop's composition (see Shop's own doc comment) -- adds ShopComponent accepting only GameTags.ItemConsumablePotion at a 10% specialist modifier, plus a random selection of the catalog's own Potion-tagged items.</summary>
public static class PotionShopStock
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000113");

    public const string Name = "Potion Shop Stock";

    private const float BuyMultiplier = 1.10f;
    private const float SellMultiplier = 0.90f;

    private static readonly GameplayTagQuery AcceptedItems = GameplayTagQuery.Any([GameTags.ItemConsumablePotion]);

    /// <summary>
    /// Every current CoreItemsModule item carrying GameTags.ItemConsumablePotion, named by its BuiltInItemIds Guid -- a
    /// stock entry needs only the id. PreferredStockLevel per item is hand-tuned the same way ItemDefinition.GoldValue is
    /// -- higher for the staple potions a specialist shop leans on, lower for the
    /// niche/test items.
    /// </summary>
    private static readonly ShopStockEntry[] Stock =
    [
        new(BuiltInItemIds.HealthPotion, PreferredStockLevel: 50),
        new(BuiltInItemIds.ManaPotion, PreferredStockLevel: 50),
        new(BuiltInItemIds.HotkeyExpansionPotion, PreferredStockLevel: 20),
        new(BuiltInItemIds.DamagePotion, PreferredStockLevel: 30),
        new(BuiltInItemIds.ToxicPotion, PreferredStockLevel: 30),
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

        componentManager.Merge(entityId, new ShopComponent(AcceptedItems, BuyMultiplier, SellMultiplier));
        componentManager.Merge(entityId, new UnlimitedStackCountComponent());
        ShopStock.GrantRandomStock(componentManager, entityId, context.Rolls, Stock);
    }
}
