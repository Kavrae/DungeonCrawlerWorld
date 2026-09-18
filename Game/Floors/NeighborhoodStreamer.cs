using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.ProcessingTier;
using Game.World;

namespace Game.Floors;

/// <summary>Unloads and loads whole neighborhoods a slice per frame: destroying every entity in one, and generating one from its record.</summary>
/// <remarks>
/// <para>
/// Unloading destroys each entity through EntityManager.DestroyEntity, whose EntityDestroying
/// handlers remove its aura sources, map footprint and tier bookkeeping, then publishes a
/// TerrainUnloadingEvent per row and drops the neighborhood's map stores. Loading allocates the
/// stores, writes the layout with a TerrainLoadedEvent per row, then creates the population, born
/// with the tier its position deserves.
/// </para>
/// <para>
/// With a window (ProcessingTierResolver.WindowCenter), it follows WindowShifted: the neighborhoods
/// entering the 3x3 are loaded, nearest the player first, and the ones leaving it join a cache of
/// CacheSize recently dropped neighborhoods that stay loaded and frozen, so turning straight back
/// reloads nothing. Past CacheSize the oldest is unloaded.
/// </para>
/// <para>
/// Every frame spends at most BudgetPerFrame units across the queued work: one per entity destroyed or
/// created, LayoutRowCost per row of terrain written or announced. Work runs one job at a time, in order,
/// and a shift queues its evictions before its loads, so no more than 9 + CacheSize neighborhoods are
/// ever loaded at once -- the headroom the session reserves at startup. Register it with SystemManager.RegisterFirst: population
/// records each new creature's spawn into the moved-entities buffer, which the systems that read it
/// must not have read yet this frame.
/// </para>
/// </remarks>
public sealed class NeighborhoodStreamer : ISystem
{
    /// <summary>Units of work spent per frame.</summary>
    public const int DefaultBudgetPerFrame = 512;

    /// <summary>What writing one layout row costs against the budget: ~1024 cells of terrain on two layers.</summary>
    public const int LayoutRowCost = 16;

    /// <summary>How many neighborhoods dropped from the window stay loaded before the oldest is unloaded.</summary>
    public const int CacheSize = 3;

    /// <summary>How far past a loaded neighborhood's edge a multi-tile footprint can reach: the largest footprint is 3x3.</summary>
    private const int FootprintReachTiles = 2;

    private enum JobKind
    {
        Load,
        Unload,
        Regenerate,
    }

    private sealed class Job(int cellX, int cellY, JobKind kind, IEnumerator<int> work)
    {
        public int CellX { get; } = cellX;
        public int CellY { get; } = cellY;
        public JobKind Kind { get; } = kind;
        public IEnumerator<int> Work { get; } = work;
        public bool Started { get; set; }
    }

    private readonly World.World _world;
    private readonly EntityManager _entityManager;
    private readonly Engine.ECS.Components.Stores.DirectComponentPool<Modules.Core.Components.TransformComponent> _transforms;
    private readonly EventBus _eventBus;
    private readonly ProcessingTierResolver _resolver;
    private readonly NeighborhoodRecords _records;
    private readonly TestMapBuilder _builder;

    private readonly List<Job> _jobs = [];

    /// <summary>Loaded neighborhoods outside the window, oldest first.</summary>
    private readonly List<(int CellX, int CellY)> _cache = [];

    private readonly HashSet<int> _straddlingEntityIds = [];

    private readonly List<(int CellX, int CellY)> _toLoad = [];

    public NeighborhoodStreamer(World.World world, EntityManager entityManager, Engine.ECS.Components.Stores.DirectComponentPool<Modules.Core.Components.TransformComponent> transforms, EventBus eventBus, ProcessingTierResolver resolver, NeighborhoodRecords records, TestMapBuilder builder)
    {
        _world = world;
        _entityManager = entityManager;
        _transforms = transforms;
        _eventBus = eventBus;
        _resolver = resolver;
        _records = records;
        _builder = builder;
        resolver.WindowShifted += OnWindowShifted;
    }

