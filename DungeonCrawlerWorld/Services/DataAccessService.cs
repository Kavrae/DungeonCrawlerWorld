using DungeonCrawlerWorld.Data;

namespace DungeonCrawlerWorld.Services
{
    public interface IDataAccessService
    {
        public World RetrieveWorld();
    }

    /// <summary>
    /// Temporary service used by game managers and systems to retrieve and store game data.
    /// </summary>
    /// <todo>
    /// This service will eventually be replaced by a more robust data persistence solution.
    /// </todo>
    public class DataAccessService : IDataAccessService
    {
        private GameVariables _gameVariables;
        private World _world;

        /// <summary>
        /// In-memory game variables needed for quick access by various game systems and managers.
        /// </summary>
        public GameVariables RetrieveGameVariables()
        {
            _gameVariables ??= new GameVariables
            {
                IsDebugMode = true,
                IsAnnouncementPaused = false,
                IsUserPaused = false,
            };

            return _gameVariables;
        }

        /// <summary>
        /// Retrieves the current game world instance.
        /// </summary>
        public World RetrieveWorld()
        {
            _world ??= new World();

            return _world;
        }
    }
}
