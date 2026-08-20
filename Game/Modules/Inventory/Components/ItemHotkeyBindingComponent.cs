using Game.Modules.Actions;

namespace Game.Modules.Inventory.Components;

/// <summary>
/// One bound hotkey slot referencing an inventory item stack rather than an action -- sibling to
/// ActionHotkeyBindingComponent (Game.Modules.Actions.Components), see IHotkeySlotBinding
/// (Game.Modules.Actions.HotkeySlot.cs) for what the two share and why this isn't a base-class
/// relationship. References the item by StackInstanceId, not ItemDefinitionId -- since a
/// divergent item can differ from other stacks of the same ItemDefinitionId (e.g. a wand's own
/// remaining charges, see the per-slot item divergence work), "which one is bound" has to mean
/// one exact physical stack, not "whichever stack of this item happens to be available." If the
/// bound stack is later fully consumed, the slot resolves to empty rather than falling back to a
/// different stack of the same item -- see InventoryQueries.TryFindByStackInstanceId. Binding
/// does not remove anything from inventory -- it's a reference, not a transfer.
/// </summary>
public struct ItemHotkeyBindingComponent(HotkeySlot slot, Guid stackInstanceId) : IHotkeySlotBinding
{
    public HotkeySlot Slot { get; } = slot;
    public Guid StackInstanceId { get; set; } = stackInstanceId;

    readonly Guid IHotkeySlotBinding.BoundId => StackInstanceId;

    public override readonly string ToString() => $"Slot : {Slot}\nStackInstanceId : {StackInstanceId}";
}
