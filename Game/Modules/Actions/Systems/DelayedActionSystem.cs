using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Actions.Systems;

/// <summary>
/// Resolves each pending delayed action on the exact frame its windup ends.
/// </summary>
/// <remarks>
/// The windup's end is a deadline carried by PendingDelayedActionComponent itself, copied from the
/// shared ActionLockComponent when the action was queued, and the component sits on a timer wheel
/// keyed to it -- so this system touches only the actions actually resolving this frame, at any
/// processing tier, instead of visiting every pending entity to ask "is the lock 0 yet?"
/// (PendingDelayedActionComponent.Count was measured over 10,000 map-wide during ordinary
/// NPC-vs-NPC combat, costing this system ~79ms of a 1000ms/sec budget before it was even tiered
/// -- see PLAN-charge-attack-fill-indicator.md's Addendum 4).
///
/// The old "stay on the same tiered cadence as ActionLockSystem so the two can't drift" invariant
/// this class used to defend is now structural: there is no second clock to drift, because the
/// lock and the pending action hold the same deadline value, and nothing ticks either of them.
/// MapWindow's charge-fill telegraph depends on that invariant (see PLAN-charge-attack-fill-
/// indicator.md's own Design section) and is strictly better served by it.
///
/// A cancelled action (right-click tap / Escape) simply removes the component; its wheel entry is
/// dropped as stale when the frame comes round (lazy cancellation, see PackedTimerWheel).
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class DelayedActionSystem : ISystem
{
    /// <summary>Every frame; the wheel only touches windups actually ending.</summary>
    public byte StripeCount => 1;

    private readonly PackedComponentPool<PendingDelayedActionComponent> _pendingActions;
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
    private readonly PackedTimerWheel<PendingDelayedActionComponent> _wheel;

    // Cached once instead of passing the method group every Update -- an instance method group
    // conversion allocates a fresh delegate every evaluation.
    private readonly TimerFired<PendingDelayedActionComponent> _resolve;

    public DelayedActionSystem(
        PackedComponentPool<PendingDelayedActionComponent> pendingActions,
        MultiComponentPool<ActionInstanceComponent> actionInstances,
        PackedComponentPool<SimpleHealthComponent> health,
        ActionCatalog actionCatalog,
        IMapQuery mapQuery,
        EventBus eventBus,
        MathUtility mathUtility,
        IPlayerQuery? playerQuery,
        StatusEffectAuraApplierRegistry statusEffectAppliers,
        ComponentManager componentManager,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        MultiComponentPool<AbilityScoreComponent>? abilityScores = null,
        MultiComponentPool<StatusEffectAuraSourceComponent>? auraSources = null,
        PackedComponentPool<HotkeyExpansionUnlockComponent>? hotkeyExpansionUnlocks = null,
        MultiComponentPool<BodyPartComponent>? bodyParts = null,
        PackedComponentPool<DodgingComponent>? dodgingEntities = null)
    {
        _pendingActions = pendingActions;
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
        _resolve = Resolve;
        _wheel = new PackedTimerWheel<PendingDelayedActionComponent>(pendingActions);
    }

    public void Update(EngineTime time, byte stripeIndex) => _wheel.Tick(time.FrameCount, _resolve);

    /// <summary>Always returns true -- a windup resolves exactly once, so the pending action is removed either way (see TimerFired's contract).</summary>
    private bool Resolve(int entityId, PendingDelayedActionComponent pending, long now)
    {
        // A corpse can't finish a windup. Removing it (rather than just skipping) is what keeps a
        // dead entity from carrying a stale pending action forever -- nothing else clears it.
        if (_deadEntities?.Has(entityId) == true)
        {
            return true;
        }

        if (ActionInstanceQueries.TryGet(_actionInstances, entityId, pending.ActionId, out var instance) &&
            ActionInstanceQueries.TryResolveEffectiveAction(_actionCatalog, instance, out var action))
        {
            ActionEffectResolver.Apply(action, entityId, pending.TargetTiles, _mapQuery, _health, _eventBus, _mathUtility, _playerQuery, _statusEffectAppliers, _componentManager, now, _statModifiers, _deadEntities, _abilityScores, _auraSources, _hotkeyExpansionUnlocks, _bodyParts, _dodgingEntities);
        }

        return true;
    }
}
