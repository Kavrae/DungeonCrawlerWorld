using Engine.Math;

namespace Game.Modules.Core.Components;

/// <summary>The position and size of an entity on the map. Size is X/Y only -- an entity's footprint never spans more than one MapLayer.</summary>
public struct TransformComponent(Vector3Int position, Vector2Byte size)
{
    public Vector3Int Position { get; set; } = position;
    public Vector2Byte Size { get; set; } = size;

    public override readonly string ToString() => $"Transform : {Position} {Size}";
}
