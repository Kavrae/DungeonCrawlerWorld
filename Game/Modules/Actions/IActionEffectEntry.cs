namespace Game.Modules.Actions;

/// <summary>
/// One piece of what an action does, owning its own application logic -- adding a new effect
/// kind means adding one new record implementing this, never touching ActionEffect or any other
/// entry. Mirrors Game.Modules.StatusEffects.IStatusEffectAuraApplier's "each implementer owns
/// its own behavior" idiom, without needing a registry: entries are already concrete typed
/// instances sitting in an ActionEffect's own list, not resolved from a bare enum value.
/// </summary>
public interface IActionEffectEntry
{
    void Apply(ActionEffectContext context);
}
