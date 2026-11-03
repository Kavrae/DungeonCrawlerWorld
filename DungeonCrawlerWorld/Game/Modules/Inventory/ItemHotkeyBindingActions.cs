using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Actions;
using Game.Modules.Inventory.Components;

namespace Game.Modules.Inventory;

/// <summary>Write-side counterpart to ItemHotkeyBindingQueries.</summary>
public static class ItemHotkeyBindingActions
{
    /// <summary>Moves entityId's hotkey bindings after one unit left the stack oldStackInstanceId for newStackInstanceId: a wand's charge spent, a toggle item lit or put out.</summary>
    /// <remarks>
    /// Several slots can be bound to one stack, so which binding follows the unit is decided, never
    /// guessed. The slot the activation came from follows the unit it moved. If the unit was the
    /// stack's last, the stack is gone and every slot still bound to it follows too, rather than
    /// being left bound to nothing. Any other slot stays on the stack it names, which still exists.
    /// </remarks>
    /// <param name="activatedFromSlot">The hotkey slot the activation came from, or null when it came from anywhere else.</param>
    /// <param name="oldStackStillHeld">Whether entityId still holds oldStackInstanceId after the unit left it.</param>
    public static void RepointAfterUnitMoved(
        MultiComponentPool<ItemHotkeyBindingComponent> bindings,
        int entityId,
        uint oldStackInstanceId,
        uint newStackInstanceId,
        HotkeySlot? activatedFromSlot,
        bool oldStackStillHeld)
    {
        if (oldStackInstanceId == newStackInstanceId)
        {
            return;
        }

        if (activatedFromSlot is { } slot)
        {
            bindings.TryUpdateFirst(entityId, new SlotOnStackMatcher(slot, oldStackInstanceId), new RepointUpdate(newStackInstanceId));
        }

        if (oldStackStillHeld)
        {
            return;
        }

        // Each pass repoints one binding, which then no longer matches, so this ends when none is left on the old stack.
        while (bindings.TryUpdateFirst(entityId, new OnStackMatcher(oldStackInstanceId), new RepointUpdate(newStackInstanceId)))
        {
        }
    }

    private readonly struct SlotOnStackMatcher(HotkeySlot slot, uint stackInstanceId) : IComponentMatcher<ItemHotkeyBindingComponent>
    {
        public bool Matches(in ItemHotkeyBindingComponent binding) => binding.Slot == slot && binding.StackInstanceId == stackInstanceId;
    }

    private readonly struct OnStackMatcher(uint stackInstanceId) : IComponentMatcher<ItemHotkeyBindingComponent>
    {
        public bool Matches(in ItemHotkeyBindingComponent binding) => binding.StackInstanceId == stackInstanceId;
    }

    private readonly struct RepointUpdate(uint newStackInstanceId) : IComponentUpdater<ItemHotkeyBindingComponent>
    {
        public void Update(ref ItemHotkeyBindingComponent binding) => binding.StackInstanceId = newStackInstanceId;
    }
}
