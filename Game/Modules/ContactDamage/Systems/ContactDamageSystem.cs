using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.ContactDamage.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers.Components;
using Game.Terrain;
using Game.World;

namespace Game.Modules.ContactDamage.Systems;

/// <summary>
/// Detects contact by draining MovementSystem's shared FrameEventBuffer&lt;EntityMovedEvent&gt; at
/// the start of each Update (replacing an EntityMovedEvent EventBus subscription -- a gameplay-demo
/// profiling investigation found that pattern, multiplied across every subscriber and the full
/// moving population, a measured hotspot; see FrameEventBuffer's own doc comment) and ticks
/// ongoing exposure via the same Update, combined in one class since both operate on the same
/// ContactDamageExposureComponent pool. Ongoing exposure is driven by a timer wheel
/// (PackedTimerWheel): only exposures due a tick this frame are touched, on their exact frame at
/// every processing tier.
///
/// The hazard is the terrain's own definition (TerrainDefinition.ContactHazard) -- terrain cells are
/// not entities, so there is no hazard component to look up.
///
/// Per the literal spec, every buffered move landing on a hazard tile deals the immediate hit
/// and resets the countdown -- including hazard-tile-to-hazard-tile moves, not just a fresh
/// entry after being off one. EventBus is still a constructor dependency -- HealthDamage.Apply
/// publishes EntityDamagedEvent through it, an unrelated, low-frequency event this redesign doesn't
/// touch.
/// </summary>
public sealed class ContactDamageSystem : ISystem
{
    /// <summary>Every frame: this frame's moves must be drained this frame, and the wheel only touches exposures actually due.</summary>
    public byte StripeCount => 1;

    private readonly TerrainRegistry _terrain;
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
    private readonly PackedTimerWheel<ContactDamageExposureComponent> _wheel;

    // Cached once instead of passing the Tick method group every Update -- unlike a static
    // method group, an instance method group conversion allocates a fresh delegate on every
    // evaluation (the compiler can't cache a delegate that captures `this`), so passing `Tick`
    // directly there would allocate one every frame for no reason.
    private readonly TimerFired<ContactDamageExposureComponent> _tick;
    private readonly SimulationClock _clock;

    public ContactDamageSystem(
        TerrainRegistry terrain,
        PackedComponentPool<ContactDamageExposureComponent> exposures,
        PackedComponentPool<SimpleHealthComponent> health,
        EventBus eventBus,
        IMapQuery mapQuery,
        IPlayerQuery? playerQuery,
        FrameEventBuffer<EntityMovedEvent> movedEntities,
        MathUtility mathUtility,
        SimulationClock simulationClock,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        MultiComponentPool<BodyPartComponent>? bodyParts = null,
        SimulationScope? simulationScope = null)
    {
        _terrain = terrain;
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
        _clock = simulationClock;
        _tick = Tick;
        _wheel = new PackedTimerWheel<ContactDamageExposureComponent>(exposures, simulationScope);
        if (simulationScope is not null)
        {
            simulationScope.EntityResumed += OnEntityResumed;
        }
    }

    /// <summary>Moves the entity's exposure past every tick owed while it froze -- see SkipOwedTicks.</summary>
    /// <remarks>Runs after the wheel's own resume handler has rescheduled the stale deadline; rewriting it here reschedules again and leaves that earlier entry to be dropped as stale, the wheel's ordinary lazy cancellation.</remarks>
    private void OnEntityResumed(int entityId) => SkipOwedTicks(entityId, _clock.CurrentFrame);

