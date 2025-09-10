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

        public static int BuildFromBlueprint<T>() where T : IBlueprint, new()
        {
            var blueprint = new T();
            return blueprint.EntityId;
        }

        public static int BuildFromBlueprint<T>(Point position) where T : IBlueprint, new()
        {
            var blueprint = new T();

            var transformComponent = ComponentRepo.TransformComponents[blueprint.EntityId];
            if (transformComponent != null)
            {
                world.MoveEntity(blueprint.EntityId, new Vector3Int(position.X, position.Y, transformComponent.Value.Position.Z));
            }

            return blueprint.EntityId;
        }

        public static int BuildFromBlueprint<T>(Vector3Int position) where T : IBlueprint, new()
        {
            var blueprint = new T();

            var transformComponent = ComponentRepo.TransformComponents[blueprint.EntityId];
            if (transformComponent != null)
            {
                world.MoveEntity(blueprint.EntityId, position);
            }

            return blueprint.EntityId;
        }

        public static int BuildFromRace<T>() where T : RaceComponent, new()
        {
            var blueprint = new T();
            return blueprint.EntityId;
        }

        public static int BuildFromRace<T>(Point position) where T : RaceComponent, new()
        {
            var blueprint = new T();

            var transformComponent = ComponentRepo.TransformComponents[blueprint.EntityId];
            if( transformComponent != null)
            {
                world.MoveEntity(blueprint.EntityId, new Vector3Int(position.X, position.Y, transformComponent.Value.Position.Z));
            }

            return blueprint.EntityId;
        }

        public static int BuildFromRace<T>(Vector3Int position) where T : RaceComponent, new()
        {
            var blueprint = new T();

            var transformComponent = ComponentRepo.TransformComponents[blueprint.EntityId];
            if(transformComponent != null)
            {
                world.MoveEntity(blueprint.EntityId, position);
            }

            return blueprint.EntityId;
        }
    }
}
