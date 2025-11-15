using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    /// <summary>
    /// Window constructor options specific to the TextWindow type.
    /// </summary>
    public class TextWindowOptions : WindowOptions
    {
        /// <summary>
        /// The text content to be displayed in the TextWindow.
        /// This value will be formatted by the StringUtility before rendering.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// The color of the text to be displayed in the TextWindow.
        /// </summary>
        /// <todo>
        /// Separate header and content font colors
        /// Header background color
        /// Content background color
        /// </todo>
        public Color? TextColor { get; set; }
    }
}