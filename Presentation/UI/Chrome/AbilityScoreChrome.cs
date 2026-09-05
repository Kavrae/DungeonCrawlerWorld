using Microsoft.Xna.Framework;

namespace Presentation.UI.Chrome;

/// <summary>Position/sizing constants for AbilityScoreWindowController's own button -- see
/// HudChrome's own doc comment for why these are plain mutable fields rather than readonly. The
/// AbilityScoreWindow it opens has no fixed position/size of its own -- it cascades beside the
/// live Inventory window instead (see AbilityScoreWindowController.CreateAbilityScoreWindow),
/// sharing InventoryChrome.WindowWidthFraction/WindowHeight rather than duplicating them.</summary>
public static class AbilityScoreChrome
{
    /// <summary>Between this button's own top edge and InventoryChrome's button sitting directly above it -- mirrors InventoryChrome.ButtonGap's own reasoning, one button-stack step further down.</summary>
    public static Vector2 ButtonGap = new(0, 8);

    public static Vector2 ButtonPosition = InventoryChrome.ButtonPosition + new Vector2(0, InventoryChrome.ButtonSize.Y) + ButtonGap;

    /// <summary>Same shape as InventoryChrome/HealthWindowChrome's own button -- the three HUD-trigger buttons read as one consistent vertical stack.</summary>
    public static Vector2 ButtonSize = new(HudChrome.EntrySize.Y, HudChrome.EntrySize.Y);

    /// <summary>Fixed width cap for the Ability Score hover popup; height auto-grows with content -- see Tooltip.</summary>
    public static Vector2 HoverPopupMaximumSize = new(220, 10000f);
}
