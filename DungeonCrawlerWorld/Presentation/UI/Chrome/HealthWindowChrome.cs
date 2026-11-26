using Microsoft.Xna.Framework;

namespace Presentation.UI.Chrome;

/// <summary>Position/sizing constants for the HealthWindow HealthWindowController opens -- see HudChrome's own doc
/// comment for why these are plain mutable fields rather than readonly.</summary>
public static class HealthWindowChrome
{
    /// <summary>Same value as InventoryChrome.WindowPosition today, but kept as its own independent field -- two separately-owned windows that happen to coincide, not one true duplicate (see InventoryChrome's own doc comment).</summary>
    public static Vector2 WindowPosition = new(300, 150);

    /// <summary>HealthWindow splits this width across 3 side-by-side columns (see HealthWindow.BuildColumns), so each needs enough room on its own for a body part's bar/burning line or a buff/debuff's "+50 MaximumHealth: 12s"-shaped text.</summary>
    public static Vector2 WindowSize = new(720, 540);
}
