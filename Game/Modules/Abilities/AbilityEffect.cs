using Game.Modules.StatusEffects;

namespace Game.Modules.Abilities;

/// <summary>DamageAmount here is the catalog fallback/reference value -- see AbilityInstanceComponent for why a granted instance's actual damage can differ per entity.</summary>
public sealed record AbilityEffect(short DamageAmount, IReadOnlyList<StatusEffectType> StatusEffects);
