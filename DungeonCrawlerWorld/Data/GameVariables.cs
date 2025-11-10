using System.Numerics;

namespace DungeonCrawlerWorld.Data
{
    public class GameVariables
    {
        public bool IsDebugMode;
        public bool IsPaused => IsAnnouncementPaused || IsUserPaused;
        public bool IsAnnouncementPaused;
        public bool IsUserPaused;
        public Vector2 GameWindowSize;
    }
}
