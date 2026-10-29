using Game.Modules.Actions;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI.Content;
using UIEngine.Input;

namespace Presentation.Input.DragDrop;

/// <summary>An item or action dropped on a hotbar slot binds there; one dragged off a bound slot is unbound wherever it lands.</summary>
/// <remarks>
/// The unbind-then-bind sequencing is HotbarContent.ResolveDroppedBinding's, on the one hotbar this handler is registered
/// for -- a drag that left one of its bound slots unbinds it wherever it lands. Never bindable: a Merged
/// Stack (no single stack a slot could mean), currency (no hotbar concept), and an item from another entity's inventory (a
/// corpse's, say -- a slot references one of the player's own stacks); hovering a slot with one shows the refusal cursor
/// the whole time. The hotbar's accepting glow starts with the drag ghost, not on press, so tapping a slot to arm it never
/// flashes every slot.
/// </remarks>
public sealed class HotbarBindingDropHandler(HotbarContent hotbarContent, IPlayerQuery playerQuery) : IContentDropHandler
{
    public void OnDragGhostShown(object payload)
    {
        if (payload is DragPayload drag && !IsFromNonPlayerInventory(drag))
        {
            hotbarContent.IsAcceptingDrag = true;
        }
    }

    public void OnDragEnded(object payload) => hotbarContent.IsAcceptingDrag = false;

    public bool IsDropRefusedAt(object payload, object? dropTarget, Point position) =>
        payload is DragPayload drag &&
        (!drag.CanBindToHotbar || IsFromNonPlayerInventory(drag)) &&
        IsSlotOfThisHotbar(dropTarget, out _);

    /// <summary>Claims a drop on one of this hotbar's slots, binding it; a drag from a slot that lands anywhere else unbinds that slot and leaves the drop to the handlers after it -- an item can go on into a container.</summary>
    public bool TryDrop(object payload, object? dropTarget, Point position)
    {
        if (payload is not DragPayload drag || !drag.CanBindToHotbar)
        {
            return false;
        }

        if (!IsFromNonPlayerInventory(drag) && IsSlotOfThisHotbar(dropTarget, out var destinationSlot))
        {
            hotbarContent.ResolveDroppedBinding(drag.OriginSlot, destinationSlot, drag.ActionId, drag.ItemStackInstanceId);
            return true;
        }

        if (drag.OriginSlot is { } originSlot)
        {
            hotbarContent.ResolveDroppedBinding(originSlot, destinationSlot: null, drag.ActionId, drag.ItemStackInstanceId);
        }

        return false;
    }

    /// <summary>Whether drag was picked up from some other entity's inventory -- never for a drag from a hotbar slot, which is always the player's.</summary>
    private bool IsFromNonPlayerInventory(DragPayload drag) =>
        drag.OriginSlot is null && drag.OriginEntityId is { } originEntityId && originEntityId != playerQuery.PlayerEntityId;

    private bool IsSlotOfThisHotbar(object? dropTarget, out HotkeySlot slot)
    {
        if (dropTarget is HotbarSlotDropTarget target && ReferenceEquals(target.Hotbar, hotbarContent))
        {
            slot = target.Slot;
            return true;
        }

        slot = default;
        return false;
    }
}
