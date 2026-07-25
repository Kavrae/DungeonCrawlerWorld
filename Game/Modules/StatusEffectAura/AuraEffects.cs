namespace Game.Modules.StatusEffectAura;

/// <summary>Status-effect aura's own rules: how often it grants a new batch of stacks.</summary>
public static class AuraEffects
{
    /// <summary>
    /// How often an entity that remains in range gains another batch of stacks. A separate
    /// constant from BurningEffects.TickIntervalFrames -- even though both are currently 60 --
    /// since one is "how often the aura grants a new batch of stacks" and the other is "how
    /// often existing Burning stacks decay/damage"; independent knobs that only coincidentally
    /// match.
    /// </summary>
    public const int TickIntervalFrames = 60;
}
