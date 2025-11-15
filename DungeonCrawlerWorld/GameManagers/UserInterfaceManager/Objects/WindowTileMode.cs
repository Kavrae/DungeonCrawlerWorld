namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    /// <summary>
    /// Specifies the tiling mode for arranging child windows within a parent window.
    /// </summary>
    public enum WindowTileMode
    {
        /// <summary>
        /// Child windows are freely positioned within the parent window without any automatic arrangement.
        /// </summary>
        Floating,
        /// <summary>
        /// Child windows are arranged horizontally within the parent window.
        /// </summary>
        Horizontal,
        /// <summary>
        /// Child windows are arranged vertically within the parent window.
        /// </summary>
        Vertical
    }
}