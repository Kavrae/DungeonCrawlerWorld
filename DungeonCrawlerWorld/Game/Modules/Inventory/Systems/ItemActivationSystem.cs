using Engine.ECS.Components;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.StatusEffects;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Blueprints;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.Poison;
using Game.Modules.ProcessingTier;
using Game.World;

namespace Game.Modules.Inventory.Systems;

/// <summary>Consumes a PendingItemActivationComponent (queued by Presentation or an NPC, never applied by either) and uses the item through Activations.</summary>
/// <remarks>
/// <para>
/// A request names the exact stack being used (StackInstanceId, not ItemDefinitionId -- see
/// PendingItemActivationComponent); its effective ItemDefinition (its own Override if diverged, else the catalog's --
/// InventoryQueries.TryResolveEffectiveItem) is what gets applied, so a diverged stack's own state (a wand's remaining
/// charges) is never bypassed by reading the catalog original.
/// </para>
/// <para>
/// Each request runs through Activations, the sequence an action's use runs through too (cooldown, blocker, lock, then
/// the timing step), and ItemUse is the item's side of it. An item honours its activator's whole ActionTiming, as an
/// action does: Immediate, Delayed or FreeCast, its lock, its cooldown and ReleasesActionLock. What a kind spends
/// (TrySpend): a potion, scroll or consumable uses up one unit (InventoryActions.RemoveOneUnit); a wand one charge
/// (PeelWandCharge), the peeled unit landing in a stack of its own, so the pressed slot is repointed to it
/// (ItemHotkeyBindingActions.RepointAfterUnitMoved). A Delayed use spends when its windup starts and lands what it
/// captured when it resolves (Resolve, the item windup resolver).
/// </para>
/// <para>
/// What a kind does (ApplyItem) goes through ActivationEffectResolver, the walk an action's effects take, so Dodge,
/// Stagger, the seam and corpses are reached the same way; a kind adds only its own rule. A potion lands only on
/// something with health, and its cooldown -- with the abuse Poison for drinking again too soon -- belongs to whoever
/// it lands on, not whoever drank or threw it (PotionTargetRule). That stays this system's own rule rather than an
/// IEffectEntry: it doesn't vary per potion, so every potion, a mod's included, gets it without its Effects having to
/// remember it. A scroll scales range and area (already in its TargetSelection, see ScrollScalingEffects) and
/// durations by its reader's Intelligence, then records the read toward mastering its spell (ScrollMasteryEffects). A
/// wand or a consumable has no rule of its own (a tome is a consumable, its effect a SpellGrant).
/// </para>
/// <para>
/// A toggle item spends nothing: using it flips one unit between lit and unlit (ToggleItemActions.TryLight, which also
/// puts it down in Ground mode, and TryToggle); what it holds while lit follows from the stack change
/// (ToggleItemHolderSync).
/// </para>
/// <para>
/// Striped, not tiered: this drains a queue of activations already committed to this frame, so deferring one to a
/// coarse tier's cadence would leave a drink the player already pressed unresolved for up to a divisor's worth of
/// frames. The population is bounded by the activations queued right now, not by entity count, so there is nothing for
/// tiering to save -- the same reasoning as ActionActivationSystem, which drains the equivalent queue for actions.
/// </para>
/// </remarks>
public sealed class ItemActivationSystem : ISystem, IItemWindupResolver
{
    private const byte StripeCountValue = 1;

    public byte StripeCount => StripeCountValue;

    private readonly PackedComponentPool<PendingItemActivationComponent> _pendingActivations;
    private readonly PackedComponentPool<PotionCooldownComponent> _potionCooldowns;
    private readonly PackedComponentPool<SimpleHealthComponent> _health;
    private readonly EffectServices _effectServices;
    private readonly ItemCatalog _itemCatalog;
    private readonly ActionCatalog _actionCatalog;
    private readonly IMapQuery _mapQuery;
    private readonly EventBus _eventBus;
    private readonly ComponentManager _componentManager;
    private readonly PackedComponentPool<DeadComponent> _deadEntities;
    private readonly PackedComponentPool<AbilityScoresComponent> _abilityScores;
    private readonly MultiComponentPool<ItemHotkeyBindingComponent> _itemHotkeyBindings;
    private readonly EntityBodyParts _bodyParts;
    private readonly ProcessingTierQuery _processingTiers;
    private readonly TargetResolution _targetResolution;
    private readonly ToggleItemGroundPlacement _toggleItemGroundPlacement;
    private readonly List<Vector3Int> _targetTilesScratch = [];

