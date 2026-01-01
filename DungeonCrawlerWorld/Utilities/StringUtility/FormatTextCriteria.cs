using FontStashSharp;

namespace DungeonCrawlerWorld.Utilities
{
    public enum FormatTextMode
    {
        Truncate,
        Wordwrap
    }

    /// <summary>
    /// Input for the StringUtility to determine how the string should be formatted for display
    /// </summary>
    public struct FormatTextCriteria(SpriteFontBase font, float maximumPixelWidth, string originalText, FormatTextMode formatTextMode)
    {
        /// <summary>
        /// The font can be specified for a specific display or default to the standard UI font.
        /// The CharacterWidth is calculated based on the given font to determine how many words/lines fit within the maximum pixel width for a line.
        /// </summary>
        public SpriteFontBase Font { get; set; } = font;

        /// <summary>
        /// The width of a single character in pixels based on the given font.
        /// </summary>
        public float CharacterWidth { get; set; } = font.MeasureString(" ").X;

        /// <summary>
        /// The maximum width in pixels that the formatted text should occupy.
        /// Text lines exceeding this width will be either truncated or word wrapped based on the criteria settings.
        /// </summary>
        public float MaximumPixelWidth { get; set; } = maximumPixelWidth;

        /// <summary>
        /// The original text to be formatted. Readonly.
        /// </summary>
        public string OriginalText { get; set; } = originalText;

        /// <summary>
        /// WIth width of the full text in pixels based on the given font.
        /// </summary>
        public float TextWidth { get; set; } = font.MeasureString(originalText).X;

        /// <summary>
        /// Truncate : Indicates that the text should be truncated if it exceeds the maximum pixel width. Overridden by WordWrap if both are true.
        /// WordWrap : Indicates that the text should be wrapped to the next line if it exceeds the maximum pixel width.
        ///     For small textboxes, a simple word wrap is performed without hyphenation and line breaks by pixel count.
        ///     For larger textboxes, a more advanced word wrap with hyphenation and smart line breaks is used.
        /// </summary>
        public FormatTextMode FormatTextMode { get; set; } = formatTextMode;
    }
}