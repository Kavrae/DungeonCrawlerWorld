using System;

using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Utilities;

namespace DungeonCrawlerWorld.Data
{
    public class World
    {
        public Map Map { get; set; }

        public GameVariables _GameVariables{ get; set; }

        public MapNode[] SelectedMapNodes { get; set; }

        public World()
        {
            Map = new Map( new Vector3Int(0,0,0) );
            _GameVariables = new GameVariables
            {
                IsDebugMode = true,
                IsPaused = false
            };
        }

        public GameVariables RetrieveGameVariables() 
        { 
            return _GameVariables; 
        }

        public bool ToggleIsPaused()
        {
            _GameVariables.IsPaused = !_GameVariables.IsPaused;
            return _GameVariables.IsPaused;
        }

        public void MoveEntity(Guid entityId, Vector3Int newPosition)
        {
            if (IsOnMap(newPosition))
            {
                if (ComponentRepo.TransformComponents.TryGetValue(entityId, out TransformComponent transformComponent))
                {
                    if (IsOnMap(transformComponent.Position))
                    {
                        for (var x = transformComponent.Position.X; x < transformComponent.Position.X + transformComponent.Size.X; x++)
                        {
                            for (var y = transformComponent.Position.Y; y < transformComponent.Position.Y + transformComponent.Size.Y; y++)
                            {
                                var mapNode = Map.MapNodes[x, y, transformComponent.Position.Z];
                                mapNode.EntityId = null;
                                mapNode.HasChanged = true;
                                Map.MapNodes[x, y, transformComponent.Position.Z] = mapNode;
                            }
                        }
                    }

                    for (var x = newPosition.X; x < newPosition.X + transformComponent.Size.X; x++)
                    {
                        for (var y = newPosition.Y; y < newPosition.Y + transformComponent.Size.Y; y++)
                        {
                            var mapNode = Map.MapNodes[x, y, newPosition.Z];
                            if (mapNode.EntityId != entityId)
                            {
                                mapNode.EntityId = entityId;
                                mapNode.HasChanged = true;
                            }
                            Map.MapNodes[x, y, newPosition.Z] = mapNode;
                        }
                    }

                    transformComponent.Position = newPosition;
                    ComponentRepo.TransformComponents[transformComponent.EntityId] = transformComponent;
                }
            }
        }

        public bool IsOnMap(Vector3Int coordinates)
        {
            return coordinates.X >= 0 && coordinates.Y >= 0 && coordinates.Z >= 0
                && coordinates.X < Map.Size.X && coordinates.Y < Map.Size.Y && coordinates.Z < Map.Size.Z;
        }

        public bool IsOnMap(CubeInt cube)
        {
            return IsOnMap(cube.Position) && IsOnMap(cube.Position + cube.Size);
        }
    }
}
