using FontStashSharp;
using System.Collections.Generic;

namespace DungeonCrawlerWorld.Utilities
{
    /// <summary>
    /// Input for the StringUtility to determine how the string should be formatted for display
    /// </summary>
    public class FormatTextCriteria
    {
        /// <summary>
        /// The font can be specified for a specific display or default to the standard UI font.
        /// The CharacterWidth is calculated based on the given font to determine how many words/lines fit within the maximum pixel width for a line.
        /// </summary>
        private SpriteFontBase _font;
        public SpriteFontBase Font
        {
            get
            {
                return _font;
            }
            set
            {
                _font = value;
                CharacterWidth = _font.MeasureString(" ").X;
            }
        }

        /// <summary>
        /// The width of a single character in pixels based on the given font.
        /// </summary>
        public float CharacterWidth { get; set; }

        /// <summary>
        /// The maximum width in pixels that the formatted text should occupy.
        /// Text lines exceeding this width will be either truncated or word wrapped based on the criteria settings.
        /// </summary>
        public float MaximumPixelWidth { get; set; }

        /// <summary>
        /// The original text to be formatted. Readonly.
        /// </summary>
        public string OriginalText { get; }

        /// <summary>
        /// The original text to be formatted after processing newline characters.
        /// This allows callers to manually specify required line breaks while still utilizing the StringUtility's formatting.
        /// </summary>
        public List<string> TextLinesToFormat { get; set; }
        
        /// <summary>
        /// Indicates whether the text should be truncated if it exceeds the maximum pixel width. Overridden by WordWrap if both are true.
        /// </summary>
        public bool Truncate { get; set; }

        /// <summary>
        /// Indicates whether the text should be wrapped to the next line if it exceeds the maximum pixel width.
        /// For small textboxes, a simple word wrap is performed without hyphenation and line breaks by pixel count.
        /// For larger textboxes, a more advanced word wrap with hyphenation and smart line breaks is used.
        /// </summary>
        public bool WordWrap { get; set; }

        public FormatTextCriteria(string originalText)
        {
            OriginalText = originalText;
            TextLinesToFormat = new List<string>();
            Truncate = false;
            WordWrap = false;
        }
    }
}