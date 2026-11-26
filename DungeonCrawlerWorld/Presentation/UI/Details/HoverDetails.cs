using Engine.Utilities;
using UIEngine.UI.Content;

namespace Presentation.UI.Details;

/// <summary>A cell under the pointer that hovering can open details for: what it is called, and whether another names the same cell.</summary>
public interface IHoverTarget<TTarget>
{
    /// <summary>What hovering names above the cursor before the details open.</summary>
    string Name { get; }

    bool IsSameCellAs(in TTarget other);
}

/// <summary>A details popup as hovering sees it.</summary>
/// <typeparam name="TContext">What the grid hovered in hands the popup: an item's shop pricing, an action's width source.</typeparam>
public interface IDetailsPopup<TTarget, in TContext>
{
    /// <summary>Whether hovering target opens a popup -- false for something compare mode can't compare, which hovering then only names.</summary>
    bool OpensFor(in TTarget target);

    /// <summary>Whether the popup is open showing target's cell.</summary>
    bool IsShowing(in TTarget target);

    /// <summary>Opens the popup at the cursor showing target.</summary>
    void OpenAtCursor(in TTarget target, TContext context);
}

/// <summary>The details popups' delay timing.</summary>
public static class HoverDetailsTiming
{
    /// <summary>How long a cell is hovered before its details open.</summary>
    public static readonly int DetailsDelayFrames = GameTiming.FramesForSeconds(1f);
}

/// <summary>What hovering a cell shows: its name above the cursor at once, then its details at the cursor after HoverDetailsTiming.DetailsDelayFrames -- Item Details' and Action Details' one rule.</summary>
/// <remarks>
/// Every grid reports its hovered cell each update, with how long it has been hovered; the label is owner-checked, so
/// one grid's routine end of hover never hides another's. The popup is wired after construction, since the grids are
/// built before it. A hover opens the popup once: closed while the cell is still hovered (Escape, Stop Comparison), it
/// stays closed until the hover ends or starts over. Updates hovered while the popup is held back (a drag) don't count
/// toward the delay, so a hover still on its cell when a drag ends waits the whole delay again.
/// </remarks>
public class HoverDetails<TTarget, TContext, TPopup>(CursorTextContent cursorLabel)
    where TTarget : struct, IHoverTarget<TTarget>
    where TPopup : class, IDetailsPopup<TTarget, TContext>
{
    /// <summary>The owner and cell whose current hover has already opened the popup.</summary>
    private (object Owner, TTarget Target)? _openedHover;

    /// <summary>The owner whose hover went on while the popup was held back, and its hovered frames at the last such update.</summary>
    private (object Owner, int HoveredFrames)? _heldBackHover;

    public TPopup? Popup { get; set; }

    /// <summary>True while something is being dragged: a hover shows only the name, never the popup.</summary>
    public bool IsPopupHeldBack { get; set; }

    /// <summary>owner's hovered cell this update, hovered for hoveredFrames updates so far (1 on the first).</summary>
    public void Hover(object owner, in TTarget target, int hoveredFrames, TContext context)
    {
        hoveredFrames = FramesCountingTowardDetails(owner, hoveredFrames);
        if (hoveredFrames < HoverDetailsTiming.DetailsDelayFrames)
        {
            ForgetOpenedHover(owner);
        }

        if (Popup is { } popup && popup.IsShowing(target))
        {
            cursorLabel.HideLabel(owner);
            return;
        }

        if (hoveredFrames >= HoverDetailsTiming.DetailsDelayFrames && !HasOpened(owner, target) && Popup is { } openingPopup && openingPopup.OpensFor(target))
        {
            cursorLabel.HideLabel(owner);
            _openedHover = (owner, target);
            openingPopup.OpenAtCursor(target, context);
            return;
        }

        cursorLabel.ShowLabel(owner, target.Name);
    }

    /// <summary>owner hovers no cell this update.</summary>
    public void EndHover(object owner)
    {
        ForgetOpenedHover(owner);
        if (_heldBackHover is { } heldBack && ReferenceEquals(heldBack.Owner, owner))
        {
            _heldBackHover = null;
        }

        cursorLabel.HideLabel(owner);
    }

    /// <summary>hoveredFrames less the updates owner's hover spent with the popup held back -- 0 while it is held back.</summary>
    private int FramesCountingTowardDetails(object owner, int hoveredFrames)
    {
        if (IsPopupHeldBack)
        {
            _heldBackHover = (owner, hoveredFrames);
            return 0;
        }

        if (_heldBackHover is { } heldBack && ReferenceEquals(heldBack.Owner, owner) && hoveredFrames >= heldBack.HoveredFrames)
        {
            return hoveredFrames - heldBack.HoveredFrames;
        }

        _heldBackHover = null;
        return hoveredFrames;
    }

    private bool HasOpened(object owner, in TTarget target) =>
        _openedHover is { } opened && ReferenceEquals(opened.Owner, owner) && opened.Target.IsSameCellAs(target);

    private void ForgetOpenedHover(object owner)
    {
        if (_openedHover is { } opened && ReferenceEquals(opened.Owner, owner))
        {
            _openedHover = null;
        }
    }
}
