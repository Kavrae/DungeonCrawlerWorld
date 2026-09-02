using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Inventory;

namespace Game.Blueprints.Objects;

/// <summary>Shared random-stock fill shared by PotionShopStock/GeneralShopStock -- same shape as TreasureChest's own random-loot loop, factored out since two stock parts need it against two different item pools.</summary>
public static class ShopStock
{
    private const int MinimumItemCount = 5;
    private const int MaximumItemCount = 10;
    private const int MinimumStackQuantity = 1;
    private const int MaximumStackQuantity = 5;

    public static void GrantRandomStock(ComponentManager componentManager, int entityId, MathUtility mathUtility, IReadOnlyList<ItemDefinition> stockPool)
    {
        var itemCount = mathUtility.Next(MinimumItemCount, MaximumItemCount + 1);
        for (var i = 0; i < itemCount; i++)
        {
            var item = stockPool[mathUtility.Next(0, stockPool.Count)];
            var maximumQuantity = System.Math.Min(MaximumStackQuantity, item.MaxStackSize ?? MaximumStackQuantity);
            var quantity = (ushort)mathUtility.Next(MinimumStackQuantity, maximumQuantity + 1);
            InventoryActions.AddItem(componentManager, entityId, item.Id, quantity);
        }
    }
}
