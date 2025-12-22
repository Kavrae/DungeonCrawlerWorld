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

        /// <summary>
        /// The position of the currently selected map node, if any, using the 2d Map coordinates.
        /// This selection includes all mapNodes on the z axis for those coordinates.
        /// Selection is generally done via the user clicking on a map tile or reference to a map tile.
        /// </summary>
        public Point? SelectedMapNodePosition { get; set; }

        public World()
        {
            Map = new Map(new Vector3Int(0, 0, 0));
        }

        /// <summary>
        /// Moves an entity to a new position within the world, updating the old and new map node entity references accordingly.
        /// Entities are effectively "teleported" to the destination mapNodes without moving through the intermediate nodes.
        /// Only entities with a transform component can be moved.
        /// Entities that occupy multiple mapModes must collision check each one before moving.
        /// </summary>
        public void MoveEntity(int entityId, Vector3Int newPosition)
        {
            if (IsOnMap(newPosition))
            {
                var nullableTransformComponent = ComponentRepo.TransformComponents[entityId];
                if (nullableTransformComponent != null)
                {
                    var transformComponent = nullableTransformComponent.Value;

                    //If the entity is already on the map, empty its map nodes.
                    if (IsOnMap(nullableTransformComponent.Value.Position))
                    {
                        for (var x = transformComponent.Position.X; x < transformComponent.Position.X + transformComponent.Size.X; x++)
                        {
                            for (var y = transformComponent.Position.Y; y < transformComponent.Position.Y + transformComponent.Size.Y; y++)
                            {
                                var mapNode = Map.MapNodes[x, y, transformComponent.Position.Z];
                                mapNode.EntityId = null;
                                Map.MapNodes[x, y, transformComponent.Position.Z] = mapNode;
                            }
                        }
                    }

                    //Place the entity on the new map nodes.
                    for (var x = newPosition.X; x < newPosition.X + transformComponent.Size.X; x++)
                    {
                        for (var y = newPosition.Y; y < newPosition.Y + transformComponent.Size.Y; y++)
                        {
                            var mapNode = Map.MapNodes[x, y, newPosition.Z];
                            if (mapNode.EntityId != entityId)
                            {
                                mapNode.EntityId = entityId;
                                Map.MapNodes[x, y, newPosition.Z] = mapNode;
                            }
                        }
                    }

                    transformComponent.Position = newPosition;
                    ComponentRepo.SaveTransformComponent(entityId, transformComponent, ComponentSaveMode.Overwrite);
                }
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
