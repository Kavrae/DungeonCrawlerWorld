using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;
using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    //TODO the factory needs to call into the EntityManager to register the entity and its components.
    //  This class is purely for the logic to pseudo-randomly generate entities.
    //  Allows for the Factory to be replaced without compromising the ability to register and save entities.
    //  This requires direct references between components. Apply that reference in the GameLoop's RegisterComponents
    //  Should still call data access to create the entity
    public class EntityFactoryManager : IGameManager
    {
        public bool CanUpdateWhilePaused { get { return false; } }

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

        /*
        private Entity BuildEntityFromTemplate<T>(Point position, Size size, params Component[] components) where T : Entity, new()
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
