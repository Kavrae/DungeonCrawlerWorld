using Engine.ECS.Systems;
using Engine.Tags;
using Game.Modules.Actions;
using Game.Modules.StatModifiers;

namespace Game.Effects.Entries;

/// <summary>Disables, for its target, every action and item whose tags Query matches, for DurationFrames: the Disabled status (DisabledActivations).</summary>
/// <remarks>
/// It refuses using them (ActivationBlocker.Disabled) and ends any already in progress -- a windup, a toggle that is on --
/// the moment it lands (DisabledActivationSystem). Applied again while it holds (an aura's tick, a contact's repeat) it
/// extends the same rule rather than adding another, so a null zone is an aura of this grant with a duration a little
/// past its tick. The duration is scaled as every debuff's is (EffectDurations) unless FixedDuration: a grant that holds
/// only by being applied again before it ends (a null zone) is fixed, so no modifier can shorten it below the interval
/// it is applied at and open a gap. Query is this entry's one shared instance, and must name at least one tag (an empty
/// query matches everything). Its codec is Core's, with the other general entries, since every build reads the auras
/// that hold it.
/// </remarks>
/// <param name="Query">Which actions and items it disables, by their tags.</param>
/// <param name="DurationFrames">How long the Disabled status lasts from each application.</param>
/// <param name="FixedDuration">Whether DurationFrames is used as written, scaled by nothing.</param>
public sealed record ActivationDisableGrant(GameplayTagQuery Query, ushort DurationFrames, bool FixedDuration = false) : IEffectEntry
{
    /// <remarks>The duration of the Disabled status; none when FixedDuration.</remarks>
    public IReadOnlyList<(StatModifierTarget Outgoing, StatModifierTarget Incoming)> AmountModifiers =>
        FixedDuration ? [] : EffectDurations.ModifiersFor(StatModifierPolarity.Debuff);

    public IEnumerable<GameplayTag> ReferencedTags => [.. Query.RequireAll, .. Query.RequireAny, .. Query.Exclude];

    public EffectOutcome Apply(in EffectContext context)
    {
        var durationFrames = FixedDuration
            ? DurationFrames
            : EffectDurations.Scale(in context, DurationFrames, StatModifierPolarity.Debuff) ?? 0;
        if (durationFrames == 0)
        {
            return EffectOutcome.NoEffect;
        }

        DisabledActivations.Grant(context.Services.DisabledActivations, context.TargetEntityId, Query, FrameDeadline.After(context.Now, durationFrames));
        return EffectOutcome.Applied;
    }
}
