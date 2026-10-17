namespace UIEngine.UI.Chrome;

/// <summary>The window framework's font-size constants -- both fixed point sizes and the per-widget size-fraction
/// multipliers passed to FontService.GetFont. The game's own windows keep theirs in GameFontChrome. Plain mutable
/// fields rather than readonly/const, as WindowPalette's colours: a future data-driven theme loader needs to be able
/// to overwrite them at startup.</summary>
public static class FontChrome
{
    /// <summary>The base/default font size for ordinary body-style text with no dynamic per-widget scaling of its own -- TextWindow/Button's own pooled-default ContentFont, ContextMenu's option text and TextDivider's default label size.</summary>
    public static int DefaultFontSize = 12;

    /// <summary>Window's own title-bar text -- larger than DefaultFontSize since small title text read as illegible/oddly-hinted at 12px. Window's header height is measured directly off this font's own line height (see Window's OriginalSize/RecalculateMinimizedSize), so raising this alone grows the header to fit -- no separate header-height constant to keep in sync.</summary>
    public static int WindowTitleFontSize = 16;

    public static int CursorTextFontSize = 14;

    public static float FolderFallbackGlyphFontFraction = 0.6f;
    public static float GridControlLabelFontFraction = 0.6f;
    public static float TabHeaderLabelFontFraction = 0.6f;

    /// <summary>Button.HotkeyLabel's font size, as a fraction of the button's height; a host drawing key labels over its own icons uses the same fraction so the two read alike.</summary>
    public static float ButtonHotkeyLabelFontFraction = 0.3f;

    /// <summary>The "current / maximum" text on a resource bar, as a fraction of the drawn bar's height.</summary>
    public static float ResourceBarValueFontFraction = 0.85f;
}
