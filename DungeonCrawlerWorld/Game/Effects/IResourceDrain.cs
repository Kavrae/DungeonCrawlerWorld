namespace Game.Effects;

/// <summary>A resource an effect entry takes from its target.</summary>
public enum DrainedResource : byte
{
    Mana,
    Health,
}

/// <summary>An effect entry that takes an amount of one resource from its target: as an activation effect, what using something costs its user.</summary>
/// <remarks>What reads a definition's costs (ActivationCosts, ManaUse) asks for this rather than listing entry types.</remarks>
public interface IResourceDrain : IEffectEntry
{
    DrainedResource Resource { get; }

    /// <summary>What this takes from context.TargetEntityId, through its modifiers on both ends, never rounded.</summary>
    float AmountFor(in EffectContext context);
}
