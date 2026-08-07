using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Mana;
using Game.Modules.Mana.Components;
using Game.Modules.Poison;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.Inventory.Systems;

/// <summary>
/// Consumes a PendingConsumableActivationComponent (queued by Presentation, never applied by it
/// -- mirrors AbilityActivationSystem/PendingAbilityActivationComponent exactly). Every
/// consumable activation sets the shared ActionLock on the *activating* entity, the same as an
/// Immediate ability (see ConsumableEffect.ActionLockFrames' own doc comment) -- there's no
/// Delayed/FreeCast equivalent for consumables today. The stack is ticked down
/// (InventoryActions.ConsumeItem) before the effect applies, per spec order. Only
/// ConsumableKind.Potion exists today: PotionCooldownComponent -- and the punishment Poison
/// stack/PotionCooldownAbusedEvent for activating it again too soon -- belongs to whoever
/// actually receives the potion's effect (see ApplyPotionToTarget), not whoever drank/threw it.
/// Drinking your own potion means those are the same entity; throwing one at a goblin means the
/// goblin's own cooldown ticks, the thrower's does not. Shared across both potion effects
/// (Health/Mana) rather than per-resource, since it's the target's own overdose state, not tied
/// to which resource the potion happened to restore.
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
    private readonly ComponentManager _componentManager;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly PackedComponentPool<ManaComponent>? _mana;
    private readonly EntityStripeSet _stripeSet;

    public ConsumableActivationSystem(
        PackedComponentPool<PendingConsumableActivationComponent> pendingActivations,
        PackedComponentPool<ActionLockComponent> actionLocks,
        PackedComponentPool<PotionCooldownComponent> potionCooldowns,
        PackedComponentPool<HealthComponent> health,
        ItemCatalog itemCatalog,
        IMapQuery mapQuery,
        EventBus eventBus,
        ComponentManager componentManager,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        PackedComponentPool<ManaComponent>? mana = null)
    {
        _pendingActivations = pendingActivations;
        _actionLocks = actionLocks;
        _potionCooldowns = potionCooldowns;
        _health = health;
        _itemCatalog = itemCatalog;
        _mapQuery = mapQuery;
        _eventBus = eventBus;
        _componentManager = componentManager;
        _statModifiers = statModifiers;
        _deadEntities = deadEntities;
        _mana = mana;

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

            if (!_itemCatalog.TryGet(request.ItemDefinitionId, out var item) || item.Consumable is not { } consumable)
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

            switch (consumable.Kind)
            {
                case ConsumableKind.Potion:
                    ActivatePotion(consumable, request.TargetTiles);
                    break;
            }

            ActionLockGate.Lock(_actionLocks, entityId, consumable.ActionLockFrames);
        }
    }

    private void ActivatePotion(ConsumableEffect consumable, Vector3Int[] targetTiles)
    {
        foreach (var tile in targetTiles)
        {
            var blockingEntityId = _mapQuery.GetEntityIdAt(tile);
            if (blockingEntityId != -1)
            {
                ApplyPotionToTarget(consumable, blockingEntityId);
            }

            // Tiny/Phasing entities never occupy the Blocking slot GetEntityIdAt just checked
            // (see World.IsBlocking) -- mirrors AbilityEffectResolver's own per-tile loop so a
            // thrown potion's splash hits every non-Blocking occupant too, not just the one
            // Blocking one.
            foreach (var nonBlockingEntityId in _mapQuery.GetNonBlockingEntityIdsAt(tile))
            {
                ApplyPotionToTarget(consumable, nonBlockingEntityId);
            }
        }
    }

    /// <summary>
    /// HealFraction/ManaFraction are each a fraction of the target's own effective Maximum*, so
    /// they're computed per target here (unlike ability damage, which scales once from the
    /// caster's own modifiers before the target loop) -- a splash hitting entities with different
    /// maximums restores each by its own fraction, not the caster's. Requires a HealthComponent
    /// to be considered a valid target at all (the same "is this a real, alive target" gate the
    /// Health-only version of this method always used) -- a Mana Potion additionally requires the
    /// target to actually have a ManaComponent (not every entity does, see ManaComponent's own
    /// doc comment) to receive anything, but a target with Health and no Mana still counts as
    /// legitimately hit for the cooldown-abuse/reset bookkeeping below. The cooldown-abuse check
    /// and PotionCooldownComponent reset both key off targetEntityId, not the activating entity
    /// -- see this class's own doc comment for why, and are shared across both potion kinds
    /// rather than per-resource. Skipped entirely for a dead target -- "the target of a potion"
    /// means it landed on them, not just that a target tile happened to contain them.
    /// </summary>
    private void ApplyPotionToTarget(ConsumableEffect consumable, int targetEntityId)
    {
        if (_deadEntities?.Has(targetEntityId) == true || !_health.TryGetReadonly(targetEntityId, out var targetHealth))
        {
            return;
        }

        if (_potionCooldowns.TryGetReadonly(targetEntityId, out var cooldown) && cooldown.FramesRemaining > 0)
        {
            PoisonEffects.ApplyStack(_componentManager, targetEntityId, StatusEffectSource.FromEntity(targetEntityId), PotionCooldownEffects.AbusePoisonDurationTicks);
            _eventBus.Publish(new PotionCooldownAbusedEvent(targetEntityId));
        }

        if (consumable.HealFraction > 0)
        {
            var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(_statModifiers, targetEntityId, StatModifierTarget.MaximumHealth, targetHealth.MaximumHealth);
            var healAmount = (short)(consumable.HealFraction * effectiveMaximumHealth);
            HealthHeal.Apply(_health, targetEntityId, healAmount, _statModifiers);
        }

        if (consumable.ManaFraction > 0 && _mana is not null && _mana.TryGetReadonly(targetEntityId, out var targetMana))
        {
            var effectiveMaximumMana = StatModifierMath.GetEffectiveValue(_statModifiers, targetEntityId, StatModifierTarget.MaximumMana, targetMana.MaximumMana);
            var manaAmount = (short)(consumable.ManaFraction * effectiveMaximumMana);
            ManaRestore.Apply(_mana, targetEntityId, manaAmount, _statModifiers);
        }

        PotionCooldownEffects.Reset(_componentManager, targetEntityId);
    }
}
