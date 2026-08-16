using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.StatusEffectAura.Systems;

/// <summary>
/// Detects aura range by draining MovementSystem's shared FrameEventBuffer&lt;EntityMovedEvent&gt;
/// at the start of each Update (replacing an EntityMovedEvent EventBus subscription -- a
/// gameplay-demo profiling investigation found that pattern, multiplied across every
/// subscriber and the full moving population, a measured hotspot; see FrameEventBuffer's own
/// doc comment) and ticks ongoing exposure via the same Update, combined in one class since
/// both operate on the same StatusEffectAuraExposureComponent pool. Striped like MovementSystem
/// (see EntityStripeSet), not StripeCount 1 -- lava covers 10% of ground terrain (see the Lava
/// blueprint) with an aura radius wide enough to blanket most of a wandering population, so the
/// exposed population isn't the "stays small" case ContactDamageSystem's own doc comment
/// describes; profiling a gameplay demo showed this system costing as much wall-clock time as
/// BurningSystem, both un-striped, combined exceeding MovementSystem's own (already-striped)
/// cost.
///
/// All range checks go through a single lazily-built AuraGrid (O(1) per lookup, keyed by both
/// cell and StatusEffectType internally -- see its own doc comment for why one shared sparse
/// grid replaced an earlier one-dense-array-per-effect-type version), not a live per-mover box
/// scan -- an earlier version of this class scanned a fixed radius around every single
/// EntityMovedEvent in the game, which is correct but was a measured production performance bug
/// once real lava density and TestMapBuilder's real wandering-population scale were involved.
///
/// _effectTypesInUse tracks which StatusEffectTypes actually have a registered source, so
/// TryGrantApplicableStacks only ever queries effect types that could possibly have a nonzero
/// total -- two sources granting *different* effects (e.g. a future Burning lava tile next to a
/// Poison bog) still never have their Strengths summed together into one meaningless total,
/// since AuraGrid keys every total by (cell, effectType) together.
///
/// Exposure is tracked per (entity, EffectType) -- StatusEffectAuraExposureComponent is a
/// MultiComponentPool entry, mirroring StatusEffectAuraSourceComponent's own per-type-instance
/// shape -- rather than one shared flag per entity. This means every grant path
/// (TryGrantApplicableStacks/TryGrantSingleType) can always be called unconditionally, for
/// every effect type in use, on every move or reactive scan: an entity already exposed to one
/// type is never a reason to skip checking whether a second, different type newly applies too
/// (GrantStacks only ever tops a type's own count UP to its current target, never down, so
/// re-checking an already-topped-off type is always a safe no-op) -- the "already exposed, so
/// skip the check entirely" shortcut that caused a real regression here once is now structurally
/// impossible to reintroduce, since there is no single "already exposed" flag left to shortcut
/// on. Each type's own tick countdown is independent too: a fast-decaying type and a slow one no
/// longer force each other's regrant cadence the way sharing one countdown used to.
///
/// EntityMovedEvent is still handled two ways, since an aura source can in principle be a moving
/// entity (e.g. a future lava golem), not just static terrain:
/// - The mover is treated as an observer, but movement only ever *starts* exposure, never
///   re-grants or resets it: an entity with an already-running exposure timer for some type is
///   left alone by its own movement entirely, so walking out of range and back in before that
///   type's next scheduled tick grants nothing extra and doesn't restart its countdown. Only
///   Update ever grants again (on schedule) or removes a stale exposure (once the timer ticks
///   while genuinely out of range) -- this is deliberately different from ContactDamageSystem,
///   which *does* re-trigger on every single step onto a hazard tile by design (see that
///   system's own doc comment); an aura's grant cadence is a property of the timer, not of
///   the entity's exact path in and out of range.
/// - If the mover itself carries StatusEffectAuraSourceComponent, its own reach needs to be
///   resynced in the grid (old position unsplatted, new position splatted) and nearby occupants
///   re-evaluated (see ResyncSourceIfStale). This is only ever done synchronously, on the spot,
///   for a Local-ProcessingTier source -- correctness matters where the player can actually
///   observe it. A source at any other tier is left stale (see _lastSyncedSourcePosition) and
///   picked up by Update's own periodic catch-up pass instead, at that tier's own coarser
///   cadence -- the same bounded-staleness trade ActionCooldownSystem/MovementSystem/
///   ProcessingTierSystem already make elsewhere in this codebase, applied here to keep a
///   moving source's O(radius^2) resync cost from multiplying across a whole population of
///   moving auras the same way EntityMovedEvent's own FrameEventBuffer exists to avoid for
///   plain movement. A player carrying a toggled-on item (see AuraSourceEffects.Toggle below)
///   is always Local relative to itself, so this is unobservable behavior change for that case;
///   it only matters once a non-player moving source exists.
///
/// A source can also now appear/disappear outside of blueprint-time population (see
/// AuraSourceEffects.Toggle/Grant, used by AuraSourceGrant -- an item like Toxic Idol, or a
/// future creature-cast action effect). OnSourceAdded/OnSourceRemoved react to
/// AuraSourceAddedEvent/AuraSourceRemovedEvent the same way OnEntityMoved reacts to a move,
/// splatting/unsplatting exactly that one source's own radius, and -- like the moving-source
/// case above -- Added proactively grants to anyone already standing in range via the same
/// GrantToOccupantsNear box scan, rather than waiting for a stationary target to move first.
/// Both grant paths are safe to run synchronously (not deferred to a later tick) because
/// GrantStacks never itself publishes anything -- no reentrancy hazard to guard against.
/// </summary>
public sealed class StatusEffectAuraSystem : ISystem
{
    private const byte StripeCountValue = 15;

