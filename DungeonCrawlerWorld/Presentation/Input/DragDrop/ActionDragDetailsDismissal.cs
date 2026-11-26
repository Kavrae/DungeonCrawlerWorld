using Microsoft.Xna.Framework;
using Presentation.UI.Actions;
using UIEngine.Input;

namespace Presentation.Input.DragDrop;

/// <summary>Dragging an action or menu command closes Action Details and ends action compare mode as the drag starts, and keeps a hover from opening Action Details until the drag ends -- ItemDragDetailsDismissal's rule.</summary>
/// <remarks>Claims no drop. A press that never passes the tap threshold is a click, which selects, so nothing closes on the press itself.</remarks>
public sealed class ActionDragDetailsDismissal(ActionDetailsCoordinator actionDetails) : IContentDropHandler
{
    public void OnDragStarted(object payload)
    {
        if (!IsActionDrag(payload))
        {
            return;
        }

        actionDetails.HoverDetails.IsPopupHeldBack = true;
        actionDetails.CloseForDrag();
    }

    public void OnDragEnded(object payload) => actionDetails.HoverDetails.IsPopupHeldBack = false;

    public bool TryDrop(object payload, object? dropTarget, Point position) => false;

    private static bool IsActionDrag(object payload) =>
        payload is DragPayload drag && (drag.ActionId is not null || drag.MenuCommandId is not null);
}
