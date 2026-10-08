using Engine.ECS.Components;
using Game.Effects;
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

/// <summary>
/// Consumes a PendingItemActivationComponent (queued by Presentation, never applied by it
/// -- mirrors ActionActivationSystem/PendingActionActivationComponent exactly). Every
/// item activation sets the shared ActionLock on the *activating* entity, the same as an
/// Immediate action (see PotionActivator.Timing's own doc comment) -- only a toggle item is
/// timed any other way (see below). Every request now names the exact stack being
/// activated (StackInstanceId, not ItemDefinitionId -- see PendingItemActivationComponent's
/// own doc comment); its effective ItemDefinition (its own Override if diverged, else the plain
/// catalog lookup -- see InventoryQueries.TryResolveEffectiveItem) is what actually gets applied,
/// so a diverged stack's own current state (a wand's remaining charges) is never bypassed by
/// accidentally reading the catalog original instead.
///
/// Dispatches on item.Activator's concrete type. PotionActivator/ScrollActivator: the stack is
/// ticked down (InventoryActions.RemoveOneUnit) before the effect applies, per
/// spec order (see TryBeginActivation, shared by both). PotionActivator: PotionCooldownComponent
/// -- and the punishment Poison stack/PotionCooldownAbusedEvent for activating it again too soon
/// -- belongs to whoever actually receives the potion's effect (see ApplyPotionToTarget), not
/// whoever drank/threw it. Drinking your own potion means those are the same entity; throwing one
/// at a goblin means the goblin's own cooldown ticks, the thrower's does not. This stays this
/// system's own kind-uniform logic rather than a composable IEffectEntry: it doesn't vary per potion
/// (Constitution, the only varying input, is caster-side), so every potion already gets it
/// automatically, and making it an entry every item's Effects list must remember to include
/// (including mod-defined potions) would turn a currently-impossible-to-forget mechanic into a
/// silently-omittable one.
///
/// ScrollActivator: no cooldown-abuse mechanic (potion-specific, never mentioned for scrolls) and
/// no hard SimpleHealthComponent requirement on the target (see ApplyScrollToTarget) -- instead scales
/// Range/AreaSize (already in the request's TargetSelection, see TargetResolution.EffectiveSpec and
/// ScrollScalingEffects' own doc comment) and any duration the effect carries by the *caster's*
/// Intelligence, then records the activation toward mastering the scroll's spell (see
/// ScrollMasteryEffects).
///
/// WandActivator: not consumed from a shared stack at all -- each wand has its own remaining
/// Charges, decremented via InventoryActions.MoveOneUnit (see PeelWandCharge) rather
/// than InventoryActions.RemoveOneUnit, and (unlike Potion/Scroll) the peeled stack
/// gets a *new* StackInstanceId once it's actually diverged -- so this slot's own
/// ItemHotkeyBindingComponent is repointed to it afterward (see ItemHotkeyBindingActions.RepointAfterUnitMoved), the one
/// piece of bookkeeping neither Potion nor Scroll ever needs. No mana cost, no cooldown-abuse
/// mechanic, no Intelligence duration-scaling (that's scroll-specific) -- charges were already
/// fixed once, at grant time (see Game.Modules.Inventory.WandGrantEffects).
///
/// ToggleItemActivator: consumes nothing and applies no effects here. The activation flips one
/// unit between lit and unlit (ToggleItemActions.TryToggle); what the item holds while lit follows
/// from the stack change (ToggleItemHolderSync). Timed by the activator's own Timing, the one kind
/// that isn't always Immediate: FreeCast neither waits for the action lock nor sets it, anything
/// else waits for it and sets it. A Delayed one that is off winds up instead, sharing the action
/// windup (PendingWindupComponent), and is lit when that resolves; putting one out is at once.
///
/// Every kind takes the item's ActivationEffects (ActivationEffectsApplier) once its own checks pass
/// and before anything else it does; a toggle takes them only when it is lit, not when put out.
/// </summary>
/// <remarks>
/// Striped, not tiered, and deliberately so: this drains a queue of activations already committed
/// to this frame (PendingItemActivationComponent, queued by the player's own input or by
/// TestCombatBehaviorSystem's self-heal branch), so deferring one to a coarse tier's cadence would
/// leave a drink the player already pressed sitting unresolved for up to a divisor's worth of
/// frames. The population is bounded by "activations queued right now", not by entity count, so
/// there is nothing for tiering to save. Same reasoning as ActionActivationSystem, which drains
/// the equivalent queue for actions.
/// </remarks>
public sealed class ItemActivationSystem : ISystem
{
    private const byte StripeCountValue = 1;

