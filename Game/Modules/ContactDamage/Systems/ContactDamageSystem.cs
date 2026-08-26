using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.ContactDamage.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.ContactDamage.Systems;

/// <summary>
/// Detects contact by draining MovementSystem's shared FrameEventBuffer&lt;EntityMovedEvent&gt; at
/// the start of each Update (replacing an EntityMovedEvent EventBus subscription -- a gameplay-demo
/// profiling investigation found that pattern, multiplied across every subscriber and the full
/// moving population, a measured hotspot; see FrameEventBuffer's own doc comment) and ticks
/// ongoing exposure via the same Update, combined in one class since both operate on the same
/// ContactDamageExposureComponent pool. StripeCount is deliberately 1, not the 10
/// SimpleHealthRegenSystem/ActionLockSystem use -- see BurningSystem's own doc comment for why:
/// the population (entities currently standing on a hazard tile) is expected to stay small,
/// and striping would stretch "every N frames" into "every N * StripeCount real frames." Still
/// wrapped in a TieredEntityStripeSet despite base StripeCount 1, though (unlike the old plain
/// EntityStripeSet this replaced) -- a Local-tier entity is visited every real frame same as
/// before, but Neighborhood/Borough/Beyond-tier entities get their own coarser cadence on top of
/// that base. The decrement-or-fire loop itself is Engine.ECS.Systems.CountdownTicker.Tick,
/// shared with BurningSystem/PoisonSystem/StatusEffectAuraSystem.
///
/// Per the literal spec, every buffered move landing on a hazard tile deals the immediate hit
/// and resets the countdown -- including hazard-tile-to-hazard-tile moves, not just a fresh
/// entry after being off one. EventBus is still a constructor dependency -- HealthDamage.Apply
/// publishes EntityDamagedEvent through it, an unrelated, low-frequency event this redesign doesn't
/// touch.
/// </summary>
public sealed class ContactDamageSystem : ISystem
{
    public byte StripeCount => 1;

    private readonly PackedComponentPool<DamageOnContactComponent> _hazards;
    private readonly PackedComponentPool<ContactDamageExposureComponent> _exposures;
    private readonly PackedComponentPool<SimpleHealthComponent> _health;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly EventBus _eventBus;
    private readonly IMapQuery _mapQuery;
    private readonly IPlayerQuery? _playerQuery;
    private readonly FrameEventBuffer<EntityMovedEvent> _movedEntities;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly MultiComponentPool<BodyPartComponent>? _bodyParts;
    private readonly MathUtility _mathUtility;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    // CountdownTicker.Tick's contract needs a reused pendingRemovals list regardless -- this
    // system just never actually populates it, since Update here never removes exposure
    // (only OnEntityMoved does, on stepping off a hazard).
    private readonly List<int> _pendingRemovals = [];

    // Cached once instead of passing the Tick method group at the CountdownTicker.Tick call
    // site every Update -- unlike a static method group, an instance method group conversion
    // allocates a fresh delegate on every evaluation (the compiler can't cache a delegate that
    // captures `this`), so passing `Tick` directly there would allocate one every frame for no
    // reason: `this` never actually changes between calls.
    private readonly Func<int, ContactDamageExposureComponent, bool> _tick;

