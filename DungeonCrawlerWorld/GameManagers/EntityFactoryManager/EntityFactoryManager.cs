using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;
using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Utilities;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    /// <summary>
    /// Manages the creation of game entities from various blueprints and races.
    /// Use the BuildFromBlueprint and BuildFromRace static methods to create entities.
    /// </summary>
    /// <todo>
    /// Modifiers
    /// MoveEntity vs PlaceEntity separation.
    /// </todo>
    public class EntityFactoryManager : IGameManager
    {
        /// <summary>
        /// Updates are not currently relevant to this factory
        /// </summary>
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

        /// <summary>
        /// Creates an entity from a blueprint without specifying a position.
        /// This is primarily used to create persistent entities that have not yet been spawned into the map.
        /// </summary>
        public static int BuildFromBlueprint<T>() where T : IBlueprint, new()
        {
            var blueprint = new T();
            return blueprint.EntityId;
        }

        /// <summary>
        /// Creates an entity from a blueprint while specifying a 2d position.
        /// The Z axis will default to the blueprint's default map height transform value.
        /// This is primarily used to immediately spawn new entities onto the map.
        /// </summary>
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


        /// <summary>
        /// Creates an entity from a blueprint while specifying a 3d position.
        /// This is primarily used to immediately spawn new entities onto the map at a non-standard height.
        /// </summary>
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

        /// <summary>
        /// Creates an entity from a race without specifying a position.
        /// This is primarily used to create persistent simple entities that have not yet been spawned into the map.
        /// </summary>
        public static int BuildFromRace<T>() where T : RaceComponent, new()
        {
            var blueprint = new T();
            return blueprint.EntityId;
        }

        /// <summary>
        /// Creates an entity from a race while specifying a 2d position.
        /// The Z axis will default to the race's default map height transform value.
        /// This is primarily used to immediately spawn new entities onto the map.
        /// </summary>
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

        /// <summary>
        /// Creates an entity from a race while specifying a 3d position.
        /// This is primarily used to immediately spawn new entities onto the map at a non-standard height.
        /// </summary>
        public static int BuildFromRace<T>(Vector3Int position) where T : RaceComponent, new()
        {
            var blueprint = new T();

            var transformComponent = ComponentRepo.TransformComponents[blueprint.EntityId];
            if (transformComponent != null)
            {
                world.MoveEntity(blueprint.EntityId, position);
            }

            return blueprint.EntityId;
        }
    }
}
