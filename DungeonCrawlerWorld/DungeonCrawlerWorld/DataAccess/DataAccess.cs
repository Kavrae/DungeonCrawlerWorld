using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonCrawlerWorld.Data
{
    //TODO add real data storage later for save/load
    //TODO split into multiple classes if this grows too large
    public class DataAccess
    {
        private Map _map;
        private List<Entity> _entities;
        private GameVariables _gameVariables;

        public DataAccess()
        {
            _entities = new List<Entity>();
            _gameVariables = new GameVariables
            {
                IsPaused = false
            };
        }

        //#### Map ####
        public void CreateMap(Map map)
        {
            _map = map;
        }

        public void UpdateMap(Map updatedMap)
        {
            _map.Size = updatedMap.Size;
            _map.Nodes = updatedMap.Nodes;
        }

        public void UpdateMapNodes(MapNode[] mapNodes)
        {
            foreach(var mapNode in mapNodes)
            {
                _map.Nodes[mapNode.Position.X, mapNode.Position.Y] = mapNode;
            }
        }

        //This should rarely be used.
        public Map RetrieveMap()
        {
            return _map;
        }

        public MapNode RetrieveMapNode(Point position)
        {
            MapNode mapNode = null;
            if (position.X <= _map.Size.X && position.Y <= _map.Size.Y)
            {
                mapNode = _map.Nodes[position.X, position.Y];
            }
            return mapNode;
        }

        public MapNode[,] RetrieveMapNodes(Rectangle retrievalArea)
        {
            var mapNodes = new MapNode[retrievalArea.Width,retrievalArea.Height];
            for ( int column = 0; column < retrievalArea.Width; column ++)
            {
                for( int row = 0; row < retrievalArea.Height; row ++)
                {
                    mapNodes[column,row] = _map.Nodes[column + retrievalArea.X, row + retrievalArea.Y];
                }
            }
            return mapNodes;
        }

        public Vector2 RetrieveMapSize()
        {
            return _map.Size;
        }

        public MapNode RetrieveSelectedNode()
        {
            return _map.SelectedMapNode;
        }

        public void SetSelectedMapNode(MapNode mapNode)
        {
            _map.SelectedMapNode = mapNode;
        }

        //#### Entities ####
        public void CreateEntity(Entity newEntity)
        {
            _entities.Add(newEntity);

            if(newEntity.EntityData.MapPosition != null)
            {
                var mapNode = RetrieveMapNode(newEntity.EntityData.MapPosition.Value);
                if(mapNode != null)
                {
                    mapNode.Entities.Add(newEntity);
                }
            }

        }

        public void UpdateEntity(Entity updatedEntity)
        {
            var entityToUpdate = _entities.FirstOrDefault(entity => entity.EntityData.Id == updatedEntity.EntityData.Id);
            if (entityToUpdate != null)
            {
                entityToUpdate = updatedEntity;
            }
        }

        public void DeleteEntity(Entity deletedEntity)
        {
            if (deletedEntity.EntityData.MapPosition != null)
            {
                var mapNode = RetrieveMapNode(deletedEntity.EntityData.MapPosition.Value);
                if (mapNode != null)
                {
                    mapNode.Entities.Remove(deletedEntity);
                }
            }

            var entityToDelete = _entities.FirstOrDefault(entity => entity.EntityData.Id == deletedEntity.EntityData.Id);
            if (entityToDelete != null)
            {
                _entities.Remove(entityToDelete);
            }
        }

        public Entity RetrieveEntity(Guid id)
        {
            return _entities.FirstOrDefault(entity => entity.EntityData.Id == id);
        }

        public List<Entity> RetrieveEntities()
        {
            return _entities;
        }


        //Note : the new position can be off the map. Which makes the new entity non-interactable, but still have a position.
        public void MoveEntity(Guid entityId,  Point newPosition)
        {
            var entityToMove = _entities.FirstOrDefault(entity => entity.EntityData.Id == entityId);
            if(entityToMove != null)
            {
                if( entityToMove.EntityData.MapPosition != null && IsOnMap(entityToMove.EntityData.MapPosition.Value))
                {
                    var originalMapNode = _map.Nodes[entityToMove.EntityData.MapPosition.Value.X, entityToMove.EntityData.MapPosition.Value.Y];
                    originalMapNode.Entities.Remove(entityToMove);
                }

                entityToMove.EntityData.MapPosition = newPosition;

                if (IsOnMap(newPosition))
                {
                    var newMapNode = _map.Nodes[newPosition.X, newPosition.Y];
                    newMapNode.Entities.Add(entityToMove);
                }
            }
        }

        public bool IsOnMap(Point point)
        {
            var mapSize = RetrieveMapSize();
            return point.X >= 0
                && point.X < mapSize.X
                && point.Y >= 0
                && point.Y < mapSize.Y;
        }

        //TODO move to data manager as logical layer on top of a simplified DataAccess layer.
        public void SelectMapNode(Point mapNodeCoordinate)
        {
            var updatedNodes = new List<MapNode>();
            var currentlySelectedNode = RetrieveSelectedNode();
            if (currentlySelectedNode != null)
            {
                currentlySelectedNode.IsSelected = false;
                updatedNodes.Add(currentlySelectedNode);
            }

            var newlySelectedNode = RetrieveMapNode(mapNodeCoordinate);
            if (newlySelectedNode != null)
            {
                newlySelectedNode.IsSelected = true;
                updatedNodes.Add(newlySelectedNode);
                SetSelectedMapNode(newlySelectedNode);
            }

            if (updatedNodes.Any())
            {
                UpdateMapNodes(updatedNodes.ToArray());
            }
        }

        //#### Game Variables ####
        public GameVariables RetrieveGameVariables() 
        { 
            return _gameVariables; 
        }

        public bool ToggleIsPaused()
        {
            _gameVariables.IsPaused = !_gameVariables.IsPaused;
            return _gameVariables.IsPaused;
        }
    }
}
