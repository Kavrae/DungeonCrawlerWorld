using Microsoft.Xna.Framework;
using Presentation.UI.Inventory;
using UIEngine.Input;

namespace Presentation.Input;

/// <summary>A press outside the open Item Details window closes it and goes no further.</summary>
/// <remarks>
/// The press doesn't also reach whatever's underneath (closing it and issuing a map-tile move in the same click) -- the
/// same "one input consumed by the cancel" rule as an open context menu. IsOutsideClick decides what still counts as
/// inside (this window, the player's Inventory window, an open secondary/corpse window, comparison columns).
/// </remarks>
public sealed class ItemDetailsOutsideClickInputHandler(ItemDetailsWindowController itemDetailsWindowController) : IUiInputHandler
{
    public bool TryConsumePress(Point position)
    {
        if (itemDetailsWindowController.IsOpen && itemDetailsWindowController.IsOutsideClick(position))
        {
            itemDetailsWindowController.Close();
            return true;
        }

        return false;
    }
}
