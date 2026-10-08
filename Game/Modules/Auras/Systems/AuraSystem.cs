using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Effects;
using Game.Modules.Auras.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.ProcessingTier.Components;
using Game.World;

namespace Game.Modules.Auras.Systems;

/// <summary>Keeps the aura grid in step with its sources, tracks which entities stand inside which auras, and applies each aura's effect to them once a second.</summary>
/// <remarks>
/// <para>
/// <b>One rule for every aura: an effect is applied only by an exposure's tick.</b> Coming into
/// range -- a move, a spawn, a source appearing nearby -- only starts an exposure
/// (AuraExposureComponent, one per entity and aura), whose first tick is staggered within the next
/// AuraEffects.TickIntervalFrames and repeats every interval after. A tick that finds the entity out
/// of range or dead removes the exposure and applies nothing. So an entity
/// that crosses an aura and is out again before its tick is untouched, and the rate an aura applies
/// at is a property of the timer, never of the path an entity takes in and out of range. This is
/// deliberately different from TerrainContactSystem, which hits on every step onto a hazard.
/// </para>
/// <para>
/// What an aura does is its definition's Effects (AuraCatalog) -- the same Effect lists an action or
/// item holds, applied with no source entity and credited to the strongest contributor at the cell
/// (AuraField.Attribute: an entity source's entity or anchor placer, a terrain, or the aura) -- so a kill
/// by a lit idol is its holder's; this system knows no
/// particular effect. They are read from the definition at every tick, so a definition replaced
/// during a session is what the next tick applies. An aura with no effects only glows: it is in the
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
/// Exposures sit on a timer wheel for the same reason -- only the ones due this frame are touched.
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
/// - <b>Moved.</b> A Local-tier source is resynced on the move, where the player can see it. Any
///   other tier is left where the field has it and queued; Update resyncs a few queued sources a
///   frame (MaximumDeferredResyncsPerFrame), which keeps a moving source's resync from
///   multiplying across a population of them. A source that never moves is never queued, so
///   stationary sources -- most of them -- cost nothing per frame.
/// - <b>Removed.</b> From AuraSourceRemovedEvent (a pool announces nothing on removal), taken out
///   of the field where the field has it, not where the entity is now.
/// A source is never changed in place: AuraSourceEffects replaces one by removing and adding.
/// </para>
/// </remarks>
public sealed class AuraSystem : ISystem
{
    /// <summary>How many non-Local sources that moved are resynced into the field per frame; the rest wait their turn in the order they moved.</summary>
    private const int MaximumDeferredResyncsPerFrame = 8;

    /// <summary>Every frame: this frame's moves must be drained this frame, and the wheel only touches exposures actually due.</summary>
    public byte StripeCount => 1;

    private readonly MultiComponentPool<AuraExposureComponent> _exposures;
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

    /// <summary>
    /// Each exposure's tick, keyed by AuraId (see AuraExposureComponent). Only exposures due this
    /// frame are touched, on their exact frame at every processing tier.
    /// </summary>
    private readonly MultiTimerWheel<AuraExposureComponent> _exposureWheel;

    private readonly List<byte> _staleExposureAuraIdsScratch = [];

    /// <summary>
    /// The simulation frame the current entry point is running on -- set first thing by each one
    /// (Update from its EngineTime; the event handlers from the clock, since they fire from the
    /// EventBus outside this system's own Update: during population, or during another system's
    /// turn). Every exposure this system starts is scheduled from it.
    /// </summary>
    private long _now;

    // Cached once instead of passing the Tick method group every Update -- an instance method
    // group conversion allocates a fresh delegate every evaluation.
    private readonly TimerFired<AuraExposureComponent> _tick;

    /// <summary>Where the field holds each source entity's sources. May lag a non-Local source's real (_transforms) position by however many moves it has made since its last resync -- see ResyncSourceIfStale. An entity with sources and no entry has none of them in the field yet.</summary>
    private readonly Dictionary<int, SourcePlacement> _sourcePlacementsInField = [];

    /// <summary>Where the field holds an entity's sources, and whether the occupants in their reach were exposed when they were put there.</summary>
    /// <param name="ExposedOccupants">False when the entity wasn't simulated at the time: nobody in reach was scanned, so its next resync scans the whole reach rather than only the cells that changed sides.</param>
    private readonly record struct SourcePlacement(Vector3Int Position, bool ExposedOccupants);

    /// <summary>The non-Local sources that moved and have not been resynced yet, in the order they moved, each once.</summary>
    private readonly Queue<int> _unsyncedSourceQueue = new();

    /// <inheritdoc cref="_unsyncedSourceQueue"/>
    private readonly HashSet<int> _unsyncedSources = [];

