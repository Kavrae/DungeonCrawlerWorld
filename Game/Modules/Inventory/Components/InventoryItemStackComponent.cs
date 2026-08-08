namespace Game.Modules.Inventory.Components;

/// <summary>
/// One stack of identical items in an entity's inventory -- entities own zero or more of these
/// via a MultiComponentPool (see InventoryModule). IsDisabled marks this specific stack as
/// unavailable (e.g. a starting item withheld until some later trigger) -- distinct from
/// InventoryDisabledComponent, which disables an entity's whole inventory. Quantity is the
/// "identical items grouped with a count" requirement; a stack that later diverges from its
/// ItemDefinition (limited uses, damage, crafting mods) is expected to become its own
/// Quantity == 1 stack once that system exists, rather than something this component predicts
/// the shape of today.
/// </summary>
public struct InventoryItemStackComponent(Guid itemDefinitionId, int quantity, bool isDisabled = false)
{
    public Guid ItemDefinitionId { get; } = itemDefinitionId;

    public int Quantity { get; set; } = quantity;

    public bool IsDisabled { get; set; } = isDisabled;

    public override readonly string ToString() => $"ItemDefinitionId : {ItemDefinitionId}\nQuantity : {Quantity}\nIsDisabled : {IsDisabled}";
}
