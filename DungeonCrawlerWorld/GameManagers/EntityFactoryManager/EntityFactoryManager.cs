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

        public void Update(GameTime gameTime, GameVariables gameVariables)
        {
        }

        public static Guid BuildFromBlueprint<T>() where T : IBlueprint, new()
        {
            var blueprint = new T();
            return blueprint.EntityId;
        }

        public static Guid BuildFromBlueprint<T>(Point position) where T : IBlueprint, new()
        {
            var blueprint = new T();

            if (ComponentRepo.TransformComponents.TryGetValue(blueprint.EntityId, out TransformComponent transformComponent))
            {
                world.MoveEntity(blueprint.EntityId, new Vector3Int(position.X, position.Y, transformComponent.Position.Z));
            }

            return blueprint.EntityId;
        }

        public static Guid BuildFromBlueprint<T>(Vector3Int position) where T : IBlueprint, new()
        {
            var blueprint = new T();

            if (ComponentRepo.TransformComponents.TryGetValue(blueprint.EntityId, out TransformComponent transformComponent))
            {
                world.MoveEntity(blueprint.EntityId, position);
            }

            return blueprint.EntityId;
        }

        public static Guid BuildFromRace<T>() where T : RaceComponent, new()
        {
            var blueprint = new T();
            return blueprint.EntityId;
        }

        public static Guid BuildFromRace<T>(Point position) where T : RaceComponent, new()
        {
            var blueprint = new T();

            if (ComponentRepo.TransformComponents.TryGetValue(blueprint.EntityId, out TransformComponent transformComponent))
            {
                world.MoveEntity(blueprint.EntityId, new Vector3Int(position.X, position.Y, transformComponent.Position.Z));
            }

            return blueprint.EntityId;
        }

        public static Guid BuildFromRace<T>(Vector3Int position) where T : RaceComponent, new()
        {
            var blueprint = new T();

            if (ComponentRepo.TransformComponents.TryGetValue(blueprint.EntityId, out TransformComponent transformComponent))
            {
                world.MoveEntity(blueprint.EntityId, position);
            }

            return blueprint.EntityId;
        }
    }
}
