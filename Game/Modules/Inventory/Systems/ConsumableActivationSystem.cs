using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Mana.Components;
using Game.Modules.Poison;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Inventory.Systems;

/// <summary>
/// Consumes a PendingConsumableActivationComponent (queued by Presentation, never applied by it
/// -- mirrors ActionActivationSystem/PendingActionActivationComponent exactly). Every
/// consumable activation sets the shared ActionLock on the *activating* entity, the same as an
/// Immediate action (see PotionActivator.Timing's own doc comment) -- there's no Delayed/
/// FreeCast equivalent for consumables today. The stack is ticked down
/// (InventoryActions.ConsumeItem) before the effect applies, per spec order. Only
/// item.Activator is PotionActivator exists today: PotionCooldownComponent -- and the punishment
/// Poison stack/PotionCooldownAbusedEvent for activating it again too soon -- belongs to whoever
/// actually receives the potion's effect (see ApplyPotionToTarget), not whoever drank/threw it.
/// Drinking your own potion means those are the same entity; throwing one at a goblin means the
/// goblin's own cooldown ticks, the thrower's does not. This stays this system's own kind-uniform
/// logic rather than a composable IActionEffectEntry -- see PLAN-action-effect-activator.md's
/// scoping decision for why: it doesn't vary per potion (Constitution, the only varying input, is
/// caster-side), so every potion already gets it automatically, and making it an entry every
/// item's Effects list must remember to include (including mod-defined potions) would
/// turn a currently-impossible-to-forget mechanic into a silently-omittable one.
/// </summary>
public sealed class ConsumableActivationSystem : ISystem
{
    private const byte StripeCountValue = 1;

    public byte StripeCount => StripeCountValue;

    private readonly PackedComponentPool<PendingConsumableActivationComponent> _pendingActivations;
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks;
    private readonly PackedComponentPool<PotionCooldownComponent> _potionCooldowns;
    private readonly PackedComponentPool<HealthComponent> _health;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly ItemCatalog _itemCatalog;
    private readonly IMapQuery _mapQuery;
    private readonly EventBus _eventBus;
    private readonly MathUtility _mathUtility;
    private readonly ComponentManager _componentManager;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly PackedComponentPool<ManaComponent>? _mana;
    private readonly PackedComponentPool<HotkeyExpansionUnlockComponent>? _hotkeyExpansionUnlocks;
    private readonly MultiComponentPool<AbilityScoreComponent>? _abilityScores;
    private readonly StatusEffectAuraApplierRegistry? _statusEffectAppliers;
    private readonly IPlayerQuery? _playerQuery;
    private readonly PackedComponentPool<StatusEffectAuraSourceComponent>? _auraSources;
    private readonly EntityStripeSet _stripeSet;

