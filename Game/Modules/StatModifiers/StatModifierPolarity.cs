namespace Game.Modules.StatModifiers;

/// <summary>
/// Whether a modifier is helping or hurting the entity it's on -- carried by every
/// StatModifierComponent so a future effect can target "all active Debuffs" (e.g. an effect
/// that shortens debuff durations). No consumer reads this yet; see StatModifierComponent's
/// own doc comment.
/// </summary>
public enum StatModifierPolarity : byte
{
    Buff,
    Debuff,
}
