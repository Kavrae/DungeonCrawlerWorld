using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Effects;
using Game.Modules.Auras.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Engine.Tiles;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.World;

namespace Game.Modules.Auras.Systems;

/// <summary>Keeps the aura grid in step with its sources, tracks which entities stand inside which auras, and applies each aura's effect to them once a second.</summary>
/// <remarks>
/// <para>
/// <b>One rule for every aura: an effect is applied only by an entity's aura tick.</b> Coming into
/// range -- a move, a spawn, a source appearing nearby -- only starts an exposure
/// (AuraExposureComponent, one per entity and aura) and, for an entity with none yet, its one tick
/// (AuraExposureTickComponent), staggered within the next AuraEffects.TickIntervalFrames and repeating
/// every interval after. Each tick applies every aura the entity is exposed to and drops each exposure
/// whose aura no longer reaches it; a dead entity loses them all. So an entity that crosses an aura and
/// is out again before its tick is untouched, and the rate an aura applies at is a property of the
/// timer, never of the path an entity takes in and out of range. This is deliberately different from
/// TerrainContactSystem, which hits on every step onto a hazard.
/// </para>
/// <para>
/// What an aura does is its definition's Effects (AuraCatalog) -- the same Effect lists an action or
/// item holds, applied with no source entity and credited to the strongest contributor at the cell
/// (AuraField.Attribute: an entity source's entity or anchor placer, a terrain, or the aura) -- so a kill
/// by a lit idol is its holder's; this system knows no
/// particular effect. They are read from the catalog's definition at every tick, the one the session
/// fixed for the aura's Guid, whatever copy a source holds. An aura with no effects only glows: it is in the
/// grid, and nothing holds an exposure to it. The magnitude the effects are scaled by is the grid's
/// total for that aura at the entity's cell with the entity's own sources subtracted, so a source
/// never affects itself.
/// </para>
/// <para>
/// An exposure is held by anything in range that isn't dead, whether or not the effects can do
/// anything to it: nothing remembers "this entity can't be affected", so a change to the aura, its
/// power, its size or the entity is picked up by the next tick with nothing to invalidate. A tick whose
/// effects are refused (an immunity) reports that once and stays silent until something lands again
/// (AuraExposureComponent.Refused).
/// </para>
/// <para>
/// All range checks go through the AuraField (O(1) per lookup, keyed by cell and aura), never a live
/// scan around each mover: lava covers enough terrain, with a radius wide enough to blanket most of
/// a wandering population, that scanning per move was a measured production performance bug.
/// Ticks sit on a timer wheel for the same reason -- only the entities due this frame are touched,
/// one tick each however many auras they stand in.
/// Moves arrive through MovementSystem's shared FrameEventBuffer, drained at the start of Update,
/// not through an EventBus subscription per move.
/// </para>
/// <para>
/// The field holds terrain auras itself; this system puts entity sources into it, since it is what
/// knows where each one is (_sourcePlacementsInField):
/// - <b>Added.</b> It observes the source pool, so a source added by any path is noticed. On an
///   entity already on the map it goes into the field at once; on one still being spawned it waits
///   for the spawn's move.
/// - <b>First placement</b> of an entity's sources is immediate at every tier.
/// - <b>Moved.</b> Resynced on the move, at every tier: only sources on entities that can't move are in the field
///   (AuraPulseSources), so a field source moves a few times a session -- a teleport, a pile picked up.
/// - <b>Removed.</b> From AuraSourceRemovedEvent (a pool announces nothing on removal), taken out
///   of the field where the field has it, not where the entity is now.
/// A source is never changed in place: AuraSourceEffects replaces one by removing and adding.
/// </para>
/// <para>
/// <b>Pulses</b> (PLAN-aura-scaling.md, Decisions 2 and 4): a source on an entity that can move is never in the field.
/// Its carrier pulses on a schedule of its own -- every LocalPulsePeriodFrames at the Local tier, every
/// NeighborhoodPulsePeriodFrames at the Neighborhood tier, never while frozen, the first one staggered by entity id
/// across that period -- scanning each of its sources' reach for targets (the distance to a target's nearest footprint
/// tile; never the carrier) and writing the strongest power and its credit onto each target's exposure, live for the
/// period plus one frame. The target's tick takes that record or the field's value, whichever is stronger, so a slow
/// pulse is as strong as a fast one. A carrier whose tier changes pulses at once and keeps the new period. Turning a
/// pulse source off, or its carrier dying or walking away, leaves its records to run out (Decision 6).
/// </para>
/// </remarks>
public sealed class AuraSystem : ISystem
{
    /// <summary>How often a Local-tier carrier pulses: once a second, the tick's own interval.</summary>
    public const int LocalPulsePeriodFrames = AuraEffects.TickIntervalFrames;

    /// <summary>How often a Neighborhood-tier carrier pulses; its records last as long, so its targets still tick every second.</summary>
    public const int NeighborhoodPulsePeriodFrames = 4 * AuraEffects.TickIntervalFrames;

    /// <summary>Frames the pulse schedule's ring covers: more than the furthest deadline it is given -- a first pulse staggered up to one and a half periods out (FrameDeadline.AfterStaggered) -- so a deadline never lands on a slot that comes round before it.</summary>
    private const int PulseRingFrames = 512;

    /// <summary>Every frame: this frame's moves must be drained this frame, and the wheel only touches exposures actually due.</summary>
    public byte StripeCount => 1;

