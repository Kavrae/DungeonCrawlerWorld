using System;
using System.Linq;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.GameManagers.EntityFactoryManager;
using DungeonCrawlerWorld.Services;
using DungeonCrawlerWorld.Utilities;

namespace DungeonCrawlerWorld.GameManagers.MapBuilderManager
{
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

            var mapSize = new Vector3Int(1000, 1000, (int)Enum.GetValues(typeof(MapHeight)).Cast<MapHeight>().Max() + 1);

            world.Map = new Map(mapSize);

            _BuildTestMap();
        }

        public void LoadContent()
        {
        }
        public void UnloadContent() { }

        public void Update( GameTime gameTime, GameVariables gameVariables)
        {
        }

        public void Draw(GameTime gameTime)
        {
        }
        
        private void _BuildTestMap()
        {
            for (int column = 0; column < world.Map.Size.X; column++)
            {
                for (int row = 0; row < world.Map.Size.Y; row++)
                {
                    if( (column == 0 || column == world.Map.Size.X - 1 || row == 0 || row == world.Map.Size.Y -1) //Border Wall
                       || ((column == 10 || column == 16) && (row < 10 || row > 16))  //Vertical hallway
                       || ((row == 10 || row == 16) && (column < 10 || column > 16)) ) //Horizontal hallway
                    {
                        EntityFactoryManager.EntityFactoryManager.Build<StoneFloor>(new Vector3Int(column, row, (int)MapHeight.Ground));
                        EntityFactoryManager.EntityFactoryManager.Build<Wall>(new Vector3Int(column, row, (int)MapHeight.Standing));
                    }
                    else
                    {
                        EntityFactoryManager.EntityFactoryManager.Build<Dirt>(new Vector3Int(column, row, (int)MapHeight.Ground));

                        if ( (row-1) %10 == 0 && (column-1) %100 == 0) //1,000 goblin engineers
                        {
                            EntityFactoryManager.EntityFactoryManager.Build<GoblinEngineerBlueprint>(new Vector3Int(column, row, (int)MapHeight.Standing));
                        }
                        if ((row - 5) % 5 == 0 && (column - 1) % 5 == 0) //40,000 Goblins
                        {
                            EntityFactoryManager.EntityFactoryManager.Build<Goblin>(new Vector3Int(column, row, (int)MapHeight.Standing));
                        }
                    }
                }
            }

            //Large goblin test
            var largeGoblinEntityId = EntityFactoryManager.EntityFactoryManager.Build<Goblin>();
            var largeCrawlerTransform = new TransformComponent(largeGoblinEntityId, new Vector3Int(2, 2, (int)MapHeight.Standing), new Vector3Int(2, 2, 1));
            world.MoveEntity(largeGoblinEntityId, largeCrawlerTransform.Position);
            
            //Huge goblin test
            var hugeGoblinEntityId = EntityFactoryManager.EntityFactoryManager.Build<Goblin>();
            var hugeCrawlerTransform = new TransformComponent(hugeGoblinEntityId, new Vector3Int(5, 5, (int)MapHeight.Standing), new Vector3Int(3, 3, 1));
            world.MoveEntity(hugeGoblinEntityId, hugeCrawlerTransform.Position);

            //Stationary Fairy engineer test
            var fairyEngineerId = EntityFactoryManager.EntityFactoryManager.Build<Fairy>(new Vector3Int(1,1, (int)MapHeight.Floating));
            EntityFactoryManager.EntityFactoryManager.Apply<Engineer>(fairyEngineerId);
            ComponentRepo.MovementComponents.Remove(fairyEngineerId);
            _ = new DisplayTextComponent(fairyEngineerId, "Stationary Fairy", "This fairy is lazy and refuses to move.");

            //Moving Fairy test
            EntityFactoryManager.EntityFactoryManager.Build<Fairy>(new Vector3Int(17,16, (int)MapHeight.Flying));
        }
    }
}