    public byte StripeCount => StripeCountValue;

    private readonly PackedComponentPool<PendingItemActivationComponent> _pendingActivations;
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks;
    private readonly PackedComponentPool<PotionCooldownComponent> _potionCooldowns;
    private readonly PackedComponentPool<SimpleHealthComponent> _health;
    private readonly EffectServices _effectServices;
    private readonly ItemCatalog _itemCatalog;
    private readonly ActionCatalog _actionCatalog;
    private readonly IMapQuery _mapQuery;
    private readonly EventBus _eventBus;
    private readonly ComponentManager _componentManager;
    private readonly EntityKeys _entityKeys;
    private readonly PackedComponentPool<DeadComponent> _deadEntities;
    private readonly PackedComponentPool<ManaComponent> _mana;
    private readonly MultiComponentPool<StatModifierComponent> _statModifiers;
    private readonly PackedComponentPool<MeleeDisabledComponent> _meleeDisabled;
    private readonly PackedComponentPool<AbilityScoresComponent> _abilityScores;
    private readonly IPlayerQuery _playerQuery;
    private readonly MultiComponentPool<ItemHotkeyBindingComponent> _itemHotkeyBindings;
    private readonly EntityBodyParts _bodyParts;
    private readonly BlueprintRegistry _creatures;
    private readonly List<int> _targetIdsScratch = [];
    private readonly HashSet<int> _seenTargetIdsScratch = [];
    private readonly ProcessingTierQuery _processingTiers;
    private readonly Toggles _toggles;
    private readonly PackedComponentPool<PendingWindupComponent> _pendingWindups;
    private readonly TargetResolution _targetResolution;
    private readonly List<Vector3Int> _targetTilesScratch = [];

