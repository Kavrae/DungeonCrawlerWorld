using Game.Areas;
using Game.Spawning;
using Game.Terrain;
using Game.World;

namespace Game.Floors;

/// <summary>Everything one generation of a neighborhood decides, as data: its layout, the aura-radiating terrain in each row of it, the area instances stamped into it, and what to spawn there, in order.</summary>
/// <remarks>
/// Produced by TestMapBuilder.Plan, which touches no pool, map or event, so it can run on a worker
/// thread; applied on the main thread by loading Layout, announcing its rows with AuraCellsByRow, and
/// spawning Spawns in order. The same record and population seed always give the same plan, whichever
/// thread made it and however long it took.
/// </remarks>
/// <param name="auraCellsByRow">Layout's aura-radiating terrain cells, one list per row from Layout.MinY -- see TerrainAuraSources.ByRow.</param>
/// <param name="spawnRowEnds">For each batch of spawns -- the shrines in batches first, then the areas' spawns, then each population row -- the index in spawns one past its last request, so spawning can stop between batches.</param>
/// <param name="uniqueSpawns">The spawns, by index, that a unique placement planned: each spawns only if the roster still allows it when its turn comes, and its fallback otherwise.</param>
public sealed class NeighborhoodPlan(
    NeighborhoodLayout layout,
    IReadOnlyList<IReadOnlyList<TerrainAuraCell>> auraCellsByRow,
    IReadOnlyList<SpawnRequest> spawns,
    IReadOnlyList<int> spawnRowEnds,
    AreaStampResult areas,
    IReadOnlyDictionary<int, AreaPlannedSpawn> uniqueSpawns)
{
    public NeighborhoodLayout Layout { get; } = layout;

    public IReadOnlyList<IReadOnlyList<TerrainAuraCell>> AuraCellsByRow { get; } = auraCellsByRow;

    public IReadOnlyList<SpawnRequest> Spawns { get; } = spawns;

    public IReadOnlyList<int> SpawnRowEnds { get; } = spawnRowEnds;

    /// <summary>The area instances stamped into the neighborhood: which cells each covers and where its markers are.</summary>
    public AreaStampResult Areas { get; } = areas;

    public IReadOnlyDictionary<int, AreaPlannedSpawn> UniqueSpawns { get; } = uniqueSpawns;
}
