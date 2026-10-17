namespace Game.Modules.StatusEffects;

/// <summary>Identifies which effect a StatusEffectStack entry belongs to. New effects add a value here -- no new component type needed per effect.</summary>
public enum StatusEffectType : byte
{
    Burning,
    Poison,
    Paralysis,
}
