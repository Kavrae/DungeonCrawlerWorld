using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;
using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Utilities;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class EntityFactoryManager : IGameManager
    {
        public bool CanUpdateWhilePaused => false;

        private static World world;

        public EntityFactoryManager()
        {
        }

        public void Initialize()
        {
            var dataAccessService = GameServices.GetService<DataAccessService>();
            world = dataAccessService.RetrieveWorld();
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

        //TODO entity deletion that removes all components via entityId.

        public static Guid Build<T>() where T : IBaseFactoryTemplate, new()
        {
            var entityId = Guid.NewGuid();
            var template = new T();
            template.Build(entityId);

            return entityId;
        }

        public static Guid Build<T>(Point position) where T : IBaseFactoryTemplate, new()
        {
            var entityId = Guid.NewGuid();
            var template = new T();
            template.Build(entityId);

            if(ComponentRepo.TransformComponents.TryGetValue(entityId, out TransformComponent transformComponent))
            {
                world.MoveEntity(entityId, new Vector3Int(position.X, position.Y, transformComponent.Position.Z));
            }

            return entityId;
        }

        public static Guid Build<T>(Vector3Int position) where T : IBaseFactoryTemplate, new()
        {
            var entityId = Guid.NewGuid();
            var template = new T();
            template.Build(entityId);

            if (ComponentRepo.TransformComponents.TryGetValue(entityId, out TransformComponent transformComponent))
            {
                world.MoveEntity(entityId, position);
            }

            return entityId;
        }

        public static void Apply<T>(Guid entityId) where T : IModifierFactoryTemplate, new()
        {
            var template = new T();
            template.Apply(entityId);
        }
    }
}