    private void OnEntityMoved(EntityMovedEvent moved, long now)
    {
        if (_deadEntities?.Has(moved.EntityId) == true)
        {
            return;
        }

        var terrainTypeId = _mapQuery.GetTerrainAt(moved.NewPosition).TypeId;
        if (_terrain.TryGetContactHazard(terrainTypeId, out var hazard))
        {
            var targetRule = new BodyPartTargetRule(hazard.PreferredTargetType, BodyPartFallback.Bottommost);
            HealthDamage.Apply(_health, _eventBus, moved.EntityId, hazard.DamagePerTick, ActionSource.FromTerrain(terrainTypeId), _playerQuery, "Contact", now, _statModifiers, _bodyParts, _mathUtility, _deadEntities, targetRule);

            var nextTickFrame = FrameDeadline.AfterStaggered(now, hazard.TickIntervalFrames, moved.EntityId);
            if (_exposures.Has(moved.EntityId))
            {
                _exposures.TryUpdate(moved.EntityId, (nextTickFrame, terrainTypeId), static (ref ContactDamageExposureComponent exposure, (uint NextTickFrame, ushort HazardTerrainTypeId) state) =>
                {
                    exposure.NextTickFrame = state.NextTickFrame;
                    exposure.HazardTerrainTypeId = state.HazardTerrainTypeId;
                });
            }
            else
            {
                _exposures.Add(moved.EntityId, new ContactDamageExposureComponent(nextTickFrame, terrainTypeId));
            }
        }
        else if (_exposures.Has(moved.EntityId))
        {
            _exposures.Remove(moved.EntityId);
        }
    }

    /// <summary>
    /// Drains this frame's moves first (entering, re-entering or leaving a hazard), then fires every
    /// exposure due a tick this frame. The drain is not tier-gated -- see StatusEffectAuraSystem's
    /// own Update comment for why (already self-limiting to entities that moved this exact frame).
    /// </summary>
    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var moved in _movedEntities.Items)
        {
            OnEntityMoved(moved, time.FrameCount);
        }

        _wheel.Tick(time.FrameCount, _tick);
    }

    /// <summary>Moves the entity's exposure past every tick owed while it was frozen, dealing none of them: an exposure is an interaction with the surroundings, not an effect already in progress, so it does not accrue while frozen.</summary>
    private void SkipOwedTicks(int entityId, long now)
    {
        if (!_exposures.TryGetReadonly(entityId, out var exposure) || !_terrain.TryGetContactHazard(exposure.HazardTerrainTypeId, out var hazard))
        {
            return;
        }

        _exposures.TryUpdate(entityId, (hazard.TickIntervalFrames, now), static (ref ContactDamageExposureComponent e, (ushort PeriodFrames, long Now) state) => e.SkipOwedPeriods(state.PeriodFrames, state.Now));
    }

    /// <summary>Always returns false (never removes here -- stepping off a hazard does that, in OnEntityMoved); see TimerFired's contract.</summary>
    private bool Tick(int entityId, ContactDamageExposureComponent exposure, long now)
    {
        // A corpse stops taking further contact damage -- otherwise a dead entity standing in
        // lava would keep re-triggering HealthDamage.Apply/EntityDamagedEvent forever. Not
        // re-armed, so the exposure rests inert (TimerFired: neither removed nor re-armed) rather
        // than being cleared.
        if (_deadEntities?.Has(entityId) == true)
        {
            return false;
        }

        // Defensive only -- a definition never loses its hazard mid-session. Rests, same as above.
        if (!_terrain.TryGetContactHazard(exposure.HazardTerrainTypeId, out var hazard))
        {
            return false;
        }

        BodyPartTargetRule? targetRule = hazard.PreferredTargetType is { } type ? new BodyPartTargetRule(type, BodyPartFallback.Bottommost) : null;
        HealthDamage.Apply(_health, _eventBus, entityId, hazard.DamagePerTick, ActionSource.FromTerrain(exposure.HazardTerrainTypeId), _playerQuery, "Contact", now, _statModifiers, _bodyParts, _mathUtility, _deadEntities, targetRule);

        _exposures.TryUpdate(entityId, hazard.TickIntervalFrames, static (ref ContactDamageExposureComponent e, ushort periodFrames) => e.RepeatEvery(periodFrames));

        return false;
    }
}