    public byte StripeCount => 1;

    /// <summary>Units of work spent per frame.</summary>
    public int BudgetPerFrame { get; init; } = DefaultBudgetPerFrame;

    /// <summary>Whether any work is queued.</summary>
    public bool IsBusy => _jobs.Count > 0;

    /// <summary>The loaded neighborhoods outside the window, oldest first.</summary>
    public IReadOnlyList<(int CellX, int CellY)> Cache => _cache;

    /// <summary>Whether neighborhood (cellX, cellY) has work queued.</summary>
    public bool IsPending(int cellX, int cellY) => _jobs.Any(job => job.CellX == cellX && job.CellY == cellY);

    /// <summary>Whether neighborhood (cellX, cellY) can be regenerated now, and if not, why.</summary>
    /// <remarks>Only a neighborhood nothing Local reaches: none of it within the Local exit radius of the player, so every entity it holds is frozen and the player sees nothing vanish mid-fight.</remarks>
    public bool CanRegenerate(int cellX, int cellY, out string reason)
    {
        if (!_world.Map.IsNeighborhoodLoaded(cellX, cellY))
        {
            reason = "Not loaded";
            return false;
        }

        if (IsPending(cellX, cellY))
        {
            reason = "Already regenerating";
            return false;
        }

        return IsBeyondLocalReach(cellX, cellY, out reason);
    }

