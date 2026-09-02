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

    /// <summary>Every current CoreItemsModule item, built once via each item's own pure Build() factory -- mirrors TreasureChest.LootTable's own "no ItemCatalog injection needed" shape.</summary>
    private static readonly ItemDefinition[] Stock =
    [
        HealthPotion.Build(),
        ManaPotion.Build(),
        HotkeyExpansionPotion.Build(),
        DamagePotion.Build(),
        ToxicPotion.Build(),
        ToxicIdol.Build(),
        ScrollOfHealing.Build(),
        ScrollOfTorch.Build(),
        WandOfFireball.Build(),
        ImmunityTestPotion.Build(),
        ResistanceTestPotion.Build(),
    ];

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new ShopComponent(allowedTags: null, BuyMultiplier, SellMultiplier));
        ShopStock.GrantRandomStock(componentManager, entityId, mathUtility, Stock);
    }
}
