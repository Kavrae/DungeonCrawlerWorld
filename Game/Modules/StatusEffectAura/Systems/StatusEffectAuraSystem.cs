using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
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
/// Detects aura range by draining MovementSystem's shared FrameEventBuffer&lt;EntityMoved&gt;
/// at the start of each Update (replacing an EntityMoved EventBus subscription -- a
/// gameplay-demo profiling investigation found that pattern, multiplied across every
/// subscriber and the full moving population, a measured hotspot; see FrameEventBuffer's own
/// doc comment) and ticks ongoing exposure via the same Update, combined in one class since
/// both operate on the same StatusEffectAuraExposureComponent pool. Striped like MovementSystem
/// (see EntityStripeSet), not StripeCount 1 -- lava covers 10% of ground terrain (see the Lava
/// blueprint) with an aura radius wide enough to blanket most of a wandering population, so the
/// exposed population isn't the "stays small" case ContactDamageSystem's own doc comment
/// describes; profiling a gameplay demo showed this system costing as much wall-clock time as
/// BurningSystem, both un-striped, combined exceeding MovementSystem's own (already-striped)
/// cost. CountdownTicker.Tick is called once per TieredEntityStripeSet tier, each passed that
/// tier's own GetTierFramesPerVisit as framesPerVisit, so each entity's exposure still
/// decrements by the correct number of real frames between visits regardless of which tier's
/// (possibly coarser than StripeCount) cadence it's on (see CountdownTicker.Tick's own doc
/// comment for why this matters) -- note the buffer drain itself is NOT tier-gated (every
/// buffered move is processed every Update call); only the ongoing CountdownTicker.Tick pass
/// over the existing exposure population is. The decrement-or-fire
/// loop itself is Engine.ECS.Systems.CountdownTicker.Tick, shared with BurningSystem/
/// PoisonSystem/ContactDamageSystem.
///
/// All range checks go through a single lazily-built AuraGrid (O(1) per lookup, keyed by both
/// cell and StatusEffectType internally -- see its own doc comment for why one shared sparse
/// grid replaced an earlier one-dense-array-per-effect-type version), not a live per-mover box
/// scan -- an earlier version of this class scanned a fixed radius around every single
/// EntityMoved in the game, which is correct but was a measured production performance bug
/// once real lava density and TestMapBuilder's real wandering-population scale were involved.
///
/// _effectTypesInUse tracks which StatusEffectTypes actually have a registered source, so
/// TryGrantApplicableStacks/IsExposedToAny only ever query effect types that could possibly
/// have a nonzero total -- two sources granting *different* effects (e.g. a future Burning
/// lava tile next to a Poison bog) still never have their Strengths summed together into one
/// meaningless total, since AuraGrid keys every total by (cell, effectType) together.
///
/// EntityMoved is still handled two ways, since an aura source can in principle be a moving
/// entity (e.g. a future lava golem), not just static terrain:
/// - The mover is treated as an observer, but movement only ever *starts* exposure, never
///   re-grants or resets it: an entity with an already-running exposure timer is left alone
///   by its own movement entirely, so walking out of range and back in before the timer's
///   next scheduled tick grants nothing extra and doesn't restart the countdown. Only Update
///   ever grants again (on schedule) or removes a stale exposure (once the timer ticks while
///   genuinely out of range) -- this is deliberately different from ContactDamageSystem,
///   which *does* re-trigger on every single step onto a hazard tile by design (see that
///   system's own doc comment); an aura's grant cadence is a property of the timer, not of
///   the entity's exact path in and out of range.
/// - If the mover itself carries StatusEffectAuraSourceComponent, that one effect type's grid
///   is updated (its old contribution removed, new contribution added) and everyone near its
///   old/new position is re-checked, so a moving source correctly stops affecting entities it
///   walks away from (removal only -- newly gaining exposure purely because a source
///   approached a stationary entity is an accepted gap: that entity picks up the aura the
///   next time it moves itself). This path is rare (no moving source exists in the game
///   today) so the box query it still uses (see _maxScanRadius, to find candidate nearby
///   occupants) is not on the hot path.
/// </summary>
public sealed class StatusEffectAuraSystem : ISystem
{
    private const byte StripeCountValue = 15;

    public byte StripeCount => StripeCountValue;

    private readonly ComponentManager _componentManager;
    private readonly PackedComponentPool<StatusEffectAuraExposureComponent> _exposures;
    private readonly PackedComponentPool<StatusEffectAuraSourceComponent> _sources;
    private readonly DirectComponentPool<TransformComponent> _transforms;
    private readonly StatusEffectAuraApplierRegistry _applierRegistry;
    private readonly IMapQuery _mapQuery;
    private readonly FrameEventBuffer<EntityMoved> _movedEntities;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    private readonly List<int> _pendingExposureRemovals = [];

