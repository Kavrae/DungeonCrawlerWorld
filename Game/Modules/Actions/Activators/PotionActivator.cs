using Engine.Math;

namespace Game.Modules.Actions.Activators;

/// <summary>
/// Item-triggered activator -- present on an Game.Modules.Inventory.ItemDefinition.Activator only
/// for items that are actually usable (an Equipment/Tool item leaves it null). A distinct type
/// from DirectAction even though the shape is now identical, because
/// Game.Modules.Inventory.Systems.ConsumableActivationSystem pattern-matches on it specifically to
/// apply the potion-cooldown-abuse mechanic (see Game.Modules.Actions.Activators.
/// PotionCooldownEffects) -- a real type-identity dependency, not incidental duplication.
///
/// Timing is always constructed Immediate with CooldownFrames: null today -- potions have no
/// Delayed/FreeCast equivalent, no per-potion cooldown beyond the shared ActionLock and the
/// separate, non-blocking PotionCooldown abuse-punish mechanic ConsumableActivationSystem owns
/// itself (see its own doc comment for why that stays system-level, not a composable entry).
/// </summary>
public sealed record PotionActivator(TargetingSpec Targeting, ActionTiming Timing) : IActionActivator;
