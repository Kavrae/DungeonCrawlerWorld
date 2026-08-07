using Engine.Math;

namespace Game.Modules.Inventory;

/// <summary>
/// Shared, catalog-level consumable data -- present on ItemDefinition.Consumable only for items
/// that are actually usable. HealFraction/ManaFraction are each a fraction of the target's own
/// effective MaximumHealth/MaximumMana (0.5f = 50%, 1f = a full restore -- adding a full-max
/// amount to any current value clamps to max regardless of what it started at, so 1f always
/// means "full" without needing a separate absolute-set path), applied by
/// ConsumableActivationSystem via HealthHeal.Apply/ManaRestore.Apply respectively -- either,
/// both, or neither may be nonzero on a given item (the Health Potion only sets HealFraction, the
/// Mana Potion only sets ManaFraction). Targeting reuses the same TargetingSpec/
/// TargetShapeResolver abilities use -- a potion's is Burst/3/1 (splash, since it breaks on
/// impact), but that's this item's own choice, not something Kind == Potion implies: a future
/// user-only item (e.g. a bandage) would just carry TargetShape.Self/0/0 instead. ActionLockFrames
/// sets the shared ActionLock on activation, the same way an Immediate AbilityTiming.
/// ActionLockFrames does -- using a consumable is its own action, not a free extra on top of
/// whatever else the entity is doing.
/// </summary>
public sealed record ConsumableEffect(ConsumableKind Kind, float HealFraction, TargetingSpec Targeting, short ActionLockFrames, float ManaFraction = 0f);
