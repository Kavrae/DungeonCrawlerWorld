using Engine.ECS.Components;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Inventory.Components;

namespace Game.Modules.Inventory;

/// <summary>Lights and puts out a toggle item, one unit at a time.</summary>
/// <remarks>
/// A unit's lit state is its stack's (ToggleItemActivator.IsToggledOn in the stack's Override), and it
/// changes only by moving the unit between the lit and unlit stacks, never by rewriting a stack in
/// place. That is what lets the holder's side follow from stack adds, updates and removals alone
/// (ToggleItemHolderSync). This is the flip itself, with no timing: whatever decides the item is
/// toggled -- an activation, or something switching it off -- has already checked what it must.
/// </remarks>
public static class ToggleItemActions
{
    /// <summary>Flips one unit of entityId's stack stackInstanceId between lit and unlit. Returns false, changing nothing, when the entity doesn't hold the stack or its item isn't a toggle.</summary>
    /// <remarks>
    /// Lighting a unit peels it into a lit stack, shared with any other lit unit of the same item.
    /// Putting one out returns it to the plain stack when that is what it then equals, and to an unlit
    /// stack of its own otherwise (a unit that differs from the catalog item in some other way too).
    /// The hotkey slot the toggle came from is repointed to the stack the unit joined, so that slot
    /// keeps switching the same unit; another slot bound to the stack the unit left stays on it
    /// while it has units left. No unit is ever consumed.
    /// </remarks>
    /// <param name="newStackInstanceId">The stack the flipped unit is in now; 0 when nothing was flipped.</param>
    /// <param name="activatedFromSlot">The hotkey slot the toggle came from, or null when it came from anywhere else; see ItemHotkeyBindingActions.RepointAfterUnitMoved.</param>
    public static bool TryToggle(ComponentManager componentManager, ItemCatalog itemCatalog, int entityId, uint stackInstanceId, out uint newStackInstanceId, HotkeySlot? activatedFromSlot = null)
    {
        newStackInstanceId = 0;

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        if (!InventoryQueries.TryFindByStackInstanceId(stacks, entityId, stackInstanceId, out var stack) ||
            !InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var item) ||
            item.Activator is not ToggleItemActivator toggleActivator)
        {
            return false;
        }

        var flippedItem = item with { Activator = toggleActivator with { IsToggledOn = !toggleActivator.IsToggledOn } };

        var flippedUnitIsPlain = toggleActivator.IsToggledOn && itemCatalog.TryGet(item.Id, out var catalogItem) && InventoryActions.AreEquivalentOverrides(flippedItem, catalogItem);
        newStackInstanceId = InventoryActions.MoveOneUnit(componentManager, entityId, stackInstanceId, flippedUnitIsPlain ? null : flippedItem);

        var oldStackStillHeld = InventoryQueries.TryFindByStackInstanceId(stacks, entityId, stackInstanceId, out _);
        ItemHotkeyBindingActions.RepointAfterUnitMoved(componentManager.GetMultiPool<ItemHotkeyBindingComponent>(), entityId, stackInstanceId, newStackInstanceId, activatedFromSlot, oldStackStillHeld);
        return true;
    }
}
