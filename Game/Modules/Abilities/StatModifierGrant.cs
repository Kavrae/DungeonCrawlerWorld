using Game.Modules.StatModifiers;

namespace Game.Modules.Abilities;

/// <summary>
/// One modifier an ability grants to whichever entity it resolves against -- parallel to
/// AbilityEffect.StatusEffects, but for the general stat-modifier system rather than the
/// StatusEffectType enum. AbilityEffectResolver applies one of these to every resolved target
/// tile's occupant(s), regardless of whether that specific entity also took damage from the
/// same ability (see AbilityEffectResolver's own doc comment) -- DurationFrames uses
/// StatModifierComponent.Permanent (-1) the same way a permanent modifier does elsewhere.
/// </summary>
public sealed record StatModifierGrant(
    StatModifierTarget Target,
    StatModifierOperation Operation,
    StatModifierPolarity Polarity,
    bool CanModify,
    float Magnitude,
    int DurationFrames);
