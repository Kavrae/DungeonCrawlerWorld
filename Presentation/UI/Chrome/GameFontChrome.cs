using UIEngine.UI.Chrome;

namespace Presentation.UI.Chrome;

/// <summary>The game windows' font-size constants -- both fixed point sizes and the per-widget size-fraction multipliers
/// passed to FontService.GetFont -- collected from across Presentation's own windows/content, mirroring FontChrome for
/// the window framework's own. Plain mutable fields for the same reason as FontChrome's.</summary>
public static class GameFontChrome
{
    public static int DiagnosticsWindowFontSize = 12;
    public static int PlayerHealthHoverFontSize = 10;

    /// <summary>Item Details window's own item-name row -- double FontChrome.DefaultFontSize, so the name reads as this window's real heading now that its title bar no longer carries one (see ItemDetailsWindow.BuildNameRow).</summary>
    public static int ItemDetailsNameFontSize = FontChrome.DefaultFontSize * 2;

    /// <summary>MapWindow's own fixed glyph sizes -- the tiny-entity grid, the main glyph at medium zoom, and two escalating "huge" zoom tiers.</summary>
    public static int MapTinyFontSize = 6;
    public static int MapMediumFontSize = 24;
    public static int MapLargeFontSize = 72;
    public static int MapHugeFontSize = 108;

    /// <summary>Double MapTinyFontSize -- legible at a glance without competing with the main glyph. See MapWindow's own up/down layer-occupancy badges.</summary>
    public static int MapBadgeFontSize = MapTinyFontSize * 2;

    /// <summary>Floating text over the map at Team zoom, and the smaller size used at Neighborhood zoom; Borough zoom draws none.</summary>
    public static int FloatingTextFontSize = 20;
    public static int FloatingTextNeighborhoodFontSize = 12;

    /// <summary>Icon-sized elements' own glyph font, as a fraction of their icon size -- shared by ItemIconElement and EntityIconElement, which are deliberately kept visually consistent siblings (see either's own doc comment).</summary>
    public static float IconGlyphFontFraction = 0.8f;

    public static float HotbarSlotGlyphFontFraction = 0.75f;
    public static float HotbarOverlayFontFraction = 0.3f;
    public static float HotbarCountdownFontFraction = 0.4375f;

    public static float ActionLockGlyphFontFraction = 0.75f;

    public static float PlayerStatusGlyphFontFraction = 0.75f;
    public static float PlayerStatusCountdownFontFraction = 0.6f;

    public static float DragGhostGlyphFontFraction = 0.6f;

    public static float InventoryStackIconGlyphFontFraction = 0.6f;
    public static float InventoryStackQuantityFontFraction = 0.5f;
    public static float InventoryStackBadgeFontFraction = 0.45f;

    public static float AbilityScoreModifierRowFontFraction = 0.6f;
    public static float AbilityScoreColumnNameFontFraction = 0.35f;
    public static float AbilityScoreColumnTotalFontFraction = 0.3f;

    public static float TargetShapePreviewNumberFontFraction = 0.6f;
    public static int TargetShapePreviewMinNumberFontSize = 6;
}
