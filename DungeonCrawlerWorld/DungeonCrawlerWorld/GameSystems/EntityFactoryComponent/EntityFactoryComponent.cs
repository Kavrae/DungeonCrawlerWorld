using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;

namespace DungeonCrawlerWorld.GameComponents.EntityFactoryComponent
{
    //TODO the factory needs to call into the EntityManager to register the entity and its components.
    //  This class is purely for the logic to pseudo-randomly generate entities.
    //  Allows for the Factory to be replaced without compromising the ability to register and save entities.
    //  This requires direct references between components. Apply that reference in the GameLoop's RegisterComponents
    //  Should still call data access to create the entity
    public class EntityFactoryComponent : IGameComponent
    {
        private DataAccess _dataAccess;
        public bool CanUpdateWhilePaused { get { return false; } }

        public EntityFactoryComponent()
        {
        }

        public void Initialize()
        {
            var dataAccessService = GameServices.GetService<DataAccessService>();
            _dataAccess = dataAccessService.Connect();

            _BuildTestEntities();
        }

        public void LoadContent()
        {
        }
        public void UnloadContent() { }

        public void Draw(GameTime gameTime)
        {
        }

        public void Update( GameTime gameTime)
        {
        }

        public Entity BuildEntity<T>(EntityData entityData) where T : Entity, new()
        {
            var entity = new T
            {
                EntityData = entityData
            };
            return entity;
        }

        private void _BuildTestEntities()
        {
            var stationaryCrawler = new Crawler();
            stationaryCrawler.EntityData.Description = "This is an immovable test entity. It doesn't move. Uses the default Movable component.";
            stationaryCrawler.EntityData.Name = "Static Entity";
            stationaryCrawler.EntityData.MapPosition = new Point(13, 13);
            _dataAccess.CreateEntity(stationaryCrawler);

            var movingCrawler = new Crawler();
            movingCrawler.EntityData.Description = "This is an movable test entity. It moves randomly around the map by overriding the Movable component";
            movingCrawler.EntityData.Name = "Movable Entity";
            movingCrawler.EntityData.MapPosition = new Point(10, 10);
            movingCrawler.AddComponent(new Actionable(1, 100));
            movingCrawler.AddComponent(new Movable(MovementMode.Random, 60));
            _dataAccess.CreateEntity(movingCrawler);
        }
    }
}