    private readonly MultiComponentPool<AuraExposureComponent> _exposures;
    private readonly PackedComponentPool<AuraExposureTickComponent> _exposureTicks;
    private readonly MultiComponentPool<AuraSourceComponent> _sources;
    private readonly DirectComponentPool<TransformComponent> _transforms;
    private readonly AuraCatalog _auras;
    private readonly IMapQuery _mapQuery;
    private readonly FrameEventBuffer<EntityMovedEvent> _movedEntities;
    private readonly PackedComponentPool<DeadComponent> _deadEntities;
    private readonly DirectComponentPool<ProcessingTierComponent> _processingTiers;
    private readonly SimulationClock _clock;
    private readonly SimulationScope _simulationScope;
    private readonly AuraField _auraField;
    private readonly EffectServices _effectServices;
    private readonly PackedComponentPool<AuraAnchorComponent> _anchors;

    /// <summary>Each exposed entity's one tick (AuraExposureTickComponent). Only entities due this frame are touched, on their exact frame at every processing tier.</summary>
    private readonly PackedTimerWheel<AuraExposureTickComponent> _exposureWheel;

    private readonly List<byte> _staleExposureAuraIdsScratch = [];

    /// <summary>The ticking entity's exposures, copied before any is applied: an effect can end exposures (a death taking its sources away re-checks everyone in their reach), so the tick never walks the pool's chain while effects run.</summary>
    private readonly List<AuraExposureComponent> _tickingExposuresScratch = [];

    /// <summary>
    /// The simulation frame the current entry point is running on -- set first thing by each one
    /// (Update from its EngineTime; the event handlers from the clock, since they fire from the
    /// EventBus outside this system's own Update: during population, or during another system's
    /// turn). Every exposure this system starts is scheduled from it.
    /// </summary>
    private long _now;

    // Cached once instead of passing the Tick method group every Update -- an instance method
    // group conversion allocates a fresh delegate every evaluation.
    private readonly TimerFired<AuraExposureTickComponent> _tick;

    /// <summary>Where the field holds each field source entity's sources (one that can't move; a pulse carrier's are never here). An entity with field sources and no entry has none of them in the field yet.</summary>
    private readonly Dictionary<int, SourcePlacement> _sourcePlacementsInField = [];

    /// <summary>Where the field holds an entity's sources, and whether the occupants in their reach were exposed when they were put there.</summary>
    /// <param name="ExposedOccupants">False when the entity wasn't simulated at the time: nobody in reach was scanned, so its next resync scans the whole reach rather than only the cells that changed sides.</param>
    private readonly record struct SourcePlacement(Vector3Int Position, bool ExposedOccupants);

    private readonly AuraPulseSources _pulses;
    private readonly IOccupiedCellQuery _occupiedCells;

    /// <summary>Each pulsing carrier's next pulse frame. A ring entry whose frame isn't its carrier's deadline any more is stale and passed over.</summary>
    private readonly Dictionary<int, long> _pulseDeadlines = [];

    /// <summary>The last frame whose due pulses ran; no pulse is scheduled on or before it.</summary>
    private long _pulsesRunThroughFrame = -1;

    /// <summary>The carriers due on each frame of the ring, by frame modulo PulseRingFrames.</summary>
    private readonly List<int>[] _pulseRing = CreatePulseRing();

    private readonly List<int> _duePulsesScratch = [];

    /// <summary>The pulsing carrier's sources with effects, copied before its reach is scanned.</summary>
    private readonly List<AuraSourceComponent> _pulsedSourcesScratch = [];

    /// <summary>The large targets a pulse has already reached through another of their tiles.</summary>
    private readonly HashSet<int> _pulsedLargeTargetsScratch = [];

    public AuraSystem(
        MultiComponentPool<AuraExposureComponent> exposures,
        PackedComponentPool<AuraExposureTickComponent> exposureTicks,
        MultiComponentPool<AuraSourceComponent> sources,
        DirectComponentPool<TransformComponent> transforms,
        IMapQuery mapQuery,
        EventBus eventBus,
        AuraCatalog auras,
        FrameEventBuffer<EntityMovedEvent> movedEntities,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        SimulationClock simulationClock,
        AuraField auraField,
        EffectServices effectServices,
        SimulationScope simulationScope,
        AuraPulseSources pulses,
        IOccupiedCellQuery occupiedCells,
        ProcessingTierEvents processingTierEvents)
    {
        _pulses = pulses;
        _occupiedCells = occupiedCells;
        processingTierEvents.TierChanged += OnTierChanged;
        _auraField = auraField;
        _exposures = exposures;
        _exposureTicks = exposureTicks;
        _sources = sources;
        _transforms = transforms;
        _mapQuery = mapQuery;
        _auras = auras;
        _movedEntities = movedEntities;
        _effectServices = effectServices;
        _anchors = effectServices.ComponentManager.GetPackedPool<AuraAnchorComponent>();
        _deadEntities = effectServices.DeadEntities;
        _processingTiers = processingTiers;
        _clock = simulationClock;

        sources.ComponentChanged += OnSourceAdded;
        eventBus.Subscribe<AuraSourceRemovedEvent>(OnSourceRemoved);
        auraField.TerrainAuraAdded += OnTerrainAuraAdded;
        auraField.TerrainAuraRemoved += OnTerrainAuraRemoved;

        _tick = Tick;
        _simulationScope = simulationScope;
        _exposureWheel = new PackedTimerWheel<AuraExposureTickComponent>(exposureTicks, simulationScope);
        simulationScope.EntityResumed += OnEntityResumed;
    }

