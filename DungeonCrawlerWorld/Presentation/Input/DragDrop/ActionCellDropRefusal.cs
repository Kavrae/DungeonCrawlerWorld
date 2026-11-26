using Microsoft.Xna.Framework;
using UIEngine.Input;

namespace Presentation.Input.DragDrop;

/// <summary>An action or menu command dragged from the Actions window binds to a hotbar slot and is dropped nowhere else: everywhere else shows the refusal cursor, and a release there does nothing.</summary>
/// <remarks>Only for a drag from an Actions window cell: one dragged off a hotbar slot still unbinds that slot wherever it lands (HotbarBindingDropHandler), which a refusal here would stop.</remarks>
public sealed class ActionCellDropRefusal : IContentDropHandler
{
    public bool IsDropRefusedAt(object payload, object? dropTarget, Point position) =>
        payload is DragPayload { Origin: DragPayloadOrigin.ActionCell } && dropTarget is not HotbarSlotDropTarget;

    /// <summary>Claims nothing: a drop on a hotbar slot is HotbarBindingDropHandler's, and every other one was refused.</summary>
    public bool TryDrop(object payload, object? dropTarget, Point position) => false;
}
