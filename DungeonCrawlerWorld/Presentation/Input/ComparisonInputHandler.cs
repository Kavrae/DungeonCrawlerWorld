using Microsoft.Xna.Framework;
using Presentation.UI.Details;
using UIEngine.Input;
using UIEngine.UI;

namespace Presentation.Input;

/// <summary>A compare mode's input -- Item Details' or Action Details': a right press or Escape ends it and closes both popups, Escape then closing nothing else; a cell it can't compare shows the refusal cursor.</summary>
public sealed class ComparisonInputHandler(IComparisonMode comparisonMode) : IUiInputHandler
{
    public void OnRightPress(Element? pressedElement, Point position) => comparisonMode.ClearComparison();

    public bool TryConsumeEscape()
    {
        if (!comparisonMode.IsComparing)
        {
            return false;
        }

        comparisonMode.ClearComparison();
        return true;
    }

    public bool IsInputRefusedAt(Element? hoveredElement, Point position) => comparisonMode.IsRefusedAt(hoveredElement);
}
