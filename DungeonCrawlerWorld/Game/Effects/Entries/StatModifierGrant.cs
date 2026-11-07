using Engine.ECS.Systems;
using Engine.Tags;
using Game.Modules.StatModifiers;

namespace Game.Effects.Entries;

/// <summary>What a second StatModifierGrant of the same modifier from the same source does.</summary>
public enum StatModifierStacking : byte
{
    /// <summary>Adds another modifier beside the first.</summary>
    Add,

    /// <summary>Keeps the one modifier and replaces its expiry (StatModifierEffects.ApplyOrRefresh).</summary>
    RefreshFromSameSource,
}

/// <summary>
/// Grants one StatModifierComponent to context.TargetEntityId -- always the resolved target, no
/// separate Source/Target choice: a caller that wants to buff itself (e.g. a self-targeted
/// action) does so via a Self-shaped TargetingSpec, which already resolves TargetEntityId to the
/// caster, the same way every other effect entry reads "who this lands on" (see AuraSourceGrant's
/// own doc comment for the identical reasoning).
/// DurationFrames is scaled by context.DurationScaleMultiplier (a ScrollActivator activation sets
/// this off the caster's Intelligence -- see ScrollScalingEffects; every other activator leaves
/// it at the default 1.0, a no-op) -- guarded so a permanent (null) duration is never multiplied
/// into a meaningless number. Then, still guarded the same way, scaled through StatModifierMath
/// against the caster's own Outgoing{Buff,Debuff}Duration (context.SourceEntityId) and then the
/// target's own Incoming{Buff,Debuff}Duration (context.TargetEntityId) -- Polarity picks which of
/// the two pairs applies, both tag-conditional via context.ActivatorTags, exactly like
/// DirectDamage's own OutgoingDamage step. The scaled result becomes an absolute deadline from
/// context.Now. Stacking decides what a second grant of the same modifier from the same source does:
/// Add holds both, RefreshFromSameSource keeps one and moves its expiry -- how a buff an entity keeps
/// being given (terrain it stands on, an aura it stands in) is held once and topped up. Magnitude
/// is the modifier's own and is never scaled by context.Magnitude.
/// </summary>
public sealed record StatModifierGrant(
    StatModifierTarget Target,
    StatModifierOperation Operation,
    StatModifierPolarity Polarity,
    bool CanModify,
    float Magnitude,
    ushort? DurationFrames,
    GameplayTag ConditionTag = default,
    StatModifierStacking Stacking = StatModifierStacking.Add) : IEffectEntry
{
    /// <remarks>The duration of a timed modifier, by polarity. The magnitude is not scaled.</remarks>
    public IReadOnlyList<(StatModifierTarget Outgoing, StatModifierTarget Incoming)> AmountModifiers =>
        DurationFrames is null ? [] : EffectDurations.ModifiersFor(Polarity);

    public IEnumerable<GameplayTag> ReferencedTags => ConditionTag.IsNone ? [] : [ConditionTag];

    public bool GrantsUntilRevoked => DurationFrames is null;

    public EffectOutcome Apply(in EffectContext context)
    {
        var durationFrames = EffectDurations.Scale(in context, DurationFrames, Polarity);
        var expiresAtFrame = durationFrames is { } frames ? FrameDeadline.After(context.Now, frames) : FrameDeadline.Never;

        if (Stacking == StatModifierStacking.RefreshFromSameSource)
        {
            StatModifierEffects.ApplyOrRefresh(context.Services.StatModifiers, context.TargetEntityId, Target, Operation, Polarity, CanModify, Magnitude,
                expiresAtFrame, context.Source, ConditionTag);
        }
        else
        {
            StatModifierEffects.Apply(context.Services.ComponentManager, context.TargetEntityId, Target, Operation, Polarity, CanModify, Magnitude,
                expiresAtFrame, context.Source, ConditionTag);
        }

        return EffectOutcome.Applied;
    }
}
