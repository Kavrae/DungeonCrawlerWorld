using Game.Modules.Actions;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI.Content;
using UIEngine.Input;
using UIEngine.UI;

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

    public bool IsDropRefusedAt(object payload, Element? hoveredElement, Point position) =>
        payload is DragPayload drag &&
        (!drag.CanBindToHotbar || IsFromNonPlayerInventory(drag)) &&
        IsOverThisHotbar(hoveredElement) &&
        hotbarContent.TryGetSlotAt(position, out _);

    public bool TryDrop(object payload, Element? dropTarget, Point position)
    {
        if (payload is not DragPayload drag || !drag.CanBindToHotbar)
        {
            return false;
        }

        HotkeySlot? destinationSlot = null;

        if (!IsFromNonPlayerInventory(drag) && IsOverThisHotbar(dropTarget) && hotbarContent.TryGetSlotAt(position, out var dropSlot))
        {
            destinationSlot = dropSlot;
        }

        if (drag.OriginSlot is null && destinationSlot is null)
        {
            return true;
        }

        hotbarContent.ResolveDroppedBinding(drag.OriginSlot, destinationSlot, drag.ActionId, drag.ItemStackInstanceId);
        return true;
    }

    /// <summary>Whether drag was picked up from some other entity's inventory -- never set for a hotbar-origin drag, which is always the player's.</summary>
    private bool IsFromNonPlayerInventory(DragPayload drag) =>
        drag.OriginEntityId is { } originEntityId && originEntityId != playerQuery.PlayerEntityId;

    private bool IsOverThisHotbar(Element? element) => element is Window window && ReferenceEquals(window.Content, hotbarContent);
}
