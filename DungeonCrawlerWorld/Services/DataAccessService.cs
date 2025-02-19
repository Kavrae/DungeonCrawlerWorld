using DungeonCrawlerWorld.Data;

namespace DungeonCrawlerWorld.Services
{
    public interface IDataAccessService
    {
        public World RetrieveWorld();
    }

    public class DataAccessService : IDataAccessService
    {
        private World _World;

        public World RetrieveWorld()
        {
            _World ??= new World();

            return _World;
        }
    }
}
