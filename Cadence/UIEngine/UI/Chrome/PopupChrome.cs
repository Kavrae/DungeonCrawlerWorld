using Engine.Utilities;
using Microsoft.Xna.Framework;

namespace UIEngine.UI.Chrome;

/// <summary>Misc runtime-popup tuning constants with no natural per-feature Chrome home or
/// cross-field chain of their own -- see HudChrome's own doc comment for why these are plain
/// mutable fields rather than readonly.</summary>
public static class PopupChrome
{
    /// <summary>The largest the shared Dropdown may be -- comfortably larger than any realistic option list; a taller list scrolls within the height Dropdown.Show is given.</summary>
    public static Vector2 DropdownMaximumSize = new(600, 2000);

    /// <summary>Fixed width cap for the shared TooltipController's own Tooltip (every grid/ability hover popup); height auto-grows with content. The one value every consumer passes to TooltipController.Show.</summary>
    public static Vector2 HoverPopupMaximumSize = new(225, 10000f);

    /// <summary>The Armed Hotkey Summary popup's bottom edge sits exactly this far above the summarized slot's top edge.</summary>
    public static Vector2 HotbarSummaryGap = new(0, 1);

    /// <summary>The Ability Score hover popup sits just to the right of whatever's hovered, vertically centered against it -- see PopupPositioning.GetPosition(East).</summary>
    public static Vector2 AbilityScorePopupGap = new(1, 1);

    /// <summary>An element's own tooltip (Element.TooltipText) sits this far below it.</summary>
    public static Vector2 ElementTooltipGap = new(0, 2);

    /// <summary>Standard hover-tooltip wait time before a hover-triggered popup appears -- shared so every such element feels
    /// consistent. 0.4s, expressed in frames via GameTiming.FramesForSeconds so it stays testable via repeated synthetic
    /// Update() calls.</summary>
    public static int HoverTooltipDelayFrames = GameTiming.FramesForSeconds(0.4f);
}