    /// <summary>Whether no part of neighborhood (cellX, cellY) is within the Local exit radius of the player, and if not, why.</summary>
    private bool IsBeyondLocalReach(int cellX, int cellY, out string reason)
    {
        if (_resolver.ReferencePosition is { } player && Neighborhoods.DistanceToArea(player, cellX, cellY) is var distance && distance <= ProcessingTierResolver.LocalExitRadiusTiles)
        {
            reason = distance == 0
                ? "The player is inside it"
                : $"The player is {distance} tiles from its edge; needs more than {ProcessingTierResolver.LocalExitRadiusTiles}";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Queues neighborhood (cellX, cellY) to be unloaded and generated again from its record: the same layout, a fresh population.</summary>
    /// <remarks>The player's distance is checked again when the job reaches the front of the queue, and the job is dropped if the player has come within reach while it waited.</remarks>
    /// <returns>False, queuing nothing, when CanRegenerate refuses.</returns>
    public bool TryRequestRegenerate(int cellX, int cellY)
    {
        if (!CanRegenerate(cellX, cellY, out _))
        {
            return false;
        }

        _jobs.Add(new Job(cellX, cellY, JobKind.Regenerate, Unload(cellX, cellY).Concat(Load(cellX, cellY)).GetEnumerator()));
        return true;
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        var budget = BudgetPerFrame;
        while (budget > 0 && _jobs.Count > 0)
        {
            var job = _jobs[0];
            if (!job.Started && job.Kind is JobKind.Regenerate && !IsBeyondLocalReach(job.CellX, job.CellY, out _))
            {
                _jobs.RemoveAt(0);
                continue;
            }

            job.Started = true;
            if (job.Work.MoveNext())
            {
                budget -= job.Work.Current;
                continue;
            }

            job.Work.Dispose();
            _jobs.RemoveAt(0);
        }
    }

    /// <summary>Loads the neighborhoods entering the window, caches the ones leaving it, and unloads whatever the cache no longer has room for.</summary>
    /// <remarks>
    /// A neighborhood entering the window that is cached just leaves the cache; one whose unload hasn't
    /// started has the unload cancelled; one whose unload has started is loaded again after it. A
    /// neighborhood leaving it whose load hasn't started has the load cancelled; otherwise it joins the
    /// cache, farthest from the player first, so of one shift's drops the nearest is evicted last.
    /// </remarks>
    private void OnWindowShifted((int CellX, int CellY) previous, (int CellX, int CellY) center)
    {
        _world.Map.CenterLookupOn(center.CellX, center.CellY);
        var player = _resolver.ReferencePosition ?? new Vector3Int(Neighborhoods.OriginOf(center.CellX), Neighborhoods.OriginOf(center.CellY), 0);

        var leaving = WindowAround(previous).Where(cell => Distance(cell, center) > 1).OrderByDescending(cell => Neighborhoods.DistanceToArea(player, cell.CellX, cell.CellY));
        foreach (var cell in leaving)
        {
            var loadIndex = _jobs.FindIndex(job => job.Kind is JobKind.Load && job.CellX == cell.CellX && job.CellY == cell.CellY);
            if (loadIndex >= 0 && !_jobs[loadIndex].Started)
            {
                _jobs.RemoveAt(loadIndex);
                continue;
            }

            if (loadIndex >= 0 || _world.Map.IsNeighborhoodLoaded(cell.CellX, cell.CellY))
            {
                _cache.Remove(cell);
                _cache.Add(cell);
            }
        }

        foreach (var cell in WindowAround(center).Where(cell => _world.Map.CanHold(cell.CellX, cell.CellY)).OrderBy(cell => Neighborhoods.DistanceToArea(player, cell.CellX, cell.CellY)))
        {
            if (_cache.Remove(cell) || _jobs.Any(job => job.Kind is JobKind.Load && job.CellX == cell.CellX && job.CellY == cell.CellY))
            {
                continue;
            }

            var unloadIndex = _jobs.FindIndex(job => job.Kind is JobKind.Unload && job.CellX == cell.CellX && job.CellY == cell.CellY);
            if (unloadIndex >= 0 && !_jobs[unloadIndex].Started)
            {
                _jobs.RemoveAt(unloadIndex);
                continue;
            }

            if (unloadIndex < 0 && _world.Map.IsNeighborhoodLoaded(cell.CellX, cell.CellY))
            {
                continue;
            }

            _toLoad.Add(cell);
        }

        while (_cache.Count > CacheSize)
        {
            var evicted = _cache[0];
            _cache.RemoveAt(0);
            _jobs.Add(new Job(evicted.CellX, evicted.CellY, JobKind.Unload, Unload(evicted.CellX, evicted.CellY).GetEnumerator()));
        }

        foreach (var cell in _toLoad)
        {
            _jobs.Add(new Job(cell.CellX, cell.CellY, JobKind.Load, Load(cell.CellX, cell.CellY).GetEnumerator()));
        }

        _toLoad.Clear();
    }

    private static IEnumerable<(int CellX, int CellY)> WindowAround((int CellX, int CellY) center)
    {
        for (var cellY = center.CellY - 1; cellY <= center.CellY + 1; cellY++)
        {
            for (var cellX = center.CellX - 1; cellX <= center.CellX + 1; cellX++)
            {
                yield return (cellX, cellY);
            }
        }
    }

    private static int Distance((int CellX, int CellY) a, (int CellX, int CellY) b) =>
        System.Math.Max(System.Math.Abs(a.CellX - b.CellX), System.Math.Abs(a.CellY - b.CellY));

    /// <summary>Destroys every entity indexed in the neighborhood, one unit each, announces its terrain a row at a time, then drops its stores.</summary>
    /// <remarks>Anything that arrives while the terrain is announced is destroyed all at once just before the stores drop, so nothing is left on cells that no longer exist.</remarks>
    private IEnumerable<int> Unload(int cellX, int cellY)
    {
        foreach (var cost in DestroyEntitiesIn(cellX, cellY))
        {
            yield return cost;
        }

        foreach (var row in RowsOf(cellX, cellY))
        {
            _eventBus.Publish(new TerrainUnloadingEvent(row));
            yield return LayoutRowCost;
        }

        foreach (var _ in DestroyEntitiesIn(cellX, cellY))
        {
        }

        _world.Map.UnloadNeighborhood(cellX, cellY);
    }

    /// <summary>Destroys every entity indexed in the neighborhood, one unit each, until the membership index holds none.</summary>
    /// <remarks>Walks the index again after each pass, in case anything arrived while the walk was spread over frames.</remarks>
    private IEnumerable<int> DestroyEntitiesIn(int cellX, int cellY)
    {
        var entityIds = new List<int>();
        do
        {
            entityIds.Clear();
            for (var z = 0; z < _world.Map.Bounds.Depth; z++)
            {
                _resolver.Membership.CopyCell(cellX, cellY, z, entityIds);
            }

            foreach (var entityId in entityIds)
            {
                if (_entityManager.EntityExists(entityId))
                {
                    _entityManager.DestroyEntity(entityId);
                    _resolver.Forget(entityId);
                    yield return 1;
                }
                else
                {
                    _resolver.Forget(entityId);
                }
            }
        }
        while (entityIds.Count > 0);
    }

    /// <summary>Allocates the neighborhood's stores and generates it from its record: layout rows at LayoutRowCost each, each announced as it is written, then the population, one unit per entity created.</summary>
    private IEnumerable<int> Load(int cellX, int cellY)
    {
        _world.Map.LoadNeighborhood(cellX, cellY);
        var record = _records.GetOrCreate(cellX, cellY);

        using var rows = RowsOf(cellX, cellY).GetEnumerator();
        foreach (var _ in _builder.GenerateLayout(_world, record))
        {
            rows.MoveNext();
            _eventBus.Publish(new TerrainLoadedEvent(rows.Current));
            yield return LayoutRowCost;
        }

        RestoreStraddlingFootprints(cellX, cellY);
        yield return LayoutRowCost;

        foreach (var created in _builder.PopulateNeighborhood(_world, record))
        {
            yield return System.Math.Max(1, created);
        }
    }

    /// <summary>Registers, in a just-loaded neighborhood, the cells of every multi-tile entity next door whose footprint reaches across the border -- cells that were dropped with the neighborhood's stores.</summary>
    /// <remarks>Scans the loaded neighbors' cells within FootprintReachTiles of the border, on every layer, for occupants whose footprint overlaps the neighborhood.</remarks>
    private void RestoreStraddlingFootprints(int cellX, int cellY)
    {
        var area = Neighborhoods.AreaOf(cellX, cellY, _world.Map.Bounds.Depth);
        _straddlingEntityIds.Clear();

        for (var z = 0; z < area.Depth; z++)
        {
            for (var y = area.MinY - FootprintReachTiles; y < area.MaxY + FootprintReachTiles; y++)
            {
                var isInsideRows = y >= area.MinY && y < area.MaxY;
                for (var x = area.MinX - FootprintReachTiles; x < area.MaxX + FootprintReachTiles; x++)
                {
                    if (isInsideRows && x == area.MinX)
                    {
                        x = area.MaxX - 1;
                        continue;
                    }

                    foreach (var entityId in _world.GetOccupantEntityIdsAt(new Vector3Int(x, y, z)))
                    {
                        _straddlingEntityIds.Add(entityId);
                    }
                }
            }
        }

        foreach (var entityId in _straddlingEntityIds)
        {
            if (_transforms.TryGetReadonly(entityId, out var transform) &&
                transform.Position.X < area.MaxX && transform.Position.X + transform.Size.X > area.MinX &&
                transform.Position.Y < area.MaxY && transform.Position.Y + transform.Size.Y > area.MinY)
            {
                _world.RestoreFootprintWithin(entityId, transform.Position, transform.Size, area);
            }
        }
    }

    /// <summary>Each row of neighborhood (cellX, cellY) inside the map's bounds, top to bottom, as a one-row area on every layer -- the same rows TestMapBuilder.GenerateLayout writes.</summary>
    private IEnumerable<MapBounds> RowsOf(int cellX, int cellY)
    {
        var bounds = _world.Map.Bounds;
        var area = Neighborhoods.AreaOf(cellX, cellY, bounds.Depth);
        var minX = System.Math.Max(area.MinX, bounds.MinX);
        var maxX = System.Math.Min(area.MaxX, bounds.MaxX);
        for (var y = System.Math.Max(area.MinY, bounds.MinY); y < System.Math.Min(area.MaxY, bounds.MaxY); y++)
        {
            yield return new MapBounds(minX, y, maxX, y + 1, bounds.Depth);
        }
    }
}
