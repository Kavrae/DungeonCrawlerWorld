using Engine.Math;

namespace Game.Modules.Core.Components;

/// <summary>The position and size of an entity on the map. Size is X/Y only -- an entity's footprint never spans more than one MapLayer.</summary>
public struct TransformComponent(Vector3Int position, Vector2Byte size)
{
    /// <summary>The X and Y of an entity that isn't on the map: built but not yet placed, or removed from it.</summary>
    /// <remarks>Far outside any map rather than (0, 0) or (-1, -1): world coordinates can be negative, so every small value is a real tile.</remarks>
    public const int UnplacedCoordinate = int.MinValue / 2;

    /// <summary>The position of an entity that isn't on the map, keeping the MapLayer it belongs on.</summary>
    public static Vector3Int UnplacedOn(MapLayer layer) => new(UnplacedCoordinate, UnplacedCoordinate, (int)layer);


    public Vector3Int Position { get; set; } = position;
    public Vector2Byte Size { get; set; } = size;

    public override readonly string ToString() => $"Position : {Position}\nSize : {Size}";
}