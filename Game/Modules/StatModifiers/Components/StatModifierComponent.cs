using Engine.ECS.Systems;
using Game.World;

namespace Game.Modules.StatModifiers.Components;

/// <summary>
/// One active modifier from one source -- an entity holding N of these (MultiComponentPool,
/// same shape as StatusEffectStack/ActionInstanceComponent) has N modifiers, which may target
/// the same or different stats and stack freely. Never mutates the stat it targets: every
/// reader recomputes the effective value from the stat's own untouched base plus whichever
/// modifiers are currently active (see StatModifierMath.GetEffectiveValue) -- so nothing about
/// this component changes after Add; it is removed outright by StatModifierExpirySystem once
/// ExpiresAtFrame arrives.
///
/// CanModify is stored for a future effect that would target other modifiers directly (e.g.
/// shortening active debuffs' remaining duration) -- distinguishing which modifiers are
/// themselves eligible to be modified prevents that kind of effect from being able to target
/// itself or chain into infinite recursion. No such effect exists yet; this pass only carries
/// the field.
///
/// ConditionTag gates this modifier on the current activation's own Tags (ActionEffectContext.
/// ActivatorTags/ItemDefinition.Tags) rather than always being active -- null means unconditional
/// (every existing modifier). See StatModifierMath.GetEffectiveValue's activeTags parameter,
/// the single place this is consumed.
/// </summary>
/// <remarks>
/// Not itself a timer-wheel timer: an entity's modifiers share one per-entity timer
/// (ExpiringStatModifierComponent, the earliest deadline among them), so N modifiers cost one
/// wheel entry rather than N. See StatModifierExpirySystem.
/// </remarks>
/// <param name="expiresAtFrame">The simulation frame this modifier is removed on -- FrameDeadline.After(now, duration), or FrameDeadline.Never for a permanent modifier.</param>
public struct StatModifierComponent(
    StatModifierTarget target,
    StatModifierOperation operation,
    StatModifierPolarity polarity,
    bool canModify,
    float magnitude,
    uint expiresAtFrame,
    StatusEffectSource source,
    Tag? conditionTag = null)
{
    public StatModifierTarget Target { get; } = target;
    public StatModifierOperation Operation { get; } = operation;
    public StatModifierPolarity Polarity { get; } = polarity;
    public bool CanModify { get; } = canModify;
    public float Magnitude { get; } = magnitude;

    /// <summary>FrameDeadline.Never means "never expires" -- StatModifierExpirySystem never schedules a modifier at this value, so an entity holding only permanent modifiers costs nothing.</summary>
    public uint ExpiresAtFrame { get; } = expiresAtFrame;
    public StatusEffectSource Source { get; } = source;
    public Tag? ConditionTag { get; } = conditionTag;

    public override readonly string ToString() => $"Target : {Target}\nSource : {Source}\nOperation : {(Operation == StatModifierOperation.Additive ? Polarity == StatModifierPolarity.Buff ? '+' : '-' : Polarity == StatModifierPolarity.Buff ? 'x' : '÷')}{Magnitude}\nCanModify : {CanModify}\nExpiresAtFrame : {(ExpiresAtFrame == FrameDeadline.Never ? "Permanent" : ExpiresAtFrame.ToString())}\nConditionTag : {(ConditionTag is { } tag ? tag.ToString() : "None")}";
}
