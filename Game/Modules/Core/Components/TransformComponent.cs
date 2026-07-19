using Engine.Math;

namespace Game.Modules.Core.Components;

/// <summary>The position and size of an entity on the map.</summary>
public struct TransformComponent(Vector3Int position, Vector3Byte size)
{
    public Vector3Int Position { get; set; } = position;
    public Vector3Byte Size { get; set; } = size;

    public override readonly string ToString() => $"Transform : {Position} {Size}";
}
