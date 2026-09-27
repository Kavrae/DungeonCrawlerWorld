using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Spawning;
using Game.Modules.ProcessingTier;
using Game.World;

namespace Game.Floors;

/// <summary>Unloads and loads whole neighborhoods a slice per frame: destroying every entity in one, and generating one from its record.</summary>
/// <remarks>
/// <para>
/// Unloading destroys each entity through EntityManager.DestroyEntity, whose EntityDestroying
/// handlers remove its aura sources, map footprint and tier bookkeeping, then publishes a
/// TerrainUnloadingEvent per row and drops the neighborhood's map stores. Loading loads the stores
/// with the whole layout, announces it with a TerrainLoadedEvent per row, then creates the
/// population, born with the tier its position deserves.
/// </para>
/// <para>
/// A load is decided on a worker thread (TestMapBuilder.Plan) from the moment it is queued, and applied
/// here no earlier than StartDelayFrames of this system's updates later. If the worker isn't done by
/// the time the load comes up, this waits for it rather than moving on. So when a load starts is a
/// matter of frames alone, never of how fast the worker ran, and a seeded session streams the same
/// world every time. A load dropped before it starts (the window turned back, a regeneration the
/// player walked up to) cancels its worker, and its population is counted on the neighborhood's record
/// only once it starts.
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
/// except that an eviction destroys its built creatures first and ahead of every other job (see
/// IsEvictingBuiltCreatures), and a shift queues its evictions before its loads, so no more than 9 + CacheSize neighborhoods are
/// ever loaded at once -- the headroom the session reserves at startup. Register it with SystemManager.RegisterFirst: population
/// records each new creature's spawn into the moved-entities buffer, which the systems that read it
/// must not have read yet this frame.
/// </para>
/// </remarks>
public sealed class NeighborhoodStreamer : ISystem
{
    /// <summary>Units of work spent per frame.</summary>
    /// <remarks>Sized for the Debug build's frame rate rather than for how soon loads finish: every unit is main-thread work (spawning, announcing a row). A shift's loads finish ~980 frames after it.</remarks>
    public const int DefaultBudgetPerFrame = 256;

    /// <summary>What writing one layout row costs against the budget: ~1024 cells of terrain on two layers.</summary>
    public const int LayoutRowCost = 16;

    /// <summary>How many neighborhoods dropped from the window stay loaded before the oldest is unloaded.</summary>
    public const int CacheSize = 3;

    /// <summary>How many of this system's updates after a load is queued it may start, giving its worker time to plan it.</summary>
    public const int DefaultStartDelayFrames = 30;

    /// <summary>How far past a loaded neighborhood's edge a multi-tile footprint can reach: the largest footprint is 3x3.</summary>
    private const int FootprintReachTiles = 2;

    private enum JobKind
    {
        Load,
        Unload,
        Regenerate,
    }

    private sealed class Job(int cellX, int cellY, JobKind kind, IEnumerator<int> work, UnloadProgress? eviction = null)
    {
        public int CellX { get; } = cellX;
        public int CellY { get; } = cellY;
        public JobKind Kind { get; } = kind;
        public IEnumerator<int> Work { get; } = work;
        public bool Started { get; set; }

        /// <summary>An eviction's progress through its built creatures; null for any other job.</summary>
        public UnloadProgress? Eviction { get; } = eviction;

        /// <summary>The update (see _updateCount) this job may start on at the earliest.</summary>
        public long EarliestStartUpdateCount { get; init; }

        /// <summary>The worker planning what this job loads; null for a job that loads nothing.</summary>
        public PendingNeighborhoodGeneration? PendingGeneration { get; init; }
    }

    /// <summary>Whether an unload has destroyed its neighborhood's built creatures yet -- the part of it whose storage a promotion can reuse.</summary>
    private sealed class UnloadProgress
    {
        public bool BuiltCreaturesDestroyed { get; set; }
    }

    private readonly World.World _world;
    private readonly EntityManager _entityManager;
    private readonly Engine.ECS.Components.Stores.DirectComponentPool<Modules.Core.Components.TransformComponent> _transforms;
    private readonly EventBus _eventBus;
    private readonly ProcessingTierResolver _resolver;
    private readonly NeighborhoodRecords _records;
    private readonly TestMapBuilder _builder;
    private readonly CreatureSkeletons? _skeletons;

    private readonly List<Job> _jobs = [];

    /// <summary>Loaded neighborhoods outside the window, oldest first.</summary>
    private readonly List<(int CellX, int CellY)> _cache = [];

    private readonly HashSet<int> _straddlingEntityIds = [];

    private readonly List<(int CellX, int CellY)> _toLoad = [];

    /// <summary>Workers queued since the last update, started by StartAwaitingGenerations.</summary>
    private readonly List<PendingNeighborhoodGeneration> _generationsAwaitingStart = [];

    /// <summary>How many times Update has run: the clock EarliestStartUpdateCount is measured on.</summary>
    private long _updateCount;

