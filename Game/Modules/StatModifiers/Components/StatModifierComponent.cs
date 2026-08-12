using Game.World;

namespace Game.Modules.StatModifiers.Components;

/// <summary>
/// One active modifier from one source -- an entity holding N of these (MultiComponentPool,
/// same shape as StatusEffectStack/ActionInstanceComponent) has N modifiers, which may target
/// the same or different stats and stack freely. Never mutates the stat it targets: every
/// reader recomputes the effective value from the stat's own untouched base plus whichever
/// modifiers are currently active (see StatModifierMath.GetEffectiveValue) -- so RemainingDurationFrames
/// is the only field that changes after Add, ticked down by StatModifierExpirySystem.
///
/// CanModify is stored for a future effect that would target other modifiers directly (e.g.
/// shortening active debuffs' remaining duration) -- distinguishing which modifiers are
/// themselves eligible to be modified prevents that kind of effect from being able to target
/// itself or chain into infinite recursion. No such effect exists yet; this pass only carries
/// the field.
/// </summary>
public struct StatModifierComponent(
    StatModifierTarget target,
    StatModifierOperation operation,
    StatModifierPolarity polarity,
    bool canModify,
    float magnitude,
    int remainingDurationFrames,
    StatusEffectSource source)
{
    /// <summary>Sentinel meaning "never expires" -- StatModifierExpirySystem skips ticking/removing a modifier at this value.</summary>
    public const int Permanent = -1;

    public StatModifierTarget Target { get; } = target;
    public StatModifierOperation Operation { get; } = operation;
    public StatModifierPolarity Polarity { get; } = polarity;
    public bool CanModify { get; } = canModify;
    public float Magnitude { get; } = magnitude;
    public int RemainingDurationFrames { get; set; } = remainingDurationFrames;
    public StatusEffectSource Source { get; } = source;

    public override readonly string ToString() => $"Target : {Target}\nOperation : {Operation}\nPolarity : {Polarity}\nCanModify : {CanModify}\nMagnitude : {Magnitude}\nRemainingDurationFrames : {RemainingDurationFrames}\nSource : {Source}";
}
