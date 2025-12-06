namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    /// <summary>
    /// Defines the display modes for windows. This is set in the constructor via WindowOptions.
    /// Each mode determines how the window sizes itself relative to its content and parent container.
    /// </summary>
    /// <todo>
    /// Expand display modes to include GrowHorizontal, GrowVertical, and GrowAll
    /// </todo>
    public enum WindowDisplayMode
    {
        /// <summary>
        /// The window is minimized. If ShowTitleWhenMinimized is set, hide the WindowContents and display the title bar regardless of other visibility settings.
        /// </summary>
        Minimized,
        /// <summary>
        /// The window size is static and does not change based on content or parent container.
        /// The specified size will be used to determine text formatting.
        /// </summary>
        Static,
        /// <summary>
        /// The window expands to fill the parent container's available content space.
        /// The resulting size will be used to determine text formatting.
        /// </summary>
        Fill,
        /// <summary>
        /// The window grows to fit its content, up to the maximum size of the parent container or a specified maximum size.
        /// </summary>
        Grow
    }
}