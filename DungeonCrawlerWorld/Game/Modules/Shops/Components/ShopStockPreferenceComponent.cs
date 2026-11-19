using Engine.ECS.Properties;

namespace Game.Modules.Shops.Components;

/// <summary>
/// The par level ShopStockPricing measures a shop's current stock of ItemDefinitionId against --
/// one instance per item type a shop has ever carried, assigned once (see ShopStock.
/// GrantRandomStock) and left untouched afterward regardless of how the shop's actual
/// InventoryItemStackComponent stacks for that item fluctuate, split, or drop to zero. Deliberately
/// decoupled from those stacks for exactly that reason -- PreferredStockLevel is what the shop
/// settles toward, not what it currently holds.
/// </summary>
[DescribedComponent]
public readonly partial struct ShopStockPreferenceComponent(Guid itemDefinitionId, byte preferredStockLevel)
{
    [PropertyMeaning(GamePropertyMeanings.ItemDefinitionId)]
    [ReadOnlyProperty]
    public Guid ItemDefinitionId { get; init; } = itemDefinitionId;

    public byte PreferredStockLevel { get; init; } = preferredStockLevel;
}
