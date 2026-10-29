namespace Game.Modules.Inventory.Components;

/// <summary>Marks an inventory that holds any number of stacks, exempt from InventoryCapacity.MaxNonPlayerStackCount -- a shop's.</summary>
/// <remarks>Lives in Inventory, which only reads it, so InventoryCapacity needs no other module; whatever owns the entity writes it (the shop blueprints, beside their ShopComponent).</remarks>
public readonly struct UnlimitedStackCountComponent
{
    public override readonly string ToString() => nameof(UnlimitedStackCountComponent);
}