    private readonly HashSet<int> _reachedScratch = [];
    private readonly PackedComponentPool<DodgingComponent> _dodgingEntities;
    private readonly EntityStripeSet _stripeSet;
    private readonly Activations _activations;

    /// <summary>The simulation frame of the Update in progress -- see Update.</summary>
    private long _now;

    public ItemActivationSystem(
        PackedComponentPool<PendingItemActivationComponent> pendingActivations,
        PackedComponentPool<ActionLockComponent> actionLocks,
        PackedComponentPool<PotionCooldownComponent> potionCooldowns,
        EffectServices effectServices,
        ItemCatalog itemCatalog,
        ActionCatalog actionCatalog,
        IMapQuery mapQuery,
        PackedComponentPool<MeleeDisabledComponent> meleeDisabled,
        PackedComponentPool<DodgingComponent> dodgingEntities,
        MultiComponentPool<ItemHotkeyBindingComponent> itemHotkeyBindings,
        ProcessingTierQuery processingTiers,
        PackedComponentPool<PendingWindupComponent> pendingWindups,
        TargetResolution targetResolution,
        ToggleItemGroundPlacement toggleItemGroundPlacement)
    {
        _toggleItemGroundPlacement = toggleItemGroundPlacement;
        _effectServices = effectServices;
        _pendingActivations = pendingActivations;
        _potionCooldowns = potionCooldowns;
        _health = effectServices.Health;
        _itemCatalog = itemCatalog;
        _actionCatalog = actionCatalog;
        _mapQuery = mapQuery;
        _eventBus = effectServices.EventBus;
        _componentManager = effectServices.ComponentManager;
        _deadEntities = effectServices.DeadEntities;
        _dodgingEntities = dodgingEntities;
        _abilityScores = effectServices.AbilityScores;
        _itemHotkeyBindings = itemHotkeyBindings;
        _bodyParts = effectServices.BodyParts;
        _processingTiers = processingTiers;
        _targetResolution = targetResolution;

        _stripeSet = EntityStripeSet.CreateAndWire(StripeCount, pendingActivations);
        _activations = new Activations(actionLocks, pendingWindups, meleeDisabled, effectServices);
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        // The frame every activation this update happens on -- read by BuildContext and the timer
        // writers below, rather than threaded through each private helper.
        _now = time.FrameCount;

        foreach (var entityId in _stripeSet.GetBucket(stripeIndex))
        {
            if (_deadEntities.Has(entityId))
            {
                _pendingActivations.Remove(entityId);
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
                !InventoryQueries.TryResolveEffectiveItem(_itemCatalog, in stack, out var item) ||
                item.Activator is not { } activator)
            {
                continue;
            }

            var use = new ItemUse(this, entityId, stack, item, activator, request);
            _activations.TryUse(entityId, ref use, _now);
        }
    }

    /// <summary>Resolves an item's windup with the definition it captured: a toggle lights one unit if the holder still has it unlit; anything else applies its effects, its unit or charge having been spent when the windup began.</summary>
    public void Resolve(int entityId, in PendingWindupComponent windup, long now)
    {
        _now = now;
        var item = (ItemDefinition)windup.Definition;

        if (item.Activator is not ToggleItemActivator)
        {
            ApplyItem(entityId, item, windup.Selection);
            return;
        }

        var stackInstanceId = windup.Activatable.StackInstanceId;
        if (InventoryQueries.TryFindByStackInstanceId(_componentManager.GetMultiPool<InventoryItemStackComponent>(), entityId, stackInstanceId, out var stack) &&
            InventoryQueries.TryResolveEffectiveItem(_itemCatalog, in stack, out var heldItem) &&
            heldItem.Activator is ToggleItemActivator { IsToggledOn: false })
        {
            TryLightUnit(entityId, heldItem, stackInstanceId, windup.ActivatedFromSlot, windup.Selection);
        }
    }

