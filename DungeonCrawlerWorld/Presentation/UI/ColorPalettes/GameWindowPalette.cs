using Microsoft.Xna.Framework;

namespace Presentation.UI.ColorPalettes;

/// <summary>
/// The game windows' shared colours, beside WindowPalette's window-framework defaults. Plain mutable fields for the
/// same reason as WindowPalette's: a future data-driven theme loader needs to be able to overwrite them at startup.
/// </summary>
internal static class GameWindowPalette
{
    /// <summary>Generic section-header text color (e.g. TextDivider labels) -- halfway between White and LightBlue so it reads as a header without being mistaken for a focused window's own accent color.</summary>
    public static Color HeaderTextColor = Color.Lerp(Color.White, Color.LightBlue, 0.5f);

    /// <summary>Map tile selection color -- hotbar/inventory item selection moved to WindowPalette.AttentionGlow (Gold), to read consistently with an armed hotbar slot; this one's left for the map's own selected-tile highlight.</summary>
    public static Color Selected = Color.LightBlue;

    /// <summary>Ability score column background color.</summary>
    public static Color AbilityScoreColumnBackground = new(45, 45, 45);

    /// <summary>The "this is on" glow -- a lit toggle item's inventory cell and hotbar slot, and a toggled-on action's hotbar slot (ToggleActiveMarker). Its own colour, apart from WindowPalette.AttentionGlow (selected or armed) and the green comparison-eligible glow, so being on never reads as either.</summary>
    public static Color ToggleActiveGlow = Color.Cyan;

    /// <summary>The "Active" line in a tooltip or Item Details for a toggle that is on -- the same colour as its glow.</summary>
    public static Color ToggleActiveText = Color.Cyan;

    /// <summary>♥ heart-glyph color -- HealthWindowController's own button icon.</summary>
    public static Color HeartGlyphColor = Color.Red;

    /// <summary>Glyph color drawn on a bound hotbar slot's own icon fill.</summary>
    public static Color SlotGlyphColor = Color.Black;

    /// <summary>A container on the map that a held item is over: green when it would take the drop, red when it wouldn't (MapViewState.ContainerDropHighlight).</summary>
    public static Color ContainerDropAccepted = Color.LimeGreen;
    public static Color ContainerDropRefused = Color.Red;

    /// <summary>Diagnostics window: row text, group headings, the paused/warning/leak text, and its graphs' background, level line, per-frame bars and frame-budget line.</summary>
    public static Color DiagnosticsText = Color.LightGray;
    public static Color DiagnosticsGroupHeading = Color.Lerp(Color.White, Color.LightBlue, 0.5f);
    public static Color DiagnosticsWarningText = Color.Orange;
    public static Color DiagnosticsGraphBackground = new(25, 25, 25);
    public static Color DiagnosticsLevelSeries = Color.LightGreen;
    public static Color DiagnosticsEventSeries = Color.Orange;
    public static Color DiagnosticsFrameBudgetLine = Color.Red * 0.7f;
}
