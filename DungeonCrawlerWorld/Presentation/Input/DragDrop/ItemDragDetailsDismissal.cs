using Microsoft.Xna.Framework;
using Presentation.UI.Inventory;
using UIEngine.Input;

namespace Presentation.Input.DragDrop;

/// <summary>Dragging an item closes Item Details and ends compare mode as the drag starts, and keeps a hover from opening Item Details until the drag ends.</summary>
/// <remarks>Claims no drop. A press that never passes the tap threshold is a click, which selects or compares, so nothing closes on the press itself.</remarks>
public sealed class ItemDragDetailsDismissal(ItemWindowCoordinator itemWindows, ItemHoverDetails itemHoverDetails) : IContentDropHandler
{
    public void OnDragStarted(object payload)
    {
        if (!IsItemDrag(payload))
        {
            return;
        }

        itemHoverDetails.IsPopupHeldBack = true;
        itemWindows.CloseItemDetailsForDrag();
    }

    public void OnDragEnded(object payload) => itemHoverDetails.IsPopupHeldBack = false;

    public bool TryDrop(object payload, object? dropTarget, Point position) => false;

    private static bool IsItemDrag(object payload) =>
        payload is DragPayload drag && (drag.ItemStackInstanceId is not null || drag.MergedItemDefinitionId is not null);
}