    private readonly AuraGrid _auraGrid;
    private readonly HashSet<StatusEffectType> _effectTypesInUse = [];
    private bool _gridBuilt;

    private int _maxScanRadius;

    // Cached once instead of passing the Tick method group at the CountdownTicker.Tick call
    // site every Update -- see ContactDamageSystem's own field for why this matters (an
    // instance method group conversion allocates a fresh delegate every evaluation).
    private readonly Func<int, StatusEffectAuraExposureComponent, bool> _tick;

    public StatusEffectAuraSystem(
        ComponentManager componentManager,
        PackedComponentPool<StatusEffectAuraExposureComponent> exposures,
        PackedComponentPool<StatusEffectAuraSourceComponent> sources,
        DirectComponentPool<TransformComponent> transforms,
        IMapQuery mapQuery,
        StatusEffectAuraApplierRegistry applierRegistry,
        FrameEventBuffer<EntityMoved> movedEntities,
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

        _auraGrid = new AuraGrid(mapQuery.MapSize);
        _tick = Tick;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, exposures, processingTiers, processingTierEvents);
    }

    /// <summary>
    /// Scatters every currently-registered source on first real use (not the constructor):
    /// StatusEffectAuraModule.RegisterSystems runs during GameBootstrapper.Build, which is
    /// before FloorBuilder.PopulateFloor places any terrain (e.g. Lava) -- so no
    /// StatusEffectAuraSourceComponent exists yet at construction time. By the time the first
    /// EntityMoved/Update fires, population has finished.
    /// </summary>
    private void EnsureGrid()
    {
        if (_gridBuilt)
        {
            return;
        }

        var sourceIds = _sources.EntityIds;
        var sourceComponents = _sources.Components;
        for (var i = 0; i < sourceIds.Length; i++)
        {
            if (!_transforms.TryGetReadonly(sourceIds[i], out var transform))
            {
                continue;
            }

            var source = sourceComponents[i];
            _effectTypesInUse.Add(source.EffectType);
            _auraGrid.AddSource(transform.Position, source.AuraAndGlowStrength, source.EffectType);
            _maxScanRadius = Math.Max(_maxScanRadius, DistanceFalloff.MaxRadius(source.AuraAndGlowStrength));
        }

        _gridBuilt = true;
    }

    private void OnEntityMoved(EntityMoved moved)
    {
        var gridAlreadyBuilt = _gridBuilt;
        EnsureGrid();

        if (gridAlreadyBuilt && _sources.TryGetReadonly(moved.EntityId, out var movedSource))
        {
            _auraGrid.RemoveSource(moved.OldPosition, movedSource.AuraAndGlowStrength, movedSource.EffectType);
            _auraGrid.AddSource(moved.NewPosition, movedSource.AuraAndGlowStrength, movedSource.EffectType);
        }

        // Only a genuinely fresh entry (no exposure already running) grants anything here --
        // see this class's own doc comment for why an already-exposed entity's own movement
        // must not re-grant or reset the timer.
        if (!_exposures.Has(moved.EntityId) && TryGrantApplicableStacks(moved.EntityId, moved.NewPosition))
        {
            _exposures.Add(moved.EntityId, new StatusEffectAuraExposureComponent(AuraEffects.TickIntervalFrames));
        }

        if (_sources.Has(moved.EntityId))
        {
            ReEvaluateExposuresNear(moved.OldPosition);
            ReEvaluateExposuresNear(moved.NewPosition);
        }
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        // Deliberately NOT EnsureGrid() up front: OnEntityMoved's own internal EnsureGrid()
        // call captures gridAlreadyBuilt BEFORE building the grid, to tell "the grid didn't
        // exist yet, so building it just now already accounts for wherever this source
        // currently stands" apart from "the grid already existed with a stale position, so
        // this move needs an explicit remove-old/add-new". Calling EnsureGrid() here first
        // would flip _gridBuilt to true before the loop below ever runs, making every
        // buffered move -- even a source's very first one -- take the second, "already
        // built" branch and double-count its own contribution (confirmed by a failing test).
        //
        // The buffer drain itself is NOT ProcessingTier-gated -- it only ever processes
        // entities that actually moved this exact frame (already self-limiting, unlike the
        // periodic re-grant pass below), and a fresh entry into an aura's range is a one-time
        // event a player could plausibly notice even off-screen (e.g. checking the entity's
        // status later), unlike the periodic re-grant's steady-state pacing.
        foreach (var moved in _movedEntities.Items)
        {
            OnEntityMoved(moved);
        }

        // Still needed here, idempotently, in case this frame had zero buffered moves (e.g.
        // before the very first move of the whole game) -- CountdownTicker.Tick below needs
        // the grid to exist.
        EnsureGrid();

        for (var tierIndex = 0; tierIndex < _tieredStripeSet.TierCount; tierIndex++)
        {
            CountdownTicker.Tick(_exposures, _tieredStripeSet.GetTierBucket(tierIndex, time.FrameCount), _pendingExposureRemovals, _tick, _tieredStripeSet.GetTierFramesPerVisit(tierIndex));
        }
    }

    /// <summary>Returns whether the exposure should be removed entirely (no longer in range of anything, or the entity's own position can't be found) -- see CountdownTicker.Tick's own doc comment for the contract.</summary>
    private bool Tick(int entityId, StatusEffectAuraExposureComponent exposure)
    {
        if (!_transforms.TryGetReadonly(entityId, out var transform))
        {
            return true;
        }

        if (!TryGrantApplicableStacks(entityId, transform.Position))
        {
            return true;
        }

        _exposures.TryUpdate(entityId, static (ref StatusEffectAuraExposureComponent e) => e.FramesUntilNextTick = AuraEffects.TickIntervalFrames);

        return false;
    }

    /// <summary>Grants stacks from every effect type actually in use that's applicable at position (each topped off via GrantStacks -- see its own doc comment), returning whether any effect type actually granted something (not just whether some total was positive -- an unsupported effect type, see GrantStacks, contributes nothing here even if the grid says otherwise). Shared by OnEntityMoved's fresh-entry path and Update's periodic re-grant, which otherwise duplicated this exact loop.</summary>
    private bool TryGrantApplicableStacks(int entityId, Vector3Int position)
    {
        // A corpse doesn't accumulate new stacks -- returning false here reads to both callers
        // as "nothing granted," which for Tick's periodic re-grant path means the exposure gets
        // cleaned up next visit (see Tick's own contract), the same as walking out of range.
        if (_deadEntities?.Has(entityId) == true)
        {
            return false;
        }

        var anyGranted = false;
        foreach (var effectType in _effectTypesInUse)
        {
            if (TotalStacksExcludingSelf(entityId, position, effectType) is var totalStacks and > 0)
            {
                anyGranted |= GrantStacks(entityId, effectType, totalStacks);
            }
        }

        return anyGranted;
    }

    /// <summary>The read-only half of TryGrantApplicableStacks -- whether any effect type still has a positive contribution at position, without granting anything. Used where only "is this still in range of something" matters (ReEvaluateExposuresNear's removal check).</summary>
    private bool IsExposedToAny(int entityId, Vector3Int position)
    {
        foreach (var effectType in _effectTypesInUse)
        {
            if (TotalStacksExcludingSelf(entityId, position, effectType) > 0)
            {
                return true;
            }
        }

        return false;
    }

    private int TotalStacksExcludingSelf(int entityId, Vector3Int position, StatusEffectType effectType)
    {
        var totalStacks = _auraGrid.GetTotalStacksAt(position, effectType);
        if (_sources.TryGetReadonly(entityId, out var selfSource) && selfSource.EffectType == effectType)
        {
            totalStacks = Math.Max(0, totalStacks - selfSource.AuraAndGlowStrength);
        }

        return totalStacks;
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
    /// exposed. TryGrantApplicableStacks relies on this to tell "exposed to an effect type
    /// with no registered applier" apart from "exposed to a supported effect but already at
    /// target."
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

    /// <summary>Removal-only re-check for occupants near a moving aura source -- see this class's own doc comment for why granting is not handled here. Each candidate's own check is now O(1) per effect type via the grid, not a nested box scan -- only the "who might be nearby" part still uses a box query, and only on this rare (no moving source exists today) path.</summary>
    private void ReEvaluateExposuresNear(Vector3Int center)
    {
        var boxWidth = _maxScanRadius * 2 + 1;
        var box = new CubeInt(
            new Vector3Int(center.X - _maxScanRadius, center.Y - _maxScanRadius, center.Z),
            new Vector3Int(boxWidth, boxWidth, 1));

        Span<int> occupantIds = stackalloc int[boxWidth * boxWidth];
        _mapQuery.GetEntityIdsInBox(box, occupantIds);

        foreach (var occupantId in occupantIds)
        {
            if (occupantId == -1 || !_exposures.Has(occupantId) || !_transforms.TryGetReadonly(occupantId, out var occupantTransform))
            {
                continue;
            }

            if (!IsExposedToAny(occupantId, occupantTransform.Position))
            {
                _exposures.Remove(occupantId);
            }
        }
    }
}
