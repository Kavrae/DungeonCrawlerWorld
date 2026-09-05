using Microsoft.Xna.Framework;

namespace Presentation.UI.Content;

/// <summary>
/// Implemented by a top-level receiving window that accepts a drop anywhere within its own
/// bounds -- not just its specific grid/currency-row child -- so a drop landing on its title bar,
/// border padding, or any other empty space still resolves to the right entity instead of
/// silently failing. Checked by UiInputController.FindDropTargetEntityId only as a fallback, once
/// the narrower IInventoryDropTarget check has already failed for every ancestor below this one --
/// landing exactly on a grid cell or currency element still resolves through that more specific,
/// unchanged path, so this interface only ever matters for "the drop point was somewhere in this
/// window, but not on one of its own known drop-surface children." See PLAN-trade-window.md's own
/// "Drop target resolution" section.
/// </summary>
public interface IWholeWindowDropTarget
{
    /// <summary>
    /// The entity to route an item-stack (or Merged Stack) drag to, given dropPosition -- already
    /// confirmed to be within this window's own Rectangle by the time this is called. A constant
    /// regardless of position for every implementer except TradeWindow, which picks between its
    /// two columns by which half of its own width dropPosition falls in.
    /// </summary>
    int ResolveItemDropEntityId(Point dropPosition);

    /// <summary>Same as ResolveItemDropEntityId, but for a currency drag -- usually identical (the same entity owns both an inventory and a currency balance), kept separate only in case a future implementer's two ever diverge.</summary>
    int ResolveCurrencyDropEntityId(Point dropPosition);
}
