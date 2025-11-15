namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    /// <summary>
    /// Represents different zoom levels for the user interface that correspend to various scopes within the game world.
    /// Defaults to Team
    /// </summary>
    /// <todo>
    /// Use this variable
    /// Larger zoom levels
    /// Each zoom level adjusts how the map works. 
    /// </todo>
    public enum ZoomLevel : byte
    {
        /// <summary>
        /// The default view that represents a team's standard line of sight and area of influence
        /// </summary>
        Team,
        /// <summary>
        /// A zoomed-out view that encompasses multiple teams within a larger Neighborhood area.
        /// </summary>
        Neighborhood,
        /// <summary>
        /// A zoomed-out view that encompasses multiple neighborhoods within a larger Borough area.
        /// </summary>
        Borough
    }
}
