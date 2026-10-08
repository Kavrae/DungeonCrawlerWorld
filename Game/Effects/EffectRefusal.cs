namespace Game.Effects;

/// <summary>Why an entry can't be applied to its target now (IEffectEntry.CanApply), or None when it can.</summary>
/// <remarks>Each reason is shown with its own text, so a new way for an entry to refuse gets a new member rather than reusing one.</remarks>
public enum EffectRefusal : byte
{
    None,

    /// <summary>The target has no mana pool to take from.</summary>
    NoManaPool,

    /// <summary>The target has less mana than is asked of it.</summary>
    NotEnoughMana,

    /// <summary>The target has no health to take from.</summary>
    NoHealth,

    /// <summary>Taking what is asked would kill the target.</summary>
    NotEnoughHealth,
}