    /// <summary>Picks a resumed entity back up: its tick moves past every one owed while it froze, applying none (see SkipOwedExposureTicks), and it is exposed to whatever aura it is standing in.</summary>
    /// <remarks>
    /// Runs after the wheel's own resume handler has rescheduled the stale deadline; rewriting it here reschedules again and leaves that earlier entry to be dropped as stale, the wheel's ordinary lazy cancellation.
    /// A frozen entity gains no exposure, so one that never moves -- a shrine, a chest -- would otherwise stay unexposed for good once its neighborhood is simulated.
    /// </remarks>
    private void OnEntityResumed(int entityId)
    {
        _now = _clock.CurrentFrame;
        SkipOwedExposureTicks(entityId, _now);

        if (_transforms.TryGetReadonly(entityId, out var transform))
        {
            StartExposures(entityId, transform.Position, transform.Size);
        }
    }

    /// <summary>Moves the entity's tick past every one owed while it was frozen, applying none: exposures do not accrue while frozen. Whatever an aura already started on the entity before it froze (a burn's stacks) keeps running through its own effect, which never stopped.</summary>
    private void SkipOwedExposureTicks(int entityId, long now)
    {
        if (_exposureTicks.TryGetReadonly(entityId, out var tick) && tick.NextTickFrame <= now)
        {
            _exposureTicks.TryUpdate(entityId, new SkipOwedPeriodsUpdate<AuraExposureTickComponent>(AuraEffects.TickIntervalFrames, now));
        }
    }

    /// <summary>A terrain cell started radiating: whoever already stands in range is exposed, the same as for a source that moved there.</summary>
    private void OnTerrainAuraAdded(Vector3Int position, AuraSourceComponent source)
    {
        _now = _clock.CurrentFrame;
        StartExposuresNear(position, AuraField.ReachOf(source));
    }

    /// <summary>A terrain cell stopped radiating: exposures it no longer reaches are dropped at once.</summary>
    private void OnTerrainAuraRemoved(Vector3Int position, AuraSourceComponent source) => ReEvaluateExposuresNear(position, AuraField.ReachOf(source));

    private void OnEntityMoved(EntityMovedEvent moved)
    {
        _auraField.EnsureBuilt();

        if (_sources.Has(moved.EntityId))
        {
            if (!_pulses.IsPulseCarrier(moved.EntityId))
            {
                ResyncSourceIfStale(moved.EntityId);
            }
            else if (_sourcePlacementsInField.ContainsKey(moved.EntityId))
            {
                LiftSourcesFromField(moved.EntityId);
                for (var denseIndex = _sources.GetFirstDenseIndex(moved.EntityId); denseIndex != -1; denseIndex = _sources.GetNextDenseIndex(denseIndex))
                {
                    _pulses.NoteReach(AuraField.ReachOf(_sources.GetReadonlyByDenseIndex(denseIndex)));
                }

                SchedulePulse(moved.EntityId, _now + 1);
            }
        }

        // A frozen entity gains no exposure while frozen -- it is exposed when it resumes (OnEntityResumed).
        // Only something built at spawn in a frozen tier gets here unsimulated: a creature there is a
        // skeleton, whose spawn records no move until it is built.
        if (_simulationScope.IsSimulated(moved.EntityId))
        {
            StartExposures(moved.EntityId, moved.NewPosition, moved.Size);
        }
    }

    /// <summary>The tile an entity's auras radiate from: the center of its footprint, so a large carrier's aura reaches as far on every side.</summary>
    /// <remarks>
    /// Every aura an entity carries -- one a toggle holds, one a Target-mode activation granted it (a torch read at a goblin),
    /// its blueprint's own -- radiates from here. A footprint even along an axis has no center tile on it; there the
    /// center is the middle tile nearer the footprint's origin (its Position, the corner it extends from).
    /// </remarks>
    public static Vector3Int SourceTileOf(in TransformComponent transform) =>
        transform.Position + new Vector3Int((transform.Size.X - 1) / 2, (transform.Size.Y - 1) / 2, 0);

    /// <summary>
    /// Moves entityId's sources in the field to where the entity is now, if that differs from where
    /// the field has them (_sourcePlacementsInField) -- a no-op otherwise. Called on the move for a
    /// Local-tier source and for any source's first placement, and from Update's queue for a
    /// source of any other tier that moved. Either way this is the only place a moving source's reach
    /// changes, so a source that moved several times while non-Local is taken out from where the
    /// field actually has it, not from one event's old position.
    /// </summary>
    /// <remarks>
    /// Each source is scanned by its own reach, and only over the cells that changed sides: an
    /// exposure can end only where the source's old reach covered and its new one doesn't, and can
    /// start only in the reverse. The whole new reach is scanned instead on a first placement, and
    /// when the last placement exposed nobody (SourcePlacement.ExposedOccupants).
    /// </remarks>
    private void ResyncSourceIfStale(int entityId)
    {
        if (!_transforms.TryGetReadonly(entityId, out var transform))
        {
            return;
        }

        var currentPosition = SourceTileOf(transform);
        var hadPreviousPlacement = _sourcePlacementsInField.TryGetValue(entityId, out var previousPlacement);
        var previousPosition = previousPlacement.Position;
        if (hadPreviousPlacement && previousPosition == currentPosition)
        {
            return;
        }

        if (!_mapQuery.IsOnMap(currentPosition))
        {
            LiftSourcesFromField(entityId);
            return;
        }

        // Every source is moved in the field before any cell is scanned, so a scan reads the totals as they end up.
        for (var denseIndex = _sources.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _sources.GetNextDenseIndex(denseIndex))
        {
            var source = _sources.GetReadonlyByDenseIndex(denseIndex);
            if (hadPreviousPlacement)
            {
                _auraField.MoveEntitySource(entityId, previousPosition, currentPosition, source);
            }
            else
            {
                _auraField.AddEntitySource(entityId, currentPosition, source, AttributionOf(entityId));
            }
        }

        // A source that isn't simulated is surrounded by occupants that aren't either, and those gain
        // no exposure while frozen (see StartExposuresNear): a shrine spawned into a frozen
        // neighborhood has nothing to scan for. A simulated occupant just across a tier boundary
        // from it is exposed by its own next move instead.
        var exposesOccupants = _simulationScope.IsSimulated(entityId);
        var overlapAlreadyExposed = hadPreviousPlacement && previousPlacement.ExposedOccupants;

        for (var denseIndex = _sources.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _sources.GetNextDenseIndex(denseIndex))
        {
            var reach = AuraField.ReachOf(_sources.GetReadonlyByDenseIndex(denseIndex));

            if (hadPreviousPlacement)
            {
                ReEvaluateExposuresNear(previousPosition, reach, exceptWithinReachOf: currentPosition);
            }

            if (exposesOccupants)
            {
                StartExposuresNear(currentPosition, reach, exceptWithinReachOf: overlapAlreadyExposed ? previousPosition : null);
            }
        }

        _sourcePlacementsInField[entityId] = new SourcePlacement(currentPosition, exposesOccupants);
    }

