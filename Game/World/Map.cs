using Engine.Math;

namespace Game.World;

/// <summary>
/// The in-memory 3D grid of map nodes. Every node is eagerly initialized at construction
/// to avoid null checks on every access during updates.
/// </summary>
public sealed class Map
{
    public Vector3Int Size { get; }

    /// <summary>MapNode [0,0,0] is drawn to the top-left of the map window.</summary>
    public MapNode[,,] MapNodes { get; }

    public Map(Vector3Int size)
    {
        Size = size;
        MapNodes = new MapNode[size.X, size.Y, size.Z];

        for (var x = 0; x < Size.X; x++)
        {
            for (var y = 0; y < Size.Y; y++)
            {
                for (var z = 0; z < Size.Z; z++)
                {
                    MapNodes[x, y, z] = new MapNode(x, y, z);
                }
            }
        }
    }

    public MapNode GetMapNode(Vector3Int coordinates) => MapNodes[coordinates.X, coordinates.Y, coordinates.Z];
}
