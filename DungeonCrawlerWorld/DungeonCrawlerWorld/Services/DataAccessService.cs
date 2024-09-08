using DungeonCrawlerWorld.Data;

namespace DungeonCrawlerWorld.Services
{
    public interface IDataAccessService
    {
        public DataAccess Connect();
    }

    public class DataAccessService : IDataAccessService
    {
        private DataAccess _dataAccess;

        public DataAccess Connect()
        {
            if ( _dataAccess == null)
            {
                _dataAccess = new DataAccess();
            }

            return _dataAccess;
        }
    }
}