    /// <summary>Takes every source entityId still carries out of the field, for an entity that left the map: its reach goes with it.</summary>
    private void LiftSourcesFromField(int entityId)
    {
        if (!_sourcePlacementsInField.Remove(entityId, out var placementInField))
        {
            return;
        }

        for (var denseIndex = _sources.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _sources.GetNextDenseIndex(denseIndex))
        {
            _auraField.RemoveEntitySource(entityId, placementInField.Position, _sources.GetReadonlyByDenseIndex(denseIndex));
        }

        for (var denseIndex = _sources.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _sources.GetNextDenseIndex(denseIndex))
        {
            ReEvaluateExposuresNear(placementInField.Position, AuraField.ReachOf(_sources.GetReadonlyByDenseIndex(denseIndex)));
        }
    }

    /// <summary>Whom entityId's sources' effects are credited to (AuraField.Attribute): whoever placed it, for an anchor; otherwise the entity, as it is when its sources are first placed.</summary>
    private ActionSource AttributionOf(int entityId) =>
        _anchors.TryGetReadonly(entityId, out var anchor)
            ? anchor.PlacedBy
            : ActionSource.FromEntity(_effectServices.ComponentManager, _effectServices.EntityKeys, entityId, _effectServices.Definitions);

    /// <summary>Fails open to Beyond for a source with no ProcessingTierComponent yet -- the same "unknown = probably far" bias the stripe sets use -- so its moves are queued rather than resynced on the spot.</summary>
    private ProcessingTierLevel GetSourceTier(int entityId) =>
        _processingTiers.TryGetReadonly(entityId, out var tier) ? tier.Tier : ProcessingTierLevel.Beyond;

    /// <summary>Puts a source just added to the pool into the field, and exposes whoever already stands in range -- a stationary target doesn't have to move before an aura that was just turned on reaches it.</summary>
    /// <remarks>
    /// The pool's change notification, so it covers every way a source is added. If the field already
    /// holds the entity's other sources, the new one joins them where they are. Otherwise this is the
    /// entity's first placement and all of its sources go in where it stands -- unless it isn't on the
    /// map yet (a spawn in progress), in which case its spawn move places them.
    /// </remarks>
    private void OnSourceAdded(int entityId, int denseIndex)
    {
        _now = _clock.CurrentFrame;

        if (_pulses.IsPulseCarrier(entityId))
        {
            _pulses.NoteReach(AuraField.ReachOf(_sources.GetReadonlyByDenseIndex(denseIndex)));
            if (!_pulseDeadlines.ContainsKey(entityId))
            {
                SchedulePulse(entityId, FrameDeadline.AfterStaggered(_now, PulsePeriodOf(entityId), entityId));
            }

            return;
        }

        if (_sourcePlacementsInField.TryGetValue(entityId, out var placementInField))
        {
            var addedSource = _sources.GetReadonlyByDenseIndex(denseIndex);
            _auraField.AddEntitySource(entityId, placementInField.Position, addedSource, AttributionOf(entityId));
            StartExposuresNear(placementInField.Position, AuraField.ReachOf(addedSource));
            return;
        }

        ResyncSourceIfStale(entityId);
    }

    /// <summary>Takes a removed source out of the field from where the field has it, and at once drops the exposures it no longer reaches, so toggling off reads as instant.</summary>
    /// <remarks>A carrier left with no source leaves the pulse schedule at once: ids are recycled the moment an entity is destroyed, and the next holder of the id is scheduled afresh when its first source is added.</remarks>
    private void OnSourceRemoved(AuraSourceRemovedEvent removed)
    {
        if (!_sources.Has(removed.EntityId))
        {
            _pulseDeadlines.Remove(removed.EntityId);
        }

        if (!_sourcePlacementsInField.TryGetValue(removed.EntityId, out var placementInField))
        {
            return;
        }

        if (!_sources.Has(removed.EntityId))
        {
            _sourcePlacementsInField.Remove(removed.EntityId);
        }

        _auraField.RemoveEntitySource(removed.EntityId, placementInField.Position, removed.Source);
        ReEvaluateExposuresNear(placementInField.Position, AuraField.ReachOf(removed.Source));
    }

