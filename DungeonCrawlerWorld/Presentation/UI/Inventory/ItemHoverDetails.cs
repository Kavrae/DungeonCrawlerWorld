using Engine.Utilities;
using Game.Modules.Inventory;
using UIEngine.UI;
using UIEngine.UI.Content;

namespace Presentation.UI.Inventory;

/// <summary>An item cell under the pointer: whose inventory it is in, the stack (none for a merged cell) and the item shown.</summary>
public readonly record struct ItemHoverTarget(int EntityId, uint? StackInstanceId, ItemDefinition Definition)
{
    /// <summary>Whether this names the same cell as other -- the same stack, or for a merged cell the same item in the same inventory.</summary>
    public bool IsSameCellAs(in ItemHoverTarget other) =>
        EntityId == other.EntityId &&
        StackInstanceId == other.StackInstanceId &&
        (StackInstanceId is not null || Definition.Id == other.Definition.Id);
}

/// <summary>Where an item's shop pricing comes from while a shop is open -- the grid the item was hovered in, which knows whether it is the shop's side and which stock it prices against.</summary>
public interface IItemShopPricingSource
{
    /// <summary>The pricing rows for target, or null when no shop is open; the same list instance while nothing it is built from changes.</summary>
    IReadOnlyList<TooltipRow>? ShopPricingRowsFor(in ItemHoverTarget target);
}

/// <summary>The Item Details popup as hovering sees it.</summary>
public interface IItemDetailsPopup
{
    /// <summary>Whether hovering target opens a popup -- false for an item compare mode can't compare, which hovering then only names.</summary>
    bool OpensFor(in ItemHoverTarget target);

    /// <summary>Whether the popup is open showing target's cell.</summary>
    bool IsShowing(in ItemHoverTarget target);

    /// <summary>Opens the popup at the cursor showing target, priced by shopPricing while a shop is open.</summary>
    void OpenAtCursor(in ItemHoverTarget target, IItemShopPricingSource shopPricing);

    /// <summary>Starts compare mode on entityId's stack, priced by shopPricing while a shop is open -- an item cell's "Compare".</summary>
    void Compare(int entityId, uint stackInstanceId, IItemShopPricingSource shopPricing);
}

/// <summary>What hovering an item cell shows: its name above the cursor at once, then Item Details at the cursor after DetailsDelayFrames.</summary>
/// <remarks>
/// Every grid reports its hovered cell each update, with how long it has been hovered; the label is owner-checked, so
/// one grid's routine end of hover never hides another's. The popup is wired after construction (ItemWindowCoordinator),
/// since the grids are built before Item Details. A hover opens the popup once: closed while the cell is still hovered
/// (Escape, Stop Comparison), it stays closed until the hover ends or starts over.
/// </remarks>
public sealed class ItemHoverDetails(CursorLabelContent cursorLabel)
{
    public static readonly int DetailsDelayFrames = GameTiming.FramesForSeconds(1.5f);

    /// <summary>The owner and cell whose current hover has already opened the popup.</summary>
    private (object Owner, ItemHoverTarget Target)? _openedHover;

    public IItemDetailsPopup? Popup { get; set; }

    /// <summary>owner's hovered cell this update, hovered for hoveredFrames updates so far (1 on the first).</summary>
    public void Hover(object owner, in ItemHoverTarget target, int hoveredFrames, IItemShopPricingSource shopPricing)
    {
        if (hoveredFrames < DetailsDelayFrames)
        {
            ForgetOpenedHover(owner);
        }

        if (Popup is { } popup && popup.IsShowing(target))
        {
            cursorLabel.Hide(owner);
            return;
        }

        if (hoveredFrames >= DetailsDelayFrames && !HasOpened(owner, target) && Popup is { } openingPopup && openingPopup.OpensFor(target))
        {
            cursorLabel.Hide(owner);
            _openedHover = (owner, target);
            openingPopup.OpenAtCursor(target, shopPricing);
            return;
        }

        cursorLabel.Show(owner, target.Definition.Name);
    }

    /// <summary>An item cell's "Compare": starts compare mode on entityId's stack, priced by the grid it was chosen in.</summary>
    public void Compare(int entityId, uint stackInstanceId, IItemShopPricingSource shopPricing) => Popup?.Compare(entityId, stackInstanceId, shopPricing);

    /// <summary>owner hovers no cell this update.</summary>
    public void EndHover(object owner)
    {
        ForgetOpenedHover(owner);
        cursorLabel.Hide(owner);
    }

    private bool HasOpened(object owner, in ItemHoverTarget target) =>
        _openedHover is { } opened && ReferenceEquals(opened.Owner, owner) && opened.Target.IsSameCellAs(target);

    private void ForgetOpenedHover(object owner)
    {
        if (_openedHover is { } opened && ReferenceEquals(opened.Owner, owner))
        {
            _openedHover = null;
        }
    }
}
