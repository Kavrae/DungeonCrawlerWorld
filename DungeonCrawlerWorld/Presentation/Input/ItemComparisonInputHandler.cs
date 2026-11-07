using Microsoft.Xna.Framework;
using Presentation.UI.Content;
using Presentation.UI.Inventory;
using UIEngine.Input;
using UIEngine.UI;

namespace Presentation.Input;

/// <summary>Compare mode's input: a right press or Escape ends it and closes Item Details and the comparison popup (see ItemComparisonController.ClearComparison), Escape then closing nothing else; an item it can't compare shows the refusal cursor.</summary>
public sealed class ItemComparisonInputHandler(ItemComparisonController itemComparisonController) : IUiInputHandler
{
    public void OnRightPress(Element? pressedElement, Point position) => itemComparisonController.ClearComparison();

    public bool TryConsumeEscape()
    {
        if (!itemComparisonController.IsComparing)
        {
            return false;
        }

        itemComparisonController.ClearComparison();
        return true;
    }

    /// <summary>Over an item cell compare mode greyed out (InventoryGridContent sets CompareState by ItemComparisonController.IsComparable's rule).</summary>
    public bool IsInputRefusedAt(Element? hoveredElement, Point position) =>
        itemComparisonController.IsComparing && hoveredElement is InventoryItemStackCell { CompareState: CellCompareState.Ineligible };
}
