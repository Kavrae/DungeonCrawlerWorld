namespace Game.World;

/// <summary>What a floating text reports about the entity it appears above.</summary>
public enum FloatingTextKind : byte
{
    DamageTaken,
    StatusEffectDamageTaken,
    Healed,
    Regenerated,
    StatusEffectStacksAdded,
    Dodged,
    Immune,
}
