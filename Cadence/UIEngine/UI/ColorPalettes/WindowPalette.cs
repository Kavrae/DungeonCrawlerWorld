using Microsoft.Xna.Framework;

namespace UIEngine.UI.ColorPalettes;

/// <summary>
/// Shared chrome-color defaults so windows read as one consistent UI instead of each hardcoding
/// its own Title/Header/Body/Border colors -- every consumer below still takes its own
/// ElementChromeOptions/ElementContentOptions/TextOptions color first and only falls back to
/// these, so an individual window can still override any of them. Plain mutable fields, not
/// readonly -- a future data-driven theme loader needs to be able to overwrite them at startup.
/// </summary>
public static class WindowPalette
{
    /// <summary>Window title bar text.</summary>
    public static Color TitleTextColor = Color.White;

    /// <summary>
    /// Universal window content-area background -- every Element/Window defaults to this now
    /// (see Element.Build's ContentColor fallback), not just an opt-in for the management-style
    /// windows that used it originally. A 50%-alpha version of this (Color * 0.5f) was tried as
    /// an Inspection V2 transparency experiment and reverted -- it hurt visibility too much once
    /// InspectionWindow sat over the map -- so don't re-propose halving this again without a
    /// different approach (e.g. per-window opt-in rather than shared-palette-wide); the 85%-alpha
    /// value here is deliberately more opaque than that reverted experiment.
    /// </summary>
    public static Color PanelBackgroundColor = new Color(45, 45, 45) * 0.85f;

    /// <summary>Light content areas set against PanelBackgroundColor's dark chrome -- a folder's tiles use this, so a management-style panel's inner content reads consistently wherever it appears.</summary>
    public static Color PanelContentColor = Color.LightGray;

    /// <summary>Default content text color.</summary>
    public static Color BodyTextColor = Color.Black;

    /// <summary>Default flat border color. Outset/Inset's raised/pressed bevel look (see BorderRenderer) is a separate two-color light/dark shading effect, not a single overridable color, so it stays independent of this.</summary>
    public static Color BorderColor = Color.Black;

    /// <summary>Translucent hover-highlight overlay, drawn under content that's currently under the cursor (a column header, say) -- alpha kept low enough that whatever it's drawn over still reads through it.</summary>
    public static Color HighlightColor = Color.Gold * 0.5f;

    /// <summary>Universal window title-bar/header background -- fixed regardless of focus; a focused window is shown via FocusAccentColor on its border instead of a header-color swap.</summary>
    public static Color HeaderBackground = new Color(22, 22, 22) * 0.85f;

    /// <summary>Border accent shown on the currently-focused window.</summary>
    public static Color FocusAccentColor = Color.Gold;

    /// <summary>Generic white accent glow -- grid-square hover, an icon slot's vignette.</summary>
    public static Color Hover = Color.White;

    /// <summary>Unified base fill for grid-square chrome shared by map tiles and item cells.</summary>
    public static Color GridSquareBase = new(90, 90, 90);

    /// <summary>Generic dark control background -- icon slots, GridControl's tiles and buttons, TabbedContent's tab tiles.</summary>
    public static Color ControlBackground = new(45, 45, 45);

    /// <summary>How far a disabled control's content background is darkened toward black, as Windows greys a disabled box or checkbox.</summary>
    public static float DisabledDarkening = 0.3f;

    /// <summary>background darkened by DisabledDarkening, its alpha kept: what a disabled text box or checkbox fills with.</summary>
    public static Color DisabledBackground(Color background)
    {
        var darkened = Color.Lerp(background, Color.Black, DisabledDarkening);
        darkened.A = background.A;
        return darkened;
    }

    /// <summary>Scrollbar track, and the corner square where two tracks meet.</summary>
    public static Color ScrollbarTrackColor = new(25, 25, 25);

    /// <summary>Scrollbar thumb.</summary>
    public static Color ScrollbarThumbColor = new(120, 120, 120);

    /// <summary>Scrollbar thumb under the cursor.</summary>
    public static Color ScrollbarThumbHoverColor = new(160, 160, 160);

    /// <summary>Scrollbar thumb being dragged.</summary>
    public static Color ScrollbarThumbPressedColor = new(200, 200, 200);

    /// <summary>Generic placeholder/ghost text color -- TextBox and GridControl's own search box both used an independently-declared LightGray for this.</summary>
    public static Color GhostText = Color.LightGray;

    /// <summary>Generic "darken on hover" overlay -- Button's own hover feedback; the darkening counterpart to Hover (white/brighten) above.</summary>
    public static Color HoverDark = Color.Black * 0.15f;

    /// <summary>Generic "draw attention to this" gold glow -- an unread notification badge/folder, an armed slot, a drag-drop target slot inviting a drop. Deliberately its own field, not FocusAccentColor (also Gold) -- that one specifically means "this window is focused," a different signal that shouldn't move just because this one does, even though both happen to be Gold today.</summary>
    public static Color AttentionGlow = Color.Gold;

    /// <summary>Generic label text drawn on ControlBackground -- GridControl's count label, sort button, and toggle labels.</summary>
    public static Color ControlLabelTextColor = Color.White;
}
