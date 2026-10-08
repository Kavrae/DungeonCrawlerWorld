using Game.Effects;

namespace Game.Modules.Actions;

/// <summary>What makes an action or item a toggle: something its holder switches on and off, whose own Effects are held while it is on.</summary>
/// <remarks>
/// Everything a toggle does is an Effect list, applied with source and target both its holder: the
/// definition's Effects are held while it is on (applied under a key when it goes on, reverted under
/// the same key when it goes off), the definition's ActivationEffects are what turning it on takes
/// (as for any action or item -- turning one off takes nothing), and Periodic is what it does every
/// interval while on. There is no separate cost type, so anything an effect can do can be a cost or
/// an upkeep.
/// </remarks>
/// <param name="Periodic">Applied every interval while the toggle is on, the first one interval after it goes on; null for a toggle that does nothing while on but hold its effects.</param>
public sealed record ToggleSpec(TogglePeriodicEffects? Periodic = null)
{
    /// <summary>A toggle that does nothing while on but hold its definition's Effects.</summary>
    public static readonly ToggleSpec HoldsEffectsOnly = new();
}

/// <summary>What a toggle does every interval while it is on.</summary>
/// <param name="IntervalFrames">Simulation frames between applications.</param>
public sealed record TogglePeriodicEffects(IReadOnlyList<Effect> Effects, ushort IntervalFrames);