    /// <summary>What a potion, scroll, wand or consumable does when used, now or when its windup resolves: its effects where selection lands, through the walk actions share, with its kind's own rule.</summary>
    /// <remarks>A potion lands only on something with health and keeps its cooldown (PotionTargetRule); a scroll scales its durations by its reader's Intelligence and records the read toward mastering its spell.</remarks>
    private void ApplyItem(int entityId, ItemDefinition item, TargetSelection selection)
    {
        var resolved = _targetResolution.Resolve(entityId, item.Activator!.Targeting, selection, _targetTilesScratch);

        switch (item.Activator)
        {
            case PotionActivator:
                var potionRule = new PotionTargetRule(this);
                ActivationEffectResolver.Apply(item, ActivatableKind.Item, entityId, _targetTilesScratch, resolved, _effectServices, _mapQuery, _now, _dodgingEntities, _processingTiers, _reachedScratch, ref potionRule);
                break;

            case ScrollActivator scrollActivator:
                var scrollRule = default(EveryTargetRule);
                ActivationEffectResolver.Apply(item, ActivatableKind.Item, entityId, _targetTilesScratch, resolved, _effectServices, _mapQuery, _now, _dodgingEntities, _processingTiers, _reachedScratch, ref scrollRule, ComputeScrollScaleMultiplier(entityId));
                ScrollMasteryEffects.RecordUsage(_componentManager, _eventBus, _actionCatalog, item, entityId, scrollActivator.SpellId);
                break;

            case WandActivator or ConsumableActivator:
                var everyTarget = default(EveryTargetRule);
                ActivationEffectResolver.Apply(item, ActivatableKind.Item, entityId, _targetTilesScratch, resolved, _effectServices, _mapQuery, _now, _dodgingEntities, _processingTiers, _reachedScratch, ref everyTarget);
                break;
        }
    }

    /// <summary>What using item takes besides its activation effects: one unit of a potion, scroll or consumable, one charge of a wand. False, taking nothing, for a wand with no charges left.</summary>
    /// <param name="usedUnitStackInstanceId">The stack the used unit is in afterwards: a wand's peeled unit, 0 for one used up.</param>
    private bool TrySpend(int entityId, InventoryItemStackComponent stack, ItemDefinition item, HotkeySlot? activatedFromSlot, out uint usedUnitStackInstanceId)
    {
        usedUnitStackInstanceId = stack.StackInstanceId;
        switch (item.Activator)
        {
            case WandActivator wandActivator:
                if (wandActivator.Charges == 0)
                {
                    return false;
                }

                usedUnitStackInstanceId = PeelWandCharge(entityId, stack, item, wandActivator, activatedFromSlot);
                return true;

            case PotionActivator or ScrollActivator or ConsumableActivator:
                InventoryActions.RemoveOneUnit(_componentManager, entityId, stack.StackInstanceId);
                usedUnitStackInstanceId = 0;
                return true;

            default:
                return true;
        }
    }

    /// <summary>Moves the used unit, in stack unitStackInstanceId, to a stack cooling for cooldownFrames. Returns the stack it is in afterwards.</summary>
    /// <remarks>A slot follows the unit only when the stack it was bound to is gone, so a slot on a stack of several keeps using the ready ones.</remarks>
    private uint StartUnitCooldown(int entityId, uint unitStackInstanceId, ushort cooldownFrames, long now)
    {
        var coolingStackInstanceId = InventoryActions.MoveOneUnitIntoCooldown(_componentManager, entityId, unitStackInstanceId, FrameDeadline.After(now, cooldownFrames));
        if (coolingStackInstanceId == 0)
        {
            return unitStackInstanceId;
        }

        var oldStackStillHeld = InventoryQueries.TryFindByStackInstanceId(_componentManager.GetMultiPool<InventoryItemStackComponent>(), entityId, unitStackInstanceId, out _);
        ItemHotkeyBindingActions.RepointAfterUnitMoved(_itemHotkeyBindings, entityId, unitStackInstanceId, coolingStackInstanceId, activatedFromSlot: null, oldStackStillHeld);
        return coolingStackInstanceId;
    }

