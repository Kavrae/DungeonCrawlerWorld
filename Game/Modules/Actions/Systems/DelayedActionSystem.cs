using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Actions.Systems;

/// <summary>
/// Consumes pending delayed actions and resolves their effects when the action lock is released.
/// </summary>
/// <remarks>
/// Tiered off ProcessingTierComponent (TieredEntityStripeSet, matching ActionLockSystem's own
/// StripeCountValue so both stay in sync for the same entity) rather than a flat, untiered
/// EntityStripeSet -- confirmed a real cost at this game's actual population scale
/// (`phase-performance-testing` skill, PLAN-charge-attack-fill-indicator.md's own Addendum 4):
/// PendingDelayedActionComponent.Count over 10,000 map-wide during ordinary NPC-vs-NPC combat, this
/// system alone costing ~79ms of a 1000ms/sec budget visiting every one of them every single frame.
/// Deliberately still reads ActionLockComponent.CurrentLockFramesRemaining directly rather than
/// owning an independent ITickCountdown/CountdownTicker-driven timer of its own (an earlier TODO.md
/// proposal) -- a separate countdown ticked on its own tiered cadence could drift out of sync with
/// ActionLockSystem's own tiered decrement of the same entity, delaying (or, worse, racing ahead
/// of) exactly when the lock visually/logically clears. Reading the same ActionLockComponent both
/// systems already share, tiered off the same ProcessingTierComponent with the same StripeCount,
/// keeps them visiting this entity on the same cadence -- this system just checks whatever
/// ActionLockSystem already decremented, with only the same bounded, self-correcting staleness
/// every other tiered consumer in this codebase already accepts, never a second, independently-
/// drifting clock. This is also the exact invariant MapWindow's charge-fill telegraph
/// (DrawChargeFillHighlight) depends on for correctness -- see PLAN-charge-attack-fill-
/// indicator.md's own Design section.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class DelayedActionSystem : ISystem
{
    // Matches ActionLockSystem's own StripeCountValue -- both are tiered off the same
    // ProcessingTierComponent, so a given entity is visited by both on the same cadence (see this
    // class's own remarks on why that matters).
    private const byte StripeCountValue = 10;

    public byte StripeCount => StripeCountValue;

    private readonly PackedComponentPool<PendingDelayedActionComponent> _pendingActions;
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks;
    private readonly MultiComponentPool<ActionInstanceComponent> _actionInstances;
    private readonly PackedComponentPool<SimpleHealthComponent> _health;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly ActionCatalog _actionCatalog;
    private readonly IMapQuery _mapQuery;
    private readonly EventBus _eventBus;
    private readonly IPlayerQuery? _playerQuery;
    private readonly StatusEffectAuraApplierRegistry _statusEffectAppliers;
    private readonly ComponentManager _componentManager;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly MultiComponentPool<AbilityScoreComponent>? _abilityScores;
    private readonly MathUtility _mathUtility;
    private readonly MultiComponentPool<StatusEffectAuraSourceComponent>? _auraSources;
    private readonly PackedComponentPool<HotkeyExpansionUnlockComponent>? _hotkeyExpansionUnlocks;
    private readonly MultiComponentPool<BodyPartComponent>? _bodyParts;
    private readonly PackedComponentPool<DodgingComponent>? _dodgingEntities;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    public DelayedActionSystem(
        PackedComponentPool<PendingDelayedActionComponent> pendingActions,
        PackedComponentPool<ActionLockComponent> actionLocks,
        MultiComponentPool<ActionInstanceComponent> actionInstances,
        PackedComponentPool<SimpleHealthComponent> health,
        ActionCatalog actionCatalog,
        IMapQuery mapQuery,
        EventBus eventBus,
        MathUtility mathUtility,
        IPlayerQuery? playerQuery,
        StatusEffectAuraApplierRegistry statusEffectAppliers,
        ComponentManager componentManager,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        MultiComponentPool<AbilityScoreComponent>? abilityScores = null,
        MultiComponentPool<StatusEffectAuraSourceComponent>? auraSources = null,
        PackedComponentPool<HotkeyExpansionUnlockComponent>? hotkeyExpansionUnlocks = null,
        MultiComponentPool<BodyPartComponent>? bodyParts = null,
        PackedComponentPool<DodgingComponent>? dodgingEntities = null)
    {
        _pendingActions = pendingActions;
        _actionLocks = actionLocks;
        _actionInstances = actionInstances;
        _health = health;
        _statModifiers = statModifiers;
        _actionCatalog = actionCatalog;
        _mapQuery = mapQuery;
        _eventBus = eventBus;
        _mathUtility = mathUtility;
        _playerQuery = playerQuery;
        _statusEffectAppliers = statusEffectAppliers;
        _componentManager = componentManager;
        _deadEntities = deadEntities;
        _abilityScores = abilityScores;
        _auraSources = auraSources;
        _hotkeyExpansionUnlocks = hotkeyExpansionUnlocks;
        _bodyParts = bodyParts;
        _dodgingEntities = dodgingEntities;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, pendingActions, processingTiers, processingTierEvents);
    }

    /// <summary>Updates the delayed actions for whichever entities are due this frame, across every tier.</summary>
    /// <remarks>
    /// Delayed actions are resolved when the action lock is released.
    /// Each delayed action sets its own action lock duration.
    /// </remarks>
    /// <param name="time">The current engine time</param>
    /// <param name="stripeIndex">Unused -- TieredEntityStripeSet.GetDueEntities computes its own per-tier due bucket from time.FrameCount directly (see ActionLockSystem's identical shape), not from SystemManager's own rotating stripeIndex.</param>
    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _tieredStripeSet.GetDueEntities(time.FrameCount))
        {
            if (_deadEntities?.Has(entityId) == true)
            {
                _pendingActions.Remove(entityId);
                continue;
            }

            if (!_pendingActions.TryGetReadonly(entityId, out var pending) ||
                !_actionLocks.TryGetReadonly(entityId, out var actionLock) ||
                actionLock.CurrentLockFramesRemaining > 0)
            {
                continue;
            }

            if (ActionInstanceQueries.TryGet(_actionInstances, entityId, pending.ActionId, out var instance) &&
                ActionInstanceQueries.TryResolveEffectiveAction(_actionCatalog, instance, out var action))
            {
                ActionEffectResolver.Apply(action, entityId, pending.TargetTiles, _mapQuery, _health, _eventBus, _mathUtility, _playerQuery, _statusEffectAppliers, _componentManager, _statModifiers, _deadEntities, _abilityScores, _auraSources, _hotkeyExpansionUnlocks, _bodyParts, _dodgingEntities);
            }

            _pendingActions.Remove(entityId);
        }
    }
}
