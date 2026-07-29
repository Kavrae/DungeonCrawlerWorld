using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.ContactDamage.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.World;

namespace Game.Modules.ContactDamage.Systems;

/// <summary>
/// Detects contact via EntityMoved (constructor subscription, same pattern WorldEventSync
/// uses) and ticks ongoing exposure via Update, combined in one class since both operate on
/// the same ContactDamageExposureComponent pool. StripeCount is deliberately 1, not the 10
/// HealthRegenSystem/ActionLockSystem use -- see BurningSystem's own doc comment for why:
/// the population (entities currently standing on a hazard tile) is expected to stay small,
/// and striping would stretch "every N frames" into "every N * StripeCount real frames." At
/// StripeCount 1 there's only ever one stripe anyway, so Update iterates _exposures.EntityIds
/// directly rather than wrapping it in an EntityStripeSet purely to reproduce the same single
/// bucket (matching StatusEffectAuraSystem, the other StripeCount-1 system in this codebase).
/// The decrement-or-fire loop itself is Engine.ECS.Systems.CountdownTicker.Tick, shared with
/// BurningSystem/PoisonSystem/StatusEffectAuraSystem.
///
/// Per the literal spec, every EntityMoved landing on a hazard tile deals the immediate hit
/// and resets the countdown -- including hazard-tile-to-hazard-tile moves, not just a fresh
/// entry after being off one.
/// </summary>
public sealed class ContactDamageSystem : ISystem
{
    public byte StripeCount => 1;

    private readonly PackedComponentPool<DamageOnContactComponent> _hazards;
    private readonly PackedComponentPool<ContactDamageExposureComponent> _exposures;
    private readonly PackedComponentPool<HealthComponent> _health;
    private readonly EventBus _eventBus;
    private readonly IMapQuery _mapQuery;
    private readonly IPlayerQuery? _playerQuery;

    // CountdownTicker.Tick's contract needs a reused pendingRemovals list regardless -- this
    // system just never actually populates it, since Update here never removes exposure
    // (only OnEntityMoved does, on stepping off a hazard).
    private readonly List<int> _pendingRemovals = [];

    public ContactDamageSystem(
        PackedComponentPool<DamageOnContactComponent> hazards,
        PackedComponentPool<ContactDamageExposureComponent> exposures,
        PackedComponentPool<HealthComponent> health,
        EventBus eventBus,
        IMapQuery mapQuery,
        IPlayerQuery? playerQuery)
    {
        _hazards = hazards;
        _exposures = exposures;
        _health = health;
        _eventBus = eventBus;
        _mapQuery = mapQuery;
        _playerQuery = playerQuery;

        eventBus.Subscribe<EntityMoved>(OnEntityMoved);
    }

    private void OnEntityMoved(EntityMoved moved)
    {
        var terrainEntityId = _mapQuery.GetTerrainEntityIdAt(moved.NewPosition);
        if (terrainEntityId != -1 && _hazards.TryGetReadonly(terrainEntityId, out var hazard))
        {
            HealthDamage.Apply(_health, _eventBus, moved.EntityId, hazard.DamagePerTick, StatusEffectSource.FromEntity(terrainEntityId), _playerQuery, "Contact");

            if (_exposures.Has(moved.EntityId))
            {
                _exposures.TryUpdate(moved.EntityId, (hazard.TickIntervalFrames, terrainEntityId), static (ref ContactDamageExposureComponent exposure, (int TickIntervalFrames, int SourceEntityId) state) =>
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

    public void Update(EngineTime time, byte stripeIndex) =>
        CountdownTicker.Tick(_exposures, _exposures.EntityIds, _pendingRemovals, Tick);

    /// <summary>Always returns false (never removes here -- see this class's own doc comment for why); see CountdownTicker.Tick's own doc comment for the contract.</summary>
    private bool Tick(int entityId, ContactDamageExposureComponent exposure)
    {
        // Defensive only -- terrain is never removed once placed, so SourceEntityId should
        // always still have DamageOnContactComponent.
        if (!_hazards.TryGetReadonly(exposure.SourceEntityId, out var hazard))
        {
            return false;
        }

        HealthDamage.Apply(_health, _eventBus, entityId, hazard.DamagePerTick, StatusEffectSource.FromEntity(exposure.SourceEntityId), _playerQuery, "Contact");

        _exposures.TryUpdate(entityId, hazard.TickIntervalFrames, static (ref ContactDamageExposureComponent e, int tickIntervalFrames) => e.FramesUntilNextTick = tickIntervalFrames);

        return false;
    }
}
