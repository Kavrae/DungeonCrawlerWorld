using Game.Modules.Core.Components;
using Game.Terrain;

namespace Game.Effects.Entries;

/// <summary>Turns the floor at context.TargetLocation into the terrain registered under TerrainKey.</summary>
/// <remarks>
/// Placed on each tile an activation resolves to (EffectPlacement.OnEachTile), whoever stands there. Goes through
/// World.SetTerrain, the announced path, so terrain contact and the aura field follow the change. A tile holding a
/// structure (a wall) is left alone -- only floor changes -- and so is one with no floor (a Flying layer), one off the
/// map, and an application with no TargetLocation (an aura's tick, a terrain contact). The build fails content naming a
/// terrain key it doesn't register (TerrainReferenceValidation).
/// </remarks>
/// <param name="TerrainKey">The key the terrain is registered under (TerrainRegistry), e.g. BuiltInTerrain.LavaKey.</param>
public sealed record TerrainChange(string TerrainKey) : IEffectEntry
{
    public IEnumerable<string> ReferencedTerrainKeys => [TerrainKey];

    public EffectPlacement Placement => EffectPlacement.OnEachTile;

    public EffectOutcome Apply(in EffectContext context)
    {
        var world = context.Services.World;
        if (context.TargetLocation is not { } location ||
            !world.IsOnMap(location) ||
            !world.Map.GetStructure(location).IsEmpty ||
            World.Map.TerrainLayerFor(location.Z) is not { } terrainLayer ||
            !world.Terrain.TryGetId(TerrainKey, out var terrainTypeId))
        {
            return EffectOutcome.NoEffect;
        }

        var terrain = new TerrainCell(terrainTypeId);
        if (world.Map.GetTerrain(location.X, location.Y, terrainLayer) == terrain)
        {
            return EffectOutcome.NoEffect;
        }

        world.SetTerrain(location.X, location.Y, terrainLayer, terrain);
        return EffectOutcome.Applied;
    }
}
