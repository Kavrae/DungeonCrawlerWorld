using DungeonCrawlerWorld.Utilities;

namespace DungeonCrawlerWorld.Components
{
    /// <summary>
    /// Indicates the Z component of a mapNode or entity on the map
    /// </summary>
    /// <todo>
    /// More mapHeights
    /// Create a new MapCoordinate type with all values >0 and the z axis bound to the MapHeight enum.
    /// Separate MapHeight into more granular properties to encapsulate things like Riding
    /// </todo>
    public enum MapHeight : byte
    {
        UnderGround = 0,
        Ground = 1,
        Standing = 2,
        Riding = 3,
        Floating = 4,
        Flying = 5
    }

    /// <summary>
    /// A core component that specifies the Position and Size of an entity.
    /// </summary>
    public struct TransformComponent(Vector3Int position, Vector3Int size) : IEntityComponent
    {
        public Vector3Int Position { get; set; } = position;
        public Vector3Int Size { get; set; } = size;

        public override string ToString()
        {
            return $"Transform : {Size} {Position}";
        }
    }
}