    /// <summary>Lights one unit (putting it down in Ground mode) and publishes the use. False when nothing was lit.</summary>
    private bool TryLightUnit(int entityId, ItemDefinition item, uint stackInstanceId, HotkeySlot? activatedFromSlot, TargetSelection selection)
    {
        if (!ToggleItemActions.TryLight(_componentManager, _itemCatalog, _toggleItemGroundPlacement, entityId, stackInstanceId, activatedFromSlot, selection))
        {
            return false;
        }

        _eventBus.Publish(new ActivationUsedEvent(entityId, ActivatableKind.Item, item.Id, item.Tags));
        return true;
    }

    /// <summary>Puts one lit unit out and publishes the use. False when nothing was put out.</summary>
    private bool TryPutOutUnit(int entityId, ItemDefinition item, uint stackInstanceId, HotkeySlot? activatedFromSlot)
    {
        if (!ToggleItemActions.TryToggle(_componentManager, _itemCatalog, entityId, stackInstanceId, out _, activatedFromSlot))
        {
            return false;
        }

        _eventBus.Publish(new ActivationUsedEvent(entityId, ActivatableKind.Item, item.Id, item.Tags));
        return true;
    }

    /// <summary>An item's side of the activation sequence: what it spends is a unit or a charge, its cooldown is the used unit's own, what it does depends on its activator, and a toggle flips one unit.</summary>
    /// <remarks>Follows the used unit from stack to stack as the sequence spends it and starts its cooldown, so the flip and the windup name the stack it is in by then.</remarks>
    private struct ItemUse(ItemActivationSystem system, int entityId, InventoryItemStackComponent stack, ItemDefinition item, IActionActivator activator, PendingItemActivationComponent request) : IActivationUse
    {
        private uint _unitStackInstanceId = request.StackInstanceId;

        public readonly ActivatableDefinition Definition => item;

        public readonly ActionTiming Timing => activator.Timing;

        public readonly bool IsToggle => activator is ToggleItemActivator;

        public readonly bool IsToggledOn => activator is ToggleItemActivator { IsToggledOn: true };

        public readonly bool IsOnCooldown(long now) => stack.IsCoolingDown(now);

        public void StartCooldown(long now)
        {
            if (activator.Timing.CooldownFrames is { } cooldownFrames && _unitStackInstanceId != 0)
            {
                _unitStackInstanceId = system.StartUnitCooldown(entityId, _unitStackInstanceId, cooldownFrames, now);
            }
        }

        public bool TrySpend(long now) => system.TrySpend(entityId, stack, item, request.ActivatedFromSlot, out _unitStackInstanceId);

        public readonly void ApplyNow(long now) => system.ApplyItem(entityId, item, request.Selection);

        public readonly void TurnOn(long now) => system.TryLightUnit(entityId, item, _unitStackInstanceId, request.ActivatedFromSlot, request.Selection);

        public readonly void TurnOff(long now) => system.TryPutOutUnit(entityId, item, _unitStackInstanceId, request.ActivatedFromSlot);

        public readonly PendingWindupComponent CreateWindup() => PendingWindupComponent.ForItem(_unitStackInstanceId, item, request.ActivatedFromSlot, request.Selection, readyAtFrame: 0);
    }

    /// <summary>
    /// A potion's rule at each target it reaches. It lands only on something with health -- Simple or Complex,
    /// alive or dead -- checked by presence across both pools: a target with no pool a given entry needs (no
    /// ManaComponent for a Mana Potion) still counts as hit for the cooldown, since each entry no-ops on its own
    /// missing pool. The cooldown belongs to whoever the potion lands on, not whoever drank or threw it (see this
    /// class's own doc comment); the abuse poison is credited to the user, since whoever used the potion caused it,
    /// so a thrown potion's poison is the thrower's damage (and kill), and a drunk one's the drinker's own. The
    /// cooldown's length comes from the target's Constitution (PotionCooldownEffects.ComputeDurationFrames), or
    /// PotionCooldownEffects.DurationFrames when it has none.
    /// </summary>
    private struct PotionTargetRule(ItemActivationSystem system) : IActivationTargetRule
    {
        private ushort _cooldownFrames;