    public ConsumableActivationSystem(
        PackedComponentPool<PendingConsumableActivationComponent> pendingActivations,
        PackedComponentPool<ActionLockComponent> actionLocks,
        PackedComponentPool<PotionCooldownComponent> potionCooldowns,
        PackedComponentPool<HealthComponent> health,
        ItemCatalog itemCatalog,
        IMapQuery mapQuery,
        EventBus eventBus,
        MathUtility mathUtility,
        ComponentManager componentManager,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        PackedComponentPool<ManaComponent>? mana = null,
        PackedComponentPool<HotkeyExpansionUnlockComponent>? hotkeyExpansionUnlocks = null,
        MultiComponentPool<AbilityScoreComponent>? abilityScores = null,
        StatusEffectAuraApplierRegistry? statusEffectAppliers = null,
        IPlayerQuery? playerQuery = null,
        PackedComponentPool<StatusEffectAuraSourceComponent>? auraSources = null)
    {
        _pendingActivations = pendingActivations;
        _actionLocks = actionLocks;
        _potionCooldowns = potionCooldowns;
        _health = health;
        _itemCatalog = itemCatalog;
        _mapQuery = mapQuery;
        _eventBus = eventBus;
        _mathUtility = mathUtility;
        _componentManager = componentManager;
        _statModifiers = statModifiers;
        _deadEntities = deadEntities;
        _mana = mana;
        _hotkeyExpansionUnlocks = hotkeyExpansionUnlocks;
        _abilityScores = abilityScores;
        _statusEffectAppliers = statusEffectAppliers;
        _playerQuery = playerQuery;
        _auraSources = auraSources;

        _stripeSet = new EntityStripeSet(StripeCount, pendingActivations.EntityIds);
        pendingActivations.EntityAdded += _stripeSet.OnEntityAdded;
        pendingActivations.EntityRemoved += _stripeSet.OnEntityRemoved;
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _stripeSet.GetBucket(stripeIndex))
        {
            if (_deadEntities?.Has(entityId) == true)
            {
                continue;
            }

            if (!_pendingActivations.TryGetReadonly(entityId, out var request))
            {
                continue;
            }

            // Removed up front, not after dispatch -- every path below is a one-shot attempt,
            // so there's no outcome that should leave this request standing for a future visit.
            _pendingActivations.Remove(entityId);

            if (!_itemCatalog.TryGet(request.ItemDefinitionId, out var item) || item.Activator is not PotionActivator potionActivator)
            {
                continue;
            }

            if (!InventoryQueries.TryGetStack(_componentManager.GetMultiPool<InventoryItemStackComponent>(), entityId, request.ItemDefinitionId, out _))
            {
                continue;
            }

            if (ActionLockGate.IsBlocked(_actionLocks, entityId))
            {
                continue;
            }

            InventoryActions.ConsumeItem(_componentManager, entityId, request.ItemDefinitionId);

            ActivatePotion(item, potionActivator, entityId, request.TargetTiles);

            ActionLockGate.Lock(_actionLocks, entityId, potionActivator.Timing.ActionLockFrames);
        }
    }

    private void ActivatePotion(ItemDefinition item, PotionActivator potionActivator, int sourceEntityId, Vector3Int[] targetTiles)
    {
        foreach (var tile in targetTiles)
        {
            foreach (var targetEntityId in TargetResolution.EnumerateTargets(tile, _mapQuery))
            {
                ApplyPotionToTarget(item, sourceEntityId, targetEntityId);
            }
        }
    }

    /// <summary>
    /// Requires a HealthComponent to be considered a valid target at all (the same "is this a
    /// real, alive target" gate this method has always used) -- a target with Health but no
    /// pool a given entry actually needs (e.g. no ManaComponent for a Mana Potion) still counts
    /// as legitimately hit for the cooldown-abuse/reset bookkeeping below, since each entry
    /// no-ops gracefully on its own missing pool. Skipped entirely for a dead target -- "the
    /// target of a potion" means it landed on them, not just that a target tile happened to
    /// contain them. The cooldown-abuse check and PotionCooldownComponent reset both key off
    /// targetEntityId, not sourceEntityId -- see this class's own doc comment for why. The
    /// cooldown's own duration is computed from the target's Constitution
    /// (PotionCooldownEffects.ComputeDurationFrames), falling back to the un-scaled
    /// PotionCooldownEffects.DurationFrames when _abilityScores isn't wired or the target has no
    /// Constitution score.
    /// </summary>
    private void ApplyPotionToTarget(ItemDefinition item, int sourceEntityId, int targetEntityId)
    {
        if (_deadEntities?.Has(targetEntityId) == true || !_health.TryGetReadonly(targetEntityId, out _))
        {
            return;
        }

        var durationFrames = _abilityScores is not null && AbilityScoreQueries.TryGetComponent(_abilityScores, targetEntityId, AbilityScoreType.Constitution, out var constitution)
            ? PotionCooldownEffects.ComputeDurationFrames(constitution.Total)
            : PotionCooldownEffects.DurationFrames;

        if (_potionCooldowns.TryGetReadonly(targetEntityId, out var cooldown) && cooldown.FramesRemaining > 0)
        {
            PoisonEffects.ApplyStack(_componentManager, targetEntityId, StatusEffectSource.FromEntity(targetEntityId), PotionCooldownEffects.ComputeAbusePoisonDurationTicks(durationFrames));
            _eventBus.Publish(new PotionCooldownAbusedEvent(targetEntityId));
        }

        var context = new ActionEffectContext(
            SourceEntityId: sourceEntityId,
            TargetEntityId: targetEntityId,
            Health: _health,
            EventBus: _eventBus,
            MathUtility: _mathUtility,
            ComponentManager: _componentManager,
            ActivatorName: item.Name,
            ActivatorTags: item.Tags,
            StatModifiers: _statModifiers,
            AbilityScores: _abilityScores,
            Mana: _mana,
            HotkeyExpansionUnlocks: _hotkeyExpansionUnlocks,
            StatusEffectAppliers: _statusEffectAppliers,
            DeadEntities: _deadEntities,
            AuraSources: _auraSources,
            PlayerQuery: _playerQuery,
            DamageOverride: null);

        ActionEffectSequence.Apply(item.Effects, context);

        PotionCooldownEffects.Reset(_componentManager, targetEntityId, durationFrames);
    }
}
