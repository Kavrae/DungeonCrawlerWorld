namespace Presentation.UI.Chrome;

/// <summary>Font-size constants -- both fixed point sizes and the per-widget size-fraction
/// multipliers passed to FontService.GetFont -- collected from across Presentation's own
/// windows/content, mirroring WindowPalette's own "one field per widget's own value, even where
/// nothing else reuses it" precedent for colors and HudChrome's for positions/sizes. See either's
/// own doc comment for why these are plain mutable fields rather than readonly/const: a future
/// data-driven theme loader needs to be able to overwrite them at startup.</summary>
public static class FontChrome
{
    /// <summary>The base/default font size for ordinary body-style text with no dynamic per-widget scaling of its own -- TextWindow/Button's own pooled-default ContentFont, ContextMenu's option text, HealthWindow's body rows, and TextDivider's default label size.</summary>
    public static int DefaultFontSize = 12;

    /// <summary>Window's own title-bar text -- larger than DefaultFontSize since small title text read as illegible/oddly-hinted at 12px. Window's header height is measured directly off this font's own line height (see Window's OriginalSize/RecalculateMinimizedSize), so raising this alone grows the header to fit -- no separate header-height constant to keep in sync.</summary>
    public static int WindowTitleFontSize = 16;

    public static int DebugWindowFontSize = 8;
    public static int PlayerHealthHoverFontSize = 10;
    public static int CursorTextFontSize = 14;

    /// <summary>Item Details window's own item-name row -- double DefaultFontSize, so the name reads as this window's real heading now that its title bar no longer carries one (see ItemDetailsWindow.BuildNameRow).</summary>
    public static int ItemDetailsNameFontSize = DefaultFontSize * 2;

    /// <summary>MapWindow's own fixed glyph sizes -- the tiny-entity grid, the main glyph at medium zoom, and two escalating "huge" zoom tiers.</summary>
    public static int MapTinyFontSize = 6;
    public static int MapMediumFontSize = 24;
    public static int MapLargeFontSize = 72;
    public static int MapHugeFontSize = 108;

    /// <summary>Double MapTinyFontSize -- legible at a glance without competing with the main glyph. See MapWindow's own up/down layer-occupancy badges.</summary>
    public static int MapBadgeFontSize = MapTinyFontSize * 2;

    /// <summary>Icon-sized elements' own glyph font, as a fraction of their icon size -- shared by ItemIconElement and EntityIconElement, which are deliberately kept visually consistent siblings (see either's own doc comment).</summary>
    public static float IconGlyphFontFraction = 0.8f;

    public static float FolderFallbackGlyphFontFraction = 0.6f;
    public static float GridControlLabelFontFraction = 0.6f;
    public static float TabHeaderLabelFontFraction = 0.6f;

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
