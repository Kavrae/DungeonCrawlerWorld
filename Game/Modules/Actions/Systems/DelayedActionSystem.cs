using Engine.ECS.Entities;
using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions.Components;
using Game.Modules.Death.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Game.World;
using Game.Blueprints;

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
/// NPC-vs-NPC combat, costing this system ~79ms of a 1000ms/sec budget before it was even tiered).
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
    /// <summary>Drops a windup whose owner has just frozen: its target is either frozen too (untargetable across the seam) or simulated and long gone by the time the owner thaws, so resolving it later would land an attack nobody could react to.</summary>
    private void CancelWindupOnFreeze(int entityId, ProcessingTier.Components.ProcessingTierLevel tier)
    {
        if (!ProcessingTierQuery.IsSimulatedTier(tier))
        {
            _pendingActions.Remove(entityId);
        }
    }

    /// <summary>Every frame; the wheel only touches windups actually ending.</summary>
    public byte StripeCount => 1;

    private readonly PackedComponentPool<PendingDelayedActionComponent> _pendingActions;
    private readonly EntityActions _actions;
    private readonly PackedComponentPool<SimpleHealthComponent> _health;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly ActionCatalog _actionCatalog;
    private readonly IMapQuery _mapQuery;
    private readonly EventBus _eventBus;
    private readonly IPlayerQuery? _playerQuery;
    private readonly StatusEffectAuraApplierRegistry _statusEffectAppliers;
    private readonly ComponentManager _componentManager;
    private readonly EntityKeys _entityKeys;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly PackedComponentPool<AbilityScoresComponent>? _abilityScores;
    private readonly MathUtility _mathUtility;
    private readonly MultiComponentPool<StatusEffectAuraSourceComponent>? _auraSources;
    private readonly PackedComponentPool<HotkeyExpansionUnlockComponent>? _hotkeyExpansionUnlocks;
    private readonly EntityBodyParts? _bodyParts;
    private readonly PackedComponentPool<DodgingComponent>? _dodgingEntities;
    private readonly ProcessingTierQuery? _processingTiers;
    private readonly BlueprintRegistry? _creatures;
    private readonly PackedTimerWheel<PendingDelayedActionComponent> _wheel;

    // Cached once instead of passing the method group every Update -- an instance method group
    // conversion allocates a fresh delegate every evaluation.
    private readonly TimerFired<PendingDelayedActionComponent> _resolve;

    public DelayedActionSystem(
        PackedComponentPool<PendingDelayedActionComponent> pendingActions,
        EntityActions actions,
        PackedComponentPool<SimpleHealthComponent> health,
        ActionCatalog actionCatalog,
        IMapQuery mapQuery,
        EventBus eventBus,
        MathUtility mathUtility,
        IPlayerQuery? playerQuery,
        StatusEffectAuraApplierRegistry statusEffectAppliers,
        ComponentManager componentManager,
        EntityKeys entityKeys,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        PackedComponentPool<AbilityScoresComponent>? abilityScores = null,
        MultiComponentPool<StatusEffectAuraSourceComponent>? auraSources = null,
        PackedComponentPool<HotkeyExpansionUnlockComponent>? hotkeyExpansionUnlocks = null,
        EntityBodyParts? bodyParts = null,
        PackedComponentPool<DodgingComponent>? dodgingEntities = null,
        SimulationScope? simulationScope = null,
        ProcessingTierQuery? processingTiers = null,
        ProcessingTierEvents? processingTierEvents = null,
        BlueprintRegistry? creatures = null)
    {
        _pendingActions = pendingActions;
        _actions = actions;
        _health = health;
        _statModifiers = statModifiers;
        _actionCatalog = actionCatalog;
        _mapQuery = mapQuery;
        _eventBus = eventBus;
        _mathUtility = mathUtility;
        _playerQuery = playerQuery;
        _statusEffectAppliers = statusEffectAppliers;
        _componentManager = componentManager;
        _entityKeys = entityKeys;
        _deadEntities = deadEntities;
        _abilityScores = abilityScores;
        _auraSources = auraSources;
        _hotkeyExpansionUnlocks = hotkeyExpansionUnlocks;
        _bodyParts = bodyParts;
        _dodgingEntities = dodgingEntities;
        _processingTiers = processingTiers;
        _creatures = creatures;
        _resolve = Resolve;
        _wheel = new PackedTimerWheel<PendingDelayedActionComponent>(pendingActions, simulationScope);

        if (processingTierEvents is not null)
        {
            processingTierEvents.TierChanged += CancelWindupOnFreeze;
        }
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

        if (_actions.TryGetEffectiveAction(entityId, pending.ActionId, out var action))
        {
            ActionEffectResolver.Apply(action, entityId, pending.TargetTiles, _mapQuery, _health, _eventBus, _mathUtility, _playerQuery, _statusEffectAppliers, _componentManager, _entityKeys, now, _statModifiers, _deadEntities, _abilityScores, _auraSources, _hotkeyExpansionUnlocks, _bodyParts, _dodgingEntities, _processingTiers, _creatures);
        }

        return true;
    }
}
