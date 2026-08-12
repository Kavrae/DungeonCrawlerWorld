using Game.Modules.Actions;

namespace Game.Modules.Inventory.Components;

/// <summary>
/// One bound hotkey slot referencing an inventory item stack rather than an action -- sibling to
/// ActionHotkeyBindingComponent (Game.Modules.Actions.Components), see IHotkeySlotBinding
/// (Game.Modules.Actions.HotkeySlot.cs) for what the two share and why this isn't a base-class
/// relationship. References the item by ItemDefinitionId, not a stack instance id: today's
/// inventory only ever holds one stack per unique ItemDefinitionId per entity (exact-match
/// stacking, see InventoryActions.AddItem), so the definition id alone already identifies "the"
/// stack. Binding does not remove anything from inventory -- it's a reference, not a transfer.
/// </summary>
public struct ItemHotkeyBindingComponent(HotkeySlot slot, Guid itemDefinitionId) : IHotkeySlotBinding
{
    public HotkeySlot Slot { get; } = slot;
    public Guid ItemDefinitionId { get; set; } = itemDefinitionId;

    public override readonly string ToString() => $"Slot : {Slot}\nItemDefinitionId : {ItemDefinitionId}";
}