    /// <param name="skeletons">When supplied, an eviction destroys its neighborhood's built creatures first and ahead of other work (see IsEvictingBuiltCreatures); otherwise every unload destroys in index order.</param>
    public NeighborhoodStreamer(World.World world, EntityManager entityManager, Engine.ECS.Components.Stores.DirectComponentPool<Modules.Core.Components.TransformComponent> transforms, EventBus eventBus, ProcessingTierResolver resolver, NeighborhoodRecords records, TestMapBuilder builder, CreatureSkeletons? skeletons = null)
    {
        _world = world;
        _entityManager = entityManager;
        _transforms = transforms;
        _eventBus = eventBus;
        _resolver = resolver;
        _records = records;
        _builder = builder;
        _skeletons = skeletons;
        resolver.WindowShifted += OnWindowShifted;
    }

    public byte StripeCount => 1;

    /// <summary>Units of work spent per frame.</summary>
    public int BudgetPerFrame { get; init; } = DefaultBudgetPerFrame;

    /// <summary>How many of this system's updates after a load is queued it may start -- see this class's remarks.</summary>
    public int StartDelayFrames { get; init; } = DefaultStartDelayFrames;

    /// <summary>Whether any work is queued.</summary>
    public bool IsBusy => _jobs.Count > 0;

    /// <summary>Whether a neighborhood evicted from the window's cache still has built creatures (anything not a creature skeleton) waiting to be destroyed.</summary>
    /// <remarks>What a promotion should wait for: those are the creatures whose storage a promotion's builds reuse. The rest of an eviction -- its skeletons, its terrain -- frees nothing a build needs, so it doesn't hold anything up, and the streamer runs every eviction's built creatures ahead of its other work.</remarks>
    public bool IsEvictingBuiltCreatures => NextEvictionOfBuiltCreatures() >= 0;

    private int NextEvictionOfBuiltCreatures()
    {
        for (var i = 0; i < _jobs.Count; i++)
        {
            if (_jobs[i].Eviction is { BuiltCreaturesDestroyed: false })
            {
                return i;
            }
        }

        return -1;
    }

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

        var pendingGeneration = QueueNeighborhoodGeneration(cellX, cellY);
        _jobs.Add(new Job(cellX, cellY, JobKind.Regenerate, Unload(cellX, cellY).Concat(Load(cellX, cellY, pendingGeneration)).GetEnumerator()) { PendingGeneration = pendingGeneration });
        return true;
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        _updateCount++;
        StartAwaitingGenerations();
        var budget = BudgetPerFrame;
        while (budget > 0 && _jobs.Count > 0)
        {
            var index = System.Math.Max(0, NextEvictionOfBuiltCreatures());
            var job = _jobs[index];
            if (!job.Started && _updateCount < job.EarliestStartUpdateCount)
            {
                break;
            }

            if (!job.Started && job.Kind is JobKind.Regenerate && !IsBeyondLocalReach(job.CellX, job.CellY, out _))
            {
                DropUnstartedJob(index);
                continue;
            }

            job.Started = true;
            if (job.Work.MoveNext())
            {
                budget -= job.Work.Current;
                continue;
            }

            job.Work.Dispose();
            _jobs.RemoveAt(index);
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
                DropUnstartedJob(loadIndex);
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
            var eviction = new UnloadProgress();
            _jobs.Add(new Job(evicted.CellX, evicted.CellY, JobKind.Unload, Unload(evicted.CellX, evicted.CellY, eviction).GetEnumerator(), eviction));
        }

