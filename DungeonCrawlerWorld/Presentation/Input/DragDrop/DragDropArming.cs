using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.UI;
using Presentation.UI.Inventory;
using UIEngine.Input;

namespace Presentation.Input.DragDrop;

/// <summary>A drag out of the player's inventory held over the map arms the drop it would make, and the cursor says what a held drag would do.</summary>
/// <remarks>
/// Once a drag of the player's own stack, Merged Stack or currency has been over the map for as long as Item Details waits for
/// the pointer to come back (ItemDetailsWindowController.PointerAwayGraceFrames), it arms that drop (IPlayerItemDrops.ArmDrop),
/// which closes the inventory -- the drag goes on -- and shows the tiles it can land on, with the hovered one under the cursor.
/// The release is still MapDropHandler's. When the drag ends, however it ends, the drop it armed is disarmed.
/// While armed, the cursor reads what the armed drop does (DropCursorText, ArmedDropHover): "Drop", or "Add" over a container.
/// Over a hotbar slot, anything a slot can hold reads "Bind", from the inventory or another slot; a drag off a hotbar slot reads
/// "Unbind" anywhere else: it only ever unbinds, never drops.
/// Claims no drop itself.
/// </remarks>
public sealed class DragDropArming(IMapViewQuery mapView, MapViewState mapViewState, IPlayerItemDrops playerItemDrops, ActionTargetingController actionTargeting, DropCursorText dropCursorText) : IContentDropHandler
{
    private int _framesOverMap;
    private bool _armedThisDrag;

    public void OnDragOver(object payload, object? dropTarget, Point position)
    {
        if (payload is not DragPayload drag)
        {
            return;
        }

        if (dropTarget is HotbarSlotDropTarget && drag.CanBindToHotbar && (drag.OriginSlot is not null || drag.OriginEntityId == mapView.PlayerEntityId))
        {
            if (!_armedThisDrag)
            {
                _framesOverMap = 0;
            }

            dropCursorText.SetDragText(DropCursorText.BindText);
            return;
        }

        if (drag.OriginSlot is not null)
        {
            dropCursorText.SetDragText(DropCursorText.UnbindText);
            return;
        }

        if (!drag.TryGetGroundDrop(mapView.PlayerEntityId, out var drop))
        {
            return;
        }

        if (dropTarget is not MapPointerHit)
        {
            if (!_armedThisDrag)
            {
                _framesOverMap = 0;
            }

            dropCursorText.SetDragText(null);
            return;
        }

        if (!_armedThisDrag && ++_framesOverMap > ItemDetailsWindowController.PointerAwayGraceFrames && playerItemDrops.CanArmDrop(drop))
        {
            playerItemDrops.ArmDrop(drop);
            _armedThisDrag = mapViewState.ArmedDrop == drop;
        }

        dropCursorText.SetDragText(null);
    }

    public void OnDragEnded(object payload)
    {
        if (_armedThisDrag)
        {
            actionTargeting.DisarmDrop();
        }

        _armedThisDrag = false;
        _framesOverMap = 0;
        dropCursorText.SetDragText(null);
    }

    public bool TryDrop(object payload, object? dropTarget, Point position) => false;
}