    public byte StripeCount => StripeCountValue;

    private readonly ComponentManager _componentManager;
    private readonly MultiComponentPool<StatusEffectAuraExposureComponent> _exposures;
    private readonly MultiComponentPool<StatusEffectAuraSourceComponent> _sources;
    private readonly DirectComponentPool<TransformComponent> _transforms;
    private readonly StatusEffectAuraApplierRegistry _applierRegistry;
    private readonly IMapQuery _mapQuery;
    private readonly FrameEventBuffer<EntityMovedEvent> _movedEntities;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly DirectComponentPool<ProcessingTierComponent> _processingTiers;
    private readonly TieredEntityStripeSet _tieredStripeSet;
    private readonly TieredEntityStripeSet _sourceTieredStripeSet;

    private readonly List<(int EntityId, StatusEffectAuraExposureComponent Component)> _pendingExposureRemovals = [];
    private readonly List<StatusEffectType> _staleExposureTypesScratch = [];

    // Cached once instead of passing the Tick method group at the MultiCountdownTicker.Tick call
    // site every visit -- see ContactDamageSystem's own field for why this matters (an instance
    // method group conversion allocates a fresh delegate every evaluation).
    private readonly Func<int, StatusEffectAuraExposureComponent, bool> _tick;

    private readonly AuraGrid _auraGrid;
    private readonly HashSet<StatusEffectType> _effectTypesInUse = [];
    private bool _gridBuilt;

    /// <summary>Per-source-entity position the grid currently reflects -- may lag a non-Local source's real (_transforms) position by however many moves it's made since its own last resync. See ResyncSourceIfStale.</summary>
    private readonly Dictionary<int, Vector3Int> _lastSyncedSourcePosition = [];

    private int _maxScanRadius;