    /// <summary>Where the last CollectTargets call landed, for the entries placed once per activation (ApplyOnce).</summary>
    private ResolvedTargets _lastResolved;
    private readonly EntityStripeSet _stripeSet;

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
        MultiComponentPool<ItemHotkeyBindingComponent> itemHotkeyBindings,
        ProcessingTierQuery processingTiers,
        Toggles toggles,
        PackedComponentPool<PendingWindupComponent> pendingWindups,
        TargetResolution targetResolution)
    {
        _effectServices = effectServices;
        _pendingActivations = pendingActivations;
        _actionLocks = actionLocks;
        _potionCooldowns = potionCooldowns;
        _health = effectServices.Health;
        _itemCatalog = itemCatalog;
        _actionCatalog = actionCatalog;
        _mapQuery = mapQuery;
        _eventBus = effectServices.EventBus;
        _componentManager = effectServices.ComponentManager;
        _entityKeys = effectServices.EntityKeys;
        _deadEntities = effectServices.DeadEntities;
        _mana = effectServices.Mana;
        _statModifiers = effectServices.StatModifiers;
        _meleeDisabled = meleeDisabled;
        _abilityScores = effectServices.AbilityScores;
        _playerQuery = effectServices.PlayerQuery;
        _itemHotkeyBindings = itemHotkeyBindings;
        _bodyParts = effectServices.BodyParts;
        _creatures = effectServices.Definitions;
        _processingTiers = processingTiers;
        _toggles = toggles;
        _pendingWindups = pendingWindups;
        _targetResolution = targetResolution;

        _stripeSet = EntityStripeSet.CreateAndWire(StripeCount, pendingActivations);
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
                ActivationQueries.GetBlocker(entityId, item, item.Activator, item.Activator is ToggleItemActivator { IsToggledOn: true }, _meleeDisabled, _effectServices, _now) != ActivationBlocker.None)
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

                    ActivationEffectsApplier.Apply(_effectServices, entityId, item, _now);
                    ActivatePotion(item, entityId, request.Selection);
                    ActionLockGate.Lock(_actionLocks, entityId, _now, potionActivator.Timing.ActionLockFrames);
                    break;

                case ScrollActivator scrollActivator:
                    if (!TryBeginActivation(entityId, request.StackInstanceId))
                    {
                        continue;
                    }

                    ActivationEffectsApplier.Apply(_effectServices, entityId, item, _now);
                    ActivateScroll(item, scrollActivator, entityId, request.Selection);
                    ActionLockGate.Lock(_actionLocks, entityId, _now, scrollActivator.Timing.ActionLockFrames);
                    break;

                case WandActivator wandActivator:
                    if (!TryBeginWandActivation(entityId, wandActivator.Charges))
                    {
                        continue;
                    }

                    ActivationEffectsApplier.Apply(_effectServices, entityId, item, _now);
                    PeelWandCharge(entityId, stack, item, wandActivator, request.ActivatedFromSlot);
                    ActivateWand(item, entityId, request.Selection);
                    ActionLockGate.Lock(_actionLocks, entityId, _now, wandActivator.Timing.ActionLockFrames);
                    break;

                case ToggleItemActivator toggleActivator:
                    var ignoresActionLock = toggleActivator.Timing.Category == ActionTimingCategory.FreeCast;
                    if (!ignoresActionLock && ActionLockGate.IsBlocked(_actionLocks, entityId, _now))
                    {
                        continue;
                    }

                    // What turning on takes is applied before the unit is lit -- for Delayed, when the windup
                    // starts, not given back if it is cancelled. The blocker check above has asked it.
                    if (!toggleActivator.IsToggledOn)
                    {
                        ActivationEffectsApplier.Apply(_effectServices, entityId, item, _now);

                        // Lit only when the windup resolves (ToggleItemWindupResolver); putting one out is at once.
                        if (toggleActivator.Timing.Category == ActionTimingCategory.Delayed)
                        {
                            Windups.Begin(_actionLocks, _pendingWindups, entityId, _now, toggleActivator.Timing.ActionLockFrames,
                                PendingWindupComponent.ForItem(request.StackInstanceId, request.ActivatedFromSlot, request.Selection, readyAtFrame: 0));
                            break;
                        }
                    }

                    if (!ToggleItemActions.TryToggle(_componentManager, _itemCatalog, entityId, request.StackInstanceId, out _, request.ActivatedFromSlot))
                    {
                        continue;
                    }

                    if (!ignoresActionLock)
                    {
                        ActionLockGate.Lock(_actionLocks, entityId, _now, toggleActivator.Timing.ActionLockFrames);
                    }

                    break;
            }
        }
    }

    /// <summary>Shared pre-checks + stack consumption for Potion/Scroll -- still holds the stack, action lock isn't currently blocking, then consumes one unit (per spec order, before the effect applies). Returns false (nothing consumed) if either check fails.</summary>
    private bool TryBeginActivation(int entityId, uint stackInstanceId)
    {
        if (!InventoryQueries.TryFindByStackInstanceId(_componentManager.GetMultiPool<InventoryItemStackComponent>(), entityId, stackInstanceId, out _))
        {
            return false;
        }

        if (ActionLockGate.IsBlocked(_actionLocks, entityId, _now))
        {
            return false;
        }

        InventoryActions.RemoveOneUnit(_componentManager, entityId, stackInstanceId);
        return true;
    }

    /// <summary>Wand counterpart to TryBeginActivation -- no stack to consume yet (see PeelWandCharge, called separately once this passes), just the two gates: charges remaining, and the shared ActionLock isn't currently blocking.</summary>
    private bool TryBeginWandActivation(int entityId, ushort charges) =>
        charges > 0 && !ActionLockGate.IsBlocked(_actionLocks, entityId, _now);

    /// <summary>Whether a consumable landing on targetEntityId's tile reaches it: not while it is frozen, the same seam ActionEffectResolver keeps -- nothing targets across the simulated/frozen boundary.</summary>
    private bool IsTargetable(int targetEntityId) => _processingTiers.IsSimulated(targetEntityId);

    /// <summary>Every targetable entity selection reaches for item, once each, in the order first reached: the marked entity alone for a MarkedOnly item in Target mode, otherwise everyone on the resolved tiles.</summary>
    /// <remarks>A multi-tile entity occupies every cell of its footprint, so a shape covering several of them reaches it once per cell; a consumable affects it once.</remarks>
    private List<int> CollectTargets(ItemDefinition item, int sourceEntityId, TargetSelection selection)
    {
        _targetIdsScratch.Clear();
        _seenTargetIdsScratch.Clear();

        var resolved = _targetResolution.Resolve(sourceEntityId, item.Activator!.Targeting, selection, _targetTilesScratch);
        _lastResolved = resolved;
        if (resolved.MarkedOnly)
        {
            if (resolved.MarkedEntityId is { } markedEntityId && IsTargetable(markedEntityId))
            {
                _targetIdsScratch.Add(markedEntityId);
            }

            return _targetIdsScratch;
        }

        foreach (var tile in _targetTilesScratch)
        {
            foreach (var targetEntityId in _mapQuery.GetOccupantEntityIdSpanAt(tile))
            {
                if (IsTargetable(targetEntityId) && _seenTargetIdsScratch.Add(targetEntityId))
                {
                    _targetIdsScratch.Add(targetEntityId);
                }
            }
        }

        return _targetIdsScratch;
    }

    private void ActivatePotion(ItemDefinition item, int sourceEntityId, TargetSelection selection)
    {
        foreach (var targetEntityId in CollectTargets(item, sourceEntityId, selection))
        {
            ApplyPotionToTarget(item, sourceEntityId, targetEntityId);
        }

        ApplyOnce(item, sourceEntityId);
    }

    /// <summary>Applies item's entries placed once per activation (EffectSequence.ApplyOnce), where the last CollectTargets call landed.</summary>
    /// <remarks>A marked entity that isn't targetable receives none of them; only the AtLocation ones are placed.</remarks>
    private void ApplyOnce(ItemDefinition item, int sourceEntityId, float durationScaleMultiplier = 1.0f)
    {
        var context = BuildContext(item, sourceEntityId, EffectContext.NoTargetEntity, durationScaleMultiplier);
        if (_lastResolved.MarkedEntityId is { } markedEntityId && !IsTargetable(markedEntityId))
        {
            EffectSequence.ApplyAtLocation(item.Effects, context, _lastResolved.Centre);
            return;
        }

        EffectSequence.ApplyOnce(item.Effects, context, _lastResolved.MarkedEntityId, _lastResolved.Centre);
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
        if (_deadEntities.Has(targetEntityId) || (!_health.Has(targetEntityId) && !_bodyParts.Has(targetEntityId)))
        {
            return;
        }

        var durationFrames = AbilityScoreQueries.TryGetComponent(_abilityScores, targetEntityId, AbilityScoreType.Constitution, out var constitution)
            ? PotionCooldownEffects.ComputeDurationFrames(constitution.Total)
            : PotionCooldownEffects.DurationFrames;

        if (_potionCooldowns.TryGetReadonly(targetEntityId, out var cooldown) && PotionCooldownEffects.FramesRemaining(cooldown, _now) > 0)
        {
            PoisonEffects.ApplyStack(_componentManager, _entityKeys, targetEntityId, ActionSource.FromEntity(_componentManager, _entityKeys, targetEntityId, _creatures), PotionCooldownEffects.ComputeAbusePoisonDurationTicks(durationFrames), _now, _eventBus, _playerQuery);
            _eventBus.Publish(new PotionCooldownAbusedEvent(targetEntityId));
        }

        EffectSequence.ApplyOnEachTarget(item.Effects, BuildContext(item, sourceEntityId, targetEntityId));

        PotionCooldownEffects.Reset(_componentManager, targetEntityId, durationFrames, _now);
    }

    private void ActivateScroll(ItemDefinition item, ScrollActivator scrollActivator, int sourceEntityId, TargetSelection selection)
    {
        var durationScaleMultiplier = ComputeScrollScaleMultiplier(sourceEntityId);

        foreach (var targetEntityId in CollectTargets(item, sourceEntityId, selection))
        {
            ApplyScrollToTarget(item, sourceEntityId, targetEntityId, durationScaleMultiplier);
        }

        ApplyOnce(item, sourceEntityId, durationScaleMultiplier);

        ScrollMasteryEffects.RecordUsage(_componentManager, _eventBus, _actionCatalog, item, sourceEntityId, scrollActivator.SpellId);
    }

    private float ComputeScrollScaleMultiplier(int sourceEntityId) =>
        AbilityScoreQueries.TryGetComponent(_abilityScores, sourceEntityId, AbilityScoreType.Intelligence, out var intelligence)
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
        if (_deadEntities.Has(targetEntityId))
        {
            return;
        }

        EffectSequence.ApplyOnEachTarget(item.Effects, BuildContext(item, sourceEntityId, targetEntityId, durationScaleMultiplier));
    }

    private void ActivateWand(ItemDefinition item, int sourceEntityId, TargetSelection selection)
    {
        foreach (var targetEntityId in CollectTargets(item, sourceEntityId, selection))
        {
            ApplyWandToTarget(item, sourceEntityId, targetEntityId);
        }

        ApplyOnce(item, sourceEntityId);
    }

    /// <summary>Same "immortal but affectable" treatment as ApplyScrollToTarget -- no hard SimpleHealthComponent requirement, each effect entry no-ops gracefully on its own missing pool. Skipped only for a dead target.</summary>
    private void ApplyWandToTarget(ItemDefinition item, int sourceEntityId, int targetEntityId)
    {
        if (_deadEntities.Has(targetEntityId))
        {
            return;
        }

        EffectSequence.ApplyOnEachTarget(item.Effects, BuildContext(item, sourceEntityId, targetEntityId));
    }

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
    private void PeelWandCharge(int entityId, InventoryItemStackComponent stack, ItemDefinition item, WandActivator wandActivator, HotkeySlot? activatedFromSlot)
    {
        var newCharges = (ushort)(wandActivator.Charges - 1);

        if (newCharges == 0)
        {
            InventoryActions.RemoveOneUnit(_componentManager, entityId, stack.StackInstanceId);
            return;
        }

        var newOverride = item with { Activator = wandActivator with { Charges = newCharges } };
        var newStackInstanceId = InventoryActions.MoveOneUnit(_componentManager, entityId, stack.StackInstanceId, newOverride);
        var oldStackStillHeld = InventoryQueries.TryFindByStackInstanceId(_componentManager.GetMultiPool<InventoryItemStackComponent>(), entityId, stack.StackInstanceId, out _);
        ItemHotkeyBindingActions.RepointAfterUnitMoved(_itemHotkeyBindings, entityId, stack.StackInstanceId, newStackInstanceId, activatedFromSlot, oldStackStillHeld);
    }

    /// <summary>The EffectContext for one target of an item's activation; only a scroll sets DurationScaleMultiplier away from 1.</summary>
    private EffectContext BuildContext(ItemDefinition item, int sourceEntityId, int targetEntityId, float durationScaleMultiplier = 1.0f) =>
        EffectContext.FromEntity(_effectServices, sourceEntityId, targetEntityId, item.Name, item.Tags, _now, durationScaleMultiplier);
}
