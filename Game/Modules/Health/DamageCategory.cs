namespace Game.Modules.Health;

/// <summary>Whether damage came straight from a hit or from a status effect ticking.</summary>
public enum DamageCategory : byte
{
    Direct,
    StatusEffect,
}
