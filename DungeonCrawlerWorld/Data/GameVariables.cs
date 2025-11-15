using System.Numerics;

namespace DungeonCrawlerWorld.Data
{
    /// <summary>
    /// In-memory variables used throughout the game.
    /// </summary>
    public class GameVariables
    {
        /// <summary>
        /// Specifies that the application is in debug mode. 
        /// This changes manager behavior such as displaying full selected entity components via reflection and the debug window
        /// </summary>
        public bool IsDebugMode;

        /// <summary>
        /// When true, the game is paused and will not update most game systems or managers.
        /// </summary>
        public bool IsPaused => IsAnnouncementPaused || IsUserPaused;

        /// <summary>
        /// When true, the game will be paused by a notification regardless of the user's pause settings.
        /// This ensures that the user does not accidentally unpause the game during an unskippable System notification.
        /// </summary>
        public bool IsAnnouncementPaused;

        /// <summary>
        /// When true, the game will be paused regardless of other pause settings.
        /// </summary>
        public bool IsUserPaused;

        //TODO use gameWindowSize when user resizing is implemented.
        public Vector2 GameWindowSize;
    }
}
