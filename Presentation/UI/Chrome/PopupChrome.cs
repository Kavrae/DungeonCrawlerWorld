using Microsoft.Xna.Framework;

namespace Presentation.UI.Chrome;

/// <summary>Misc runtime-popup tuning constants with no natural per-feature Chrome home or
/// cross-field chain of their own -- see HudChrome's own doc comment for why these are plain
/// mutable fields rather than readonly.</summary>
public static class PopupChrome
{
    /// <summary>
    /// Generous ceiling for ContextMenu's own Fixed-mode MaximumSize -- comfortably larger than any
    /// realistic option list could ever need (the same "generous ceiling" reasoning
    /// NotificationChrome.FolderMaximumSize already uses for its own WrapContent Folder). Fixed
    /// mode's own size clamp (RecalculateFixedSize) reads MaximumSize as whatever Build last set
    /// it to and never re-derives it later -- a parent-null Element (see ContextMenu's own doc
    /// comment on why it must stay top-level) has no parent ContentSize to fall back on either,
    /// so without an explicit MaximumSize here it defaults to (0,0) and every later Show/SetBounds
    /// call gets silently clamped down to nothing, however large a size it actually asks for.
    /// </summary>
    public static Vector2 ContextMenuMaximumSize = new(600, 2000);

    /// <summary>Fixed width cap for the shared TooltipController's own Tooltip (every grid/ability hover popup); height auto-grows with content. The single value every one of those consumers now passes to TooltipController.Show, replacing what used to be several near-identical per-consumer constants (220-225px) back when each owned a private Tooltip.</summary>
    public static Vector2 HoverPopupMaximumSize = new(225, 10000f);

    /// <summary>The Armed Hotkey Summary popup's bottom edge sits exactly this far above the summarized slot's top edge.</summary>
    public static Vector2 HotbarSummaryGap = new(0, 1);

    /// <summary>The Ability Score hover popup sits just to the right of whatever's hovered, vertically centered against it -- see PopupPositioning.GetPosition(East).</summary>
    public static Vector2 AbilityScorePopupGap = new(1, 1);
}