        public readonly bool Admits(int targetEntityId) => system._health.Has(targetEntityId) || system._bodyParts.Has(targetEntityId);

        public void BeforeEffects(int targetEntityId, in EffectContext context)
        {
            _cooldownFrames = AbilityScoreQueries.TryGetComponent(system._abilityScores, targetEntityId, AbilityScoreType.Constitution, out var constitution)
                ? PotionCooldownEffects.ComputeDurationFrames(constitution.Total)
                : PotionCooldownEffects.DurationFrames;

            if (system._potionCooldowns.TryGetReadonly(targetEntityId, out var cooldown) && PotionCooldownEffects.FramesRemaining(cooldown, system._now) > 0)
            {
                new StatusEffectGrant(StatusEffectType.Poison, DurationTicks: PotionCooldownEffects.ComputeAbusePoisonDurationTicks(_cooldownFrames)).Apply(context);
                system._eventBus.Publish(new PotionCooldownAbusedEvent(targetEntityId));
            }
        }

        public readonly void AfterEffects(int targetEntityId) =>
            PotionCooldownEffects.Reset(system._componentManager, targetEntityId, _cooldownFrames, system._now);
    }

    private float ComputeScrollScaleMultiplier(int sourceEntityId) =>
        AbilityScoreQueries.TryGetComponent(_abilityScores, sourceEntityId, AbilityScoreType.Intelligence, out var intelligence)
            ? ScrollScalingEffects.ComputeScaleMultiplier(intelligence.Total)
            : 1.0f;

    /// <summary>
    /// Decrements this specific wand's own Charges by one, uniformly whether it's the first shot
    /// off a fresh plain batch or the Nth shot depleting an already-divergent instance -- no
    /// plain-vs-divergent branch (see this class's own doc comment for why that uniformity is
    /// what keeps "equal states share one stack" true at every step, not just at creation). At 0
    /// remaining charges the wand is simply destroyed (InventoryActions.RemoveOneUnit
    /// decrements-and-removes the source stack directly) rather than adding a permanent 0-charge
    /// husk back via AddDivergentItem. Otherwise peels the depleted state into its own divergent
    /// stack (InventoryActions.MoveOneUnit) and repoints this slot's own hotkey
    /// binding to wherever that state actually landed (new stack, or merged into an existing one
    /// at the same charge count) -- without this repoint, every subsequent press would peel a
    /// fresh wand off the original stack instead of depleting the one already in the slot.
    /// </summary>
    /// <returns>The stack the peeled unit landed in; 0 when its last charge destroyed it.</returns>
    private uint PeelWandCharge(int entityId, InventoryItemStackComponent stack, ItemDefinition item, WandActivator wandActivator, HotkeySlot? activatedFromSlot)
    {
        var newCharges = (ushort)(wandActivator.Charges - 1);

        if (newCharges == 0)
        {
            InventoryActions.RemoveOneUnit(_componentManager, entityId, stack.StackInstanceId);
            return 0;
        }

        var newOverride = item with { Activator = wandActivator with { Charges = newCharges } };
        var newStackInstanceId = InventoryActions.MoveOneUnit(_componentManager, entityId, stack.StackInstanceId, newOverride);
        var oldStackStillHeld = InventoryQueries.TryFindByStackInstanceId(_componentManager.GetMultiPool<InventoryItemStackComponent>(), entityId, stack.StackInstanceId, out _);
        ItemHotkeyBindingActions.RepointAfterUnitMoved(_itemHotkeyBindings, entityId, stack.StackInstanceId, newStackInstanceId, activatedFromSlot, oldStackStillHeld);
        return newStackInstanceId;
    }
}
