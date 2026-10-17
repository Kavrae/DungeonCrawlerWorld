namespace Game.Modules.Shops.Components;

/// <summary>
/// The par level ShopStockPricing measures a shop's current stock of ItemDefinitionId against --
/// one instance per item type a shop has ever carried, assigned once (see ShopStock.
/// GrantRandomStock) and left untouched afterward regardless of how the shop's actual
/// InventoryItemStackComponent stacks for that item fluctuate, split, or drop to zero. Deliberately
/// decoupled from those stacks for exactly that reason -- PreferredStockLevel is what the shop
/// settles toward, not what it currently holds.
/// </summary>
public readonly struct ShopStockPreferenceComponent(Guid itemDefinitionId, byte preferredStockLevel)
{
    public Guid ItemDefinitionId { get; } = itemDefinitionId;

    public byte PreferredStockLevel { get; } = preferredStockLevel;

    public override readonly string ToString() => $"ItemDefinitionId : {ItemDefinitionId}\nPreferredStockLevel : {PreferredStockLevel}";
}
