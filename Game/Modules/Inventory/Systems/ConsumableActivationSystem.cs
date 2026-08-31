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
/// FreeCast equivalent for consumables today. Every request now names the exact stack being
/// activated (StackInstanceId, not ItemDefinitionId -- see PendingConsumableActivationComponent's
/// own doc comment); its effective ItemDefinition (its own Override if diverged, else the plain
/// catalog lookup -- see InventoryQueries.TryResolveEffectiveItem) is what actually gets applied,
/// so a diverged stack's own current state (a wand's remaining charges) is never bypassed by
/// accidentally reading the catalog original instead.
///
/// Dispatches on item.Activator's concrete type. PotionActivator/ScrollActivator: the stack is
/// ticked down (InventoryActions.ConsumeItemByStackInstanceId) before the effect applies, per
/// spec order (see TryBeginActivation, shared by both). PotionActivator: PotionCooldownComponent
/// -- and the punishment Poison stack/PotionCooldownAbusedEvent for activating it again too soon
/// -- belongs to whoever actually receives the potion's effect (see ApplyPotionToTarget), not
/// whoever drank/threw it. Drinking your own potion means those are the same entity; throwing one
/// at a goblin means the goblin's own cooldown ticks, the thrower's does not. This stays this
/// system's own kind-uniform logic rather than a composable IActionEffectEntry -- see
/// PLAN-action-effect-activator.md's scoping decision for why: it doesn't vary per potion
/// (Constitution, the only varying input, is caster-side), so every potion already gets it
/// automatically, and making it an entry every item's Effects list must remember to include
/// (including mod-defined potions) would turn a currently-impossible-to-forget mechanic into a
/// silently-omittable one.
///
/// ScrollActivator: no cooldown-abuse mechanic (potion-specific, never mentioned for scrolls) and
/// no hard SimpleHealthComponent requirement on the target (see ApplyScrollToTarget) -- instead scales
/// Range/AreaSize (already resolved into request.TargetTiles by Presentation, see
/// ScrollScalingEffects' own doc comment) and any duration the effect carries by the *caster's*
/// Intelligence, then records the activation toward mastering the scroll's spell (see
/// ScrollMasteryEffects).
///
/// WandActivator: not consumed from a shared stack at all -- each wand has its own remaining
/// Charges, decremented via InventoryActions.PeelOneIntoDivergentStack (see PeelWandCharge) rather
/// than InventoryActions.ConsumeItemByStackInstanceId, and (unlike Potion/Scroll) the peeled stack
/// gets a *new* StackInstanceId once it's actually diverged -- so this slot's own
/// ItemHotkeyBindingComponent is repointed to it afterward (see RepointItemHotkeyBinding), the one
/// piece of bookkeeping neither Potion nor Scroll ever needs. No mana cost, no cooldown-abuse
/// mechanic, no Intelligence duration-scaling (that's scroll-specific) -- charges were already
/// fixed once, at grant time (see Game.Modules.Inventory.WandGrantEffects).
/// </summary>
public sealed class ConsumableActivationSystem : ISystem
{
    private const byte StripeCountValue = 1;

    public byte StripeCount => StripeCountValue;

    private readonly PackedComponentPool<PendingConsumableActivationComponent> _pendingActivations;
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks;
    private readonly PackedComponentPool<PotionCooldownComponent> _potionCooldowns;
    private readonly PackedComponentPool<SimpleHealthComponent> _health;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly ItemCatalog _itemCatalog;
    private readonly ActionCatalog _actionCatalog;
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
    private readonly MultiComponentPool<StatusEffectAuraSourceComponent>? _auraSources;
    private readonly MultiComponentPool<ItemHotkeyBindingComponent>? _itemHotkeyBindings;
    private readonly MultiComponentPool<BodyPartComponent>? _bodyParts;
    private readonly EntityStripeSet _stripeSet;

