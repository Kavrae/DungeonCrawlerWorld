namespace Game.Resources;

/// <summary>Whether a resource (health, mana) was gained from an action or item, or from passive regeneration.</summary>
public enum ResourceGainCategory : byte
{
    Direct,
    Regeneration,
}
