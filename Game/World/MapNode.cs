using Engine.Math;

namespace Game.World;

/// <summary>A single position on the 3D map grid. EntityId -1 is the sentinel for "empty."</summary>
public struct MapNode(int x, int y, int z)
{
    public int EntityId { get; set; } = -1;

    public Vector3Int Position { get; set; } = new Vector3Int(x, y, z);
}
