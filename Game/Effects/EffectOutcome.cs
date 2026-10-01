namespace Game.Effects;

/// <summary>What applying an effect to a target came to.</summary>
/// <remarks>
/// An action or item ignores it. Something that applies the same effects to the same target again
/// and again -- an aura's tick, a terrain contact's repeat -- reads it to tell a stay where nothing
/// lands (an immune entity beside lava) from one where something does, so it reports the refusal
/// once rather than on every application.
/// </remarks>
public enum EffectOutcome : byte
{
    /// <summary>Nothing changed and nothing stood in the way: the target has no health to damage, is already at full health, already holds every stack.</summary>
    NoEffect,

    /// <summary>The target refused it: an immunity.</summary>
    Refused,

    /// <summary>Something landed.</summary>
    Applied,
}

public static class EffectOutcomes
{
    /// <summary>The outcome of two applications together: Applied if either landed, otherwise Refused if either was refused.</summary>
    public static EffectOutcome Combine(EffectOutcome first, EffectOutcome second) => first > second ? first : second;
}