        foreach (var cell in _toLoad)
        {
            var pendingGeneration = QueueNeighborhoodGeneration(cell.CellX, cell.CellY);
            _jobs.Add(new Job(cell.CellX, cell.CellY, JobKind.Load, Load(cell.CellX, cell.CellY, pendingGeneration).GetEnumerator()) { PendingGeneration = pendingGeneration, EarliestStartUpdateCount = _updateCount + StartDelayFrames });
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
    private IEnumerable<int> Unload(int cellX, int cellY, UnloadProgress? eviction = null)
    {
        if (eviction is not null)
        {
            if (_skeletons is not null)
            {
                foreach (var cost in DestroyEntitiesIn(cellX, cellY, entityId => !_skeletons.IsSkeleton(entityId)))
                {
                    yield return cost;
                }
            }

            eviction.BuiltCreaturesDestroyed = true;
        }

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
    /// <param name="which">Destroys only the entities it accepts; null for every one.</param>
    private IEnumerable<int> DestroyEntitiesIn(int cellX, int cellY, Func<int, bool>? which = null)
    {
        var entityIds = new List<int>();
        bool destroyedAny;
        do
        {
            destroyedAny = false;
            entityIds.Clear();
            for (var z = 0; z < _world.Map.Bounds.Depth; z++)
            {
                _resolver.Membership.CopyCell(cellX, cellY, z, entityIds);
            }

            foreach (var entityId in entityIds)
            {
                if (!_entityManager.EntityExists(entityId))
                {
                    _resolver.Forget(entityId);
                }
                else if (which is null || which(entityId))
                {
                    _entityManager.DestroyEntity(entityId);
                    _resolver.Forget(entityId);
                    destroyedAny = true;
                    yield return 1;
                }
            }
        }
        while (destroyedAny);
    }

    /// <summary>A neighborhood being planned on a worker: its task, how to stop it, and the population seed it plans with.</summary>
    /// <remarks>The seed is the record's pending one, not yet counted: a load dropped before it starts leaves the record as if it had never been queued, so the next visit still gets the population this one would have had.</remarks>
    private sealed class PendingNeighborhoodGeneration(NeighborhoodRecord neighborhoodRecord, int populationSeed, Task<NeighborhoodPlan> planningTask, CancellationTokenSource planningCancellation)
    {
        public NeighborhoodRecord NeighborhoodRecord { get; } = neighborhoodRecord;
        public int PopulationSeed { get; } = populationSeed;
        public Task<NeighborhoodPlan> PlanningTask { get; } = planningTask;
        public CancellationTokenSource PlanningCancellation { get; } = planningCancellation;
    }

    /// <summary>Starts every worker queued since the last update, unless its load was dropped in the meantime.</summary>
    /// <remarks>A shift queues its loads from inside another system's update, in the frame the player crossed -- already the heaviest frame of a shift. Starting the workers first thing in the next frame keeps them from competing with it for the CPU and memory.</remarks>
    private void StartAwaitingGenerations()
    {
        foreach (var awaitingGeneration in _generationsAwaitingStart)
        {
            if (!awaitingGeneration.PlanningCancellation.IsCancellationRequested && awaitingGeneration.PlanningTask.Status == TaskStatus.Created)
            {
                awaitingGeneration.PlanningTask.Start();
            }
        }

        _generationsAwaitingStart.Clear();
    }

    /// <summary>Queues planning neighborhood (cellX, cellY) on a worker, with its record's pending population seed; StartAwaitingGenerations starts it.</summary>
    private PendingNeighborhoodGeneration QueueNeighborhoodGeneration(int cellX, int cellY)
    {
        var neighborhoodRecord = _records.GetOrCreate(cellX, cellY);
        var populationSeed = neighborhoodRecord.PendingPopulationSeed;
        var map = _world.Map;
        var builder = _builder;
        var planningCancellation = new CancellationTokenSource();
        var planningCancellationToken = planningCancellation.Token;
        var planningTask = new Task<NeighborhoodPlan>(() => builder.Plan(map, neighborhoodRecord, populationSeed, planningCancellationToken), planningCancellationToken);

        var pendingGeneration = new PendingNeighborhoodGeneration(neighborhoodRecord, populationSeed, planningTask, planningCancellation);
        _generationsAwaitingStart.Add(pendingGeneration);
        return pendingGeneration;
    }

    /// <summary>Removes the job at index before it started, stopping its worker if it has one.</summary>
    private void DropUnstartedJob(int index)
    {
        _jobs[index].PendingGeneration?.PlanningCancellation.Cancel();
        _jobs.RemoveAt(index);
    }

    /// <summary>The worker's plan, waiting for it if it isn't finished, and the population it plans counted on its record.</summary>
    private NeighborhoodPlan FinishPendingGeneration(PendingNeighborhoodGeneration pendingGeneration)
    {
        StartAwaitingGenerations();
        var neighborhoodPlan = pendingGeneration.PlanningTask.GetAwaiter().GetResult();
        if (pendingGeneration.NeighborhoodRecord.NextPopulationSeed() != pendingGeneration.PopulationSeed)
        {
            throw new InvalidOperationException($"Neighborhood ({pendingGeneration.NeighborhoodRecord.CellX}, {pendingGeneration.NeighborhoodRecord.CellY}) was populated while its load was being planned.");
        }

        return neighborhoodPlan;
    }

    /// <summary>Loads the neighborhood's planned layout, announces it a row at a time with the row's planned aura cells, at LayoutRowCost each, then spawns its population, one unit per entity created.</summary>
    private IEnumerable<int> Load(int cellX, int cellY, PendingNeighborhoodGeneration pendingGeneration)
    {
        var neighborhoodPlan = FinishPendingGeneration(pendingGeneration);
        var neighborhoodLayout = neighborhoodPlan.Layout;
        _world.Map.LoadNeighborhood(neighborhoodLayout);

        for (var row = neighborhoodLayout.MinY; row < neighborhoodLayout.MaxY; row++)
        {
            var rowArea = new MapBounds(neighborhoodLayout.MinX, row, neighborhoodLayout.MaxX, row + 1, neighborhoodLayout.Depth);
            _eventBus.Publish(new TerrainLoadedEvent(rowArea, neighborhoodPlan.AuraCellsByRow[row - neighborhoodLayout.MinY]));
            yield return LayoutRowCost;
        }

        RestoreStraddlingFootprints(cellX, cellY);
        yield return LayoutRowCost;

        foreach (var createdEntityCount in _builder.Spawn(neighborhoodPlan))
        {
            yield return System.Math.Max(1, createdEntityCount);
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