    public ContactDamageSystem(
        PackedComponentPool<DamageOnContactComponent> hazards,
        PackedComponentPool<ContactDamageExposureComponent> exposures,
        PackedComponentPool<SimpleHealthComponent> health,
        EventBus eventBus,
        IMapQuery mapQuery,
        IPlayerQuery? playerQuery,
        FrameEventBuffer<EntityMovedEvent> movedEntities,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        MathUtility mathUtility,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        MultiComponentPool<BodyPartComponent>? bodyParts = null)
    {
        _hazards = hazards;
        _exposures = exposures;
        _health = health;
        _statModifiers = statModifiers;
        _eventBus = eventBus;
        _mapQuery = mapQuery;
        _playerQuery = playerQuery;
        _movedEntities = movedEntities;
        _deadEntities = deadEntities;
        _mathUtility = mathUtility;
        _bodyParts = bodyParts;
        _tick = Tick;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, exposures, processingTiers, processingTierEvents);
    }

    private void OnEntityMoved(EntityMovedEvent moved)
    {
        if (_deadEntities?.Has(moved.EntityId) == true)
        {
            return;
        }

        var terrainEntityId = _mapQuery.GetTerrainEntityIdAt(moved.NewPosition);
        if (terrainEntityId != -1 && _hazards.TryGetReadonly(terrainEntityId, out var hazard))
        {
            var targetRule = new BodyPartTargetRule(hazard.PreferredTargetType, BodyPartFallback.Bottommost);
            HealthDamage.Apply(_health, _eventBus, moved.EntityId, hazard.DamagePerTick, StatusEffectSource.FromEntity(terrainEntityId), _playerQuery, "Contact", _statModifiers, _bodyParts, _mathUtility, _deadEntities, targetRule);

            if (_exposures.Has(moved.EntityId))
            {
                _exposures.TryUpdate(moved.EntityId, (hazard.TickIntervalFrames, terrainEntityId), static (ref ContactDamageExposureComponent exposure, (ushort TickIntervalFrames, int SourceEntityId) state) =>
                {
                    exposure.FramesUntilNextTick = state.TickIntervalFrames;
                    exposure.SourceEntityId = state.SourceEntityId;
                });
            }
            else
            {
                _exposures.Add(moved.EntityId, new ContactDamageExposureComponent(hazard.TickIntervalFrames, terrainEntityId));
            }
        }
        else if (_exposures.Has(moved.EntityId))
        {
            _exposures.Remove(moved.EntityId);
        }
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var moved in _movedEntities.Items)
        {
            OnEntityMoved(moved);
        }

        // The buffer drain above is NOT tier-gated -- see StatusEffectAuraSystem's own Update
        // comment for why (already self-limiting to entities that moved this exact frame).
        // Only the periodic re-check pass below is throttled.
        for (var tierIndex = 0; tierIndex < _tieredStripeSet.TierCount; tierIndex++)
        {
            CountdownTicker.Tick(_exposures, _tieredStripeSet.GetTierBucket(tierIndex, time.FrameCount), _pendingRemovals, _tick, _tieredStripeSet.GetTierFramesPerVisit(tierIndex));
        }
    }

    /// <summary>Always returns false (never removes here -- see this class's own doc comment for why); see CountdownTicker.Tick's own doc comment for the contract.</summary>
    private bool Tick(int entityId, ContactDamageExposureComponent exposure)
    {
        // A corpse stops taking further contact damage -- otherwise a dead entity standing in
        // lava would keep re-triggering HealthDamage.Apply/EntityDamagedEvent forever. The stale
        // exposure component is left in place but inert (matching the "never removes here"
        // convention this method already follows), not cleared.
        if (_deadEntities?.Has(entityId) == true)
        {
            return false;
        }

        // Defensive only -- terrain is never removed once placed, so SourceEntityId should
        // always still have DamageOnContactComponent.
        if (!_hazards.TryGetReadonly(exposure.SourceEntityId, out var hazard))
        {
            return false;
        }

        BodyPartTargetRule? targetRule = hazard.PreferredTargetType is { } type ? new BodyPartTargetRule(type, BodyPartFallback.Bottommost) : null;
        HealthDamage.Apply(_health, _eventBus, entityId, hazard.DamagePerTick, StatusEffectSource.FromEntity(exposure.SourceEntityId), _playerQuery, "Contact", _statModifiers, _bodyParts, _mathUtility, _deadEntities, targetRule);

        _exposures.TryUpdate(entityId, hazard.TickIntervalFrames, static (ref ContactDamageExposureComponent e, ushort tickIntervalFrames) => e.FramesUntilNextTick = tickIntervalFrames);

        return false;
    }
}
