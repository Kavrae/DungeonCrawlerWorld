using Game.Modules.Inventory;
using UIEngine.UI;
using UIEngine.UI.Content;

namespace Presentation.UI.Inventory;

/// <summary>Owns compare mode and routes item hovers: to Item Details normally, to the comparison popup while comparing.</summary>
/// <remarks>
/// Compare (Item Details' button, or an item's "Compare" option) pins Item Details beside the window holding the item --
/// the anchor -- and starts compare mode. While it lasts, hovering a comparable item (IsComparable, the rule the grids
/// grey the rest out by) opens the comparison popup on it (a second ItemDetailsWindowController, kept just below the
/// anchor), coloured against the anchor and the anchor against it; anything else only shows its name, under the refusal
/// cursor (ItemComparisonInputHandler). When the popup closes the anchor goes back to plain. Stop Comparison, a right press, Escape or the
/// anchor closing ends compare mode and closes both windows.
/// </remarks>
public sealed class ItemComparisonController(
    MapViewState mapViewState,
    ItemDetailsWindowController itemDetails,
    ItemDetailsWindowController comparisonPopup,
    CursorTextContent cursorTextContent,
    Func<int, Window?> windowHoldingItemsOf) : IItemDetailsPopup
{
    /// <summary>Shown at the cursor for as long as compare mode lasts.</summary>
    private const string CompareModeMessage = "Hover an item to compare";

    /// <summary>True from Arm until ClearComparison.</summary>
    public bool IsComparing { get; private set; }

    public bool OpensFor(in ItemHoverTarget target) => !IsComparing || IsComparable(target);

    /// <summary>Whether compare mode can compare target with the anchor: one stack (not a merged cell) with the same kind of activator (MapViewState.CompareRequiredActivatorType) -- the rule the grids grey the rest out by.</summary>
    public bool IsComparable(in ItemHoverTarget target) =>
        target.StackInstanceId is not null && mapViewState.CompareRequiredActivatorType is { } requiredType && target.Definition.Activator?.GetType() == requiredType;

    public bool IsShowing(in ItemHoverTarget target) =>
        IsComparing ? itemDetails.IsShowing(target) || comparisonPopup.IsShowing(target) : itemDetails.IsShowing(target);

    public void OpenAtCursor(in ItemHoverTarget target, IItemShopPricingSource shopPricing)
    {
        if (!IsComparing)
        {
            itemDetails.OpenAtCursor(target, shopPricing, []);
            return;
        }

        if (itemDetails.CurrentDefinition is not { } anchorDefinition || !IsComparable(target))
        {
            return;
        }

        comparisonPopup.OpenAtCursor(target, shopPricing, [anchorDefinition]);
        itemDetails.UpdateComparedAgainst([target.Definition]);
    }

    /// <summary>The comparison popup closed: the anchor goes back to plain.</summary>
    public void HandleComparisonPopupClosed() => itemDetails.UpdateComparedAgainst([]);

    /// <summary>
    /// Starts compare mode on entityId's stack: shows it in Item Details, priced by shopPricing while a shop is open, and
    /// pins it beside the window holding it. A different anchor than the one shown ends any comparison first. A no-op for
    /// an item with no activator -- nothing to compare.
    /// </summary>
    public void Arm(int entityId, uint stackId, IItemShopPricingSource? shopPricing = null)
    {
        var isNewAnchor = itemDetails.CurrentEntityId != entityId || itemDetails.CurrentStackInstanceId != stackId;
        if (isNewAnchor && IsComparing)
        {
            ClearComparison();
        }

        itemDetails.Open(entityId, stackId, shopPricing: shopPricing);

        if (itemDetails.CurrentDefinition?.Activator is not { } activator)
        {
            return;
        }

        IsComparing = true;
        mapViewState.CompareRequiredActivatorType = activator.GetType();
        cursorTextContent.ShowPersistent(this, CompareModeMessage);
        if (windowHoldingItemsOf(entityId) is { } besideWindow)
        {
            itemDetails.PinBeside(besideWindow);
        }
    }

    /// <summary>Ends compare mode and closes Item Details and the comparison popup -- a no-op outside compare mode.</summary>
    public void ClearComparison()
    {
        if (!IsComparing)
        {
            return;
        }

        IsComparing = false;
        mapViewState.CompareRequiredActivatorType = null;
        cursorTextContent.HidePersistent(this);
        itemDetails.Close();
        comparisonPopup.Close();
    }

    public void Compare(int entityId, uint stackInstanceId, IItemShopPricingSource shopPricing) => Arm(entityId, stackInstanceId, shopPricing);

    /// <summary>Ends a comparison against another anchor before a loot box reward opens in Item Details -- a no-op when nothing is compared or the reward is the anchor.</summary>
    public void ClearIfAnchorChanging(int entityId, uint stackId)
    {
        if (itemDetails.CurrentEntityId == entityId && itemDetails.CurrentStackInstanceId == stackId)
        {
            return;
        }

        ClearComparison();
    }
}