    /// <summary>Whether (x, y) on layer z is within reach tiles of center, by Manhattan distance on the same layer. False for no center.</summary>
    private static bool IsWithinReachOf(Vector3Int? center, int reach, int x, int y, int z) =>
        center is { } position && position.Z == z && Math.Abs(x - position.X) + Math.Abs(y - position.Y) <= reach;

    /// <summary>
    /// Start-only counterpart to ReEvaluateExposuresNear -- same diamond walk, but for
    /// wherever a source is now (just added, or just resynced to) rather than wherever one just left. The source entity itself showing
    /// up in this same scan is harmless: its own power is left out of its total (see
    /// PowerReaching).
    /// </summary>
    /// <remarks>Walks IMapQuery.GetOccupantEntityIdSpanAt per cell, every occupant Blocking or not, so Tiny/Phasing occupants are exposed too.</remarks>
    /// <param name="center">Where the source is.</param>
    /// <param name="reach">The source's reach (AuraField.ReachOf): the cells walked are those within this Manhattan distance of center.</param>
    /// <param name="exceptWithinReachOf">Where the source was, to skip the cells its reach already covered from there -- ruled out by arithmetic, with no map read. Null walks the whole reach.</param>
    private void StartExposuresNear(Vector3Int center, int reach, Vector3Int? exceptWithinReachOf = null)
    {
        var z = center.Z;

        for (var deltaY = -reach; deltaY <= reach; deltaY++)
        {
            var y = center.Y + deltaY;
            var remainingReach = reach - Math.Abs(deltaY);
            for (var x = center.X - remainingReach; x <= center.X + remainingReach; x++)
            {
                if (IsWithinReachOf(exceptWithinReachOf, reach, x, y, z))
                {
                    continue;
                }

                foreach (var occupantId in _mapQuery.GetOccupantEntityIdSpanAt(new Vector3Int(x, y, z)))
                {
                    // Frozen occupants gain no new exposure while frozen: a skeleton's starts when it is built (CreatureSkeletons.EnsureBuilt), anything else's when it resumes (OnEntityResumed).
                    if (!_simulationScope.IsSimulated(occupantId))
                    {
                        continue;
                    }

                    if (!_transforms.TryGetReadonly(occupantId, out var occupantTransform))
                    {
                        continue;
                    }

                    StartExposures(occupantId, occupantTransform.Position, occupantTransform.Size);
                }
            }
        }
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        _now = time.FrameCount;

        // The buffer drain itself is NOT ProcessingTier-gated -- it only ever processes
        // entities that actually moved this exact frame (already self-limiting, unlike the
        // periodic passes below).
        foreach (var moved in _movedEntities.ItemSpan)
        {
            OnEntityMoved(moved);
        }

        // Still needed here, idempotently, in case this frame had zero buffered moves.
        _auraField.EnsureBuilt();

        RunDuePulses();

        _exposureWheel.Tick(time.FrameCount, _tick);
    }

    /// <summary>Applies one tick of every aura the entity is exposed to, and ends each exposure whose aura no longer reaches it. Returns whether the tick itself should be removed: the entity has no exposure left.</summary>
    /// <remarks>
    /// A dead entity, or one with no position, loses every exposure. Otherwise each exposure applies its aura at the power
    /// reaching the entity now, or is removed if none does (or the aura has no effects). The exposures are copied first
    /// (_tickingExposuresScratch) and each is found again by aura to write or remove it, since an effect may end other
    /// exposures of this entity. False re-arms the tick itself -- see TimerFired's contract.
    /// </remarks>
    private bool Tick(int entityId, AuraExposureTickComponent tick, long now)
    {
        if (_deadEntities.Has(entityId) || !_transforms.TryGetReadonly(entityId, out var transform))
        {
            _exposures.Remove(entityId);
            return true;
        }

        _tickingExposuresScratch.Clear();
        for (var denseIndex = _exposures.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _exposures.GetNextDenseIndex(denseIndex))
        {
            _tickingExposuresScratch.Add(_exposures.GetReadonlyByDenseIndex(denseIndex));
        }

        foreach (var exposure in _tickingExposuresScratch)
        {
            ApplyExposure(entityId, transform, exposure, now);
        }

        if (!_exposures.Has(entityId))
        {
            return true;
        }

        using var rearmedTick = _exposureTicks.TryWrite(entityId);
        if (rearmedTick.Found)
        {
            rearmedTick.Value.AdvanceOnePeriod(AuraEffects.TickIntervalFrames);
        }

        return false;
    }

    /// <summary>Applies one exposure's aura to the entity at the strongest power reaching its footprint -- the field's, or a live pulse record's, whichever is stronger -- or removes the exposure if nothing reaches it.</summary>
    private void ApplyExposure(int entityId, in TransformComponent transform, AuraExposureComponent exposure, long now)
    {
        var auraId = exposure.AuraId;
        var definition = _auras.Get(auraId);
        ActionSource credit = default;
        var power = definition.HasEffects ? PowerReachingFootprint(entityId, transform.Position, transform.Size, auraId, out credit) : 0;
        if (definition.HasEffects && exposure.PulseExpiresAtFrame > now && exposure.PulsePower > power)
        {
            power = exposure.PulsePower;
            credit = exposure.PulseCredit;
        }

        if (power <= 0)
        {
            _exposures.RemoveFirst(entityId, new AuraExposureMatcher(auraId));
            return;
        }

        var context = new EffectContext(_effectServices, credit, SourceEntityId: null, entityId, definition.EffectName, definition.Tags, now)
        {
            Magnitude = definition.MagnitudeAt(power),
            AnnouncesRefusal = !exposure.Refused,
        };

        var refused = EffectSequence.Apply(definition.Effects, in context) == EffectOutcome.Refused;
        if (refused == exposure.Refused)
        {
            return;
        }

        using var refusalWrite = _exposures.TryWriteFirst(entityId, new AuraExposureMatcher(auraId));
        if (refusalWrite.Found)
        {
            refusalWrite.Value.Refused = refused;
        }
    }

