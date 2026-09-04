using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Inventory;
using Game.Modules.Shops.Components;

namespace Game.Blueprints.Objects;

/// <summary>One item a shop's stock table can grant, paired with the PreferredStockLevel ShopStockPricing measures that shop's actual stock against -- hand-authored per item, the same "hand-tuned per item" convention ItemDefinition.GoldValue already established (PLAN-shops.md).</summary>
public readonly record struct ShopStockEntry(ItemDefinition Item, byte PreferredStockLevel);

/// <summary>Shared random-stock fill shared by PotionShopStock/GeneralShopStock -- same shape as TreasureChest's own random-loot loop, factored out since two stock parts need it against two different item pools.</summary>
public static class ShopStock
{
    private const int MinimumItemCount = 5;
    private const int MaximumItemCount = 10;
    private const int MinimumStackQuantity = 1;
    private const int MaximumStackQuantity = 5;

    public static void GrantRandomStock(ComponentManager componentManager, int entityId, MathUtility mathUtility, IReadOnlyList<ShopStockEntry> stockPool)
    {
        var itemCount = mathUtility.Next(MinimumItemCount, MaximumItemCount + 1);
        for (var i = 0; i < itemCount; i++)
        {
            var entry = stockPool[mathUtility.Next(0, stockPool.Count)];
            var maximumQuantity = System.Math.Min(MaximumStackQuantity, (int)InventoryActions.GetEffectiveMaxStackSize(componentManager, entityId));
            var quantity = (ushort)mathUtility.Next(MinimumStackQuantity, maximumQuantity + 1);
            InventoryActions.AddItem(componentManager, entityId, entry.Item.Id, quantity);
            EnsurePreferredStockLevel(componentManager, entityId, entry.Item.Id, entry.PreferredStockLevel);
        }
    }

    /// <summary>Assigned once, the first time this item is granted to this shop (per PLAN-stock-based-shop-pricing.md's ask) -- ShopStockPreferenceComponent deliberately persists independent of the actual stacks' Quantity fluctuating afterward, so a later duplicate roll of the same item from GrantRandomStock's random pick is a no-op here.</summary>
    private static void EnsurePreferredStockLevel(ComponentManager componentManager, int entityId, Guid itemDefinitionId, byte preferredStockLevel)
    {
        var preferences = componentManager.GetMultiPool<ShopStockPreferenceComponent>();
        if (!preferences.TryGetFirst(entityId, itemDefinitionId, static (ref readonly ShopStockPreferenceComponent pref, Guid id) => pref.ItemDefinitionId == id, out _))
        {
            preferences.Add(entityId, new ShopStockPreferenceComponent(itemDefinitionId, preferredStockLevel));
        }
    }
}
