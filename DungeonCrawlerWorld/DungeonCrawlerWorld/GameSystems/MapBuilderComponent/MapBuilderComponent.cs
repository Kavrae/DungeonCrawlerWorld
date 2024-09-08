using System;
using System.Collections.Generic;
using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.GameComponents.EntityFactoryComponent;
using DungeonCrawlerWorld.Services;
using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.GameComponents.MapBuilderComponent
{
    public class MapBuilderComponent : IGameComponent
    {
        public DataAccess _dataAccess;
        public bool CanUpdateWhilePaused { get { return false; } }

        public void Initialize()
        {
            var dataAccessService = GameServices.GetService<DataAccessService>();
            _dataAccess = dataAccessService.Connect();

            var mapSize = new Vector2(1000, 1000);

            var baseMap = new Map(mapSize);
            _dataAccess.CreateMap(baseMap);

            _BuildTestMap(baseMap);
        }

        public void LoadContent()
        {
        }
        public void UnloadContent() { }

        public void Update( GameTime gameTime)
        {
        }

        public void Draw(GameTime gameTime)
        {
        }

        //TODO real map
        private void _BuildTestMap(Map baseMap)
        {
            for (int column = 0; column < baseMap.Size.X; column++)
            {
                for (int row = 0; row < baseMap.Size.Y; row++)
                {
                    var mapNode = new MapNode
                    {
                        Position = new Point(column, row)
                    };

                    //Border Wall
                    var entities = new List<Entity>();
                    if(column == 0 || column == baseMap.Size.X - 1 || row == 0 || row == baseMap.Size.Y -1)
                    {
                        //TODO this is a problem. COmponents should not reference each other. Wall should exist outside of these components and they just use it.
                        entities.Add(new Wall());
                        mapNode.Terrain = new StoneFloor();
                    }

                    //Vertical hallway test
                    if( (column == 10 || column == 16) && (row < 10 || row > 16) )
                    {
                        entities.Add(new Wall());
                        mapNode.Terrain = new StoneFloor();
                    }

                    //Horizontal hallway test
                    if ( (row == 10 || row == 16) && (column < 10 || column > 16) )
                    {
                        entities.Add(new Wall());
                        mapNode.Terrain = new StoneFloor();
                    }

                    if(mapNode.Terrain == null)
                    {
                        mapNode.Terrain = new Dirt();
                    }

                    mapNode.Entities = entities;

                    baseMap.Nodes[column, row] = mapNode;
                }
            }

            _dataAccess.UpdateMap(baseMap);
        }
    }
}
