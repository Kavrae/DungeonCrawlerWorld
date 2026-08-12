using Game.Modules.StatusEffects;

namespace Game.World;

/// <summary>
/// Published by ActionEffectResolver.GrantStatusEffects each time an action successfully
/// grants one status effect to one target via a registered IStatusEffectAuraApplier -- not
/// published for aura-granted stacks (Burning/Poison via StatusEffectAuraSystem), only the
/// ability-activation path. Exists for achievement/logging consumers (see InertGasAchievement)
/// that need to know an effect was granted without each effect module plumbing its own EventBus
/// wiring.
/// </summary>
public readonly record struct StatusEffectAppliedEvent(int EntityId, StatusEffectType EffectType, StatusEffectSource Source);
