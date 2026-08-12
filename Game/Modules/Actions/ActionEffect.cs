namespace Game.Modules.Actions;

/// <summary>
/// A composable list of IActionEffectEntry -- replaces the old per-effect scalar fields
/// (HealFraction/ManaFraction/HotkeySlotGrant/...) that used to live directly on each activator.
/// Owns its own application loop directly; there is deliberately no separate resolver class --
/// Apply contains no per-kind knowledge at all, every entry applies itself. Entries apply in
/// strict list order, and later entries observe the live component state earlier ones left
/// behind -- see PLAN-action-effect-activator.md's "composition order is meaningful" section.
/// </summary>
public sealed record ActionEffect(IReadOnlyList<IActionEffectEntry> Entries)
{
    public static readonly ActionEffect None = new([]);

    public void Apply(ActionEffectContext context)
    {
        foreach (var entry in Entries)
        {
            entry.Apply(context);
        }
    }
}
