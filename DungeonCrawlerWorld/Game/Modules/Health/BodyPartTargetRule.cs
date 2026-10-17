using Game.Modules.Health.Components;

namespace Game.Modules.Health;

/// <summary>Fallback selection strategy for BodyPartTargetRule when the preferred BodyPartType isn't present.</summary>
public enum BodyPartFallback
{
    Random,
    Topmost,
    Bottommost,
}

/// <summary>Expresses a damage source's preferred body part to hit, plus what to do when that type isn't present (or no type is preferred at all) -- see BodyPartSelection.PickByTypeWithFallback, its sole consumer.</summary>
/// <remarks>
/// PreferredType is nullable -- null means "no type preference, go straight to Fallback" (e.g.
/// lava's generic bottom-up targeting, which doesn't care about BodyPartType at all, only
/// VerticalPosition). Deliberately public and general rather than Health-module-private -- any
/// future targeted-effect caller (e.g. a Burning application) reuses this same type and
/// PickByTypeWithFallback rather than growing its own bespoke targeting rule.
/// </remarks>
public readonly record struct BodyPartTargetRule(BodyPartType? PreferredType, BodyPartFallback Fallback);
