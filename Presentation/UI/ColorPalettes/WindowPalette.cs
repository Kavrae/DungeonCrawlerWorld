using Microsoft.Xna.Framework;

namespace Presentation.UI.ColorPalettes;

/// <summary>
/// Shared chrome-color defaults so windows read as one consistent UI instead of each hardcoding
/// its own Title/Header/Body/Border colors -- every consumer below still takes its own
/// ElementChromeOptions/ElementContentOptions/TextOptions color first and only falls back to
/// these, so an individual window can still override any of them. Plain mutable fields, not
/// readonly -- a future data-driven theme loader needs to be able to overwrite them at startup.
/// </summary>
internal static class WindowPalette
{
    /// <summary>Window title bar text.</summary>
    public static Color TitleTextColor = Color.White;

    /// <summary>Generic section-header text color (e.g. TextDivider labels) -- halfway between White and LightBlue so it reads as a header without being mistaken for a focused window's own accent color.</summary>
    public static Color HeaderTextColor = Color.Lerp(Color.White, Color.LightBlue, 0.5f);

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

    /// <summary>Light content areas set against PanelBackgroundColor's dark chrome -- the Inventory folder's own tiles use this, so a management-style panel's inner content reads consistently wherever it appears.</summary>
    public static Color PanelContentColor = Color.LightGray;

    /// <summary>Default content text color.</summary>
    public static Color BodyTextColor = Color.Black;

    /// <summary>Default flat border color. Outset/Inset's raised/pressed bevel look (see BorderRenderer) is a separate two-color light/dark shading effect, not a single overridable color, so it stays independent of this.</summary>
    public static Color BorderColor = Color.Black;

    /// <summary>Translucent hover-highlight overlay, drawn under content that's currently under the cursor (e.g. AbilityScoreColumnHeader) -- alpha kept low enough that whatever it's drawn over still reads through it.</summary>
    public static Color HighlightColor = Color.Gold * 0.5f;

    /// <summary>Universal window title-bar/header background -- fixed regardless of focus; a focused window is shown via FocusAccentColor on its border instead of a header-color swap.</summary>
    public static Color HeaderBackground = new Color(22, 22, 22) * 0.85f;

    /// <summary>Border accent shown on the currently-focused window.</summary>
    public static Color FocusAccentColor = Color.Gold;

    /// <summary>Map tile selection color -- hotbar/inventory item selection moved to AttentionGlow (Gold), to read consistently with an armed hotbar slot; this one's left for the map's own selected-tile highlight.</summary>
    public static Color Selected = Color.LightBlue;

    /// <summary>Generic white accent glow -- grid-square hover, the hotbar slot vignette.</summary>
    public static Color Hover = Color.White;

    /// <summary>Unified base fill for grid-square chrome shared by map tiles and inventory cells.</summary>
    public static Color GridSquareBase = new(90, 90, 90);

    /// <summary>Generic dark control background -- hotbar slots, GridControl's own tiles/buttons, TabbedContent's tab tiles. Collapses what used to be three independently-drifted near-duplicates (45/48-ish dark grays).</summary>
    public static Color ControlBackground = new(45, 45, 45);

    /// <summary>Ability score column background color.</summary>
    public static Color AbilityScoreColumnBackground = new(45, 45, 45);

    /// <summary>Generic placeholder/ghost text color -- TextBox and GridControl's own search box both used an independently-declared LightGray for this.</summary>
    public static Color GhostText = Color.LightGray;

    /// <summary>Generic "darken on hover" overlay -- Button's own hover feedback; the darkening counterpart to Hover (white/brighten) above.</summary>
    public static Color HoverDark = Color.Black * 0.15f;

    /// <summary>Generic "draw attention to this" gold glow -- an unread notification badge/folder, an armed hotbar slot, a drag-drop target slot inviting a drop. Deliberately its own field, not FocusAccentColor (also Gold) -- that one specifically means "this window is focused," a different signal that shouldn't move just because this one does, even though both happen to be Gold today.</summary>
    public static Color AttentionGlow = Color.Gold;

    /// <summary>♥ heart-glyph color -- HealthWindowController's own button icon.</summary>
    public static Color HeartGlyphColor = Color.Red;

    /// <summary>Glyph color drawn on a bound hotbar slot's own icon fill.</summary>
    public static Color SlotGlyphColor = Color.Black;

    /// <summary>Generic label text drawn on ControlBackground -- GridControl's count label, sort button, and toggle labels.</summary>
    public static Color ControlLabelTextColor = Color.White;
}
