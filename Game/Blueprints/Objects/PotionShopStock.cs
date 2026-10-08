using Engine.Tags;
using Game.Modules;
using Game.Modules.Inventory.Definitions;
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
    /// Every current CoreItemsModule item carrying GameTags.ItemConsumablePotion, built once via each item's own pure
    /// Build() factory -- mirrors TreasureChest.LootTable's own "no ItemCatalog injection needed"
    /// shape. PreferredStockLevel per item is hand-tuned the same way ItemDefinition.GoldValue is
    /// -- higher for the staple potions a specialist shop leans on, lower for the
    /// niche/test items.
    /// </summary>
    private static readonly ShopStockEntry[] Stock =
    [
        new(HealthPotion.Build(), PreferredStockLevel: 50),
        new(ManaPotion.Build(), PreferredStockLevel: 50),
        new(HotkeyExpansionPotion.Build(), PreferredStockLevel: 20),
        new(DamagePotion.Build(), PreferredStockLevel: 30),
        new(ToxicPotion.Build(), PreferredStockLevel: 30),
        new(ImmunityTestPotion.Build(), PreferredStockLevel: 10),
        new(ResistanceTestPotion.Build(), PreferredStockLevel: 10),
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
        ShopStock.GrantRandomStock(componentManager, entityId, context.Rolls, Stock);
    }
}
