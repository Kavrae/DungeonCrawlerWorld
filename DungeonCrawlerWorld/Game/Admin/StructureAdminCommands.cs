using Engine.Math;
using Game.Terrain;

namespace Game.Admin;

/// <summary>Admin Mode's wall commands: build a stone wall on a tile, or knock down the structure on one and lay grass in its place.</summary>
/// <remarks>
/// A debugging tool over World.SetStructure, the one runtime path that changes a structure -- so the map's cached walls,
/// their joined pieces and their neighbours' re-link exactly as for any other change. A wall isn't built over a Blocking
/// occupant, which it would trap.
/// </remarks>
public sealed class StructureAdminCommands(World.World world)
{
    /// <summary>Whether a stone wall could be built at position, and if not, why.</summary>
    public bool CanBuildStoneWall(Vector3Int position, out string reason)
    {
        if (!world.IsOnMap(position))
        {
            reason = "off the map";
            return false;
        }

        if (!world.GetStructureAt(position).IsEmpty)
        {
            reason = "a wall is already here";
            return false;
        }

        if (world.Map.GetBlockingEntityId(position) != -1)
        {
            reason = "occupied";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Builds a stone wall at position (its look comes from the cell's seed, so the same tile always gets the same one); false when CanBuildStoneWall refuses.</summary>
    public bool TryBuildStoneWall(Vector3Int position)
    {
        if (!CanBuildStoneWall(position, out _))
        {
            return false;
        }

        var stoneWall = world.Terrain.GetId(BuiltInTerrain.StoneWallKey);
        world.SetStructure(position, new TerrainCell(stoneWall));
        return true;
    }

    /// <summary>Whether a structure stands at position to be removed.</summary>
    public bool CanRemoveStructure(Vector3Int position) => !world.GetStructureAt(position).IsEmpty;

    /// <summary>Removes the structure at position, whatever it is, and lays grass on the floor beneath it; false when there is none.</summary>
    /// <remarks>A wall stands on stone floor, which has no sprite of its own, so taking the wall away alone would leave a bare hole. A MapLayer with no floor (Flying) gets none.</remarks>
    public bool TryRemoveStructure(Vector3Int position)
    {
        if (!CanRemoveStructure(position))
        {
            return false;
        }

        world.SetStructure(position, default);

        if (World.Map.TerrainLayerFor(position.Z) is { } terrainLayer)
        {
            var grass = world.Terrain.GetId(BuiltInTerrain.GrassKey);
            world.SetTerrain(position.X, position.Y, terrainLayer, new TerrainCell(grass));
        }

        return true;
    }
}