    /// <summary>Starts an exposure for every aura with effects that reaches the footprint at position and that the entity isn't already exposed to, and the entity's tick if it has none. Applies nothing: the tick does.</summary>
    /// <remarks>Safe to call for an entity already exposed to some auras -- each aura is checked on its own, so being inside one never hides coming into range of another. A corpse is never exposed.</remarks>
    private void StartExposures(int entityId, Vector3Int position, Vector2Byte size)
    {
        // Most movers are in no aura at all, which the field answers without a lookup per aura.
        if (!AnyAuraReachesFootprint(position, size) || _deadEntities.Has(entityId))
        {
            return;
        }

        foreach (var auraId in _auraField.AuraIdsInField)
        {
            if (_auras.Get(auraId).HasEffects && PowerReachingFootprint(entityId, position, size, auraId) > 0 && !HasExposure(entityId, auraId))
            {
                _exposures.Add(entityId, new AuraExposureComponent(auraId));
                EnsureTick(entityId);
            }
        }
    }

    /// <summary>Gives an exposed entity its tick, staggered within the next interval, unless it already has one: a newly entered aura applies at the entity's next tick.</summary>
    private void EnsureTick(int entityId)
    {
        if (!_exposureTicks.Has(entityId))
        {
            _exposureTicks.Add(entityId, new AuraExposureTickComponent(FrameDeadline.AfterStaggered(_now, AuraEffects.TickIntervalFrames, entityId)));
        }
    }

