using Engine.ECS.Components;
using Engine.Math;
using Game.Modules;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Definitions;
using Game.Modules.Shops.Components;

namespace Game.Blueprints.Objects;

/// <summary>The "class" half of PotionShop's composition (see Shop's own doc comment) -- adds ShopComponent restricted to Tag.Potion at a 10% specialist modifier, plus a random selection of the catalog's own Potion-tagged items.</summary>
public sealed class PotionShopStock(MathUtility mathUtility) : IBlueprint
{
    private const float BuyMultiplier = 1.10f;
    private const float SellMultiplier = 0.90f;

    /// <summary>Every current CoreItemsModule item carrying Tag.Potion, built once via each item's own pure Build() factory -- mirrors TreasureChest.LootTable's own "no ItemCatalog injection needed" shape.</summary>
    private static readonly ItemDefinition[] Stock =
    [
        HealthPotion.Build(),
        ManaPotion.Build(),
        HotkeyExpansionPotion.Build(),
        DamagePotion.Build(),
        ToxicPotion.Build(),
        ToxicIdol.Build(),
        ImmunityTestPotion.Build(),
        ResistanceTestPotion.Build(),
    ];

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new ShopComponent(allowedTags: [Tag.Potion], BuyMultiplier, SellMultiplier));
        ShopStock.GrantRandomStock(componentManager, entityId, mathUtility, Stock);
    }
}
