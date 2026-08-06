using Game.Modules.StatusEffects;

namespace Game.Modules.Abilities;

/// <summary>DamageAmount here is the catalog fallback/reference value -- see AbilityInstanceComponent for why a granted instance's actual damage can differ per entity. HealFraction mirrors Game.Modules.Inventory.ConsumableEffect's own field of the same name -- a fraction of the target's own effective MaximumHealth, always read from the catalog (never overridden per-instance the way DamageAmount is -- a heal ability "does not level up"). StatModifierGrants is applied to every resolved target -- see StatModifierGrant's own doc comment.</summary>
public sealed record AbilityEffect(short DamageAmount, IReadOnlyList<StatusEffectType> StatusEffects, IReadOnlyList<StatModifierGrant> StatModifierGrants = null!, float HealFraction = 0f)
{
    public IReadOnlyList<StatModifierGrant> StatModifierGrants { get; init; } = StatModifierGrants ?? [];
}
