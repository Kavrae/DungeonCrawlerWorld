using Microsoft.Xna.Framework;
using Presentation.UI.Inventory;
using UIEngine.Input;
using UIEngine.UI;

namespace Presentation.Input;

/// <summary>Every right press stops Item Comparison adding columns (see ItemComparisonController.Disarm) -- a right-click tap on an eligible item can re-arm it through its own "Compare" option.</summary>
public sealed class ItemComparisonDisarmInputHandler(ItemComparisonController itemComparisonController) : IUiInputHandler
{
    public void OnRightPress(Element? pressedElement, Point position) => itemComparisonController.Disarm();
}
