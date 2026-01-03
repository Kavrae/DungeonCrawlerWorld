using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Data.Blueprints.Classes;
using DungeonCrawlerWorld.Data.Blueprints.Npcs;
using DungeonCrawlerWorld.Data.Blueprints.Objects;
using DungeonCrawlerWorld.Data.Blueprints.Races;
using DungeonCrawlerWorld.Data.Blueprints.Terrain;
using DungeonCrawlerWorld.Services;
using DungeonCrawlerWorld.Utilities;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace DungeonCrawlerWorld.GameManagers.MapBuilderManager
{
    /// <summary>
    /// Manages the construction and layout of the game map.
    /// </summary> 
    /// <todo>
    /// Actual map generation algorithms.
    /// Retrieve currently explored map via saved game data
    /// Set map size via configuration
    /// </todo>
    public class MapBuilderManager : IGameManager
    {
        public bool CanUpdateWhilePaused => false;

        private Random randomizer;
        private World world;

        public void Initialize()
        {
            randomizer = new Random();

            var dataAccessService = GameServices.GetService<DataAccessService>();
            world = dataAccessService.RetrieveWorld();

            var mapSize = new Vector3Int(2000, 1000, (int)Enum.GetValues(typeof(MapHeight)).Cast<MapHeight>().Max() + 1);

            world.Map = new Map(mapSize);

            _BuildTestMap();
        }

        public void LoadContent()
        {
        }
        public void UnloadContent() { }

        public void Update(GameTime gameTime, GameVariables gameVariables)
        {
        }

        public void Draw(GameTime gameTime)
        {
        }

        /// <summary>
        /// A temporary map for testing various game improvements and performance
        /// </summary>
        private void _BuildTestMap()
        {
            for (int column = 0; column < world.Map.Size.X; column++)
            {
                for (int row = 0; row < world.Map.Size.Y; row++)
                {
                    if ((column == 0 || column == world.Map.Size.X - 1 || row == 0 || row == world.Map.Size.Y - 1) //Border Wall
                       || ((column == 10 || column == 16) && (row < 10 || row > 16))  //Vertical hallway
                       || ((row == 10 || row == 16) && (column < 10 || column > 16))) //Horizontal hallway
                    {
                        EntityFactoryManager.EntityFactoryManager.BuildFromBlueprint<StoneFloor>(new Vector3Int(column, row, (int)MapHeight.Ground));
                        EntityFactoryManager.EntityFactoryManager.BuildFromBlueprint<Wall>(new Vector3Int(column, row, (int)MapHeight.Standing));
                    }
                    else
                    {
                        EntityFactoryManager.EntityFactoryManager.BuildFromBlueprint<Dirt>(new Vector3Int(column, row, (int)MapHeight.Ground));

                        if ((row - 1) % 10 == 0 && (column - 1) % 100 == 0) //2,000 goblin engineers
                        {
                            EntityFactoryManager.EntityFactoryManager.BuildFromBlueprint<GoblinEngineerBlueprint>(new Vector3Int(column, row, (int)MapHeight.Standing));
                        }
                        if ((row - 5) % 5 == 0 && (column - 1) % 2 == 0) //200,000 Goblins
                        {
                            EntityFactoryManager.EntityFactoryManager.BuildFromBlueprint<Goblin>(new Vector3Int(column, row, (int)MapHeight.Standing));
                        }
                    }
                }
            }

            //Large goblin test
            var largeGoblinEntityId = EntityFactoryManager.EntityFactoryManager.BuildFromBlueprint<Goblin>();
            var largeGoblinPosition = new Vector3Int(2, 2, (int)MapHeight.Standing);
            var largeGoblinTransformComponent = new TransformComponent(largeGoblinPosition, new Vector3Byte(2, 2, 1));
            ComponentRepo.SaveTransformComponent(largeGoblinEntityId, largeGoblinTransformComponent, ComponentSaveMode.Overwrite);
            var largeGoblinDisplayText = ComponentRepo.DisplayTextComponents[largeGoblinEntityId].Value;
            largeGoblinDisplayText.Description = "ThisIsAReallyLongDescriptionToTestTheWordWrapCapabilitiesAroundHyphenatingLongWordsMultipleTimes";
            ComponentRepo.SaveDisplayTextComponent(largeGoblinEntityId, largeGoblinDisplayText, ComponentSaveMode.Overwrite);
            world.PlaceEntityOnMap(largeGoblinEntityId, largeGoblinPosition, largeGoblinTransformComponent);

            //Huge goblin test
            var hugeGoblinEntityId = EntityFactoryManager.EntityFactoryManager.BuildFromBlueprint<GoblinEngineerBlueprint>();
            var hugeGoblinPosition = new Vector3Int(5, 5, (int)MapHeight.Standing);
            var hugeGoblinTransformComponent = new TransformComponent(hugeGoblinPosition, new Vector3Byte(3, 3, 1));
            ComponentRepo.SaveTransformComponent(hugeGoblinEntityId, hugeGoblinTransformComponent, ComponentSaveMode.Overwrite);
            world.PlaceEntityOnMap(hugeGoblinEntityId, hugeGoblinPosition, hugeGoblinTransformComponent);

            //Stationary Fairy engineer test
            var fairyEngineerId = EntityFactoryManager.EntityFactoryManager.BuildFromBlueprint<Fairy>(new Vector3Int(1, 1, (int)MapHeight.Floating));
            Engineer.Build(fairyEngineerId);
            ComponentRepo.RemoveMovementComponent(fairyEngineerId);

            //Moving Fairy test
            EntityFactoryManager.EntityFactoryManager.BuildFromBlueprint<Fairy>(new Vector3Int(17, 16, (int)MapHeight.Flying));

            //Multiple races
            var goblinFairyId = EntityFactoryManager.EntityFactoryManager.BuildFromBlueprint<Goblin>(new Vector3Int(17, 9, (int)MapHeight.Flying));
            ComponentRepo.RemoveMovementComponent(goblinFairyId);
            Fairy.Build(goblinFairyId);

            //Multiple class
            var tankEngineerId = EntityFactoryManager.EntityFactoryManager.BuildFromBlueprint<Goblin>(new Vector3Int(11, 2, (int)MapHeight.Standing));
            Engineer.Build(tankEngineerId);
            Tank.Build(tankEngineerId);
        }
    }
}