    public ConsumableActivationSystem(
        PackedComponentPool<PendingConsumableActivationComponent> pendingActivations,
        PackedComponentPool<ActionLockComponent> actionLocks,
        PackedComponentPool<PotionCooldownComponent> potionCooldowns,
        PackedComponentPool<SimpleHealthComponent> health,
        ItemCatalog itemCatalog,
        ActionCatalog actionCatalog,
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
        MultiComponentPool<StatusEffectAuraSourceComponent>? auraSources = null,
        MultiComponentPool<ItemHotkeyBindingComponent>? itemHotkeyBindings = null,
        MultiComponentPool<BodyPartComponent>? bodyParts = null)
    {
        _pendingActivations = pendingActivations;
        _actionLocks = actionLocks;
        _potionCooldowns = potionCooldowns;
        _health = health;
        _itemCatalog = itemCatalog;
        _actionCatalog = actionCatalog;
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
        _itemHotkeyBindings = itemHotkeyBindings;
        _bodyParts = bodyParts;

        _stripeSet = EntityStripeSet.CreateAndWire(StripeCount, pendingActivations);
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

            if (!InventoryQueries.TryFindByStackInstanceId(_componentManager.GetMultiPool<InventoryItemStackComponent>(), entityId, request.StackInstanceId, out var stack) ||
                !InventoryQueries.TryResolveEffectiveItem(_itemCatalog, in stack, out var item))
            {
                continue;
            }

            switch (item.Activator)
            {
                case PotionActivator potionActivator:
                    if (!TryBeginActivation(entityId, request.StackInstanceId))
                    {
                        continue;
                    }

                    ActivatePotion(item, potionActivator, entityId, request.TargetTiles);
                    ActionLockGate.Lock(_actionLocks, entityId, potionActivator.Timing.ActionLockFrames);
                    break;

                case ScrollActivator scrollActivator:
                    if (!TryBeginActivation(entityId, request.StackInstanceId))
                    {
                        continue;
                    }

                    ActivateScroll(item, scrollActivator, entityId, request.TargetTiles);
                    ActionLockGate.Lock(_actionLocks, entityId, scrollActivator.Timing.ActionLockFrames);
                    break;

                case WandActivator wandActivator:
                    if (!TryBeginWandActivation(entityId, wandActivator.Charges))
                    {
                        continue;
                    }

                    PeelWandCharge(entityId, stack, item, wandActivator);
                    ActivateWand(item, entityId, request.TargetTiles);
                    ActionLockGate.Lock(_actionLocks, entityId, wandActivator.Timing.ActionLockFrames);
                    break;
            }
        }
    }

    /// <summary>Shared pre-checks + stack consumption for Potion/Scroll -- still holds the stack, action lock isn't currently blocking, then consumes one unit (per spec order, before the effect applies). Returns false (nothing consumed) if either check fails.</summary>
    private bool TryBeginActivation(int entityId, Guid stackInstanceId)
    {
        if (!InventoryQueries.TryFindByStackInstanceId(_componentManager.GetMultiPool<InventoryItemStackComponent>(), entityId, stackInstanceId, out _))
        {
            return false;
        }

        if (ActionLockGate.IsBlocked(_actionLocks, entityId))
        {
            return false;
        }

        InventoryActions.ConsumeItemByStackInstanceId(_componentManager, entityId, stackInstanceId);
        return true;
    }

    /// <summary>Wand counterpart to TryBeginActivation -- no stack to consume yet (see PeelWandCharge, called separately once this passes), just the two gates: charges remaining, and the shared ActionLock isn't currently blocking.</summary>
    private bool TryBeginWandActivation(int entityId, ushort charges) =>
        charges > 0 && !ActionLockGate.IsBlocked(_actionLocks, entityId);

    private void ActivatePotion(ItemDefinition item, PotionActivator potionActivator, int sourceEntityId, Vector3Int[] targetTiles)
    {
        foreach (var tile in targetTiles)
        {
            foreach (var targetEntityId in _mapQuery.GetOccupantEntityIdsAt(tile))
            {
                ApplyPotionToTarget(item, sourceEntityId, targetEntityId);
            }
        }
    }

    /// <summary>
    /// Requires health -- Simple or Complex -- to be considered a valid target at all (the same
    /// "is this a real, alive target" gate this method has always used), checked by presence
    /// across both pools rather than hard-requiring SimpleHealthComponent specifically -- a Complex
    /// target with no pool a given entry actually needs (e.g. no ManaComponent for a Mana Potion)
    /// still counts as legitimately hit for the cooldown-abuse/reset bookkeeping below, since each
    /// entry no-ops gracefully on its own missing pool. Skipped entirely for a dead target -- "the
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
        if (_deadEntities?.Has(targetEntityId) == true || (!_health.Has(targetEntityId) && _bodyParts?.Has(targetEntityId) != true))
        {
            return;
        }

        var durationFrames = _abilityScores is not null && AbilityScoreQueries.TryGetComponent(_abilityScores, targetEntityId, AbilityScoreType.Constitution, out var constitution)
            ? PotionCooldownEffects.ComputeDurationFrames(constitution.Total)
            : PotionCooldownEffects.DurationFrames;

        if (_potionCooldowns.TryGetReadonly(targetEntityId, out var cooldown) && cooldown.FramesRemaining > 0)
        {
            PoisonEffects.ApplyStack(_componentManager, targetEntityId, StatusEffectSource.FromEntity(targetEntityId), PotionCooldownEffects.ComputeAbusePoisonDurationTicks(durationFrames), _eventBus, _playerQuery);
            _eventBus.Publish(new PotionCooldownAbusedEvent(targetEntityId));
        }

        ActionEffectSequence.Apply(item.Effects, BuildContext(item, sourceEntityId, targetEntityId));

        PotionCooldownEffects.Reset(_componentManager, targetEntityId, durationFrames);
    }

    private void ActivateScroll(ItemDefinition item, ScrollActivator scrollActivator, int sourceEntityId, Vector3Int[] targetTiles)
    {
        var durationScaleMultiplier = ComputeScrollScaleMultiplier(sourceEntityId);

        foreach (var tile in targetTiles)
        {
            foreach (var targetEntityId in _mapQuery.GetOccupantEntityIdsAt(tile))
            {
                ApplyScrollToTarget(item, sourceEntityId, targetEntityId, durationScaleMultiplier);
            }
        }

        ScrollMasteryEffects.RecordUsage(_componentManager, _eventBus, _actionCatalog, item, sourceEntityId, scrollActivator.SpellId);
    }

    private float ComputeScrollScaleMultiplier(int sourceEntityId) =>
        _abilityScores is not null && AbilityScoreQueries.TryGetComponent(_abilityScores, sourceEntityId, AbilityScoreType.Intelligence, out var intelligence)
            ? ScrollScalingEffects.ComputeScaleMultiplier(intelligence.Total)
            : 1.0f;

    /// <summary>
    /// Unlike ApplyPotionToTarget, doesn't hard-require a SimpleHealthComponent on the target -- each
    /// effect entry already no-ops gracefully when its required component/pool is missing (the
    /// same "immortal but affectable" targeting melee already uses), and a scroll effect (e.g.
    /// TorchMarkEffectEntry) may not need Health at all. Skipped only for a dead target.
    /// </summary>
    private void ApplyScrollToTarget(ItemDefinition item, int sourceEntityId, int targetEntityId, float durationScaleMultiplier)
    {
        if (_deadEntities?.Has(targetEntityId) == true)
        {
            return;
        }

        ActionEffectSequence.Apply(item.Effects, BuildContext(item, sourceEntityId, targetEntityId, durationScaleMultiplier));
    }

    private void ActivateWand(ItemDefinition item, int sourceEntityId, Vector3Int[] targetTiles)
    {
        foreach (var tile in targetTiles)
        {
            foreach (var targetEntityId in _mapQuery.GetOccupantEntityIdsAt(tile))
            {
                ApplyWandToTarget(item, sourceEntityId, targetEntityId);
            }
        }
    }

    /// <summary>Same "immortal but affectable" treatment as ApplyScrollToTarget -- no hard SimpleHealthComponent requirement, each effect entry no-ops gracefully on its own missing pool. Skipped only for a dead target.</summary>
    private void ApplyWandToTarget(ItemDefinition item, int sourceEntityId, int targetEntityId)
    {
        if (_deadEntities?.Has(targetEntityId) == true)
        {
            return;
        }

        ActionEffectSequence.Apply(item.Effects, BuildContext(item, sourceEntityId, targetEntityId));
    }

    /// <summary>
    /// Decrements this specific wand's own Charges by one, uniformly whether it's the first shot
    /// off a fresh plain batch or the Nth shot depleting an already-divergent instance -- no
    /// plain-vs-divergent branch (see this class's own doc comment for why that uniformity is
    /// what keeps "equal states share one stack" true at every step, not just at creation). At 0
    /// remaining charges the wand is simply destroyed (InventoryActions.ConsumeItemByStackInstanceId
    /// decrements-and-removes the source stack directly) rather than adding a permanent 0-charge
    /// husk back via AddDivergentItem. Otherwise peels the depleted state into its own divergent
    /// stack (InventoryActions.PeelOneIntoDivergentStack) and repoints this slot's own hotkey
    /// binding to wherever that state actually landed (new stack, or merged into an existing one
    /// at the same charge count) -- without this repoint, every subsequent press would peel a
    /// fresh wand off the original stack instead of depleting the one already in the slot.
    /// </summary>
    private void PeelWandCharge(int entityId, InventoryItemStackComponent stack, ItemDefinition item, WandActivator wandActivator)
    {
        var newCharges = (ushort)(wandActivator.Charges - 1);

        if (newCharges == 0)
        {
            InventoryActions.ConsumeItemByStackInstanceId(_componentManager, entityId, stack.StackInstanceId);
            return;
        }

        var newOverride = item with { Activator = wandActivator with { Charges = newCharges } };
        var newStackInstanceId = InventoryActions.PeelOneIntoDivergentStack(_componentManager, entityId, stack.StackInstanceId, newOverride);
        RepointItemHotkeyBinding(entityId, stack.StackInstanceId, newStackInstanceId);
    }

    /// <summary>
    /// Repoints whichever hotkey slot referenced oldStackInstanceId (if any -- the wand may have
    /// been activated some other way, though today the hotbar is the only path) to
    /// newStackInstanceId instead. A no-op if _itemHotkeyBindings wasn't wired in (a test harness
    /// exercising activation without the full hotbar module) or nothing was actually bound to the
    /// old id.
    /// </summary>
    private void RepointItemHotkeyBinding(int entityId, Guid oldStackInstanceId, Guid newStackInstanceId) =>
        _itemHotkeyBindings?.TryUpdateFirst(
            entityId,
            (oldStackInstanceId, newStackInstanceId),
            static (ref readonly ItemHotkeyBindingComponent binding, (Guid Old, Guid New) state) => binding.StackInstanceId == state.Old,
            static (ref ItemHotkeyBindingComponent binding, (Guid Old, Guid New) state) => binding.StackInstanceId = state.New);

    /// <summary>Shared ActionEffectContext shape for both ApplyPotionToTarget and ApplyScrollToTarget -- identical field-for-field except DurationScaleMultiplier, which only a scroll activation ever sets away from its 1.0 default.</summary>
    private ActionEffectContext BuildContext(ItemDefinition item, int sourceEntityId, int targetEntityId, float durationScaleMultiplier = 1.0f) =>
        new(
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
            BodyParts: _bodyParts,
            PlayerQuery: _playerQuery,
            DurationScaleMultiplier: durationScaleMultiplier);
}