    public StatusEffectAuraSystem(
        ComponentManager componentManager,
        MultiComponentPool<StatusEffectAuraExposureComponent> exposures,
        MultiComponentPool<StatusEffectAuraSourceComponent> sources,
        DirectComponentPool<TransformComponent> transforms,
        IMapQuery mapQuery,
        EventBus eventBus,
        StatusEffectAuraApplierRegistry applierRegistry,
        FrameEventBuffer<EntityMovedEvent> movedEntities,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        PackedComponentPool<DeadComponent>? deadEntities = null)
    {
        _componentManager = componentManager;
        _exposures = exposures;
        _sources = sources;
        _transforms = transforms;
        _mapQuery = mapQuery;
        _applierRegistry = applierRegistry;
        _movedEntities = movedEntities;
        _deadEntities = deadEntities;
        _processingTiers = processingTiers;

        _auraGrid = new AuraGrid(mapQuery.MapSize);

        eventBus.Subscribe<AuraSourceAddedEvent>(OnSourceAdded);
        eventBus.Subscribe<AuraSourceRemovedEvent>(OnSourceRemoved);

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, exposures, processingTiers, processingTierEvents);
        _sourceTieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, sources, processingTiers, processingTierEvents);

        _tick = Tick;
    }

    /// <summary>
    /// Scatters every currently-registered source on first real use (not the constructor):
    /// StatusEffectAuraModule.RegisterSystems runs during GameBootstrapper.Build, which is
    /// before FloorBuilder.PopulateFloor places any terrain (e.g. Lava) -- so no
    /// StatusEffectAuraSourceComponent exists yet at construction time. By the time the first
    /// EntityMovedEvent/Update fires, population has finished.
    /// </summary>
    private void EnsureGrid()
    {
        if (_gridBuilt)
        {
            return;
        }

        SourceSplatting.ScatterAll(_sources, TryGetTransformPosition, (entityId, source, position) =>
        {
            _effectTypesInUse.Add(source.EffectType);
            _auraGrid.AddSource(position, source.AuraAndGlowStrength, source.EffectType);
            _maxScanRadius = Math.Max(_maxScanRadius, DistanceFalloff.MaxRadius(source.AuraAndGlowStrength));
            _lastSyncedSourcePosition[entityId] = position;
        });

        _gridBuilt = true;
    }

    private Vector3Int? TryGetTransformPosition(int entityId) =>
        _transforms.TryGetReadonly(entityId, out var transform) ? transform.Position : null;

    private void OnEntityMoved(EntityMovedEvent moved)
    {
        EnsureGrid();

        if (_sources.Has(moved.EntityId) && GetSourceTier(moved.EntityId) == ProcessingTierLevel.Local)
        {
            ResyncSourceIfStale(moved.EntityId);
        }

        // Always attempted, even for an entity already exposed to some OTHER effect type -- see
        // this class's own doc comment for why there's no "already exposed" shortcut left to
        // gate this on. Exposure-entry bookkeeping (only a genuinely new type creates a fresh
        // countdown) is handled internally by TryGrantApplicableStacks now.
        TryGrantApplicableStacks(moved.EntityId, moved.NewPosition);
    }

    /// <summary>
    /// Resyncs entityId's own source contribution(s) into the grid if _lastSyncedSourcePosition
    /// disagrees with its current Transform -- a no-op otherwise. Called synchronously (every
    /// move) for a Local-tier source by OnEntityMoved, and periodically (at whatever cadence
    /// GetSourceTier last computed) for every other tier by Update's own catch-up pass -- either
    /// way this is the only place that actually mutates AuraGrid/re-evaluates nearby occupants
    /// for a moving source, so a source that moved several times while non-Local still resyncs
    /// correctly using its last KNOWN grid position (not just "this one event's" old position,
    /// which could be several moves stale by the time a deferred catch-up runs).
    /// </summary>
    private void ResyncSourceIfStale(int entityId)
    {
        if (!_transforms.TryGetReadonly(entityId, out var transform))
        {
            return;
        }

        var currentPosition = transform.Position;
        var hadPreviousPosition = _lastSyncedSourcePosition.TryGetValue(entityId, out var previousPosition);
        if (hadPreviousPosition && previousPosition == currentPosition)
        {
            return;
        }

        SourceSplatting.ResyncEntity(_sources, entityId, hadPreviousPosition ? previousPosition : null, currentPosition,
            unsplat: (source, position) => _auraGrid.RemoveSource(position, source.AuraAndGlowStrength, source.EffectType),
            splat: (source, position) => _auraGrid.AddSource(position, source.AuraAndGlowStrength, source.EffectType));

        if (hadPreviousPosition)
        {
            ReEvaluateExposuresNear(previousPosition);
        }

        ReEvaluateExposuresNear(currentPosition);
        GrantToOccupantsNear(currentPosition);

        _lastSyncedSourcePosition[entityId] = currentPosition;
    }

    /// <summary>Fails open to Beyond (the coarsest, least-frequently-visited tier) for a source with no ProcessingTierComponent yet -- the same "unknown = probably far, self-corrects once its real tier lands" bias ProcessingTierWiring's own lookup delegate already uses, not a new tradeoff invented here.</summary>
    private ProcessingTierLevel GetSourceTier(int entityId) =>
        _processingTiers.TryGetReadonly(entityId, out var tier) ? tier.Tier : ProcessingTierLevel.Beyond;

    /// <summary>
    /// Splats a source that appeared outside of blueprint-time population (see
    /// AuraSourceEffects.Toggle). Guarded by _gridBuilt -- if the grid hasn't been built yet,
    /// the source is already sitting in the pool by the time EnsureGrid's own full scan
    /// eventually runs, so splatting it here too would double-count it. Also does a synchronous
    /// box-scan grant to anyone already standing in range -- unlike a moving source's own
    /// tier-gated resync (see ResyncSourceIfStale), a toggle-on is a rare, one-shot event
    /// regardless of the toggling entity's own tier, and GrantStacks itself never publishes
    /// anything (confirmed: no nested-Toggle reentrancy hazard exists today), so there's no
    /// reason to make the player wait for a stationary target to move before an aura they just
    /// turned on starts doing anything.
    /// </summary>
    private void OnSourceAdded(AuraSourceAddedEvent added)
    {
        if (!_gridBuilt || !_transforms.TryGetReadonly(added.EntityId, out var transform))
        {
            return;
        }

        _effectTypesInUse.Add(added.Source.EffectType);
        _auraGrid.AddSource(transform.Position, added.Source.AuraAndGlowStrength, added.Source.EffectType);
        _maxScanRadius = Math.Max(_maxScanRadius, DistanceFalloff.MaxRadius(added.Source.AuraAndGlowStrength));
        _lastSyncedSourcePosition[added.EntityId] = transform.Position;

        GrantToOccupantsNear(transform.Position);
    }

    /// <summary>Unsplats a source that was removed outside of blueprint-time population (toggle-off, or DeathSystem retracting a corpse's still-active aura). Same _gridBuilt guard as OnSourceAdded, for the same reason. Also immediately re-checks nearby exposures (ReEvaluateExposuresNear, the same removal-only pass a moving source's old position already gets) so toggling off reads as instant, not laggy until each affected entity's own next tick.</summary>
    private void OnSourceRemoved(AuraSourceRemovedEvent removed)
    {
        _lastSyncedSourcePosition.Remove(removed.EntityId);

        if (!_gridBuilt || !_transforms.TryGetReadonly(removed.EntityId, out var transform))
        {
            return;
        }

        _auraGrid.RemoveSource(transform.Position, removed.Source.AuraAndGlowStrength, removed.Source.EffectType);
        ReEvaluateExposuresNear(transform.Position);
    }

    /// <summary>
    /// Grant-only counterpart to ReEvaluateExposuresNear -- same box-scan shape, but for
    /// wherever a source is now (just toggled on, via OnSourceAdded, or just resynced to, via
    /// ResyncSourceIfStale) rather than wherever one just left. A candidate already self-excludes
    /// correctly via TotalStacksExcludingSelf (see SourceDoesNotIgniteItself), so the source
    /// entity itself showing up in this same scan is harmless.
    /// </summary>
    /// <remarks>Walks IMapQuery.GetOccupantEntityIdsAt per cell rather than the Blocking-only GetEntityIdsInBox, so Tiny/Phasing occupants are evaluated for exposure too -- they used to be silently skipped.</remarks>
    private void GrantToOccupantsNear(Vector3Int center)
    {
        var boxWidth = _maxScanRadius * 2 + 1;
        var minX = center.X - _maxScanRadius;
        var minY = center.Y - _maxScanRadius;
        var z = center.Z;

        for (var y = minY; y < minY + boxWidth; y++)
        {
            for (var x = minX; x < minX + boxWidth; x++)
            {
                var position = new Vector3Int(x, y, z);
                if (!_mapQuery.IsOnMap(position))
                {
                    continue;
                }

                foreach (var occupantId in _mapQuery.GetOccupantEntityIdsAt(position))
                {
                    if (!_transforms.TryGetReadonly(occupantId, out var occupantTransform))
                    {
                        continue;
                    }

                    TryGrantApplicableStacks(occupantId, occupantTransform.Position);
                }
            }
        }
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        // The buffer drain itself is NOT ProcessingTier-gated -- it only ever processes
        // entities that actually moved this exact frame (already self-limiting, unlike the
        // periodic passes below), and a fresh entry into an aura's range is a one-time
        // event a player could plausibly notice even off-screen (e.g. checking the entity's
        // status later), unlike the periodic re-grant's steady-state pacing.
        foreach (var moved in _movedEntities.Items)
        {
            OnEntityMoved(moved);
        }

        // Still needed here, idempotently, in case this frame had zero buffered moves (e.g.
        // before the very first move of the whole game) -- both periodic passes below need the
        // grid to exist.
        EnsureGrid();

        for (var tierIndex = 0; tierIndex < _tieredStripeSet.TierCount; tierIndex++)
        {
            TickExposures(_tieredStripeSet.GetTierBucket(tierIndex, time.FrameCount), _tieredStripeSet.GetTierFramesPerVisit(tierIndex));
        }

        // Catches up any non-Local source OnEntityMoved deferred (see ResyncSourceIfStale) --
        // a no-op for a Local source (already resynced on every move) or a source that hasn't
        // moved since its last resync, so chaining every tier via GetDueEntities and letting the
        // position-equality check inside ResyncSourceIfStale short-circuit is simpler than
        // excluding tier 0 here and no more expensive for it.
        foreach (var entityId in _sourceTieredStripeSet.GetDueEntities(time.FrameCount))
        {
            ResyncSourceIfStale(entityId);
        }
    }

    /// <summary>Per-tier decrement-or-tick pass over _exposures.</summary>
    /// <remarks>Delegates to the shared MultiCountdownTicker (see its own doc comment for the dense-chain-walk/deferred-removal mechanics) -- Tick below is the only per-effect-specific piece.</remarks>
    private void TickExposures(ReadOnlySpan<int> dueEntityIds, uint framesPerVisit) =>
        MultiCountdownTicker.Tick(_exposures, dueEntityIds, _pendingExposureRemovals, _tick, framesPerVisit);

    /// <summary>Returns whether this specific (entity, EffectType) exposure entry should be removed entirely.</summary>
    /// <remarks>True when no longer in range of this type, or the entity's own position can't be found. False re-arms the exposure's own countdown itself (via TryUpdateFirst, matched by EffectType) rather than leaving that to the caller -- see MultiCountdownTicker.Tick's onTick contract.</remarks>
    private bool Tick(int entityId, StatusEffectAuraExposureComponent exposure)
    {
        if (!_transforms.TryGetReadonly(entityId, out var transform) || !TryGrantSingleType(entityId, transform.Position, exposure.EffectType))
        {
            return true;
        }

        _exposures.TryUpdateFirst(entityId, exposure.EffectType,
            static (ref readonly StatusEffectAuraExposureComponent e, StatusEffectType type) => e.EffectType == type,
            static (ref StatusEffectAuraExposureComponent e, StatusEffectType type) => e.FramesUntilNextTick = AuraEffects.TickIntervalFrames);

        return false;
    }

    /// <summary>Grants stacks from every effect type actually in use that's applicable at position (each topped off via GrantStacks -- see its own doc comment), creating a fresh exposure entry for any type that's newly applicable and doesn't already have one. Shared by OnEntityMoved's fresh-entry path and GrantToOccupantsNear's reactive scan, which otherwise duplicated this exact loop.</summary>
    private void TryGrantApplicableStacks(int entityId, Vector3Int position)
    {
        foreach (var effectType in _effectTypesInUse)
        {
            if (TryGrantSingleType(entityId, position, effectType) && !HasExposure(entityId, effectType))
            {
                _exposures.Add(entityId, new StatusEffectAuraExposureComponent(effectType, AuraEffects.TickIntervalFrames));
            }
        }
    }

    /// <summary>
    /// Tops entityId's current stack count for effectType *up to* its distance-based target at
    /// position, if the aura reaches it and something is registered to receive it -- shared by
    /// TryGrantApplicableStacks (looping every type in use, to catch a newly-in-range one) and
    /// Tick (revisiting one already-tracked type). A corpse never accumulates new stacks (see
    /// DeathSystem/DeadComponent): checked here, not by the caller, so every caller gets the
    /// same "corpse reads as no longer applicable" answer for free, the same as walking out of
    /// range would.
    /// </summary>
    private bool TryGrantSingleType(int entityId, Vector3Int position, StatusEffectType effectType)
    {
        if (_deadEntities?.Has(entityId) == true)
        {
            return false;
        }

        var totalStacks = TotalStacksExcludingSelf(entityId, position, effectType);
        return totalStacks > 0 && GrantStacks(entityId, effectType, totalStacks);
    }

    private bool HasExposure(int entityId, StatusEffectType effectType)
    {
        for (var denseIndex = _exposures.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _exposures.GetNextDenseIndex(denseIndex))
        {
            if (_exposures.GetReadonlyByDenseIndex(denseIndex).EffectType == effectType)
            {
                return true;
            }
        }

        return false;
    }

    private int TotalStacksExcludingSelf(int entityId, Vector3Int position, StatusEffectType effectType)
    {
        var totalStacks = _auraGrid.GetTotalStacksAt(position, effectType);

        for (var denseIndex = _sources.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _sources.GetNextDenseIndex(denseIndex))
        {
            var selfSource = _sources.GetReadonlyByDenseIndex(denseIndex);
            if (selfSource.EffectType == effectType)
            {
                totalStacks -= selfSource.AuraAndGlowStrength;
            }
        }

        return Math.Max(0, totalStacks);
    }

    /// <summary>
    /// Tops the entity's current stack count for effectType *up to* targetStackCount --
    /// it does not add targetStackCount more stacks on every call. AuraGrid.GetTotalStacksAt
    /// (via TotalStacksExcludingSelf) reports "how many stacks this position should carry
    /// right now," not "how many stacks to add this tick": granting the full target again on
    /// every periodic re-grant (every AuraEffects.TickIntervalFrames, independent of and not
    /// synchronized with the effect's own decay tick, see BurningSystem/PoisonSystem) would
    /// add stacks faster than the effect's own decay removes them, snowballing toward its
    /// MaxStacks while standing near a source and then taking a correspondingly long tail to
    /// fully decay after leaving -- exactly the bug this fixes, for any effect an aura might
    /// grant, not just Burning. Topping off instead keeps the steady-state stack count at the
    /// position's target while remaining in range, and the effect's own decay naturally
    /// unwinds whatever's left once out of range.
    ///
    /// Dispatches through the shared StatusEffectAuraApplierRegistry rather than hardcoding
    /// one concrete effect -- an aura is assumed to be able to grant *any* status effect,
    /// harmful or beneficial, as long as that effect's own module registered an
    /// IStatusEffectAuraApplier (see BurningModule/PoisonModule.Configure). Returns whether
    /// effectType actually has a registered applier, not whether stacks were added on this
    /// specific call -- an entity already topped off to its target still counts as validly
    /// exposed. TryGrantSingleType relies on this to tell "exposed to an effect type with no
    /// registered applier" apart from "exposed to a supported effect but already at target."
    /// </summary>
    private bool GrantStacks(int entityId, StatusEffectType effectType, int targetStackCount)
    {
        if (!_applierRegistry.TryGet(effectType, out var applier))
        {
            // Nothing has registered support for granting this effect type via an aura yet --
            // the grid still tracks it (for whichever module registers one later), this just
            // can't apply anything until then. Not an error: a StatusEffectAuraSourceComponent
            // can be authored for an effect type before that effect's own module exists.
            return false;
        }

        var currentStackCount = applier.GetCurrentStackCount(_componentManager, entityId);
        var stacksToGrant = targetStackCount - currentStackCount;

        // Attribution to a specific source entity isn't cheaply recoverable from a grid that
        // only stores a running total (see AuraGrid) -- Admin is used the same way
        // FloorBuilder's old temporary seeding used it, for a non-entity-specific source.
        for (var i = 0; i < stacksToGrant; i++)
        {
            applier.ApplyStack(_componentManager, entityId, StatusEffectSource.Admin);
        }

        return true;
    }

    /// <summary>
    /// Removal-only re-check for occupants near a moving/toggled aura source -- see this
    /// class's own doc comment for why granting is not handled here. Per-effect-type now: an
    /// occupant exposed to two different types (e.g. Burning from one source, Poison from
    /// another) only has the type whose contribution actually dropped to zero removed, not its
    /// whole exposure wholesale -- a stale entry for a still-out-of-reach type would otherwise
    /// have kept ticking (harmlessly, since GrantStacks re-tops-off to zero to nothing there)
    /// until its own next scheduled visit, rather than being cleaned up immediately here.
    /// </summary>
    /// <remarks>See GrantToOccupantsNear's own remark -- same GetOccupantEntityIdsAt-per-cell walk, so a Tiny/Phasing occupant's exposure is correctly dropped when it (or the source) moves out of range, not just granted.</remarks>
    private void ReEvaluateExposuresNear(Vector3Int center)
    {
        var boxWidth = _maxScanRadius * 2 + 1;
        var minX = center.X - _maxScanRadius;
        var minY = center.Y - _maxScanRadius;
        var z = center.Z;

        for (var y = minY; y < minY + boxWidth; y++)
        {
            for (var x = minX; x < minX + boxWidth; x++)
            {
                var position = new Vector3Int(x, y, z);
                if (!_mapQuery.IsOnMap(position))
                {
                    continue;
                }

                foreach (var occupantId in _mapQuery.GetOccupantEntityIdsAt(position))
                {
                    if (!_transforms.TryGetReadonly(occupantId, out var occupantTransform))
                    {
                        continue;
                    }

                    // Snapshot which of the occupant's current exposure types are now out of range
                    // before removing any of them -- MultiComponentPool.RemoveFirst reorders the dense
                    // chain, so removing mid-walk of that same chain would skip or revisit entries.
                    _staleExposureTypesScratch.Clear();
                    for (var denseIndex = _exposures.GetFirstDenseIndex(occupantId); denseIndex != -1; denseIndex = _exposures.GetNextDenseIndex(denseIndex))
                    {
                        var exposure = _exposures.GetReadonlyByDenseIndex(denseIndex);
                        if (TotalStacksExcludingSelf(occupantId, occupantTransform.Position, exposure.EffectType) <= 0)
                        {
                            _staleExposureTypesScratch.Add(exposure.EffectType);
                        }
                    }

                    foreach (var staleType in _staleExposureTypesScratch)
                    {
                        _exposures.RemoveFirst(occupantId, staleType, static (ref readonly StatusEffectAuraExposureComponent e, StatusEffectType type) => e.EffectType == type);
                    }
                }
            }
        }
    }
}
