using Game.Modules.StatusEffects;

namespace Game.World;

/// <summary>
/// Published by AbilityEffectResolver.GrantStatusEffects each time an ability successfully
/// grants one status effect to one target via a registered IStatusEffectAuraApplier -- not
/// published for aura-granted stacks (Burning/Poison via StatusEffectAuraSystem), only the
/// ability-activation path. Exists for achievement/logging consumers (see InertGasAchievement)
/// that need to know an effect was granted without each effect module plumbing its own EventBus
/// wiring.
/// </summary>
public readonly record struct StatusEffectApplied(int EntityId, StatusEffectType EffectType, StatusEffectSource Source);