    private bool HasExposure(int entityId, byte auraId)
    {
        for (var denseIndex = _exposures.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _exposures.GetNextDenseIndex(denseIndex))
        {
            if (_exposures.GetReadonlyByDenseIndex(denseIndex).AuraId == auraId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The strongest power of the aura reaching any tile of a footprint, the sources the entity carries left out (AuraField.PowerReaching): a large entity is in an aura wherever any of its tiles is, and a source never affects the entity carrying it.</summary>
    private int PowerReachingFootprint(int entityId, Vector3Int position, Vector2Byte size, byte auraId)
    {
        if (size.X <= 1 && size.Y <= 1)
        {
            return _auraField.PowerReaching(position, auraId, entityId);
        }

        var strongest = 0;
        for (var deltaY = 0; deltaY < size.Y; deltaY++)
        {
            for (var deltaX = 0; deltaX < size.X; deltaX++)
            {
                var tile = position + new Vector3Int(deltaX, deltaY, 0);
                if (_auraField.GetTotalPowerAt(tile, auraId) > strongest)
                {
                    strongest = Math.Max(strongest, _auraField.PowerReaching(tile, auraId, entityId));
                }
            }
        }

        return strongest;
    }

    /// <summary>PowerReachingFootprint, and whom the strongest tile's power is credited to (AuraField.PowerReaching).</summary>
    /// <remarks>
    /// A tile's power is at most its total (AuraField.GetTotalPowerAt, an array read), so the tile with the largest total
    /// is asked in full first, and any other only if its total could beat what was found: a large entity's tick asks one
    /// or two of its tiles in full, not all of them.
    /// </remarks>
    private int PowerReachingFootprint(int entityId, Vector3Int position, Vector2Byte size, byte auraId, out ActionSource credit)
    {
        if (size.X <= 1 && size.Y <= 1)
        {
            return _auraField.PowerReaching(position, auraId, entityId, out credit);
        }

        var firstTile = position;
        var firstTileTotal = int.MinValue;
        for (var deltaY = 0; deltaY < size.Y; deltaY++)
        {
            for (var deltaX = 0; deltaX < size.X; deltaX++)
            {
                var tile = position + new Vector3Int(deltaX, deltaY, 0);
                var total = _auraField.GetTotalPowerAt(tile, auraId);
                if (total > firstTileTotal)
                {
                    firstTile = tile;
                    firstTileTotal = total;
                }
            }
        }

        var strongest = _auraField.PowerReaching(firstTile, auraId, entityId, out credit);
        for (var deltaY = 0; deltaY < size.Y; deltaY++)
        {
            for (var deltaX = 0; deltaX < size.X; deltaX++)
            {
                var tile = position + new Vector3Int(deltaX, deltaY, 0);
                if (tile == firstTile || _auraField.GetTotalPowerAt(tile, auraId) <= strongest)
                {
                    continue;
                }

                var power = _auraField.PowerReaching(tile, auraId, entityId, out var tileCredit);
                if (power > strongest)
                {
                    strongest = power;
                    credit = tileCredit;
                }
            }
        }

        return strongest;
    }

    /// <summary>Whether any aura reaches any tile of a footprint -- an array read a tile, for ruling an entity out before asking about each aura.</summary>
    private bool AnyAuraReachesFootprint(Vector3Int position, Vector2Byte size)
    {
        for (var deltaY = 0; deltaY < size.Y; deltaY++)
        {
            for (var deltaX = 0; deltaX < size.X; deltaX++)
            {
                if (_auraField.AnyAuraReaches(position + new Vector3Int(deltaX, deltaY, 0)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Removal-only re-check for occupants near a moving/toggled aura source. Per aura: an
    /// occupant exposed to two (e.g. Burning from one source, Poison from another) only has the
    /// one whose power actually dropped to zero removed, rather than waiting for that
    /// entity's next tick to find it out of range.
    /// </summary>
    /// <remarks>See StartExposuresNear's own remark -- same GetOccupantEntityIdSpanAt-per-cell walk, so a Tiny/Phasing occupant's exposure is dropped when it (or the source) moves out of range too.</remarks>
    /// <param name="center">Where the source was.</param>
    /// <param name="reach">The source's reach (AuraField.ReachOf): the cells walked are those within this Manhattan distance of center.</param>
    /// <param name="exceptWithinReachOf">Where the source is now, to skip the cells its reach still covers from there -- ruled out by arithmetic, with no map read. Null walks the whole reach.</param>
    private void ReEvaluateExposuresNear(Vector3Int center, int reach, Vector3Int? exceptWithinReachOf = null)
    {
        var z = center.Z;

        for (var deltaY = -reach; deltaY <= reach; deltaY++)
        {
            var y = center.Y + deltaY;
            var remainingReach = reach - Math.Abs(deltaY);
            for (var x = center.X - remainingReach; x <= center.X + remainingReach; x++)
            {
                if (IsWithinReachOf(exceptWithinReachOf, reach, x, y, z))
                {
                    continue;
                }

                foreach (var occupantId in _mapQuery.GetOccupantEntityIdSpanAt(new Vector3Int(x, y, z)))
                {
                    // A frozen occupant is left as it is: an unbuilt skeleton holds no exposures to read, and a
                    // built one's are dropped by their own tick once it resumes.
                    if (!_simulationScope.IsSimulated(occupantId) || !_transforms.TryGetReadonly(occupantId, out var occupantTransform))
                    {
                        continue;
                    }

                    // Snapshot which of the occupant's current exposures are now out of range
                    // before removing any of them -- MultiComponentPool.RemoveFirst reorders the dense
                    // chain, so removing mid-walk of that same chain would skip or revisit entries.
                    _staleExposureAuraIdsScratch.Clear();
                    for (var denseIndex = _exposures.GetFirstDenseIndex(occupantId); denseIndex != -1; denseIndex = _exposures.GetNextDenseIndex(denseIndex))
                    {
                        var exposure = _exposures.GetReadonlyByDenseIndex(denseIndex);
                        if (exposure.PulseExpiresAtFrame <= _clock.CurrentFrame && PowerReachingFootprint(occupantId, occupantTransform.Position, occupantTransform.Size, exposure.AuraId) <= 0)
                        {
                            _staleExposureAuraIdsScratch.Add(exposure.AuraId);
                        }
                    }

                    foreach (var staleAuraId in _staleExposureAuraIdsScratch)
                    {
                        _exposures.RemoveFirst(occupantId, new AuraExposureMatcher(staleAuraId));
                    }
                }
            }
        }
    }

    private static List<int>[] CreatePulseRing()
    {
        var ring = new List<int>[PulseRingFrames];
        for (var frame = 0; frame < ring.Length; frame++)
        {
            ring[frame] = [];
        }

        return ring;
    }

    /// <summary>How often entityId pulses at its tier: Local once a second, any other tier every NeighborhoodPulsePeriodFrames.</summary>
    private int PulsePeriodOf(int entityId) =>
        GetSourceTier(entityId) == ProcessingTierLevel.Local ? LocalPulsePeriodFrames : NeighborhoodPulsePeriodFrames;

    /// <summary>Sets entityId's next pulse to deadline, replacing any it had: an earlier ring entry for it is then stale.</summary>
    /// <remarks>Never on or before a frame whose pulses already ran: a tier change or a source added after this frame's pulses (another system's turn, between frames) would otherwise land on a slot already emptied, and wait a whole turn of the ring to be passed over.</remarks>
    private void SchedulePulse(int entityId, long deadline)
    {
        deadline = Math.Max(deadline, _pulsesRunThroughFrame + 1);
        _pulseDeadlines[entityId] = deadline;
        _pulseRing[(int)(deadline % PulseRingFrames)].Add(entityId);
    }

    /// <summary>A carrier's tier changed: it pulses at once, and keeps the new tier's period from there, so its targets' records never run out between an old period's pulse and a new one's.</summary>
    private void OnTierChanged(int entityId, ProcessingTierLevel tier)
    {
        if (_pulseDeadlines.ContainsKey(entityId))
        {
            SchedulePulse(entityId, _clock.CurrentFrame + 1);
        }
    }

    /// <summary>Runs every pulse due this frame, then schedules each carrier's next one.</summary>
    /// <remarks>
    /// A carrier that lost its sources leaves the schedule; one that can no longer move puts its sources in the field
    /// instead (Decision 2); a frozen one owes nothing and is looked at again a Neighborhood period later.
    /// </remarks>
    private void RunDuePulses()
    {
        _pulsesRunThroughFrame = _now;
        var bucket = _pulseRing[(int)(_now % PulseRingFrames)];
        if (bucket.Count == 0)
        {
            return;
        }

        _duePulsesScratch.Clear();
        _duePulsesScratch.AddRange(bucket);
        bucket.Clear();

        foreach (var entityId in _duePulsesScratch)
        {
            if (!_pulseDeadlines.TryGetValue(entityId, out var deadline) || deadline != _now)
            {
                continue;
            }

            if (!_sources.Has(entityId))
            {
                _pulseDeadlines.Remove(entityId);
                continue;
            }

            if (!_pulses.IsPulseCarrier(entityId))
            {
                _pulseDeadlines.Remove(entityId);
                ResyncSourceIfStale(entityId);
                continue;
            }

            if (!_simulationScope.IsSimulated(entityId))
            {
                SchedulePulse(entityId, _now + NeighborhoodPulsePeriodFrames);
                continue;
            }

            var period = PulsePeriodOf(entityId);
            Pulse(entityId, period);
            SchedulePulse(entityId, _now + period);
        }
    }

    /// <summary>Writes what each of a carrier's sources gives every target in its reach onto the target's exposure, live for period plus one frame.</summary>
    /// <remarks>
    /// A target is reached by the distance from the carrier's source tile to the nearest tile of its footprint, and is
    /// counted once however many of its tiles are in reach. The carrier is never its own target, a dead or frozen target is
    /// passed over, and so is an aura with no effects: it only glows (AuraPulseSources answers for the glow and
    /// inspection).
    /// </remarks>
    private void Pulse(int carrierId, int period)
    {
        if (_deadEntities.Has(carrierId) || !_transforms.TryGetReadonly(carrierId, out var carrierTransform) || !_mapQuery.IsOnMap(carrierTransform.Position))
        {
            return;
        }

        _pulsedSourcesScratch.Clear();
        var reach = 0;
        for (var denseIndex = _sources.GetFirstDenseIndex(carrierId); denseIndex != -1; denseIndex = _sources.GetNextDenseIndex(denseIndex))
        {
            var source = _sources.GetReadonlyByDenseIndex(denseIndex);
            if (_auras.Get(source.AuraId).HasEffects)
            {
                _pulsedSourcesScratch.Add(source);
                reach = Math.Max(reach, AuraField.ReachOf(source));
            }
        }

        if (_pulsedSourcesScratch.Count == 0)
        {
            return;
        }

        _pulses.CountPulse();
        var center = SourceTileOf(carrierTransform);
        var credit = AttributionOf(carrierId);
        var expiresAtFrame = (uint)(_now + period + 1);
        _pulsedLargeTargetsScratch.Clear();

        foreach (var cell in _occupiedCells.GetOccupiedCellsIn(center.X - reach, center.Y - reach, center.X + reach + 1, center.Y + reach + 1, center.Z))
        {
            if (Math.Abs(cell.Position.X - center.X) + Math.Abs(cell.Position.Y - center.Y) > reach)
            {
                continue;
            }

            foreach (var targetId in cell.Occupants)
            {
                if (targetId == carrierId
                    || !_transforms.TryGetReadonly(targetId, out var targetTransform)
                    || (IsLarge(targetTransform) && !_pulsedLargeTargetsScratch.Add(targetId))
                    || !_simulationScope.IsSimulated(targetId)
                    || _deadEntities.Has(targetId))
                {
                    continue;
                }

                var distance = DistanceToFootprint(center, targetTransform);
                foreach (var source in _pulsedSourcesScratch)
                {
                    if (distance > source.Size)
                    {
                        continue;
                    }

                    var value = _auras.Get(source.AuraId).Falloff.ValueAt(source.Power, source.Size, distance);
                    if (value > 0)
                    {
                        RecordPulse(targetId, source.AuraId, (ushort)Math.Min(value, ushort.MaxValue), credit, expiresAtFrame);
                    }
                }
            }
        }
    }

    private static bool IsLarge(in TransformComponent transform) => transform.Size.X > 1 || transform.Size.Y > 1;

    /// <summary>The Manhattan distance from tile to the nearest tile of transform's footprint, on its layer.</summary>
    private static int DistanceToFootprint(Vector3Int tile, in TransformComponent transform)
    {
        var origin = transform.Position;
        var deltaX = Math.Max(0, Math.Max(origin.X - tile.X, tile.X - (origin.X + transform.Size.X - 1)));
        var deltaY = Math.Max(0, Math.Max(origin.Y - tile.Y, tile.Y - (origin.Y + transform.Size.Y - 1)));
        return deltaX + deltaY;
    }

    /// <summary>Whether two pulse records were written by the same carrier: a carrier's newer pulse always replaces its own earlier one, so walking away weakens it at once, while within one pulse its strongest source of the aura holds.</summary>
    private static bool IsSameCarrier(ActionSource recorded, ActionSource credit) => recorded.Kind == credit.Kind && recorded.Key == credit.Key;

    /// <summary>Writes a pulse's power onto the target's exposure to the aura -- unless a live record there from another carrier is stronger -- adding the exposure, and the target's tick, if it has none.</summary>
    private void RecordPulse(int targetId, byte auraId, ushort power, ActionSource credit, uint expiresAtFrame)
    {
        _pulses.CountRecord();
        for (var denseIndex = _exposures.GetFirstDenseIndex(targetId); denseIndex != -1; denseIndex = _exposures.GetNextDenseIndex(denseIndex))
        {
            var exposure = _exposures.GetReadonlyByDenseIndex(denseIndex);
            if (exposure.AuraId != auraId)
            {
                continue;
            }

            var writtenByThisPulse = exposure.PulseExpiresAtFrame == expiresAtFrame && IsSameCarrier(exposure.PulseCredit, credit);
            if (exposure.PulseExpiresAtFrame > _now && exposure.PulsePower > power && (writtenByThisPulse || !IsSameCarrier(exposure.PulseCredit, credit)))
            {
                return;
            }

            using var write = _exposures.WriteByDenseIndex(denseIndex);
            write.Value.PulsePower = power;
            write.Value.PulseCredit = credit;
            write.Value.PulseExpiresAtFrame = expiresAtFrame;
            return;
        }

        _exposures.Add(targetId, new AuraExposureComponent(auraId) { PulsePower = power, PulseCredit = credit, PulseExpiresAtFrame = expiresAtFrame });
        EnsureTick(targetId);
    }
}