    public AuraSystem(
        MultiComponentPool<AuraExposureComponent> exposures,
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
        SimulationScope simulationScope)
    {
        _auraField = auraField;
        _exposures = exposures;
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
        auras.DefinitionChanged += OnAuraDefinitionChanged;

        _tick = Tick;
        _simulationScope = simulationScope;
        _exposureWheel = new MultiTimerWheel<AuraExposureComponent>(exposures, simulationScope);
        simulationScope.EntityResumed += OnEntityResumed;
    }

    /// <summary>Picks a resumed entity back up: its exposures move past every tick owed while it froze, applying none (see SkipOwedExposureTicks), and it is exposed to whatever aura it is standing in.</summary>
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
            StartExposures(entityId, transform.Position);
        }
    }

    /// <summary>Moves each of the entity's exposures past every tick owed while it was frozen, applying none: exposures do not accrue while frozen. Whatever an aura already started on the entity before it froze (a burn's stacks) keeps running through its own effect, which never stopped.</summary>
    private void SkipOwedExposureTicks(int entityId, long now)
    {
        for (var denseIndex = _exposures.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _exposures.GetNextDenseIndex(denseIndex))
        {
            if (_exposures.GetReadonlyByDenseIndex(denseIndex).NextTickFrame <= now)
            {
                _exposures.UpdateByDenseIndex(denseIndex, now, static (ref AuraExposureComponent e, long frame) => e.SkipOwedPeriods(AuraEffects.TickIntervalFrames, frame));
            }
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

    /// <summary>An aura's definition was replaced. If it has effects, whoever stands in reach of one of its sources is exposed -- before, with no effects, nothing held an exposure to it. One that lost its effects needs nothing: each exposure ends on its next tick.</summary>
    /// <remarks>Walks every entity source and every loaded terrain cell for the ones radiating this aura: rare, and there is no index from an aura to its sources.</remarks>
    private void OnAuraDefinitionChanged(byte auraId)
    {
        if (!_auras.Get(auraId).HasEffects)
        {
            return;
        }

        _now = _clock.CurrentFrame;
        _auraField.EnsureBuilt();

        var sourceEntityIds = _sources.EntityIds;
        var sourceComponents = _sources.Components;
        for (var index = 0; index < sourceEntityIds.Length; index++)
        {
            if (sourceComponents[index].AuraId == auraId && _sourcePlacementsInField.TryGetValue(sourceEntityIds[index], out var placementInField))
            {
                StartExposuresNear(placementInField.Position, AuraField.ReachOf(sourceComponents[index]));
            }
        }

        _auraField.ForEachTerrainSource(auraId, (position, source) => StartExposuresNear(position, AuraField.ReachOf(source)));
    }

    private void OnEntityMoved(EntityMovedEvent moved)
    {
        _auraField.EnsureBuilt();

        if (_sources.Has(moved.EntityId))
        {
            if (!_sourcePlacementsInField.ContainsKey(moved.EntityId) || GetSourceTier(moved.EntityId) == ProcessingTierLevel.Local)
            {
                ResyncSourceIfStale(moved.EntityId);
            }
            else if (_unsyncedSources.Add(moved.EntityId))
            {
                _unsyncedSourceQueue.Enqueue(moved.EntityId);
            }
        }

        // A frozen entity gains no exposure while frozen -- it is exposed when it resumes (OnEntityResumed).
        // Only something built at spawn in a frozen tier gets here unsimulated: a creature there is a
        // skeleton, whose spawn records no move until it is built.
        if (_simulationScope.IsSimulated(moved.EntityId))
        {
            StartExposures(moved.EntityId, moved.NewPosition);
        }
    }

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

        var currentPosition = transform.Position;
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
    private void OnSourceRemoved(AuraSourceRemovedEvent removed)
    {
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
    /// <remarks>Walks IMapQuery.GetOccupantEntityIdSpanAt per cell rather than the Blocking-only GetEntityIdsInBox, so Tiny/Phasing occupants are exposed too.</remarks>
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

                    StartExposures(occupantId, occupantTransform.Position);
                }
            }
        }
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        _now = time.FrameCount;

        // Before this frame's moves are read, so a source queued by one of them waits at least a frame.
        ResyncQueuedSources();

        // The buffer drain itself is NOT ProcessingTier-gated -- it only ever processes
        // entities that actually moved this exact frame (already self-limiting, unlike the
        // periodic passes below).
        foreach (var moved in _movedEntities.ItemSpan)
        {
            OnEntityMoved(moved);
        }

        // Still needed here, idempotently, in case this frame had zero buffered moves.
        _auraField.EnsureBuilt();

        _exposureWheel.Tick(time.FrameCount, _tick);
    }

    /// <summary>Resyncs the sources that have waited longest since they moved, up to MaximumDeferredResyncsPerFrame.</summary>
    /// <remarks>A queued entity that has since lost its sources, or been destroyed, is passed over: its removal already took it out of the field.</remarks>
    private void ResyncQueuedSources()
    {
        for (var resynced = 0; resynced < MaximumDeferredResyncsPerFrame && _unsyncedSourceQueue.TryDequeue(out var entityId); resynced++)
        {
            _unsyncedSources.Remove(entityId);
            if (_sources.Has(entityId))
            {
                ResyncSourceIfStale(entityId);
            }
        }
    }

    /// <summary>Applies one tick of the exposure's aura to the entity, or ends the exposure. Returns whether this (entity, aura) exposure should be removed -- the wheel removes that one instance by AuraId.</summary>
    /// <remarks>True when the entity is dead, has no position, is out of the aura's range, or the aura has no effects. False re-arms the exposure's own next tick itself (via TryUpdateFirst, matched by AuraId) -- see TimerFired's contract.</remarks>
    private bool Tick(int entityId, AuraExposureComponent exposure, long now)
    {
        if (_deadEntities.Has(entityId) || !_transforms.TryGetReadonly(entityId, out var transform))
        {
            return true;
        }

        var auraId = exposure.AuraId;
        var definition = _auras.Get(auraId);
        if (!definition.HasEffects)
        {
            return true;
        }

        var power = _auraField.PowerReaching(transform.Position, auraId, entityId, out var credit);
        if (power <= 0)
        {
            return true;
        }

        var context = new EffectContext(_effectServices, credit, SourceEntityId: null, entityId, definition.EffectName, definition.Tags, now)
        {
            Magnitude = definition.Magnitude == AuraMagnitude.Flat ? 1f : power,
            AnnouncesRefusal = !exposure.Refused,
        };

        var refused = EffectSequence.Apply(definition.Effects, in context) == EffectOutcome.Refused;

        _exposures.TryUpdateFirst(entityId, (auraId, refused),
            static (ref readonly AuraExposureComponent e, (byte AuraId, bool Refused) state) => e.AuraId == state.AuraId,
            static (ref AuraExposureComponent e, (byte AuraId, bool Refused) state) =>
            {
                e.Refused = state.Refused;
                e.RepeatEvery(AuraEffects.TickIntervalFrames);
            });

        return false;
    }

    /// <summary>Starts an exposure for every aura with effects that reaches position and that the entity isn't already exposed to. Applies nothing: the exposure's tick does.</summary>
    /// <remarks>Safe to call for an entity already exposed to some auras -- each aura is checked on its own, so being inside one never hides coming into range of another. A corpse is never exposed.</remarks>
    private void StartExposures(int entityId, Vector3Int position)
    {
        // Most movers are in no aura at all, which the field answers without a lookup per aura.
        if (!_auraField.AnyAuraReaches(position) || _deadEntities.Has(entityId))
        {
            return;
        }

        foreach (var auraId in _auraField.AuraIdsInField)
        {
            if (_auras.Get(auraId).HasEffects && PowerReaching(entityId, position, auraId) > 0 && !HasExposure(entityId, auraId))
            {
                _exposures.Add(entityId, new AuraExposureComponent(auraId, FrameDeadline.AfterStaggered(_now, AuraEffects.TickIntervalFrames, entityId)));
            }
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

    /// <summary>The aura's power reaching entityId at position, the sources it carries left out (AuraField.PowerReaching): a source never affects the entity carrying it.</summary>
    private int PowerReaching(int entityId, Vector3Int position, byte auraId) =>
        _auraField.PowerReaching(position, auraId, entityId);

    /// <summary>
    /// Removal-only re-check for occupants near a moving/toggled aura source. Per aura: an
    /// occupant exposed to two (e.g. Burning from one source, Poison from another) only has the
    /// one whose power actually dropped to zero removed, rather than waiting for that
    /// exposure's own next tick to find it out of range.
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
                        if (PowerReaching(occupantId, occupantTransform.Position, exposure.AuraId) <= 0)
                        {
                            _staleExposureAuraIdsScratch.Add(exposure.AuraId);
                        }
                    }

                    foreach (var staleAuraId in _staleExposureAuraIdsScratch)
                    {
                        _exposures.RemoveFirst(occupantId, staleAuraId, static (ref readonly AuraExposureComponent e, byte auraId) => e.AuraId == auraId);
                    }
                }
            }
        }
    }
}
