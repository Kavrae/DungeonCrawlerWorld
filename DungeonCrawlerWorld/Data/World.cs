using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Utilities;
using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.Data
{
    /// <summary>
    /// Represents the in-memory game world, including the map and entities within it.
    /// </summary>
    public class World
    {
        /// <summary>
        /// The current map locations and data loaded into memory
        /// </summary>
        public Map Map { get; set; }
        private Vector3Byte TransformSize1 = new(1, 1, 1);

        /// <summary>
        /// The position of the currently selected map node, if any, using the 2d Map coordinates.
        /// This selection includes all mapNodes on the z axis for those coordinates.
        /// Selection is generally done via the user clicking on a map tile or reference to a map tile.
        /// </summary>
        public Point? SelectedMapNodePosition { get; set; }

        //Working variables
        int xCoordinate;
        int yCoordinate;
        int zCoordinate;

        public World()
        {
            Map = new Map(new Vector3Int(0, 0, 0));
        }

        /// <summary>
        /// MoveEntity is assumed to always have valid starting and ending positions, unlike Place and Remove.
        /// </summary>
        public void MoveEntity(int entityId, Vector3Int newPosition, TransformComponent transformComponent)
        {
            if (transformComponent.Size == TransformSize1)
            {
                Map.MapNodes[transformComponent.Position.X, transformComponent.Position.Y, transformComponent.Position.Z].EntityId = -1;
                Map.MapNodes[newPosition.X, newPosition.Y, newPosition.Z].EntityId = entityId;
            }
            else
            {
                zCoordinate = transformComponent.Position.Z;

                for (xCoordinate = transformComponent.Position.X; xCoordinate < transformComponent.Position.X + transformComponent.Size.X; xCoordinate++)
                {
                    for (yCoordinate = transformComponent.Position.Y; yCoordinate < transformComponent.Position.Y + transformComponent.Size.Y; yCoordinate++)
                    {
                        Map.MapNodes[xCoordinate, yCoordinate, zCoordinate].EntityId = -1;
                    }
                }

                zCoordinate = newPosition.Z;
                for (xCoordinate = newPosition.X; xCoordinate < newPosition.X + transformComponent.Size.X; xCoordinate++)
                {
                    for (yCoordinate = newPosition.Y; yCoordinate < newPosition.Y + transformComponent.Size.Y; yCoordinate++)
                    {
                        Map.MapNodes[xCoordinate, yCoordinate, zCoordinate].EntityId = entityId;
                    }
                }
            }

            transformComponent.Position = newPosition;
            ComponentRepo.SaveTransformComponent(entityId, transformComponent, ComponentSaveMode.Overwrite);
        }

        public void RemoveEntityFromMap(int entityId, TransformComponent transformComponent)
        {
            if (IsOnMap(transformComponent.Position))
            {
                if (transformComponent.Size == TransformSize1)
                {
                    Map.MapNodes[transformComponent.Position.X, transformComponent.Position.Y, transformComponent.Position.Z].EntityId = -1;
                }
                else
                {
                    zCoordinate = transformComponent.Position.Z;

                    for (xCoordinate = transformComponent.Position.X; xCoordinate < transformComponent.Position.X + transformComponent.Size.X; xCoordinate++)
                    {
                        for (yCoordinate = transformComponent.Position.Y; yCoordinate < transformComponent.Position.Y + transformComponent.Size.Y; yCoordinate++)
                        {
                            Map.MapNodes[xCoordinate, yCoordinate, zCoordinate].EntityId = -1;
                        }
                    }
                }
            }
            transformComponent.Position = new Vector3Int();
            ComponentRepo.SaveTransformComponent(entityId, transformComponent, ComponentSaveMode.Overwrite);
        }

        public void PlaceEntityOnMap(int entityId, Vector3Int newPosition, TransformComponent transformComponent)
        {
            if (IsOnMap(newPosition))
            {
                if (transformComponent.Size == TransformSize1)
                {
                    Map.MapNodes[newPosition.X, newPosition.Y, newPosition.Z].EntityId = entityId;
                }
                else
                {
                    zCoordinate = newPosition.Z;

                    for (xCoordinate = newPosition.X; xCoordinate < newPosition.X + transformComponent.Size.X; xCoordinate++)
                    {
                        for (yCoordinate = newPosition.Y; yCoordinate < newPosition.Y + transformComponent.Size.Y; yCoordinate++)
                        {
                            Map.MapNodes[xCoordinate, yCoordinate, zCoordinate].EntityId = entityId;
                        }
                    }
                }
                transformComponent.Position = newPosition;
                ComponentRepo.SaveTransformComponent(entityId, transformComponent, ComponentSaveMode.Overwrite);
            }
        }

        /// <summary>
        /// Returns true if the given coordinates are within the bounds of the map cube.
        /// </summary>
        public bool IsOnMap(Vector3Int coordinates)
        {
            return coordinates.X >= 0 && coordinates.Y >= 0 && coordinates.Z >= 0
                && coordinates.X < Map.Size.X && coordinates.Y < Map.Size.Y && coordinates.Z < Map.Size.Z;
        }

        /// <summary>
        /// Returns true if the given cute is entirely within the bounds of the map cube.
        /// By checking that the cube is entirely on the map, we void moving multi-node entities partially off the map.
        /// </summary>
        public bool IsOnMap(CubeInt cube)
        {
            return IsOnMap(cube.Position) && IsOnMap(cube.Position + cube.Size);
        }
    }
}
