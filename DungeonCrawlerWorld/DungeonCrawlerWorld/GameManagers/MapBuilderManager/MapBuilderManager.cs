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
    //TODO refactor this manager to start using the EntityFactoryManager for terrain too.
    public class MapBuilderManager : IGameManager
    {
        public bool CanUpdateWhilePaused { get { return false; } }

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
                       || (column == 10 || column == 16) && (row < 10 || row > 16)  //Vertical hallway
                       || (row == 10 || row == 16) && (column < 10 || column > 16) ) //Horizontal hallway
                    {
                        var wall = new Wall();
                        var wallTransform = new TransformComponent(wall.EntityId, new Vector3Int(column, row, (int)MapHeight.Standing), new Vector3Int(1,1,1));
                        world.MoveEntity(wall.EntityId, wallTransform.Position);

                        var stoneFloor = new StoneFloor();
                        var stoneFloorTransform = new TransformComponent(stoneFloor.EntityId, new Vector3Int(column, row, (int)MapHeight.Ground), new Vector3Int(1, 1, 1));
                        world.MoveEntity(stoneFloor.EntityId, stoneFloorTransform.Position);
                    }
                    else
                    {
                        var dirt = new Dirt();
                        var dirtTransform = new TransformComponent(dirt.EntityId, new Vector3Int(column, row, (int)MapHeight.Ground), new Vector3Int(1, 1, 1));
                        world.MoveEntity(dirt.EntityId, dirtTransform.Position);

                        if( (row-1) %10 == 0 && (column-1) %100 == 0) //1,000 Crawlers
                        {
                            var crawler = new Crawler();
                            var crawlerTransform = new TransformComponent(crawler.EntityId, new Vector3Int(column, row, (int)MapHeight.Standing), new Vector3Int(1, 1, 1));
                            _ = new MovementComponent(crawler.EntityId, MovementMode.Random, 20);
                            _ = new EnergyComponent(crawler.EntityId, (short)randomizer.Next(0, 100), 1, 100);
                            world.MoveEntity(crawler.EntityId, crawlerTransform.Position);
                        }
                        if ((row - 5) % 5 == 0 && (column - 1) % 5 == 0) //40,000 Goblins
                        {
                            var goblin = new Goblin();
                            var goblinTransform = new TransformComponent(goblin.EntityId, new Vector3Int(column, row, (int)MapHeight.Standing), new Vector3Int(1, 1, 1));
                            _ = new MovementComponent(goblin.EntityId, MovementMode.Random, 20);
                            _ = new EnergyComponent(goblin.EntityId, (short)randomizer.Next(0, 100), 2, 30);
                            world.MoveEntity(goblin.EntityId, goblinTransform.Position);
                        }
                    }
                }
            }

            //Large crawler test
            var largeCrawler = new Crawler();
            var largeCrawlerTransform = new TransformComponent(largeCrawler.EntityId, new Vector3Int(2, 2, (int)MapHeight.Standing), new Vector3Int(2, 2, 1));
            _ = new EnergyComponent(largeCrawler.EntityId, 0, 1, 100);
            _ = new MovementComponent(largeCrawler.EntityId, MovementMode.Random, 40);
            _ = new DisplayTextComponent(largeCrawler.EntityId, "Large Crawler", "Should take up 2x2 tiles");
            _ = new GlyphComponent(largeCrawler.EntityId, "Q", Color.Red, new Point(0, 0));
            world.MoveEntity(largeCrawler.EntityId, largeCrawlerTransform.Position);

            //Huge crawler test
            var hugeCrawler = new Crawler();
            var hugeCrawlerTransform = new TransformComponent(hugeCrawler.EntityId, new Vector3Int(6, 6, (int)MapHeight.Standing), new Vector3Int(3, 3, 1));
            _ = new EnergyComponent(hugeCrawler.EntityId, 50, 1, 100);
            _ = new MovementComponent(hugeCrawler.EntityId, MovementMode.Random, 40);
            _ = new DisplayTextComponent(hugeCrawler.EntityId, "Huge Crawler", "Should take up 3x3 tiles" );
            world.MoveEntity(hugeCrawler.EntityId, hugeCrawlerTransform.Position);

            //stationary Fairy test
            var stationaryFairy = new Fairy();
            var stationaryFairyTransform = new TransformComponent(stationaryFairy.EntityId, new Vector3Int(0, 0, (int)MapHeight.Flying), new Vector3Int(1, 1, 1));
            _ = new MovementComponent(stationaryFairy.EntityId, MovementMode.Stationary, 100);
            _ = new DisplayTextComponent(stationaryFairy.EntityId, "Stationary Fairy", "This fairy is lazy and refuses to move.");
            world.MoveEntity(stationaryFairy.EntityId, stationaryFairyTransform.Position);

            //Moving Fairy test
            var fairy = new Fairy();
            var fairyTransform = new TransformComponent(fairy.EntityId, new Vector3Int(17, 16, (int)MapHeight.Flying), new Vector3Int(1, 1, 1));
            world.MoveEntity(fairy.EntityId, fairyTransform.Position);
        }
    }
}
