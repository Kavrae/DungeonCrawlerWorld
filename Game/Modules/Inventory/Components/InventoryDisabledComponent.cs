namespace Game.Modules.Inventory.Components;

/// <summary>
/// Whether an entity's whole inventory is temporarily disabled -- items still exist and can
/// still be granted, but InventoryFolderController (Presentation) refuses to open the
/// management window while this is true. A value component (not a presence marker) since
/// DirectComponentPool has no Remove -- re-enabling merges IsDisabled back to false rather than
/// removing the component.
/// </summary>
public struct InventoryDisabledComponent(bool isDisabled)
{
    public bool IsDisabled { get; set; } = isDisabled;
}
