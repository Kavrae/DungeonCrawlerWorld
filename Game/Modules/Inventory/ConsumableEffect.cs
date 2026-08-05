using Engine.Math;

namespace Game.Modules.Inventory;

/// <summary>
/// Shared, catalog-level consumable data -- present on ItemDefinition.Consumable only for items
/// that are actually usable. HealFraction is a fraction of the target's effective MaximumHealth
/// (0.5f = 50%), applied by ConsumableActivationSystem via HealthHeal.Apply. Targeting reuses the
/// same TargetingSpec/TargetShapeResolver abilities use -- a potion's is Burst/3/1 (splash, since
/// it breaks on impact), but that's this item's own choice, not something Kind == Potion implies:
/// a future user-only item (e.g. a bandage) would just carry TargetShape.Self/0/0 instead.
/// ActionLockFrames sets the shared ActionLock on activation, the same way an Immediate
/// AbilityTiming.ActionLockFrames does -- using a consumable is its own action, not a free extra
/// on top of whatever else the entity is doing.
/// </summary>
public sealed record ConsumableEffect(ConsumableKind Kind, float HealFraction, TargetingSpec Targeting, short ActionLockFrames);
