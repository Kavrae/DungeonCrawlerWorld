namespace Game.Resources;

/// <summary>Whether a resource (health, mana) was lost straight from a hit or from a status effect ticking.</summary>
public enum ResourceLossCategory : byte
{
    Direct,
    StatusEffect,
}
