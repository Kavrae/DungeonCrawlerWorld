namespace Game.Modules.Health;

/// <summary>Whether a heal came from an action or item, or from passive regeneration.</summary>
public enum HealCategory : byte
{
    Direct,
    Regeneration,
}
