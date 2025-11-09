using DungeonCrawlerWorld.Data;

namespace DungeonCrawlerWorld.Services
{
    public interface IDataAccessService
    {
        public World RetrieveWorld();
    }

    public class DataAccessService : IDataAccessService
    {
        private GameVariables _gameVariables;
        private World _world;

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

        public World RetrieveWorld()
        {
            _world ??= new World();

            return _world;
        }
    }
}
