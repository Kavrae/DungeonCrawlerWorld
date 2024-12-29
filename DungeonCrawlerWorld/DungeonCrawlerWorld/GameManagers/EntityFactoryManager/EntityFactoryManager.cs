using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class EntityFactoryManager : IGameManager
    {
        public bool CanUpdateWhilePaused => false;

        private World _dataAccess;

        public EntityFactoryManager()
        {
        }

        public void Initialize()
        {
            var dataAccessService = GameServices.GetService<DataAccessService>();
            _dataAccess = dataAccessService.RetrieveWorld();

            //_BuildTestEntities();
        }

        public void LoadContent()
        {
        }
        public void UnloadContent() { }

        public void Draw(GameTime gameTime)
        {
        }

        public void Update( GameTime gameTime, GameVariables gameVariables)
        {
        }

        //TODO next point of upgrade here
        //Is there a way to provide common "options" for an entity build?  EntityOptions class?
        //Use the templates to start, then apply options. Can replace components individually as needed
        /*
        private Entity BuildEntityFromTemplate<T>(Point position, Size size)
        {
            var entity = new T();
            foreach (var component in components)
            {
                entity.AddComponent(component);
            }
            _dataAccess.CreateEntity(entity, position, size);
            return entity;
        }

        private void _BuildTestEntities()
        {
            var defaultCrawler = new Crawler();
            _dataAccess.CreateEntity(defaultCrawler, new Point(0,0 ), Size.Medium);

            var stationaryCrawler = new Crawler();
            stationaryCrawler.AddComponent(new DisplayText { Name = "Static Entity", Description = "This is an immovable test entity. It doesn't move. Uses the default Movable component." });
            stationaryCrawler.RemoveComponent<Movement>();
            _dataAccess.CreateEntity(stationaryCrawler, new Point(11,11), Size.Medium);

            var movingCrawler = new Crawler();
            movingCrawler.AddComponent(new DisplayText { Name = "Movable Entity", Description = "This is an movable test entity. It moves randomly around the map by overriding the Movable component." });
            movingCrawler.AddComponent(new Movement { MovementMode = MovementMode.Random, EnergyToMove = 60 });
            _dataAccess.CreateEntity(stationaryCrawler, new Point(12,2), Size.Medium);
        }*/
    }
}
