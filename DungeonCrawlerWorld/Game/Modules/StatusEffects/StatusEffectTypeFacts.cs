using Game.Modules.StatModifiers;

namespace Game.Modules.StatusEffects;

/// <summary>What is true of a status effect type wherever it is read: whether it helps or hurts its holder, and the name a player sees.</summary>
/// <remarks>
/// Read by content (a grant's duration modifiers), by the grant's duration scaling and by every display, none of
/// which may disagree, and by the IDE's inspector, which has no registry -- so it is one table here rather than a
/// member of each effect's applier or display.
/// </remarks>
public static class StatusEffectTypeFacts
{
    /// <summary>Buff for an effect that helps its holder, Debuff for one that hurts it.</summary>
    public static StatModifierPolarity PolarityOf(StatusEffectType effectType) => effectType switch
    {
        _ => StatModifierPolarity.Debuff,
    };

    /// <summary>The effect's name as a player reads it.</summary>
    public static string DisplayNameOf(StatusEffectType effectType) => effectType switch
    {
        _ => effectType.ToString(),
    };
}
