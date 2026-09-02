using Microsoft.Xna.Framework;

namespace Presentation.UI.Chrome;

/// <summary>Position/sizing constants for HealthWindowController's own button and the HealthWindow
/// it opens -- see HudChrome's own doc comment for why these are plain mutable fields rather than
/// readonly.</summary>
public static class HealthWindowChrome
{
    /// <summary>Beneath the Notification folder, with enough clearance that NotificationCenter's own folder never overlaps this one even fully expanded (NotificationChrome.FolderMaximumSize) -- the same clearance InventoryChrome.FolderPosition used to keep for itself before this button took its slot.</summary>
    public static Vector2 NotificationClearanceGap = new(0, 20);

    public static Vector2 ButtonPosition = HudChrome.Margin + new Vector2(0, NotificationChrome.FolderMaximumSize.Y) + NotificationClearanceGap;

    /// <summary>Square, one HudChrome.EntrySize row tall -- reads as a real icon button (see Button's own single-glyph ink-centered DrawContent) rather than a wide text tile.</summary>
    public static Vector2 ButtonSize = new(HudChrome.EntrySize.Y, HudChrome.EntrySize.Y);

    /// <summary>Same value as InventoryChrome.WindowPosition today, but kept as its own independent field -- two separately-owned windows that happen to coincide, not one true duplicate (see InventoryChrome's own doc comment).</summary>
    public static Vector2 WindowPosition = new(300, 150);

    /// <summary>Wider than its pre-two-column size (260) -- HealthWindow now splits this width across 2 side-by-side columns (see HealthWindow.BuildColumns), so each needs enough room on its own for a body part's bar/status line or a buff/debuff's "+50 MaximumHealth: 12s"-shaped text.</summary>
    public static Vector2 WindowSize = new(480, 360);
}
